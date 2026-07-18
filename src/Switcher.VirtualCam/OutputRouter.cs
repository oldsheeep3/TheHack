using Switcher.Contracts;
using Switcher.VirtualCam.Display;
using Switcher.VirtualCam.Ndi;

namespace Switcher.VirtualCam;

/// <summary>
/// Resolves `PUT /api/v1/outputs` assignments (docs/specs/00-system-overview.md §4.2,
/// docs/specs/multiview-output-revision.md §2.5) into a routing table (which PGM source feeds which
/// sink) and distributes composited frames to the sinks accordingly once they arrive. Sinks are VCAM1/2
/// (via <see cref="IDualVirtualCameraOutput"/>, NV12), HDMI (via <see cref="IHdmiFullscreenOutput"/>),
/// and — when an <see cref="Ndi.IDualNdiOutput"/> is supplied — NDI1/2 (BGRA, a separate copy-free path
/// from VCAM). Pulling frames from <c>ICompositorEngine.GetProgramFrame(bus)</c> and attaching
/// <see cref="IHdmiFullscreenOutput"/> to a real window/monitor is App integration's job
/// (agent-A2-006/A3-005); this type owns the assignment table and the fan-out once a frame is handed to
/// <see cref="RouteFrame"/>. Multiview full-screen presentation is a separate system (see
/// <see cref="Display.IFullscreenPresenterFactory"/>), not a routed sink.
/// </summary>
public sealed class OutputRouter
{
    /// <summary>Default assignment (docs/specs/00-system-overview.md §4.2): PGM1→VCAM1, PGM2→VCAM2.
    /// HDMI is unassigned until explicitly configured (it requires a display_id).</summary>
    public static IReadOnlyList<OutputAssignment> DefaultAssignments { get; } =
    [
        new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null),
        new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, DisplayId: null, HideCursor: null, Fullscreen: null),
    ];

    /// <summary>Default NDI assignments (docs/specs/multiview-output-revision.md §2.5): PGM1→NDI1,
    /// PGM2→NDI2, with sender names <c>SWITCHER PGM1</c>/<c>SWITCHER PGM2</c>. These are only seeded when
    /// an <see cref="IDualNdiOutput"/> is supplied to the router (otherwise the NDI sinks are absent).</summary>
    public static IReadOnlyList<OutputAssignment> DefaultNdiAssignments { get; } =
    [
        new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: DualNdiOutput.DefaultNdi1SenderName),
        new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, DisplayId: null, HideCursor: null, Fullscreen: null, NdiName: DualNdiOutput.DefaultNdi2SenderName),
    ];

    private readonly IDualVirtualCameraOutput _virtualCameraOutput;
    private readonly IHdmiFullscreenOutput? _hdmiOutput;
    private readonly IDualNdiOutput? _ndiOutput;
    private readonly object _lock = new();
    private readonly Dictionary<OutputSink, OutputAssignment> _assignments;

    public OutputRouter(
        IDualVirtualCameraOutput virtualCameraOutput,
        IHdmiFullscreenOutput? hdmiOutput = null,
        IDualNdiOutput? ndiOutput = null)
    {
        ArgumentNullException.ThrowIfNull(virtualCameraOutput);

        _virtualCameraOutput = virtualCameraOutput;
        _hdmiOutput = hdmiOutput;
        _ndiOutput = ndiOutput;
        _assignments = DefaultAssignments.ToDictionary(a => a.Sink);

        // Only expose the NDI sinks when an NDI output is wired in; without it, NDI1/NDI2 are simply not
        // assigned (matching how HDMI stays unassigned until configured).
        if (_ndiOutput is not null)
        {
            foreach (var assignment in DefaultNdiAssignments)
            {
                _assignments[assignment.Sink] = assignment;
            }
        }
    }

    /// <summary>The currently resolved assignments, one per configured sink.</summary>
    public IReadOnlyList<OutputAssignment> CurrentAssignments
    {
        get
        {
            lock (_lock)
            {
                return [.. _assignments.Values];
            }
        }
    }

    /// <summary>Validates and applies <paramref name="request"/>'s assignments, replacing any existing
    /// assignment for the same sink. Assignments for sinks not present in the request are left
    /// unchanged. Throws <see cref="ArgumentException"/> for an unknown sink/source, a duplicate sink
    /// within the request, or an HDMI assignment missing <c>display_id</c>; on failure no assignment is
    /// changed.</summary>
    public void ApplyOutputs(OutputsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Outputs);

        var seenSinks = new HashSet<OutputSink>();
        foreach (var assignment in request.Outputs)
        {
            ArgumentNullException.ThrowIfNull(assignment);

            if (!Enum.IsDefined(assignment.Sink))
            {
                throw new ArgumentException($"Unknown output sink: {assignment.Sink}.", nameof(request));
            }

            if (!Enum.IsDefined(assignment.Source))
            {
                throw new ArgumentException($"Unknown output source: {assignment.Source}.", nameof(request));
            }

            if (!seenSinks.Add(assignment.Sink))
            {
                throw new ArgumentException($"Duplicate assignment for sink {assignment.Sink}.", nameof(request));
            }

            if (assignment.Sink == OutputSink.Hdmi && assignment.DisplayId is null)
            {
                throw new ArgumentException("HDMI output assignment requires display_id.", nameof(request));
            }
        }

        List<(OutputSink Sink, string NdiName)>? ndiRenames = null;
        lock (_lock)
        {
            foreach (var assignment in request.Outputs)
            {
                _assignments[assignment.Sink] = assignment;

                if ((assignment.Sink == OutputSink.Ndi1 || assignment.Sink == OutputSink.Ndi2)
                    && !string.IsNullOrWhiteSpace(assignment.NdiName))
                {
                    (ndiRenames ??= []).Add((assignment.Sink, assignment.NdiName));
                }
            }
        }

        // Push sender-name changes outside the router lock: the NDI output takes its own lock, and this
        // keeps the router from ever holding two locks at once (a missing/empty ndi_name leaves the
        // current name in place, per the spec's "未指定は既定名で補完").
        if (ndiRenames is not null && _ndiOutput is not null)
        {
            foreach (var (sink, ndiName) in ndiRenames)
            {
                _ndiOutput.SetSenderName(sink, ndiName);
            }
        }
    }

    /// <summary>Distributes <paramref name="frame"/> to every sink currently assigned to
    /// <paramref name="source"/>. Sinks are independent: a failure on one (e.g. HDMI not yet attached,
    /// or a camera device error) does not prevent the frame from reaching the others; any failures are
    /// surfaced together as an <see cref="AggregateException"/> after all sinks have been attempted.</summary>
    public void RouteFrame(OutputSource source, FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        List<Exception>? errors = null;
        foreach (var sink in GetSinksForLocked(source))
        {
            try
            {
                Dispatch(sink, frame);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is { Count: > 0 })
        {
            throw new AggregateException("One or more output sinks failed to receive the frame.", errors);
        }
    }

    private void Dispatch(OutputSink sink, FrameData frame)
    {
        switch (sink)
        {
            case OutputSink.Vcam1:
            case OutputSink.Vcam2:
                _virtualCameraOutput.SubmitFrame(sink, frame);
                break;
            case OutputSink.Hdmi:
                _hdmiOutput?.Present(frame);
                break;
            case OutputSink.Ndi1:
            case OutputSink.Ndi2:
                _ndiOutput?.SubmitFrame(sink, frame);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(sink), sink, "Unknown output sink.");
        }
    }

    private List<OutputSink> GetSinksForLocked(OutputSource source)
    {
        lock (_lock)
        {
            return [.. _assignments.Values.Where(a => a.Source == source).Select(a => a.Sink)];
        }
    }
}
