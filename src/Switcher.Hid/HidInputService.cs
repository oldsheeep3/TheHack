using Switcher.Hid.Devices;
using Switcher.Hid.Input;
using Switcher.Hid.Reports;

namespace Switcher.Hid;

/// <summary>
/// Subscribes to the Pico 2W's USB-HID input reports (docs/specs/00-system-overview.md §4.1) on a
/// dedicated background thread (blocking reads, to keep SW→PC latency in the low-single-digit-ms
/// range per docs/specs/pc-switcher-app.md §2.6), parses each report, and republishes it as
/// normalized <see cref="SwitchEdgeEvent"/>/<see cref="VrChangedEvent"/> events. Purely a
/// read→normalize→publish pipeline: turning these events into PGM changes or ATEM commands is the
/// App layer's job (docs/tasks/agent-A2-006-app-integration-v2.md).
/// </summary>
public sealed class HidInputService : IDisposable
{
    private readonly IHidDevice _device;
    private readonly SwitchEdgeDetector _edgeDetector = new();
    private readonly VrFilter _vrFilter = new();
    private readonly SeqGapTracker _seqGapTracker = new();
    private readonly object _lock = new();

    private Thread? _readThread;
    private volatile bool _running;
    private bool _disposed;

    // Read-thread only. -1 (no valid bitmap can be negative) forces the first report to publish, and
    // resetting it on Stop re-publishes presence after a reconnect.
    private int _lastModulePresent = -1;

    public HidInputService() : this(new HidSharpDevice())
    {
    }

    internal HidInputService(IHidDevice device)
    {
        _device = device;
    }

    /// <summary>Raised on a rising or falling edge of a module's switch, detected from the diff
    /// between two consecutive input reports.</summary>
    public event Action<SwitchEdgeEvent>? SwitchEdge;

    /// <summary>Raised when a module's VR value moves by at least the configured deadband.</summary>
    public event Action<VrChangedEvent>? VrChanged;

    /// <summary>Raised when the input report's rotating <c>seq</c> byte skips a value, indicating one
    /// or more dropped frames.</summary>
    public event Action? SequenceGapDetected;

    /// <summary>Raised with the report's <c>module_present</c> bitmap (bit n = module n attached,
    /// docs/specs/00-system-overview.md §4.1) on the first report after <see cref="Start"/> and then
    /// only when the bitmap changes. This is the controller telling the App which physical modules
    /// exist, so the App can add/drop module rows instead of assuming a fixed <c>MAX_MODULES</c>.</summary>
    public event Action<byte>? ModulePresenceChanged;

    /// <summary>Opens the device and starts the background read loop. Safe to call again while
    /// already started (no-op).</summary>
    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_running)
            {
                return;
            }

            _device.Open();
            _lastModulePresent = -1;
            _running = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = nameof(HidInputService) };
            _readThread.Start();
        }
    }

    /// <summary>Stops the read loop and closes the device. Safe to call again, or when never
    /// started (no-op). Blocks until the read thread has exited.</summary>
    public void Stop()
    {
        Thread? threadToJoin;
        lock (_lock)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _device.Close();
            threadToJoin = _readThread;
            _readThread = null;
        }

        threadToJoin?.Join();
    }

    private void ReadLoop()
    {
        while (_running)
        {
            byte[]? body;
            try
            {
                body = _device.ReadInputReport();
            }
            catch (Exception) when (!_running)
            {
                break;
            }

            if (body is null)
            {
                break;
            }

            if (body.Length < HidReportParser.InputReportLength)
            {
                continue;
            }

            var report = HidReportParser.ParseInput(body);

            // Presence is republished only on change: the Pico sends module_present in every report
            // (~1 kHz), and the App's handler persists the reconciled module list to disk.
            if (_lastModulePresent != report.ModulePresent)
            {
                _lastModulePresent = report.ModulePresent;
                ModulePresenceChanged?.Invoke(report.ModulePresent);
            }

            if (_seqGapTracker.Update(report.Seq))
            {
                SequenceGapDetected?.Invoke();
            }

            foreach (var edge in _edgeDetector.Process(report))
            {
                SwitchEdge?.Invoke(edge);
            }

            foreach (var change in _vrFilter.Process(report))
            {
                VrChanged?.Invoke(change);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        _device.Dispose();
    }
}
