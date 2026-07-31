using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.App.ViewModels;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.Hid.Input;

// AppConfig has a legacy type of the same name (the static appsettings mappings); this window only
// ever deals in the wire contract the orchestrator persists.
using AtemButtonMapping = Switcher.Contracts.AtemButtonMapping;

namespace Switcher.App;

/// <summary>
/// Everything about the ATEM in one place (docs/specs/pc-switcher-app.md §2.8): which switcher to
/// control, what the physical modules do to it, and how to get its ON AIR program back into this PC.
///
/// The three sit together because they are one setup job with one shared answer — the switcher's
/// address. Picking it from a scan rather than typing it is the point of the first section: an ATEM
/// answers the connection hello on UDP 9910, so a sweep of the local subnets finds it and reads its
/// model name, and the operator picks it off a list.
/// </summary>
public partial class AtemSettingsWindow : Window
{
    /// <summary>Rows to offer when no controller is attached, so a show can be configured beforehand.</summary>
    private const int OfflineModuleRowCount = 4;

    private readonly AppOrchestrator _orchestrator;
    private readonly AtemController _atemController;
    private readonly IDeviceQueryService _deviceQueryService;
    private readonly ILogger _logger;

    private readonly ObservableCollection<AtemDeviceInfo> _devices = [];
    private readonly ObservableCollection<AtemAssignmentRowViewModel> _assignments = [];

    private string? _selectedDeviceName;

    public AtemSettingsWindow(
        AppOrchestrator orchestrator,
        AtemController atemController,
        IDeviceQueryService deviceQueryService,
        AppConfig config,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(atemController);
        ArgumentNullException.ThrowIfNull(deviceQueryService);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        InitializeComponent();

        _orchestrator = orchestrator;
        _atemController = atemController;
        _deviceQueryService = deviceQueryService;
        _logger = logger;

        DevicesList.ItemsSource = _devices;
        AssignmentsControl.ItemsSource = _assignments;

        var current = orchestrator.CurrentAtemConfig;

        // An address has to start somewhere: the appsettings value is a hint for a first run, not a
        // connection - nothing is dialled until the operator ticks the box and applies.
        IpBox.Text = string.IsNullOrWhiteSpace(current.Ip) ? config.AtemIp : current.Ip;
        EnabledCheck.IsChecked = current.Enabled;
        _selectedDeviceName = current.Name;

        BuildAssignmentRows(current);
        RefreshConnectionState();

        _atemController.ConnectionStateChanged += OnConnectionStateChanged;
        _atemController.ProductNameChanged += OnProductNameChanged;
        Unloaded += (_, _) =>
        {
            _atemController.ConnectionStateChanged -= OnConnectionStateChanged;
            _atemController.ProductNameChanged -= OnProductNameChanged;
        };

        _ = LoadStreamUrlAsync();
    }

    // ── section 1: which switcher ───────────────────────────────────────────────

    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ScanStatusText.Text = "Scanning...";
        NoDevicesText.Visibility = Visibility.Collapsed;

        IReadOnlyList<AtemDeviceInfo> found;
        try
        {
            found = await _orchestrator.DiscoverAtemDevicesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ATEM discovery failed.");
            ScanStatusText.Text = "Scan failed.";
            ScanButton.IsEnabled = true;
            return;
        }

        _devices.Clear();
        foreach (var device in found)
        {
            _devices.Add(device);
        }

        ScanStatusText.Text = found.Count switch
        {
            0 => "None found.",
            1 => "1 ATEM found.",
            _ => $"{found.Count} ATEMs found.",
        };
        NoDevicesText.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.IsEnabled = true;

