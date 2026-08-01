using HidSharp;
using Switcher.Contracts;
using Switcher.Hid.Reports;

namespace Switcher.Tools.ModuleSimulator;

/// <summary>
/// Talks to a <c>-DENABLE_FAKE_MODULES=ON</c> build of the pico2w-controller firmware over raw HID.
///
/// Three reports are involved:
/// <list type="bullet">
/// <item>input <c>0x01</c> — the production state report, parsed with the production
/// <see cref="HidReportParser"/> so the simulator can never drift from the real layout.</item>
/// <item>output <c>0x04</c> — debug only: injects one fake module's present/SW/VR
/// (firmware <c>fake_modules_codec.c</c>).</item>
/// <item>feature <c>0x05</c> — debug only: reads back the backlight most recently distributed by
/// <c>backlight_task()</c> (firmware <c>i2c_modules_fake.c</c>).</item>
/// </list>
///
/// Reconnects on its own: a failed read drops the stream and the read loop retries
/// <see cref="ReconnectDelay"/> later, so unplugging and replugging the Pico does not require
/// restarting the tool.
/// </summary>
internal sealed class PicoDebugDevice : IDisposable
{
    private const int VendorId = 0xCafe;
    private const int ProductId = 0x4011;

    private const byte InputStateReportId = 0x01;
    private const byte FakeModuleOutReportId = 0x04;
    private const byte FakeBacklightFeatureReportId = 0x05;
    private const byte FakeRebootOutReportId = 0x06;

    /// <summary>module_index + present + switches + VR×2 (firmware <c>HID_REPORT_FAKE_MODULE_OUT_LEN</c>).</summary>
    private const int FakeModuleOutLength = 3 + 2;

    /// <summary>module_index + 4×RGB (firmware <c>HID_REPORT_FAKE_BACKLIGHT_FEATURE_LEN</c>).</summary>
    private const int FakeBacklightFeatureLength = 1 + 12;

    /// <summary>Sentinel meaning "no backlight distributed yet" (firmware <c>FAKE_BACKLIGHT_MODULE_NONE</c>).</summary>
    private const byte BacklightModuleNone = 0xFF;

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    /// <summary>Backlight distribution is rare and the readback is a control transfer competing with
    /// the interrupt IN endpoint, so it is polled well below the ~50 Hz state report rate.</summary>
    private const int BacklightPollIntervalMs = 200;

    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _readThread;
    private readonly System.Diagnostics.Stopwatch _backlightPollWatch = System.Diagnostics.Stopwatch.StartNew();

    private HidStream? _stream;
    private volatile bool _connected;
    private HidInputReport? _lastReport;
    private int _reportCount;
    private int _seqGaps;
    private int _lastSeq = -1;
    private string? _lastError;
    private string? _backlightError;
    private byte[]? _lastRawInput;

    // moduleIndex -> last 12 RGB bytes seen distributed to it. Accumulated because the firmware's
    // feature report only remembers the single most recent distribution.
    private readonly Dictionary<int, byte[]> _backlights = new();

