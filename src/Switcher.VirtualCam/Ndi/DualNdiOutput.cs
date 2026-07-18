using Switcher.Contracts;

namespace Switcher.VirtualCam.Ndi;

/// <summary>
/// Default <see cref="IDualNdiOutput"/>: manages two independent <see cref="NdiOutput"/> instances
/// (NDI1/NDI2), each publishing under its own sender name (defaulting to <see cref="DefaultNdi1SenderName"/>
/// / <see cref="DefaultNdi2SenderName"/>, docs/specs/multiview-output-revision.md §2.5). The two
/// senders are independent: a failure on one (or a missing NDI SDK) does not stop or otherwise affect
/// the other.
/// </summary>
public sealed class DualNdiOutput : IDualNdiOutput, IDisposable
{
    public const string DefaultNdi1SenderName = "SWITCHER PGM1";
    public const string DefaultNdi2SenderName = "SWITCHER PGM2";

    private readonly NdiOutput _ndi1;
    private readonly NdiOutput _ndi2;

    public DualNdiOutput()
        : this(new NdiOutput(DefaultNdi1SenderName), new NdiOutput(DefaultNdi2SenderName))
    {
    }

    internal DualNdiOutput(NdiOutput ndi1, NdiOutput ndi2)
    {
        _ndi1 = ndi1;
        _ndi2 = ndi2;
    }

    public void Start()
    {
        _ndi1.Start();
        _ndi2.Start();
    }

    public void SubmitFrame(OutputSink sink, FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Resolve(sink).SubmitFrame(frame);
    }

    public void SetSenderName(OutputSink sink, string senderName) => Resolve(sink).SetSenderName(senderName);

    public void Stop()
    {
        _ndi1.Stop();
        _ndi2.Stop();
    }

    private NdiOutput Resolve(OutputSink sink) => sink switch
    {
        OutputSink.Ndi1 => _ndi1,
        OutputSink.Ndi2 => _ndi2,
        _ => throw new ArgumentOutOfRangeException(nameof(sink), sink, $"{nameof(DualNdiOutput)} only handles {OutputSink.Ndi1}/{OutputSink.Ndi2}."),
    };

    public void Dispose()
    {
        _ndi1.Dispose();
        _ndi2.Dispose();
    }
}
