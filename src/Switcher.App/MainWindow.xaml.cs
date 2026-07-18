using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.App.Rendering;
using Switcher.App.Services;
using Switcher.App.ViewModels;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.VirtualCam;
using Switcher.VirtualCam.Display;

namespace Switcher.App;

/// <summary>
/// Main operator window: 4x4 configurable multiview, source dock, PGM1/PGM2/PVW1/PVW2 tally, output
/// routing/module mapping panels, ATEM connection status, and the entry point to open the sub-operator's
/// full-screen source projector (docs/tasks/agent-A2-006-app-integration-v2.md steps 4-5). All
/// PGM/PVW-affecting actions go through <see cref="AppOrchestrator"/> rather than touching module
/// internals directly, per the "配線/DI" review pattern (docs/.claude/review-patterns.md).
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppOrchestrator _orchestrator;
    private readonly IInputSourceManager _sourceManager;
    private readonly AtemController _atemController;
    private readonly FramePumpService _framePump;
    private readonly OutputRouter _outputRouter;
    private readonly IHdmiFullscreenOutput _hdmiOutput;
    private readonly AppConfig _config;

    private readonly ObservableCollection<SourceTileViewModel> _tiles = [];
    private readonly ObservableCollection<string> _availableTokens = [];
    private readonly ObservableCollection<string> _availableSourceIds = [];
    private readonly List<MultiviewCellViewModel> _cells = [];
    private readonly ObservableCollection<OutputAssignmentRowViewModel> _outputRows = [];
    private readonly ObservableCollection<ModuleMappingRowViewModel> _moduleRows = [];
    private readonly Dictionary<string, WriteableBitmap> _cellBitmaps = [];

    private ProjectorWindow? _projectorWindow;
    private bool _suppressMultiviewApply;

    public MainWindow(
        AppOrchestrator orchestrator,
        IInputSourceManager sourceManager,
        AtemController atemController,
        FramePumpService framePump,
        OutputRouter outputRouter,
        IHdmiFullscreenOutput hdmiOutput,
        AppConfig config)
    {
        InitializeComponent();

        _orchestrator = orchestrator;
        _sourceManager = sourceManager;
        _atemController = atemController;
        _framePump = framePump;
        _outputRouter = outputRouter;
        _hdmiOutput = hdmiOutput;
        _config = config;

        SourceTilesControl.ItemsSource = _tiles;
        MultiviewControl.ItemsSource = _cells;
        OutputsControl.ItemsSource = _outputRows;
        ModulesControl.ItemsSource = _moduleRows;

        foreach (var source in _sourceManager.GetSources())
        {
            _tiles.Add(new SourceTileViewModel(source));
        }

        RefreshAvailableTokensAndIds();

        _suppressMultiviewApply = true;
        for (var i = 0; i < RuntimeConfig.MultiviewCellCount; i++)
        {
            _cells.Add(new MultiviewCellViewModel(i, _availableTokens));
        }

        LoadMultiviewLayout(_orchestrator.CurrentMultiviewLayout);
        _suppressMultiviewApply = false;

        foreach (var sink in new[] { OutputSink.Vcam1, OutputSink.Vcam2, OutputSink.Hdmi })
        {
            var row = new OutputAssignmentRowViewModel(sink);
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

        Unloaded += (_, _) =>
        {
            _sourceManager.SourceStatusChanged -= OnSourceStatusChanged;
            _atemController.ConnectionStateChanged -= OnAtemConnectionStateChanged;
            _orchestrator.TallyChangedV2 -= OnTallyChangedV2;
            _orchestrator.MultiviewChanged -= OnMultiviewChanged;
            _framePump.Tick -= OnFramePumpTick;
        };
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
        for (var i = 0; i < _cells.Count && i < layout.Cells.Count; i++)
        {
            _cells[i].Token = _availableTokens.Contains(layout.Cells[i]) ? layout.Cells[i] : "EMPTY";
        }
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
        Dispatcher.BeginInvoke(() =>
        {
            _suppressMultiviewApply = true;
            LoadMultiviewLayout(layout);
            _suppressMultiviewApply = false;
        });

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

    private void OnMultiviewCellSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMultiviewApply)
        {
            return;
        }

        var layout = new MultiviewLayout(_cells.Select(c => c.Token).ToList());
        _ = _orchestrator.ApplyMultiviewAsync(layout);
    }

    private async void OnAddSourceClick(object sender, RoutedEventArgs e)
    {
        var id = NewSourceIdBox.Text.Trim();
        var name = NewSourceNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var typeText = (NewSourceTypeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "NDI";
        var config = NewSourceConfigBox.Text.Trim();
        SourceDefinition definition = typeText switch
        {
            "WEBCAM" => new SourceDefinition(id, name, SourceType.Webcam, null, new WebcamConfig(config, null), null),
            "SRT" => new SourceDefinition(id, name, SourceType.Srt, null, null, new SrtConfig(config, 200)),
            _ => new SourceDefinition(id, name, SourceType.Ndi, new NdiConfig(config), null, null),
        };

        await _orchestrator.AddSourceAsync(definition).ConfigureAwait(true);
        RefreshAvailableTokensAndIds();
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

    private async void OnApplyOutputsClick(object sender, RoutedEventArgs e)
    {
        var request = new OutputsRequest(_outputRows.Select(r => r.ToOutputAssignment()).ToList());
        await _orchestrator.ApplyOutputsAsync(request).ConfigureAwait(true);
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
