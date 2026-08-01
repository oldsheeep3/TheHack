using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.Contracts;

namespace Switcher.App;

/// <summary>
/// Modal editor that collects one <see cref="SourceDefinition"/> from the operator. Extracted from the
/// overlay panel that used to sit inside the Sources dock: an add form crowded the dock every time it
/// opened, and hid the source list underneath, so the same fields now live in their own window.
///
/// The window owns its own device rescans and SRT setup lookup so the main window is untouched while it
/// is open. On Add it exposes the built <see cref="Result"/> and closes with <c>DialogResult=true</c>;
/// on Cancel or the window's close box it returns <c>false</c> and <c>Result</c> stays <c>null</c>.
/// </summary>
public partial class AddSourceWindow : Window
{
    /// <summary>Capture modes offered for a webcam. First entry keeps win-dshow on its device-preferred
    /// media type; the rest map to <c>WebcamConfig.Format</c> as "WxH@FPS".</summary>
    private static readonly (string Label, string? Format)[] WebcamCaptureModes =
    [
        ("Device default", null),
        ("1920x1080 @ 60", "1920x1080@60"),
        ("1920x1080 @ 30", "1920x1080@30"),
        ("1280x720 @ 60", "1280x720@60"),
        ("1280x720 @ 30", "1280x720@30"),
        ("960x540 @ 30", "960x540@30"),
        ("640x480 @ 30", "640x480@30"),
    ];

    private const int DeviceQueryDefaultLatencyMs = 40;

    /// <summary>Name pre-filled when the operator picks an ATEM as the SRT sender.</summary>
    private const string AtemProgramSourceName = "ATEM ON AIR";

    private readonly IDeviceQueryService _deviceQueryService;
    private readonly AppOrchestrator _orchestrator;
    private readonly ILogger _logger;
    private readonly ObservableCollection<DeviceInfo> _devices = [];
    private readonly ObservableCollection<SrtSenderOption> _srtSenders = [];
    private readonly ObservableCollection<string> _srtHosts = [];

    private SrtSetupInfo? _lastSrtSetup;
    private bool _uiReady;

    /// <summary>Set once the host list is filled, so seeding the picker does not count as a pick and
    /// overwrite an address the operator typed by hand.</summary>
    private bool _srtHostsReady;

    public AddSourceWindow(IDeviceQueryService deviceQueryService, AppOrchestrator orchestrator, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(deviceQueryService);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(logger);

        InitializeComponent();

        _deviceQueryService = deviceQueryService;
        _orchestrator = orchestrator;
        _logger = logger;

        SrtAtemCombo.ItemsSource = _srtSenders;
        SrtHostCombo.ItemsSource = _srtHosts;
        DeviceCombo.ItemsSource = _devices;
        WebcamFormatCombo.ItemsSource = WebcamCaptureModes.Select(m => m.Label).ToList();
        WebcamFormatCombo.SelectedIndex = 0;
        NewSourceTypeCombo.SelectedIndex = 0;

        _uiReady = true;
        OnSourceTypeChanged(this, null!);

        Loaded += (_, _) =>
        {
            NewSourceNameBox.Focus();
            NewSourceNameBox.SelectAll();
        };
    }

    /// <summary>The source the operator built. Only set when the dialog closes with <c>true</c>.</summary>
    public SourceDefinition? Result { get; private set; }

    private void OnSourceTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
        {
            return;
        }

        var type = SelectedSourceTypeText();
        var usesDevice = type is "WEBCAM" or "NDI";
        DeviceSelectPanel.Visibility = usesDevice ? Visibility.Visible : Visibility.Collapsed;
        WebcamFormatPanel.Visibility = type == "WEBCAM" ? Visibility.Visible : Visibility.Collapsed;
        SrtPanel.Visibility = type == "SRT" ? Visibility.Visible : Visibility.Collapsed;
        ImagePanel.Visibility = type == "IMAGE" ? Visibility.Visible : Visibility.Collapsed;
        HtmlPanel.Visibility = type == "HTML" ? Visibility.Visible : Visibility.Collapsed;

