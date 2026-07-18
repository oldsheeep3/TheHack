using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.App.Display;
using Switcher.App.Multiview;
using Switcher.App.Orchestration;
using Switcher.App.Rendering;
using Switcher.App.Services;
using Switcher.App.ViewModels;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.Media;
using Switcher.VirtualCam;
using Switcher.VirtualCam.Display;
using Forms = System.Windows.Forms;

namespace Switcher.App;

/// <summary>
/// Main operator window (docs/specs/multiview-output-revision.md): configurable multiview with
/// rectangular merge (requirement 3) + independent full-screen (requirement 4), 3-stage source add with
/// device enumeration + SRT setup helper (requirement 6/7), output routing with HDMI display picker
/// (requirement 1) and NDI sinks (requirement 5), operator-display move (requirement 2), module mapping,
/// and PGM1/PGM2/PVW1/PVW2 tally. All PGM/PVW-affecting actions go through <see cref="AppOrchestrator"/>;
/// the same-screen warning and rectangular-merge rules live in UI-independent, unit-tested classes
/// (<see cref="DisplayConflictEvaluator"/> / <see cref="MultiviewRegionModel"/>).
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppOrchestrator _orchestrator;
    private readonly IInputSourceManager _sourceManager;
    private readonly AtemController _atemController;
    private readonly FramePumpService _framePump;
    private readonly OutputRouter _outputRouter;
    private readonly IHdmiFullscreenOutput _hdmiOutput;
    private readonly CompositorEngine _compositor;
    private readonly IFullscreenPresenterFactory _presenterFactory;
    private readonly IDeviceQueryService _deviceQueryService;
    private readonly AppConfig _config;
    private readonly ILogger<MainWindow> _logger;

    private readonly ObservableCollection<SourceTileViewModel> _tiles = [];
    private readonly ObservableCollection<string> _availableTokens = [];
    private readonly ObservableCollection<string> _availableSourceIds = [];
    private readonly ObservableCollection<MultiviewCellViewModel> _cells = [];
    private readonly ObservableCollection<OutputAssignmentRowViewModel> _outputRows = [];
    private readonly ObservableCollection<ModuleMappingRowViewModel> _moduleRows = [];
    private readonly ObservableCollection<DisplayOption> _availableDisplays = [];
    private readonly ObservableCollection<DeviceInfo> _devices = [];
    private readonly Dictionary<string, WriteableBitmap> _cellBitmaps = [];
    private readonly MultiviewRegionModel _regionModel = new();

    private ProjectorWindow? _projectorWindow;
    private MultiviewFullscreenWindow? _multiviewFullscreen;
    private SrtSetupInfo? _lastSrtSetup;
    private int _operatorDisplayIndex;
    private bool _suppressMultiviewApply;
    private bool _isDraggingSelection;
    private bool _uiReady;

    public MainWindow(
        AppOrchestrator orchestrator,
        IInputSourceManager sourceManager,
        AtemController atemController,
        FramePumpService framePump,
        OutputRouter outputRouter,
        IHdmiFullscreenOutput hdmiOutput,
        CompositorEngine compositor,
        IFullscreenPresenterFactory presenterFactory,
        IDeviceQueryService deviceQueryService,
        AppConfig config,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();

        _orchestrator = orchestrator;
        _sourceManager = sourceManager;
        _atemController = atemController;
        _framePump = framePump;
        _outputRouter = outputRouter;
        _hdmiOutput = hdmiOutput;
        _compositor = compositor;
        _presenterFactory = presenterFactory;
        _deviceQueryService = deviceQueryService;
        _config = config;
        _logger = logger;
        _operatorDisplayIndex = config.OperatorDisplayIndex;

        SourceTilesControl.ItemsSource = _tiles;
        MultiviewControl.ItemsSource = _cells;
        OutputsControl.ItemsSource = _outputRows;
        ModulesControl.ItemsSource = _moduleRows;
        DeviceCombo.ItemsSource = _devices;
        OperatorDisplayCombo.ItemsSource = _availableDisplays;
        MultiviewFullscreenDisplayCombo.ItemsSource = _availableDisplays;

        RefreshDisplays();

        foreach (var source in _sourceManager.GetSources())
        {
            _tiles.Add(new SourceTileViewModel(source));
        }

        RefreshAvailableTokensAndIds();
        LoadMultiviewLayout(_orchestrator.CurrentMultiviewLayout);

        foreach (var sink in new[] { OutputSink.Vcam1, OutputSink.Vcam2, OutputSink.Hdmi, OutputSink.Ndi1, OutputSink.Ndi2 })
        {
            var row = new OutputAssignmentRowViewModel(sink, _availableDisplays);
            var current = _outputRouter.CurrentAssignments.FirstOrDefault(a => a.Sink == sink);
            if (current is not null)
            {
                row.LoadFrom(current);
            }

            _outputRows.Add(row);
        }

        var mappingsByIndex = _orchestrator.CurrentModuleMappings.ToDictionary(m => m.Index);
        for (var i = 0; i < ProtocolConstants.MaxModules; i++)
        {
            var row = new ModuleMappingRowViewModel(i, _availableSourceIds);
            if (mappingsByIndex.TryGetValue(i, out var existing))
            {
                row.LoadFrom(existing);
            }

            _moduleRows.Add(row);
        }

        _sourceManager.SourceStatusChanged += OnSourceStatusChanged;
        _atemController.ConnectionStateChanged += OnAtemConnectionStateChanged;
        _orchestrator.TallyChangedV2 += OnTallyChangedV2;
        _orchestrator.MultiviewChanged += OnMultiviewChanged;
        _framePump.Tick += OnFramePumpTick;

        AtemStateText.Text = _atemController.State.ToString();

        SelectOperatorDisplay();
        NewSourceTypeCombo.SelectedIndex = 0;
        _uiReady = true;
        OnSourceTypeChanged(this, null!);

        Loaded += (_, _) => PositionOnDisplay(_operatorDisplayIndex, activate: false);

        Unloaded += (_, _) =>
        {
            _sourceManager.SourceStatusChanged -= OnSourceStatusChanged;
            _atemController.ConnectionStateChanged -= OnAtemConnectionStateChanged;
            _orchestrator.TallyChangedV2 -= OnTallyChangedV2;
            _orchestrator.MultiviewChanged -= OnMultiviewChanged;
            _framePump.Tick -= OnFramePumpTick;
        };
    }

    private void RefreshDisplays()
    {
        var screens = Forms.Screen.AllScreens;
        _availableDisplays.Clear();
        for (var i = 0; i < screens.Length; i++)
        {
            var bounds = screens[i].Bounds;
            _availableDisplays.Add(new DisplayOption(i, bounds.Width, bounds.Height, screens[i].Primary));
        }
    }

    private void SelectOperatorDisplay()
    {
        OperatorDisplayCombo.SelectedItem = _availableDisplays.FirstOrDefault(d => d.Index == _operatorDisplayIndex);
        MultiviewFullscreenDisplayCombo.SelectedItem =
            _availableDisplays.FirstOrDefault(d => d.Index != _operatorDisplayIndex) ??
            _availableDisplays.FirstOrDefault();
    }

    private void RefreshAvailableTokensAndIds()
    {
        _availableTokens.Clear();
        foreach (var token in new[] { "EMPTY", "PGM1", "PGM2", "PVW1", "PVW2" })
        {
            _availableTokens.Add(token);
        }

        _availableSourceIds.Clear();
        _availableSourceIds.Add(ModuleMappingRowViewModel.NoneSourceId);

        foreach (var tile in _tiles)
        {
            if (tile.Id is { } id)
            {
                _availableTokens.Add($"SRC:{id}");
                _availableSourceIds.Add(id);
            }
        }
    }

    private void LoadMultiviewLayout(MultiviewLayout layout)
    {
        _regionModel.Load(layout);
        RebuildMultiviewCells();
    }

    private void RebuildMultiviewCells()
    {
        _suppressMultiviewApply = true;
        _cells.Clear();
        foreach (var region in _regionModel.Regions)
        {
            var token = _availableTokens.Contains(region.Content) ? region.Content : "EMPTY";
            _cells.Add(new MultiviewCellViewModel(region.Row, region.Col, region.RowSpan, region.ColSpan, token, _availableTokens));
        }

        _isDraggingSelection = false;
        _suppressMultiviewApply = false;
    }

    private void OnSourceStatusChanged(object? sender, SourceInfo info) =>
        Dispatcher.BeginInvoke(() =>
        {
            var tile = _tiles.FirstOrDefault(t => t.Channel == info.Channel);
            if (tile is null)
            {
                _tiles.Add(new SourceTileViewModel(info));
            }
            else
            {
                tile.Update(info);
            }

            RefreshAvailableTokensAndIds();
        });

    private void OnAtemConnectionStateChanged(object? sender, AtemConnectionState state) =>
        Dispatcher.BeginInvoke(() => AtemStateText.Text = state.ToString());

    private void OnTallyChangedV2(object? sender, TallyStateV2 state) =>
        Dispatcher.BeginInvoke(() =>
        {
            ActivePgm1Text.Text = Format(state.ActivePgm1);
            ActivePgm2Text.Text = Format(state.ActivePgm2);
            ActivePvw1Text.Text = Format(state.ActivePvw1);
            ActivePvw2Text.Text = Format(state.ActivePvw2);
        });

    private static string Format(IReadOnlyList<int> channels) => channels.Count == 0 ? "-" : string.Join(", ", channels);

    private void OnMultiviewChanged(object? sender, MultiviewLayout layout) =>
        Dispatcher.BeginInvoke(() => LoadMultiviewLayout(layout));

    private void OnFramePumpTick(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var cell in _cells)
            {
                if (cell.Token == "EMPTY")
                {
                    cell.Image = null;
                    continue;
                }

                var frame = _framePump.TryGetCellFrame(cell.Token);
                if (frame is not { } f)
                {
                    continue;
                }

                _cellBitmaps.TryGetValue(cell.Token, out var bitmap);
                bitmap = FrameBitmapWriter.Write(bitmap, f);
                _cellBitmaps[cell.Token] = bitmap;
                cell.Image = bitmap;
            }
        });

    // --- Multiview rectangular merge drag (requirement 3) ---

    private void OnMultiviewCellMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MultiviewCellViewModel vm })
        {
            return;
        }

        _isDraggingSelection = true;
        foreach (var cell in _cells)
        {
            cell.Selected = false;
        }

        vm.Selected = true;
    }

    private void OnMultiviewCellMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingSelection || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: MultiviewCellViewModel vm })
        {
            vm.Selected = true;
        }
    }

    private void OnMultiviewCellMouseUp(object sender, MouseButtonEventArgs e) => _isDraggingSelection = false;

    private void OnMergeSelectedClick(object sender, RoutedEventArgs e)
    {
        var cells = new List<(int Row, int Col)>();
        foreach (var vm in _cells.Where(c => c.Selected))
        {
            for (var r = vm.Row; r < vm.Row + vm.RowSpan; r++)
            {
                for (var c = vm.Col; c < vm.Col + vm.ColSpan; c++)
                {
                    cells.Add((r, c));
                }
            }
        }

        if (!_regionModel.TryMerge(cells, out var error))
        {
            System.Windows.MessageBox.Show(error, "Multiview merge", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ApplyMultiviewFromModel();
    }

    private void OnSplitSelectedClick(object sender, RoutedEventArgs e)
    {
        var targets = _cells.Where(c => c.Selected).Select(c => (c.Row, c.Col)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var (row, col) in targets)
        {
            _regionModel.Split(row, col);
        }

        ApplyMultiviewFromModel();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        _isDraggingSelection = false;
        foreach (var cell in _cells)
        {
            cell.Selected = false;
        }
    }

    private void ApplyMultiviewFromModel()
    {
        var layout = _regionModel.ToLayout();
        RebuildMultiviewCells();
        _ = _orchestrator.ApplyMultiviewAsync(layout);
    }

    private void OnMultiviewCellSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMultiviewApply || sender is not FrameworkElement { DataContext: MultiviewCellViewModel vm })
        {
            return;
        }

        _regionModel.SetContent(vm.Row, vm.Col, vm.Token);
        _ = _orchestrator.ApplyMultiviewAsync(_regionModel.ToLayout());
    }

    // --- Multiview full-screen (requirement 4) ---

    private void OnMultiviewFullscreenClick(object sender, RoutedEventArgs e)
    {
        if (MultiviewFullscreenDisplayCombo.SelectedItem is not DisplayOption display)
        {
            return;
        }

        OpenMultiviewFullscreen(display.Index);
    }

    /// <summary>Opens (or re-opens) the independent multiview full-screen window on the given display,
    /// after the same-screen warning if it targets the operator console (requirement 4).</summary>
    public void OpenMultiviewFullscreen(int displayIndex)
    {
        if (!ConfirmDisplayConflict(displayIndex))
        {
            return;
        }

        _multiviewFullscreen?.Close();
        _multiviewFullscreen = new MultiviewFullscreenWindow(
            _presenterFactory,
            _compositor,
            _framePump,
            () => _regionModel.ToLayout(),
            displayIndex);
        _multiviewFullscreen.Closed += (_, _) => _multiviewFullscreen = null;
        _multiviewFullscreen.ShowFullscreen();
    }

    // --- Operator display move (requirement 2) ---

    private void OnMoveOperatorClick(object sender, RoutedEventArgs e)
    {
        if (OperatorDisplayCombo.SelectedItem is DisplayOption display)
        {
            MoveOperatorToDisplay(display.Index);
        }
    }

    /// <summary>Moves the operator console to the given display and persists the choice (requirement 2).</summary>
    public void MoveOperatorToDisplay(int displayIndex)
    {
        _operatorDisplayIndex = displayIndex;
        PositionOnDisplay(displayIndex, activate: true);
        SelectOperatorDisplay();
        AppConfigLoader.Save(AppContext.BaseDirectory, _config with { OperatorDisplayIndex = displayIndex }, _logger);
    }

    private void PositionOnDisplay(int displayIndex, bool activate)
    {
        var screens = Forms.Screen.AllScreens;
        if (displayIndex < 0 || displayIndex >= screens.Length)
        {
            return;
        }

        var bounds = screens[displayIndex].WorkingArea;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        Width = Math.Min(Width, bounds.Width);
        Height = Math.Min(Height, bounds.Height);
        Left = bounds.Left + ((bounds.Width - Width) / 2);
        Top = bounds.Top + ((bounds.Height - Height) / 2);

        if (activate)
        {
            Show();
            Activate();
        }
    }

    private bool ConfirmDisplayConflict(int displayId)
    {
        if (!DisplayConflictEvaluator.ConflictsWithOperator(_operatorDisplayIndex, displayId))
        {
            return true;
        }

        var result = System.Windows.MessageBox.Show(
            DisplayConflictEvaluator.BuildWarningMessage(displayId),
            "Switcher.App",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.OK;
    }

    // --- Source add (requirement 6/7) ---

    private void OnSourceTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
        {
            return;
        }

        var type = SelectedSourceTypeText();
        var isSrt = type == "SRT";
        DeviceSelectPanel.Visibility = isSrt ? Visibility.Collapsed : Visibility.Visible;
        SrtPanel.Visibility = isSrt ? Visibility.Visible : Visibility.Collapsed;

        if (isSrt)
        {
            _ = LoadSrtSetupAsync();
        }
        else
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
        SrtLatencyBox.Text = info.RecommendedLatencyMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SrtAtemText.Text =
            $"ATEM Mini host: {_config.AtemIp}. Set the ATEM's streaming output to Caller and enter the URL above.";
    }

    private void OnUseRecommendedSrtUrlClick(object sender, RoutedEventArgs e)
    {
        if (_lastSrtSetup is { } setup)
        {
            SrtUrlBox.Text = setup.RecommendedUrl;
        }
    }

    private async void OnAddSourceClick(object sender, RoutedEventArgs e)
    {
        var name = NewSourceNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var type = SelectedSourceTypeText();
        var id = GenerateId(name);
        SourceDefinition? definition = type switch
        {
            "WEBCAM" => DeviceCombo.SelectedItem is DeviceInfo webcam
                ? new SourceDefinition(id, name, SourceType.Webcam, null, new WebcamConfig(webcam.Id, webcam.Formats?.FirstOrDefault()), null)
                : null,
            "NDI" => DeviceCombo.SelectedItem is DeviceInfo ndi
                ? new SourceDefinition(id, name, SourceType.Ndi, new NdiConfig(ndi.Id), null, null)
                : null,
            "SRT" => BuildSrtDefinition(id, name),
            _ => null,
        };

        if (definition is null)
        {
            System.Windows.MessageBox.Show("Select a device (or enter an SRT URL) before adding the source.", "Add source", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await _orchestrator.AddSourceAsync(definition).ConfigureAwait(true);
        RefreshAvailableTokensAndIds();
        NewSourceNameBox.Clear();
    }

    private SourceDefinition? BuildSrtDefinition(string id, string name)
    {
        var url = SrtUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var latency = int.TryParse(SrtLatencyBox.Text, out var parsed) ? parsed : DeviceQueryDefaultLatencyMs;
        return new SourceDefinition(id, name, SourceType.Srt, null, null, new SrtConfig(url, latency));
    }

    private const int DeviceQueryDefaultLatencyMs = 40;

    private string SelectedSourceTypeText() =>
        (NewSourceTypeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "WEBCAM";

    private DeviceQueryType? SelectedDeviceQueryType() => SelectedSourceTypeText() switch
    {
        "WEBCAM" => DeviceQueryType.Webcam,
        "NDI" => DeviceQueryType.Ndi,
        _ => null,
    };

    private static string GenerateId(string name)
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

    private async void OnRemoveSourceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string id })
        {
            return;
        }

        await _orchestrator.RemoveSourceAsync(id).ConfigureAwait(true);

        var tile = _tiles.FirstOrDefault(t => t.Id == id);
        if (tile is not null)
        {
            _tiles.Remove(tile);
        }

        RefreshAvailableTokensAndIds();
    }

    // --- Outputs (requirement 1/5) ---

    private async void OnApplyOutputsClick(object sender, RoutedEventArgs e)
    {
        var hdmi = _outputRows.FirstOrDefault(r => r.IsHdmi);
        if (hdmi is not null && !ConfirmDisplayConflict(hdmi.DisplayId))
        {
            return;
        }

        var request = new OutputsRequest(_outputRows.Select(r => r.ToOutputAssignment()).ToList());
        try
        {
            await _orchestrator.ApplyOutputsAsync(request).ConfigureAwait(true);
        }
        catch (ArgumentException ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Apply outputs", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnApplyModulesClick(object sender, RoutedEventArgs e)
    {
        var request = new ModulesRequest(_moduleRows.Select(r => r.ToModuleMapping()).ToList());
        await _orchestrator.ApplyModulesAsync(request).ConfigureAwait(true);
    }

    private void OnOpenProjectorClick(object sender, RoutedEventArgs e)
    {
        if (_projectorWindow is null)
        {
            _projectorWindow = new ProjectorWindow(_hdmiOutput, _outputRouter, _config);
            _projectorWindow.Closed += (_, _) => _projectorWindow = null;
        }

        _projectorWindow.ShowOnConfiguredDisplay();
    }
}
