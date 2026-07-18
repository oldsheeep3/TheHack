using Switcher.Contracts;
using Switcher.VirtualCam.Display;

namespace Switcher.VirtualCam;

/// <summary>
/// Resolves `PUT /api/v1/outputs` assignments (docs/specs/00-system-overview.md §4.2) into a routing
/// table (which PGM source feeds which sink) and distributes composited frames to the sinks accordingly
/// once they arrive. Pulling frames from <c>ICompositorEngine.GetProgramFrame(bus)</c> and attaching
/// <see cref="IHdmiFullscreenOutput"/> to a real window/monitor is App integration's job
/// (agent-A2-006); this type owns the assignment table and the fan-out once a frame is handed to
/// <see cref="RouteFrame"/>.
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

    private readonly IDualVirtualCameraOutput _virtualCameraOutput;
    private readonly IHdmiFullscreenOutput? _hdmiOutput;
    private readonly object _lock = new();
    private readonly Dictionary<OutputSink, OutputAssignment> _assignments;

    public OutputRouter(IDualVirtualCameraOutput virtualCameraOutput, IHdmiFullscreenOutput? hdmiOutput = null)
    {
        ArgumentNullException.ThrowIfNull(virtualCameraOutput);

        _virtualCameraOutput = virtualCameraOutput;
        _hdmiOutput = hdmiOutput;
        _assignments = DefaultAssignments.ToDictionary(a => a.Sink);
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

        lock (_lock)
        {
            foreach (var assignment in request.Outputs)
            {
                _assignments[assignment.Sink] = assignment;
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