        if (type == "SRT")
        {
            RebuildSrtSenders();
            _ = LoadSrtSetupAsync();
        }
        else if (usesDevice)
        {
            _ = RescanDevicesAsync();
        }
    }

    private void OnRescanDevicesClick(object sender, RoutedEventArgs e) => _ = RescanDevicesAsync();

    private async Task RescanDevicesAsync()
    {
        var queryType = SelectedDeviceQueryType();
        if (queryType is not { } type)
        {
            return;
        }

        IReadOnlyList<DeviceInfo> devices;
        try
        {
            devices = await _deviceQueryService.EnumerateAsync(type).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Device enumeration failed for {Type}.", type);
            devices = Array.Empty<DeviceInfo>();
        }

        _devices.Clear();
        foreach (var device in devices)
        {
            _devices.Add(device);
        }

        NoDevicesText.Text = type == DeviceQueryType.Ndi
            ? "No NDI sources. If this list is always empty, install the DistroAV (obs-ndi) plugin into " +
              "OBS Studio — NDI input and the NDI outputs need it — then restart the app."
            : "No devices found. Connect the device, then press Rescan.";
        NoDevicesText.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_devices.Count > 0)
        {
            DeviceCombo.SelectedIndex = 0;
        }
    }

    private async Task LoadSrtSetupAsync()
    {
        SrtSetupInfo info;
        try
        {
            info = await _deviceQueryService.GetSrtSetupAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SRT setup lookup failed.");
            return;
        }

        _lastSrtSetup = info;
        SrtSetupText.Text = info.InstructionsText;
        SrtLatencyBox.Text = info.RecommendedLatencyMs.ToString(CultureInfo.InvariantCulture);

        // Every address this PC answers on, best guess first. A PC with a VPN, a Hyper-V switch or a
        // second NIC has several and only the one on the sender's network works, so the guess has to stay
        // changeable rather than being the only address on offer.
        _srtHostsReady = false;
        _srtHosts.Clear();
        foreach (var host in info.HostCandidates)
        {
            _srtHosts.Add(host);
        }

        SrtHostCombo.SelectedIndex = _srtHosts.Count > 0 ? 0 : -1;
        SrtRecommendedUrlBox.Text = info.RecommendedUrl;
        _srtHostsReady = true;
        UpdateSenderHint();

        // The picker may have been used before the setup lookup came back, in which case the URL it
        // filled in was a guess at the default port; redo it now that the real port is known.
        if (SelectedAtem() is not null)
        {
            ApplyAtemPreset();
        }
    }

    private void OnSrtHostChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_srtHostsReady || SrtHostCombo.SelectedItem is not string host)
        {
            return;
        }

        var port = _lastSrtSetup?.ListenerPort ?? ProtocolConstants.SrtListenPort;
        SrtRecommendedUrlBox.Text = SrtUrl.ForSender(host, port);
        UpdateSenderHint();
    }

    /// <summary>The address the sender should dial: whatever is in the box, which starts as the
    /// recommendation and follows the host picker until the operator types over it.</summary>
    private string SenderUrl()
    {
        var typed = SrtRecommendedUrlBox.Text.Trim();
        return typed.Length > 0 ? typed : _lastSrtSetup?.RecommendedUrl ?? string.Empty;
    }

    private void UpdateSenderHint() =>
        SrtAtemText.Text =
            $"On the sender, stream to {SenderUrl()} in Caller mode. " +
            "Picking an ATEM above sets this side up for that automatically.";

    // ── SRT: the ATEM as the sender ─────────────────────────────────────────────

    /// <summary>One entry of the "where is this SRT stream coming from?" list. Public because the combo
    /// box binds <see cref="Label"/> through <c>DisplayMemberPath</c>, and WPF's binding engine will not
    /// reach a property on a non-public type.</summary>
    public sealed record SrtSenderOption(string Label, AtemDeviceInfo? Atem);

    /// <summary>
    /// Seeds the sender list with the ATEM the app is already set up to control, so the common case -
    /// "receive the ON AIR output of the ATEM I just selected" - is one click and needs no scan.
    /// </summary>
    private void RebuildSrtSenders()
    {
        var previous = SelectedAtem()?.Ip;
        var discovered = _srtSenders.Where(s => s.Atem is not null && s.Atem.Ip != ConfiguredAtem()?.Ip).ToList();

        _srtSenders.Clear();
        _srtSenders.Add(new SrtSenderOption("Something else — I will set the URL myself", null));

        if (ConfiguredAtem() is { } configured)
        {
            _srtSenders.Add(new SrtSenderOption($"{configured.Name} — {configured.Ip}  (selected ATEM)", configured));
        }

        foreach (var option in discovered)
        {
            _srtSenders.Add(option);
        }

        SrtAtemCombo.SelectedItem = _srtSenders.FirstOrDefault(s => s.Atem?.Ip == previous) ?? _srtSenders[0];
    }

    /// <summary>The ATEM chosen in the ATEM settings window, if one is chosen.</summary>
    private AtemDeviceInfo? ConfiguredAtem()
    {
        var config = _orchestrator.CurrentAtemConfig;
        return string.IsNullOrWhiteSpace(config.Ip)
            ? null
            : new AtemDeviceInfo(config.Ip, config.Name ?? "ATEM");
    }

    private AtemDeviceInfo? SelectedAtem() => (SrtAtemCombo.SelectedItem as SrtSenderOption)?.Atem;

    private void OnSrtAtemChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
        {
            return;
        }

        if (SelectedAtem() is null)
        {
            SrtAtemStatusText.Text = string.Empty;
            SrtConfigureAtemButton.IsEnabled = false;
            return;
        }

        ApplyAtemPreset();
    }

    /// <summary>
    /// Points this side at the ATEM: the PC waits (Listener) on its SRT port and the ATEM dials in.
    /// Listener is the right way round even though the ATEM is the sender — the PC has the stable
    /// address here, and it is the only mode that survives the ATEM being power-cycled mid-show.
    /// </summary>
    private void ApplyAtemPreset()
    {
        if (SelectedAtem() is not { } atem)
        {
            return;
        }

        var port = _lastSrtSetup?.ListenerPort ?? ProtocolConstants.SrtListenPort;
        SrtModeCombo.SelectedIndex = 0;   // Listener
        SrtUrlBox.Text = SrtUrl.ForMode($"srt://0.0.0.0:{port}", listener: true);

        if (string.IsNullOrWhiteSpace(NewSourceNameBox.Text))
        {
            NewSourceNameBox.Text = AtemProgramSourceName;
        }

        // "Point the ATEM here" talks over the control connection, which only exists for the ATEM the
        // app is connected to - a switcher merely found by a scan has to be selected first.
        var connected = _orchestrator.CurrentAtemConfig is { Enabled: true } config &&
                        string.Equals(config.Ip, atem.Ip, StringComparison.Ordinal);
        SrtConfigureAtemButton.IsEnabled = connected;

        SrtAtemStatusText.Text = connected
            ? $"This PC will wait for {atem.Name} on port {port}. Set that address in the ATEM's streaming settings, or press \"Point the ATEM here\"."
            : $"This PC will wait for {atem.Name} on port {port}. Select this ATEM in the ATEM window to let the app configure it for you.";
    }

    private async void OnScanAtemsClick(object sender, RoutedEventArgs e)
    {
        SrtScanAtemButton.IsEnabled = false;
        SrtAtemStatusText.Text = "Scanning for ATEMs...";

        IReadOnlyList<AtemDeviceInfo> found;
        try
        {
            found = await _orchestrator.DiscoverAtemDevicesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ATEM discovery failed.");
            SrtAtemStatusText.Text = "Scan failed.";
            SrtScanAtemButton.IsEnabled = true;
            return;
        }

        foreach (var device in found.Where(d => _srtSenders.All(s => s.Atem?.Ip != d.Ip)))
        {
            _srtSenders.Add(new SrtSenderOption($"{device.Name} — {device.Ip}", device));
        }

        SrtAtemStatusText.Text = found.Count == 0
            ? "No ATEM answered. Check that it is powered on and on this network."
            : $"{found.Count} ATEM(s) found — pick one above.";
        SrtScanAtemButton.IsEnabled = true;
    }

    private async void OnConfigureAtemStreamingClick(object sender, RoutedEventArgs e)
    {
        // The address the ATEM is given is the one showing in the box - the operator may have picked a
        // different NIC of this PC, or typed an address the scan cannot know.
        var url = SenderUrl();
        if (url.Length == 0)
        {
            SrtAtemStatusText.Text = "Still working out this PC's address — try again in a moment.";
            return;
        }

        SrtConfigureAtemButton.IsEnabled = false;
        try
        {
            var applied = await _orchestrator
                .ConfigureAtemStreamingAsync(new AtemStreamingRequest(url))
                .ConfigureAwait(true);

            SrtAtemStatusText.Text = applied
                ? $"Told the ATEM to stream to {url}. Add the source, then check the ATEM went on air."
                : "No ATEM is connected — select one in the ATEM window first.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Configuring ATEM streaming failed.");
            SrtAtemStatusText.Text = "Could not configure the ATEM's streaming output.";
        }
        finally
        {
            SrtConfigureAtemButton.IsEnabled = true;
        }
    }

    private void OnUseRecommendedSrtUrlClick(object sender, RoutedEventArgs e)
    {
        if (_lastSrtSetup is { } setup)
        {
            // RecommendedUrl is the address the *sender* dials (this PC's LAN IP). Pasting it here in
            // Listener mode would put the PC in caller mode against its own IP, which connects to
            // nothing; what this side needs is the matching bind URL.
            SrtUrlBox.Text = SrtListenerModeSelected()
                ? SrtUrl.ForMode($"srt://0.0.0.0:{setup.ListenerPort}", listener: true)
                : setup.RecommendedUrl;
        }
    }

    private void OnBrowseImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.tga;*.psd|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            ImagePathBox.Text = dialog.FileName;
        }
    }

    private void OnBrowseHtmlClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an HTML file",
            Filter = "HTML|*.html;*.htm|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            HtmlUrlBox.Text = dialog.FileName;
            HtmlLocalFileCheck.IsChecked = true;
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var name = NewSourceNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(this, "Give the source a name first.", "Add source",
                MessageBoxButton.OK, MessageBoxImage.Information);
            NewSourceNameBox.Focus();
            return;
        }

        var type = SelectedSourceTypeText();
        var id = GenerateId(name);
        SourceDefinition? definition = type switch
        {
            "WEBCAM" => DeviceCombo.SelectedItem is DeviceInfo webcam
                ? new SourceDefinition(id, name, SourceType.Webcam, null, new WebcamConfig(webcam.Id, SelectedWebcamFormat()), null)
                : null,
            "NDI" => DeviceCombo.SelectedItem is DeviceInfo ndi
                ? new SourceDefinition(id, name, SourceType.Ndi, new NdiConfig(ndi.Id), null, null)
                : null,
            "SRT" => BuildSrtDefinition(id, name),
            "IMAGE" => BuildImageDefinition(id, name),
            "HTML" => BuildHtmlDefinition(id, name),
            _ => null,
        };

        if (definition is null)
        {
            System.Windows.MessageBox.Show(this,
                "Fill in step 3 first: pick a device, or enter an SRT URL / image file / web address.",
                "Add source", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result = definition;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private string? SelectedWebcamFormat()
    {
        var index = WebcamFormatCombo.SelectedIndex;
        return index >= 0 && index < WebcamCaptureModes.Length ? WebcamCaptureModes[index].Format : null;
    }

    private SourceDefinition? BuildSrtDefinition(string id, string name)
    {
        var listener = SrtListenerModeSelected();
        var url = SrtUrlBox.Text.Trim();

        // A Listener has nothing to dial, so an empty box is a complete answer: it binds the default
        // port. A Caller does need a target address.
        if (string.IsNullOrWhiteSpace(url) && !listener)
        {
            return null;
        }

        var latency = int.TryParse(SrtLatencyBox.Text, out var parsed) ? parsed : DeviceQueryDefaultLatencyMs;
        return new SourceDefinition(id, name, SourceType.Srt, null, null, new SrtConfig(SrtUrl.ForMode(url, listener), latency));
    }

    private bool SrtListenerModeSelected() => SrtModeCombo.SelectedIndex != 1;   // 0 = Listener, 1 = Caller

    private SourceDefinition? BuildImageDefinition(string id, string name)
    {
        var path = ImagePathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!System.IO.File.Exists(path))
        {
            System.Windows.MessageBox.Show(this, $"No file at \"{path}\".", "Add source",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        return new SourceDefinition(id, name, SourceType.Image, null, null, null, new ImageConfig(path), null);
    }

    private SourceDefinition? BuildHtmlDefinition(string id, string name)
    {
        var url = HtmlUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var isLocalFile = HtmlLocalFileCheck.IsChecked == true;
        if (isLocalFile && !System.IO.File.Exists(url))
        {
            System.Windows.MessageBox.Show(this, $"No file at \"{url}\".", "Add source",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var width = int.TryParse(HtmlWidthBox.Text, out var w) && w > 0 ? w : EngineDefaults.CanvasWidth;
        var height = int.TryParse(HtmlHeightBox.Text, out var h) && h > 0 ? h : EngineDefaults.CanvasHeight;
        var fps = int.TryParse(HtmlFpsBox.Text, out var f) && f > 0 ? f : 30;

        return new SourceDefinition(
            id, name, SourceType.Html, null, null, null, null,
            new HtmlConfig(url, width, height, isLocalFile, fps, Css: null));
    }

    private string SelectedSourceTypeText() =>
        (NewSourceTypeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "WEBCAM";

    private DeviceQueryType? SelectedDeviceQueryType() => SelectedSourceTypeText() switch
    {
        "WEBCAM" => DeviceQueryType.Webcam,
        "NDI" => DeviceQueryType.Ndi,
        _ => null,
    };

    /// <summary>Kebab-cased short id derived from the operator's name plus 8 random hex chars so two
    /// sources named the same still get distinct ids. Shared with the mix editor.</summary>
    internal static string GenerateId(string name)
    {
        var slug = new string([.. name.Where(char.IsLetterOrDigit)]).ToLowerInvariant();
        if (slug.Length == 0)
        {
            slug = "src";
        }
        else if (slug.Length > 16)
        {
            slug = slug[..16];
        }

        return $"{slug}-{Guid.NewGuid():N}"[..(slug.Length + 9)];
    }
}
