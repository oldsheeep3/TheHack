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
using Forms = System.Windows.Forms;

namespace Switcher.App;

/// <summary>
/// Operator console, laid out as a studio dock (design direction B): both M/E buses show their own
/// preview/program monitor pair simultaneously (splittable rows so the operator picks how much room each
/// gets), a shared transition bar underneath, a read-only multiview alongside, and four titled docks
/// along the bottom — Sources, Buses, Outputs, Controller.
///
/// Arranging the multiview moved out to <see cref="MultiviewSettingsWindow"/>: it is a setup-time job,
/// and keeping the merge/split editor on the live screen cost the program monitors the space they need.
/// This window only renders the layout, framing each cell red when its content is on air and green when
/// it is staged (<see cref="MultiviewTally"/>), which the native engine mirrors on the multiview output.
///
/// All PGM/PVW-affecting actions still go through <see cref="AppOrchestrator"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppOrchestrator _orchestrator;
    private readonly IVideoEngine _engine;
    private readonly AtemController _atemController;
    private readonly FramePumpService _framePump;
    private readonly IDeviceQueryService _deviceQueryService;
    /// <summary>Mutable so an operator preference change (stage view, console display) both applies now
    /// and is persisted on the next save — the field is the authoritative in-memory copy.</summary>
    private AppConfig _config;
    private readonly ILogger<MainWindow> _logger;

    private readonly ObservableCollection<SourceTileViewModel> _tiles = [];
    private readonly ObservableCollection<string> _availableTokens = [];
    private readonly ObservableCollection<string> _availableSourceIds = [];
    private readonly ObservableCollection<MultiviewCellViewModel> _cells = [];
    private readonly ObservableCollection<OutputAssignmentRowViewModel> _outputRows = [];
    private readonly ObservableCollection<AudioOutputRowViewModel> _audioRows = [];
    private readonly ObservableCollection<ModuleMappingRowViewModel> _moduleRows = [];
    private readonly ObservableCollection<DisplayOption> _availableDisplays = [];

    private readonly ObservableCollection<BusControlViewModel> _buses =
    [
        new BusControlViewModel(ProgramBus.Pgm1),
        new BusControlViewModel(ProgramBus.Pgm2),
    ];

    private readonly PreviewBitmapCache _previewBitmaps;
    private readonly MultiviewRegionModel _regionModel = new();

    private ProjectorWindow? _projectorWindow;
    private MultiviewFullscreenWindow? _multiviewFullscreen;
    private MultiviewSettingsWindow? _multiviewSettings;
    private MixSourceWindow? _mixEditor;
    private AddSourceWindow? _addSourceWindow;
    private int _operatorDisplayIndex;
    private bool _uiReady;

    /// <summary>Which M/E the transition bar and keyboard shortcuts act on. Both ME rows are visible at
    /// once, and both buses stay fully operable from the Buses dock — the "focused" ME just picks who
    /// receives Space/A/Escape/1-9 and the shared CUT/AUTO buttons.</summary>
    private ProgramBus _stageBus = ProgramBus.Pgm1;

    public MainWindow(
        AppOrchestrator orchestrator,
        IVideoEngine engine,
        AtemController atemController,
        FramePumpService framePump,
        IDeviceQueryService deviceQueryService,
        AppConfig config,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();

        _orchestrator = orchestrator;
        _engine = engine;
        _atemController = atemController;
        _framePump = framePump;
        _deviceQueryService = deviceQueryService;
        _config = config;
        _logger = logger;
        _operatorDisplayIndex = config.OperatorDisplayIndex;
        _previewBitmaps = new PreviewBitmapCache(framePump);

        SourceTilesControl.ItemsSource = _tiles;
        MultiviewControl.ItemsSource = _cells;
        OutputsControl.ItemsSource = _outputRows;
        AudioOutputsControl.ItemsSource = _audioRows;
        ModulesControl.ItemsSource = _moduleRows;
        BusesControl.ItemsSource = _buses;
        OperatorDisplayCombo.ItemsSource = _availableDisplays;

        RefreshDisplays();

        foreach (var source in _engine.GetSources())
        {
            _tiles.Add(new SourceTileViewModel(source));
        }

        RefreshAvailableTokensAndIds();
        LoadMultiviewLayout(_orchestrator.CurrentMultiviewLayout);

        foreach (var sink in new[] { OutputSink.Vcam1, OutputSink.Vcam2, OutputSink.Hdmi, OutputSink.Ndi1, OutputSink.Ndi2 })
        {
            var row = new OutputAssignmentRowViewModel(sink, _availableDisplays);
            var current = _engine.CurrentAssignments.FirstOrDefault(a => a.Sink == sink);
            if (current is not null)
            {
                row.LoadFrom(current);
            }

            _outputRows.Add(row);
        }

        RebuildAudioRows();
        RebuildModuleRows(_orchestrator.CurrentModuleMappings);

        _engine.SourceStatusChanged += OnSourceStatusChanged;
        _engine.SourceRemoved += OnSourceRemoved;
        _atemController.ConnectionStateChanged += OnAtemConnectionStateChanged;
        _orchestrator.TallyChangedV2 += OnTallyChangedV2;
        _orchestrator.MultiviewChanged += OnMultiviewChanged;
        _orchestrator.ModulesChanged += OnModulesChanged;
        _orchestrator.TallyColorsChanged += OnTallyColorsChanged;
        _framePump.Tick += OnFramePumpTick;

        TallyPalette.Apply(_orchestrator.CurrentTallyColors);
        AtemStateText.Text = $"ATEM {_atemController.State}";

        SelectOperatorDisplay();
        ApplyStageViewMode(_config.StageViewMode);
        _uiReady = true;
        RefreshBusState();

        PreviewKeyDown += OnConsoleKeyDown;

        Loaded += (_, _) => PositionOnDisplay(_operatorDisplayIndex, activate: false);

        Unloaded += (_, _) =>
        {
            _engine.SourceStatusChanged -= OnSourceStatusChanged;
            _engine.SourceRemoved -= OnSourceRemoved;
            _atemController.ConnectionStateChanged -= OnAtemConnectionStateChanged;
            _orchestrator.TallyChangedV2 -= OnTallyChangedV2;
            _orchestrator.MultiviewChanged -= OnMultiviewChanged;
            _orchestrator.ModulesChanged -= OnModulesChanged;
            _orchestrator.TallyColorsChanged -= OnTallyColorsChanged;
            _framePump.Tick -= OnFramePumpTick;
        };
    }

    // ── displays ────────────────────────────────────────────────────────────────

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

    private void SelectOperatorDisplay() =>
        OperatorDisplayCombo.SelectedItem = _availableDisplays.FirstOrDefault(d => d.Index == _operatorDisplayIndex);

    // ── source / token bookkeeping ──────────────────────────────────────────────

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

        SyncTileAudioModes();
        SourceCountText.Text = _tiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RebuildMultiviewCells();
        RefreshBusSources();
        _multiviewSettings?.ReloadSources();
    }

    private void LoadMultiviewLayout(MultiviewLayout layout)
    {
        _regionModel.Load(layout);
        RebuildMultiviewCells();
    }

    /// <summary>Rebuilds the read-only multiview dock from the region model. Unlike the editor in
    /// <see cref="MultiviewSettingsWindow"/> these cells have no combo boxes, so there is no shared
    /// ItemsSource to transiently blank and no reentrancy to guard against.</summary>
    private void RebuildMultiviewCells()
    {
        // Set the grid dimensions before the cells: the panel binds through Tag, and reassigning it
        // after the items would arrange them against the previous grid for a frame.
        MultiviewControl.Tag = new MultiviewGridSize(_regionModel.Rows, _regionModel.Cols);

        _cells.Clear();
        foreach (var region in _regionModel.Regions)
        {
            var token = _availableTokens.Contains(region.Content) ? region.Content : "EMPTY";
            var (state, bus) = MultiviewTally.ResolveDetailed(token, _orchestrator);
            _cells.Add(new MultiviewCellViewModel(region.Row, region.Col, region.RowSpan, region.ColSpan, token, _availableTokens)
            {
                Tally = state,
                TallyBus = bus,
            });
        }

        MultiviewSummaryText.Text = $"{_regionModel.Rows} x {_regionModel.Cols} · {_cells.Count} CELLS";
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

    /// <summary>Drops the tile for a source removed anywhere — including through the Web API, which the
    /// window otherwise never hears about, leaving a tile whose id no longer exists in the engine.</summary>
    private void OnSourceRemoved(object? sender, string id) =>
        Dispatcher.BeginInvoke(() =>
        {
            var tile = _tiles.FirstOrDefault(t => t.Id == id);
            if (tile is null)
            {
                return;
            }

            _tiles.Remove(tile);
            RefreshAvailableTokensAndIds();
        });

    private void OnAtemConnectionStateChanged(object? sender, AtemConnectionState state) =>
        Dispatcher.BeginInvoke(() => AtemStateText.Text = $"ATEM {state}");

    private void OnTallyChangedV2(object? sender, TallyStateV2 state) =>
        Dispatcher.BeginInvoke(() =>
        {
            ActivePgm1Text.Text = Format(state.ActivePgm1);
            ActivePgm2Text.Text = Format(state.ActivePgm2);
            ActivePvw1Text.Text = Format(state.ActivePvw1);
            ActivePvw2Text.Text = Format(state.ActivePvw2);

            // The buses can also be driven from the HID module switches and the Web API, so the docks
            // follow the published tally rather than only this window's own button presses.
            RefreshBusState();
        });

    private static string Format(IReadOnlyList<int> channels) => channels.Count == 0 ? "-" : string.Join(", ", channels);

    private void OnMultiviewChanged(object? sender, MultiviewLayout layout) =>
        Dispatcher.BeginInvoke(() =>
        {
            LoadMultiviewLayout(layout);

            // Only forward a layout the editor didn't originate, or reloading it would clobber the
            // selection the operator is mid-drag on.
            if (_multiviewSettings is { IsEditing: false } editor)
            {
                editor.ReloadLayout(layout);
            }
        });

    private void OnModulesChanged(object? sender, IReadOnlyList<ModuleMapping> mappings) =>
        Dispatcher.BeginInvoke(() => RebuildModuleRows(mappings));

    /// <summary>Republishes the palette into the application resources; every DynamicResource-bound
    /// surface restyles itself from there.</summary>
    private void OnTallyColorsChanged(object? sender, TallyColors colors) =>
        Dispatcher.BeginInvoke(() =>
        {
            TallyPalette.Apply(colors);
            RefreshBusState();

            // The audio rows are labelled in their bus colour, and TallyPalette hands out frozen
            // brushes rather than a live resource, so they have to be re-read here.
            foreach (var row in _audioRows)
            {
                row.RaiseBusBrushChanged();
            }
        });

    /// <summary>Rebuilds the Controller dock from the modules the Pico actually reports. A row exists
    /// only while its module does, so the panel never offers bindings for hardware that is unplugged.</summary>
    private void RebuildModuleRows(IReadOnlyList<ModuleMapping> mappings)
    {
        _moduleRows.Clear();
        foreach (var mapping in mappings.OrderBy(m => m.Index))
        {
            var row = new ModuleMappingRowViewModel(mapping.Index, _availableSourceIds);
            row.LoadFrom(mapping);
            _moduleRows.Add(row);
        }

        NoModulesText.Visibility = _moduleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModuleCountText.Text = _moduleRows.Count == 0
            ? "NOT CONNECTED"
            : $"{_moduleRows.Count} MODULE{(_moduleRows.Count == 1 ? string.Empty : "S")}";
    }

    // ── frame pump ──────────────────────────────────────────────────────────────

    private void OnFramePumpTick(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            // One conversion per token per tick, shared with the multiview editor if it is open.
            _previewBitmaps.BeginTick();

            UpdateStageMonitors();
            RefreshCellPreviews(_cells, _previewBitmaps, _orchestrator);
            _multiviewSettings?.RefreshPreviews(_previewBitmaps);
            _mixEditor?.RefreshPreviews(_previewBitmaps);
        });

    /// <summary>Repaints a set of multiview cells: the shared bitmap for each token plus its red/green
    /// tally frame. Shared with <see cref="MultiviewSettingsWindow"/> so both grids behave identically.</summary>
    internal static void RefreshCellPreviews(
        IEnumerable<MultiviewCellViewModel> cells,
        PreviewBitmapCache bitmaps,
        AppOrchestrator orchestrator)
    {
        foreach (var cell in cells)
        {
            var (state, bus) = MultiviewTally.ResolveDetailed(cell.Token, orchestrator);
            cell.Tally = state;
            cell.TallyBus = bus;
            cell.Image = cell.Token == "EMPTY" ? null : bitmaps.Get(cell.Token);
        }
    }

    private void UpdateStageMonitors()
    {
        // Both ME rows are visible simultaneously since v3; the Me1/Me2 radio only decides which one
        // receives the transition bar and keyboard shortcuts.
        Preview1Image.Source = _previewBitmaps.Get("PVW1");
        Program1Image.Source = _previewBitmaps.Get("PGM1");
        Preview2Image.Source = _previewBitmaps.Get("PVW2");
        Program2Image.Source = _previewBitmaps.Get("PGM2");
    }

    // ── bus strips ──────────────────────────────────────────────────────────────

    private void RefreshBusSources()
    {
        var sources = _tiles
            .Where(t => t.Id is not null)
            .Select(t => (Id: t.Id!, t.Name))
            .ToList();

        foreach (var bus in _buses)
        {
            bus.SyncSources(sources);
        }

        RefreshBusState();
    }

    /// <summary>Mirrors the orchestrator's authoritative PGM/PVW membership onto the strips, both stage
    /// captions and the multiview frames — including the toggle states, so a bus driven from the Web API
    /// or a HID module switch doesn't leave this window describing a staging set that is no longer real.</summary>
    private void RefreshBusState()
    {
        foreach (var bus in _buses)
        {
            var program = _orchestrator.GetProgramSourceIds(bus.Bus);
            var preview = _orchestrator.GetPreviewSourceIds(bus.Bus);
            bus.SetOnProgram(program);
            bus.SetStaged(preview);
            bus.ProgramText = DescribeSources(program);
            bus.PreviewText = DescribeSources(preview);

            // Both ME rows have their own captions since v3.
            var programCaption = program.Count == 0
                ? $"{bus.ProgramLabel} — off air"
                : $"{bus.ProgramLabel} — {bus.ProgramText}";
            var previewCaption = preview.Count == 0
                ? $"{bus.PreviewLabel} — nothing staged"
                : $"{bus.PreviewLabel} — {bus.PreviewText}";

            if (bus.Bus == ProgramBus.Pgm1)
            {
                Program1Caption.Text = programCaption;
                Preview1Caption.Text = previewCaption;
            }
            else
            {
                Program2Caption.Text = programCaption;
                Preview2Caption.Text = previewCaption;
            }
        }

        foreach (var cell in _cells)
        {
            var (state, bus) = MultiviewTally.ResolveDetailed(cell.Token, _orchestrator);
            cell.Tally = state;
            cell.TallyBus = bus;
        }
    }

    private string DescribeSources(IReadOnlySet<string> ids)
    {
        if (ids.Count == 0)
        {
            return "-";
        }

        var names = ids
            .Select(id => _tiles.FirstOrDefault(t => t.Id == id)?.Name ?? id)
            .OrderBy(n => n, StringComparer.CurrentCulture);
        return string.Join(", ", names);
    }

    /// <summary>Staging a source is only a PVW change, so it never takes: the layer set is pushed with
    /// <c>Take: false</c> and only CUT/AUTO moves it to air.</summary>
    private async void OnBusSourceToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || FindBusFor(element) is not { } bus)
        {
            return;
        }

        await StagePreviewAsync(bus).ConfigureAwait(true);
    }

    private async Task StagePreviewAsync(BusControlViewModel bus)
    {
        var layers = bus.StagedSourceIds
            .Select((id, index) => new ProgramLayer(id, FullFrameLayer(index)))
            .ToList();

        try
        {
            await _orchestrator.ApplyProgramAsync(new ProgramRequest(bus.Bus, layers, Take: false)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Failed to stage the preview composition for {Bus}.", bus.Bus);
            System.Windows.MessageBox.Show(ex.Message, "Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshBusState();
    }

    private static PipSettings FullFrameLayer(int zOrder) =>
        new(Enabled: true, X: 0, Y: 0, Width: EngineDefaults.CanvasWidth, Height: EngineDefaults.CanvasHeight,
            Opacity: 1.0, ZOrder: zOrder, Crop: null);

    private void OnBusCutClick(object sender, RoutedEventArgs e) => _ = TakeBusAsync(FindBusFor((FrameworkElement)sender), 0);

    private void OnBusAutoClick(object sender, RoutedEventArgs e) =>
        _ = TakeBusAsync(FindBusFor((FrameworkElement)sender), SelectedAutoDurationMs());

    private async Task TakeBusAsync(BusControlViewModel? bus, int durationMs)
    {
        if (bus is null)
        {
            return;
        }

        try
        {
            await _orchestrator.TakeAsync(bus.Bus, durationMs).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "TAKE failed for {Bus}.", bus.Bus);
            System.Windows.MessageBox.Show(ex.Message, "Take", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // The engine swapped program<->preview for this bus; RefreshBusState re-reads both sets, so the
        // toggles now describe what the take left staged (whatever was on air a moment ago).
        RefreshBusState();
    }

    private int SelectedAutoDurationMs() =>
        AutoDurationCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
        int.TryParse(tag, out var ms) ? ms : 500;

    /// <summary>Walks up from a control inside one bus template to that bus's view model. The buttons
    /// live in a nested <c>ItemsControl</c>, so their own DataContext may be a
    /// <see cref="BusSourceViewModel"/>.</summary>
    private static BusControlViewModel? FindBusFor(DependencyObject element)
    {
        for (var node = element; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { DataContext: BusControlViewModel bus })
            {
                return bus;
            }
        }

        return null;
    }

    // ── stage (selected M/E) ────────────────────────────────────────────────────

    private void OnStageBusChanged(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
        {
            return;
        }

        _stageBus = Me2Radio.IsChecked == true ? ProgramBus.Pgm2 : ProgramBus.Pgm1;
        RefreshBusState();
    }

    private BusControlViewModel StageBusViewModel() => _buses.First(b => b.Bus == _stageBus);

    /// <summary>
    /// Operator keyboard shortcuts for the selected M/E. During a show the hands are on the keyboard and
    /// a cut has to happen on a beat, so the transition must not require finding a button with the
    /// mouse: <c>Space</c> cuts, <c>A</c> auto-transitions, <c>1</c>-<c>9</c> stage the n-th source,
    /// <c>Esc</c> clears the preview and <c>Tab</c> swaps which M/E the monitors follow.
    ///
    /// Suppressed while a text box or combo has focus, so typing a source name never fires a take.
    /// </summary>
    private void OnConsoleKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox ||
            Keyboard.Modifiers is not ModifierKeys.None)
        {
            return;
        }

        var bus = StageBusViewModel();

        switch (e.Key)
        {
            case Key.Space:
                _ = TakeBusAsync(bus, 0);
                break;

            case Key.A:
                _ = TakeBusAsync(bus, SelectedAutoDurationMs());
                break;

            case Key.Escape:
                OnStageClearClick(this, new RoutedEventArgs());
                break;

            case Key.Tab:
                (Me2Radio.IsChecked, Me1Radio.IsChecked) = (Me1Radio.IsChecked, Me2Radio.IsChecked);
                break;

            case >= Key.D1 and <= Key.D9:
                ToggleStagedByIndex(bus, e.Key - Key.D1);
                break;

            default:
                return;  // leave anything else to the focused control
        }

        e.Handled = true;
    }

    private void ToggleStagedByIndex(BusControlViewModel bus, int index)
    {
        if (index < 0 || index >= bus.Sources.Count)
        {
            return;
        }

        var source = bus.Sources[index];
        source.Staged = !source.Staged;
        _ = StagePreviewAsync(bus);
    }

    private void OnStageCutClick(object sender, RoutedEventArgs e) => _ = TakeBusAsync(StageBusViewModel(), 0);

    private void OnStageAutoClick(object sender, RoutedEventArgs e) =>
        _ = TakeBusAsync(StageBusViewModel(), SelectedAutoDurationMs());

    private async void OnStageClearClick(object sender, RoutedEventArgs e)
    {
        var bus = StageBusViewModel();
        foreach (var source in bus.Sources)
        {
            source.Staged = false;
        }

        await StagePreviewAsync(bus).ConfigureAwait(true);
    }

    /// <summary>Stages an empty composition and takes it: the operator's "get this bus off air" panic
    /// button, which the transition bar otherwise has no single-click path to.</summary>
    private async void OnStageAllOffClick(object sender, RoutedEventArgs e)
    {
        var bus = StageBusViewModel();
        foreach (var source in bus.Sources)
        {
            source.Staged = false;
        }

        try
        {
            await _orchestrator.ApplyProgramAsync(new ProgramRequest(bus.Bus, [], Take: true)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Failed to clear {Bus}.", bus.Bus);
        }

        RefreshBusState();
    }

    // ── multiview ───────────────────────────────────────────────────────────────

    private void OnOpenMultiviewSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_multiviewSettings is not null)
        {
            _multiviewSettings.Activate();
            return;
        }

        RefreshDisplays();
        _multiviewSettings = new MultiviewSettingsWindow(
            _orchestrator, _engine, [.. _availableDisplays], OpenMultiviewFullscreen)
        {
            Owner = this,
        };
        _multiviewSettings.Closed += (_, _) => _multiviewSettings = null;
        _multiviewSettings.Show();
    }

    private void OnMultiviewFullscreenClick(object sender, RoutedEventArgs e)
    {
        // Default to a display that isn't the console, which is what the operator means every time.
        var target = _availableDisplays.FirstOrDefault(d => d.Index != _operatorDisplayIndex)
            ?? _availableDisplays.FirstOrDefault();
        if (target is not null)
        {
            OpenMultiviewFullscreen(target.Index);
        }
    }

    /// <summary>Opens (or re-opens) the independent multiview full-screen window on the given display,
    /// after the same-screen warning if it targets the operator console.</summary>
    public void OpenMultiviewFullscreen(int displayIndex)
    {
        if (!ConfirmDisplayConflict(displayIndex))
        {
            return;
        }

        _multiviewFullscreen?.Close();
        _multiviewFullscreen = new MultiviewFullscreenWindow(
            _engine,
            () => _regionModel.ToLayout(),
            displayIndex);
        _multiviewFullscreen.Closed += (_, _) => _multiviewFullscreen = null;
        _multiviewFullscreen.ShowFullscreen();
    }

    // ── stage view preference ──────────────────────────────────────────────────

    /// <summary>Menu-driven radio: "Both / ME1 only / ME2 only". IsCheckable=True lets the item show a
    /// tick when it is the active mode; we untick the siblings ourselves since WPF has no built-in
    /// radio semantics for menu items.</summary>
    private void OnStageViewChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<StageViewMode>(tag, out var mode))
        {
            return;
        }

        // A second click on the already-active item would otherwise uncheck it and leave no mode
        // selected. Snap it back on and stop.
        if (mode == _config.StageViewMode)
        {
            SyncStageViewMenu(mode);
            return;
        }

        ApplyStageViewMode(mode);
        _config = _config with { StageViewMode = mode };
        AppConfigLoader.Save(AppContext.BaseDirectory, _config, _logger);
    }

    /// <summary>Resizes the stage rows and re-syncs the menu ticks. Called at startup with the persisted
    /// value and after every operator selection.</summary>
    private void ApplyStageViewMode(StageViewMode mode)
    {
        // Star rows for what should be visible, zero for what should not. Collapsing the splitter row
        // too keeps a 6px seam from staying visible when only one ME is showing.
        var star = new GridLength(1, GridUnitType.Star);
        var zero = new GridLength(0);
        switch (mode)
        {
            case StageViewMode.Me1Only:
                StageMe1Row.Height = star;
                StageSplitterRow.Height = zero;
                StageMe2Row.Height = zero;
                break;
            case StageViewMode.Me2Only:
                StageMe1Row.Height = zero;
                StageSplitterRow.Height = zero;
                StageMe2Row.Height = star;
                break;
            default:  // Both
                StageMe1Row.Height = star;
                StageSplitterRow.Height = new GridLength(6);
                StageMe2Row.Height = star;
                break;
        }

        SyncStageViewMenu(mode);
    }

    private void SyncStageViewMenu(StageViewMode mode)
    {
        StageViewBothItem.IsChecked = mode == StageViewMode.Both;
        StageViewMe1Item.IsChecked = mode == StageViewMode.Me1Only;
        StageViewMe2Item.IsChecked = mode == StageViewMode.Me2Only;
    }

    // ── operator display ────────────────────────────────────────────────────────

    private void OnMoveOperatorClick(object sender, RoutedEventArgs e)
    {
        if (OperatorDisplayCombo.SelectedItem is DisplayOption display)
        {
            MoveOperatorToDisplay(display.Index);
        }
    }

    /// <summary>Moves the operator console to the given display and persists the choice.</summary>
    public void MoveOperatorToDisplay(int displayIndex)
    {
        _operatorDisplayIndex = displayIndex;
        PositionOnDisplay(displayIndex, activate: true);
        SelectOperatorDisplay();
        _config = _config with { OperatorDisplayIndex = displayIndex };
        AppConfigLoader.Save(AppContext.BaseDirectory, _config, _logger);
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

    // ── add source ──────────────────────────────────────────────────────────────

    /// <summary>Opens the modal add-source dialog (both the toolbar's "+ Add source" and the Sources
    /// dock's "+" land here). Extracted from an overlay panel in v3 so the source list stays visible
    /// while the operator is filling the form in.</summary>
    private async void OnOpenAddSourceClick(object sender, RoutedEventArgs e)
    {
        if (_addSourceWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new AddSourceWindow(_deviceQueryService, _config, _logger) { Owner = this };
        _addSourceWindow = window;
        try
        {
            if (window.ShowDialog() != true || window.Result is not { } definition)
            {
                return;
            }

            try
            {
                await _orchestrator.AddSourceAsync(definition).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // The engine rejects a source it cannot create natively (e.g. an NDI source with DistroAV
                // absent). Without this catch the exception escapes an async void handler and takes the
                // whole app down instead of telling the operator what to install.
                _logger.LogWarning(ex, "Adding source {SourceId} failed.", definition.Id);
                System.Windows.MessageBox.Show(ex.Message, "Add source", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefreshAvailableTokensAndIds();
        }
        finally
        {
            _addSourceWindow = null;
        }
    }

    private async void OnRemoveSourceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string id })
        {
            return;
        }

        try
        {
            await _orchestrator.RemoveSourceAsync(id).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Removing source {SourceId} failed.", id);
            System.Windows.MessageBox.Show(ex.Message, "Remove source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tile = _tiles.FirstOrDefault(t => t.Id == id);
        if (tile is not null)
        {
            _tiles.Remove(tile);
        }

        RefreshAvailableTokensAndIds();
    }

    /// <summary>Opens the tally palette editor. The same values light the module LEDs, so applying
    /// re-lights the controller immediately.</summary>
    private async void OnTallyColorsClick(object sender, RoutedEventArgs e)
    {
        var editor = new TallyColorsWindow(_orchestrator.CurrentTallyColors) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is { } colors)
        {
            await _orchestrator.ApplyTallyColorsAsync(colors).ConfigureAwait(true);
        }
    }

    // ── mix sources ─────────────────────────────────────────────────────────────

    private void OnNewMixClick(object sender, RoutedEventArgs e) => OpenMixEditor(null);

    private void OnEditMixClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
        {
            OpenMixEditor(id);
        }
    }

    /// <summary>
    /// Opens the mix editor for a new mix, or for <paramref name="mixId"/> to change an existing one.
    /// The mix itself is excluded from the layer choices so it can never contain itself.
    /// </summary>
    private async void OpenMixEditor(string? mixId)
    {
        var existing = mixId is null ? null : _orchestrator.GetSourceDefinition(mixId);
        if (mixId is not null && existing?.Mix is null)
        {
            System.Windows.MessageBox.Show(
                "This source isn't a mix, or it was created outside this window so its layers aren't known.",
                "Mix source", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var available = _tiles
            .Where(t => t.Id is not null && t.Id != mixId)
            .Select(t => (Id: t.Id!, t.Name))
            .ToList();

        if (available.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Add at least one source before building a mix.",
                "Mix source", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new MixSourceWindow(existing, SuggestMixName(), available) { Owner = this };
        _mixEditor = editor;
        try
        {
            if (editor.ShowDialog() != true || editor.Result is not { } mix)
            {
                return;
            }

            var id = mixId ?? AddSourceWindow.GenerateId(editor.MixName);
            var definition = new SourceDefinition(
                id, editor.MixName, SourceType.Mix, null, null, null, null, null, mix);

            if (mixId is null)
            {
                await _orchestrator.AddSourceAsync(definition).ConfigureAwait(true);
            }
            else
            {
                await _orchestrator.UpdateSourceAsync(mixId, definition).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Saving mix source failed.");
            System.Windows.MessageBox.Show(ex.Message, "Mix source", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _mixEditor = null;
        }

        RefreshAvailableTokensAndIds();
    }

    private string SuggestMixName()
    {
        var used = _tiles.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var candidate = $"Mix {n}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Re-applies a source's definition so libobs re-opens the device — the recovery path for a
    /// capture device that was busy or unplugged when the source was first created.</summary>
    private async void OnReconnectSourceClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
        {
            await ReconnectAsync(id).ConfigureAwait(true);
        }
    }

    private async void OnReconnectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var id in _tiles.Where(t => t.Status != SourceStatus.Connected && t.Id is not null).Select(t => t.Id!).ToList())
        {
            await ReconnectAsync(id).ConfigureAwait(true);
        }
    }

    private async Task ReconnectAsync(string id)
    {
        try
        {
            if (!await _orchestrator.ReconnectSourceAsync(id).ConfigureAwait(true))
            {
                System.Windows.MessageBox.Show(
                    "This source was created outside the Add-Source form, so its device settings are not " +
                    "known. Remove it and add it again.",
                    "Reconnect", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Reconnecting source {SourceId} failed.", id);
            System.Windows.MessageBox.Show(ex.Message, "Reconnect", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── outputs / modules / projector ───────────────────────────────────────────

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
            return;
        }

        // An accepted assignment is not a working one: NDI needs the NDI runtime installed, and the
        // virtual camera can be held by another application. Say so rather than let a bus go dark.
        if (_orchestrator.BusesWithoutRunningOutput() is { Count: > 0 } dead)
        {
            System.Windows.MessageBox.Show(
                $"{string.Join(" and ", dead)} has outputs assigned but none of them started.\n\n"
                + "NDI sinks need the NDI runtime (NDI Tools) installed, and the virtual camera "
                + "cannot start while another application is using it. An HDMI sink counts as running "
                + "once its full-screen window is open.",
                "Apply outputs", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── audio ───────────────────────────────────────────────────────────────────

    private IReadOnlyList<AudioDeviceInfo> QueryAudioDevicesOrEmpty()
    {
        try
        {
            return _orchestrator.QueryAudioDevices();
        }
        catch (InvalidOperationException ex)
        {
            // Enumeration needs the engine; before it starts there is simply nothing to offer.
            _logger.LogDebug(ex, "Audio devices unavailable");
            return [];
        }
    }

    /// <summary>Rebuilds the audio rows from the saved routing: one row per assignment, plus an empty
    /// row for a bus that has none. A bus may legitimately feed several devices (house PA and a
    /// recorder), so the rows follow the saved table rather than being fixed at one per bus — otherwise
    /// pressing Apply would silently drop the extras.</summary>
    private void RebuildAudioRows()
    {
        var devices = QueryAudioDevicesOrEmpty();
        var saved = _orchestrator.CurrentAudioOutputs;

        _audioRows.Clear();
        foreach (var bus in new[] { ProgramBus.Pgm1, ProgramBus.Pgm2 })
        {
            var forBus = saved.Where(a => a.Bus == bus).ToList();
            if (forBus.Count == 0)
            {
                _audioRows.Add(NewAudioRow(bus, devices, deviceId: null));
                continue;
            }

            foreach (var assignment in forBus)
            {
                _audioRows.Add(NewAudioRow(bus, devices, assignment.DeviceId));
            }
        }
    }

    private static AudioOutputRowViewModel NewAudioRow(
        ProgramBus bus, IReadOnlyList<AudioDeviceInfo> devices, string? deviceId)
    {
        var row = new AudioOutputRowViewModel(bus);
        row.SyncDevices(devices);
        if (deviceId is not null)
        {
            row.Select(deviceId);
        }

        return row;
    }

    /// <summary>Re-reads the machine's audio endpoints, keeping each row's choice where the device is
    /// still there. An interface may be plugged in mid-show.</summary>
    private void OnRescanAudioClick(object sender, RoutedEventArgs e)
    {
        var devices = QueryAudioDevicesOrEmpty();
        foreach (var row in _audioRows)
        {
            row.SyncDevices(devices);
        }
    }

    private void OnAddAudioRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag }
            && Enum.TryParse<ProgramBus>(tag, out var bus))
        {
            _audioRows.Add(NewAudioRow(bus, QueryAudioDevicesOrEmpty(), deviceId: null));
        }
    }

    private void OnRemoveAudioRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: AudioOutputRowViewModel row })
        {
            _audioRows.Remove(row);
        }
    }

    private async void OnApplyAudioClick(object sender, RoutedEventArgs e)
    {
        var outputs = _audioRows.Select(r => r.ToAssignment()).OfType<AudioOutputAssignment>().ToList();
        try
        {
            await _orchestrator.ApplyAudioOutputsAsync(new AudioOutputsRequest(outputs)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(ex.Message, "Apply audio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Pushes an AFV/ON/OFF change back to the orchestrator, which owns the policy. The combo
    /// also fires while the rows are being populated, so a selection matching the stored definition is
    /// treated as an echo and ignored.</summary>
    private async void OnSourceAudioModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady
            || sender is not System.Windows.Controls.ComboBox { Tag: string id, SelectedItem: SourceAudioMode mode })
        {
            return;
        }

        var definition = _orchestrator.GetSourceDefinition(id);
        if (definition is null || definition.AudioMode == mode)
        {
            return;
        }

        try
        {
            await _orchestrator.UpdateSourceAsync(id, definition with { AudioMode = mode }).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(ex.Message, "Audio mode", MessageBoxButton.OK, MessageBoxImage.Warning);
            SyncTileAudioModes();
        }
    }

    /// <summary>Copies each source's stored audio mode onto its tile. Tiles are built from
    /// <see cref="SourceInfo"/>, which carries only what the engine reports about the signal.</summary>
    private void SyncTileAudioModes()
    {
        foreach (var tile in _tiles)
        {
            if (tile.Id is { } id && _orchestrator.GetSourceDefinition(id) is { } definition)
            {
                tile.AudioMode = definition.AudioMode;
            }
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
            _projectorWindow = new ProjectorWindow(_engine, _config);
            _projectorWindow.Closed += (_, _) => _projectorWindow = null;
        }

        _projectorWindow.ShowOnConfiguredDisplay();
    }
}