    public PicoDebugDevice()
    {
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "pico-debug-read" };
        _readThread.Start();
    }

    public SimulatorState Snapshot()
    {
        lock (_lock)
        {
            var modules = new List<ModuleState>(ProtocolConstants.MaxModules);
            for (var i = 0; i < ProtocolConstants.MaxModules; i++)
            {
                var report = _lastReport;
                var present = report is not null && (report.ModulePresent & (1 << i)) != 0;
                var sw = report?.Switches[i];
                var vr = report?.Vrs[i];
                _backlights.TryGetValue(i, out var rgb);

                modules.Add(new ModuleState(
                    Index: i,
                    Present: present,
                    Pgm1Src1: sw?.Pgm1Src1 ?? false,
                    Pgm1Src2: sw?.Pgm1Src2 ?? false,
                    Pgm2Src1: sw?.Pgm2Src1 ?? false,
                    Pgm2Src2: sw?.Pgm2Src2 ?? false,
                    VrSrc1: vr?.VrSrc1 ?? 0,
                    VrSrc2: vr?.VrSrc2 ?? 0,
                    Backlight: rgb is null ? null : ToColors(rgb)));
            }

            return new SimulatorState(
                Connected: _connected,
                Error: _lastError,
                BacklightError: _backlightError,
                ModulePresent: _lastReport?.ModulePresent ?? 0,
                Seq: _lastReport?.Seq ?? 0,
                ReportCount: _reportCount,
                SeqGaps: _seqGaps,
                Modules: modules);
        }
    }

    /// <summary>Injects one fake module's state via debug output report 0x04.</summary>
    public void SetModule(int index, bool present, bool pgm1Src1, bool pgm1Src2, bool pgm2Src1, bool pgm2Src2,
                           byte vrSrc1, byte vrSrc2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ProtocolConstants.MaxModules);

        // Bit layout mirrors HidReportParser.ParseInput (docs/specs/00-system-overview.md §4.1).
        var switches = 0;
        if (pgm1Src1) switches |= 1 << 0;
        if (pgm1Src2) switches |= 1 << 1;
        if (pgm2Src1) switches |= 1 << 2;
        if (pgm2Src2) switches |= 1 << 3;

        Span<byte> body = stackalloc byte[FakeModuleOutLength];
        body[0] = (byte)index;
        body[1] = present ? (byte)1 : (byte)0;
        body[2] = (byte)switches;
        body[3] = vrSrc1;
        body[4] = vrSrc2;

        WriteOutputReport(FakeModuleOutReportId, body);
    }

    /// <summary>
    /// Sends the production backlight output report 0x02, standing in for what Switcher.App would
    /// send. Lets the distribution path (queue → <c>backlight_task()</c> → fake module) be exercised
    /// without running the full app.
    /// </summary>
    public void SetBacklight(int index, IReadOnlyList<RgbColor> colors)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ProtocolConstants.MaxModules);
        if (colors.Count != ProtocolConstants.BacklightsPerModule)
        {
            throw new ArgumentException($"expected {ProtocolConstants.BacklightsPerModule} colors", nameof(colors));
        }

        var body = new byte[1 + ProtocolConstants.BacklightsPerModule * 3];
        body[0] = (byte)index;
        for (var i = 0; i < colors.Count; i++)
        {
            body[1 + i * 3] = colors[i].R;
            body[1 + i * 3 + 1] = colors[i].G;
            body[1 + i * 3 + 2] = colors[i].B;
        }

        WriteOutputReport(ProtocolConstants.HidOutputReportId, body);
    }

    /// <summary>
    /// Asks a FAKE_MODULES build to reboot into BOOTSEL (debug output report 0x06). This firmware
    /// has no USB CDC, so without it every reflash needs a physical BOOTSEL button press.
    /// </summary>
    public void RebootToBootsel()
    {
        Span<byte> body = stackalloc byte[1];
        body[0] = 0xB7; // firmware FAKE_REBOOT_MAGIC
        WriteOutputReport(FakeRebootOutReportId, body);
    }

    private void WriteOutputReport(byte reportId, ReadOnlySpan<byte> body)
    {
        HidStream stream;
        lock (_lock)
        {
            if (_stream is null)
            {
                throw new InvalidOperationException("Pico is not connected.");
            }
            stream = _stream;
        }

        // Windows requires the write buffer to be exactly the device's max output report length,
        // not the length of this particular report. The firmware enumerates two output reports
        // (0x02 backlight = 13 bytes, 0x04 fake module = 5 bytes), so the short one must be padded.
        var buffer = new byte[stream.Device.GetMaxOutputReportLength()];
        buffer[0] = reportId;
        body.CopyTo(buffer.AsSpan(1));
        stream.Write(buffer);
    }

    /// <summary>Raw device/report diagnostics, for working out why a report is not landing.</summary>
    public object Diagnostics()
    {
        HidStream? stream;
        lock (_lock) { stream = _stream; }
        if (stream is null) { return new { connected = false }; }

        byte[]? feature = null;
        string? featureError = null;
        try
        {
            var fb = new byte[stream.Device.GetMaxFeatureReportLength()];
            fb[0] = FakeBacklightFeatureReportId;
            stream.GetFeature(fb);
            feature = fb;
        }
        catch (Exception ex) { featureError = ex.Message; }

        byte[]? lastInput;
        lock (_lock) { lastInput = _lastRawInput; }

        return new
        {
            connected = true,
            maxInput = stream.Device.GetMaxInputReportLength(),
            maxOutput = stream.Device.GetMaxOutputReportLength(),
            maxFeature = stream.Device.GetMaxFeatureReportLength(),
            lastInputHex = lastInput is null ? null : Convert.ToHexString(lastInput),
            featureHex = feature is null ? null : Convert.ToHexString(feature),
            featureError,
        };
    }

    /// <summary>Reads feature report 0x05 and folds the result into the per-module backlight map.</summary>
    private void PollBacklight(HidStream stream)
    {
        var buffer = new byte[stream.Device.GetMaxFeatureReportLength()];
        buffer[0] = FakeBacklightFeatureReportId;
        stream.GetFeature(buffer);

        if (buffer.Length < 1 + FakeBacklightFeatureLength)
        {
            return;
        }

        // buffer[0] is the echoed Report ID; the body starts at 1.
        var moduleIndex = buffer[1];
        if (moduleIndex == BacklightModuleNone || moduleIndex >= ProtocolConstants.MaxModules)
        {
            return;
        }

        var rgb = buffer[2..(2 + 12)];
        lock (_lock)
        {
            _backlights[moduleIndex] = rgb;
        }
    }

    private void ReadLoop()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            HidStream? stream;
            try
            {
                stream = EnsureOpen();
            }
            catch (Exception ex)
            {
                SetDisconnected(ex.Message);
                token.WaitHandle.WaitOne(ReconnectDelay);
                continue;
            }

            try
            {
                var buffer = new byte[stream.Device.GetMaxInputReportLength()];
                var read = stream.Read(buffer);

                if (read > 0)
                {
                    var raw = buffer[..read];
                    lock (_lock) { _lastRawInput = raw; }
                }

                if (read > 1 && buffer[0] == InputStateReportId)
                {
                    OnStateReport(buffer.AsSpan(1, read - 1));
                }

                // Backlight readback is auxiliary: a non-FAKE_MODULES firmware has no feature report
                // 0x05 at all, so GetFeature legitimately fails. It must never tear down the input
                // stream — doing so drops state reports and shows up as phantom seq gaps.
                if (_backlightPollWatch.ElapsedMilliseconds >= BacklightPollIntervalMs)
                {
                    _backlightPollWatch.Restart();
                    try
                    {
                        PollBacklight(stream);
                        lock (_lock) { _backlightError = null; }
                    }
                    catch (Exception ex)
                    {
                        lock (_lock) { _backlightError = ex.Message; }
                    }
                }
            }
            catch (Exception ex)
            {
                SetDisconnected(ex.Message);
                token.WaitHandle.WaitOne(ReconnectDelay);
            }
        }
    }

    private HidStream EnsureOpen()
    {
        lock (_lock)
        {
            if (_stream is not null)
            {
                return _stream;
            }
        }

        var device = DeviceList.Local.GetHidDeviceOrNull(VendorId, ProductId)
            ?? throw new InvalidOperationException(
                $"No HID device found for VID=0x{VendorId:X4} PID=0x{ProductId:X4}. " +
                "Is the Pico plugged in and running a firmware build?");

        var opened = device.Open();
        lock (_lock)
        {
            _stream = opened;
            _connected = true;
            _lastError = null;
        }
        return opened;
    }

    private void OnStateReport(ReadOnlySpan<byte> body)
    {
        if (body.Length < HidReportParser.InputReportLength)
        {
            return; // Not a state report we understand; ignore rather than throw on the read thread.
        }

        var report = HidReportParser.ParseInput(body);
        lock (_lock)
        {
            _lastReport = report;
            _reportCount++;
            if (_lastSeq >= 0)
            {
                var expected = (byte)(_lastSeq + 1);
                if (report.Seq != expected)
                {
                    _seqGaps++;
                }
            }
            _lastSeq = report.Seq;
        }
    }

    private void SetDisconnected(string error)
    {
        lock (_lock)
        {
            _stream?.Dispose();
            _stream = null;
            _connected = false;
            _lastError = error;
        }
    }

    private static IReadOnlyList<RgbColor> ToColors(byte[] rgb)
    {
        var colors = new List<RgbColor>(ProtocolConstants.BacklightsPerModule);
        for (var i = 0; i < ProtocolConstants.BacklightsPerModule; i++)
        {
            colors.Add(new RgbColor(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]));
        }
        return colors;
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_lock)
        {
            _stream?.Dispose();
            _stream = null;
        }
        _cts.Dispose();
    }
}

internal sealed record RgbColor(byte R, byte G, byte B);

internal sealed record ModuleState(
    int Index,
    bool Present,
    bool Pgm1Src1,
    bool Pgm1Src2,
    bool Pgm2Src1,
    bool Pgm2Src2,
    byte VrSrc1,
    byte VrSrc2,
    IReadOnlyList<RgbColor>? Backlight);

internal sealed record SimulatorState(
    bool Connected,
    string? Error,
    /// <summary>Non-fatal: set when feature report 0x05 cannot be read, which is the normal state
    /// against a firmware built without ENABLE_FAKE_MODULES.</summary>
    string? BacklightError,
    byte ModulePresent,
    byte Seq,
    int ReportCount,
    int SeqGaps,
    IReadOnlyList<ModuleState> Modules);

internal sealed record SetBacklightRequest(IReadOnlyList<RgbColor> Colors);

internal sealed record SetModuleRequest(
    bool Present,
    bool Pgm1Src1,
    bool Pgm1Src2,
    bool Pgm2Src1,
    bool Pgm2Src2,
    byte VrSrc1,
    byte VrSrc2);