        // One switcher is the normal case; pre-selecting it saves the extra click.
        if (found.Count == 1)
        {
            DevicesList.SelectedIndex = 0;
        }
    }

    private void OnDeviceSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DevicesList.SelectedItem is AtemDeviceInfo device)
        {
            IpBox.Text = device.Ip;
            _selectedDeviceName = device.Name;
            EnabledCheck.IsChecked = true;
        }
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        var ip = IpBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            StatusText.Text = "Enter an address first.";
            return;
        }

        StatusText.Text = $"Checking {ip}...";
        AtemDeviceInfo? found;
        try
        {
            found = await _orchestrator.ProbeAtemDeviceAsync(ip).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ATEM probe of {Ip} failed.", ip);
            StatusText.Text = $"Could not reach {ip}.";
            return;
        }

        if (found is null)
        {
            StatusText.Text = $"No ATEM answered at {ip}.";
            return;
        }

        _selectedDeviceName = found.Name;
        StatusText.Text = $"{found.Name} answered at {found.Ip}.";
    }

    // ── section 2: controller assignments ───────────────────────────────────────

    /// <summary>
    /// One row per switch of every module worth showing: the modules the controller currently reports,
    /// plus any the saved config mentions (so a mapping never silently disappears when its module is
    /// unplugged), falling back to a few rows when nothing is attached at all.
    /// </summary>
    private void BuildAssignmentRows(AtemConfig config)
    {
        var indices = new SortedSet<int>(_orchestrator.CurrentModuleMappings.Select(m => m.Index));
        foreach (var mapping in config.Mappings)
        {
            indices.Add(mapping.ModuleIndex);
        }

        if (indices.Count == 0)
        {
            for (var i = 0; i < OfflineModuleRowCount; i++)
            {
                indices.Add(i);
            }
        }

        _assignments.Clear();
        foreach (var index in indices)
        {
            foreach (var switchId in Enum.GetValues<SwitchId>())
            {
                var row = new AtemAssignmentRowViewModel(index, switchId);
                var saved = config.Mappings.FirstOrDefault(m =>
                    m.ModuleIndex == index &&
                    string.Equals(m.Switch, switchId.ToString(), StringComparison.OrdinalIgnoreCase));

                if (saved is not null)
                {
                    row.LoadFrom(saved);
                }

                _assignments.Add(row);
            }
        }
    }

    // ── section 3: ON AIR back into this PC ─────────────────────────────────────

    private async Task LoadStreamUrlAsync()
    {
        try
        {
            var setup = await _deviceQueryService.GetSrtSetupAsync().ConfigureAwait(true);
            StreamUrlBox.Text = setup.RecommendedUrl;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SRT setup lookup failed.");
            StreamUrlBox.Text = string.Empty;
        }
    }

    private async void OnConfigureStreamingClick(object sender, RoutedEventArgs e)
    {
        var url = StreamUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText.Text = "No SRT address to give the ATEM yet.";
            return;
        }

        ConfigureStreamButton.IsEnabled = false;
        try
        {
            var applied = await _orchestrator.ConfigureAtemStreamingAsync(new AtemStreamingRequest(url))
                .ConfigureAwait(true);

            StatusText.Text = applied
                ? $"Told the ATEM to stream to {url}."
                : "No ATEM is connected — apply a connection first.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Configuring ATEM streaming failed.");
            StatusText.Text = "Could not configure the ATEM's streaming output.";
        }
        finally
        {
            ConfigureStreamButton.IsEnabled = true;
        }
    }

    // ── apply / close ───────────────────────────────────────────────────────────

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var ip = IpBox.Text.Trim();
        var enabled = EnabledCheck.IsChecked == true;

        if (enabled && string.IsNullOrWhiteSpace(ip))
        {
            System.Windows.MessageBox.Show(this, "Pick an ATEM or enter its address first.", "ATEM",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mappings = _assignments
            .Select(row => row.ToMapping())
            .OfType<AtemButtonMapping>()
            .ToList();

        try
        {
            await _orchestrator.ApplyAtemConfigAsync(new AtemConfig(enabled, ip, mappings, _selectedDeviceName))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying the ATEM configuration failed.");
            System.Windows.MessageBox.Show(this, ex.Message, "ATEM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusText.Text = enabled
            ? $"Connecting to {ip}. {mappings.Count} switch assignment(s) saved."
            : $"Saved. {mappings.Count} switch assignment(s); not connecting.";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ── live state ──────────────────────────────────────────────────────────────

    private void OnConnectionStateChanged(object? sender, AtemConnectionState state) =>
        Dispatcher.BeginInvoke(RefreshConnectionState);

    private void OnProductNameChanged(object? sender, string productName) =>
        Dispatcher.BeginInvoke(() =>
        {
            _selectedDeviceName = productName;
            RefreshConnectionState();
        });

    private void RefreshConnectionState()
    {
        var name = _atemController.ProductName ?? _selectedDeviceName;
        ConnectionStateText.Text = _atemController.State switch
        {
            AtemConnectionState.Connected when name is not null => $"CONNECTED — {name}".ToUpperInvariant(),
            AtemConnectionState.Connected => "CONNECTED",
            AtemConnectionState.Connecting => "CONNECTING...",
            _ => "NOT CONNECTED",
        };
    }
}
