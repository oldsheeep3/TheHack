using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
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

    private readonly IDeviceQueryService _deviceQueryService;
    private readonly AppConfig _config;
    private readonly ILogger _logger;
    private readonly ObservableCollection<DeviceInfo> _devices = [];

    private SrtSetupInfo? _lastSrtSetup;
    private bool _uiReady;

    public AddSourceWindow(IDeviceQueryService deviceQueryService, AppConfig config, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(deviceQueryService);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        InitializeComponent();

        _deviceQueryService = deviceQueryService;
        _config = config;
        _logger = logger;

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
        SrtRecommendedUrlBox.Text = info.RecommendedUrl;
        SrtHostsList.ItemsSource = info.HostCandidates;
        SrtLatencyBox.Text = info.RecommendedLatencyMs.ToString(CultureInfo.InvariantCulture);
        SrtAtemText.Text =
            $"ATEM Mini host: {_config.AtemIp}. Set the ATEM's streaming output to Caller and enter the URL above.";
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
