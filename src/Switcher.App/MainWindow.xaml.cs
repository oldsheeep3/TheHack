using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.App.Rendering;
using Switcher.App.Services;
using Switcher.App.ViewModels;
using Switcher.Atem;
using Switcher.Contracts;

namespace Switcher.App;

/// <summary>
/// Main operator window: multiview of all input sources + live PGM/PVW previews, tally/ATEM
/// connection status, and the entry point to open the sub-operator's full-screen source projector
/// (docs/tasks/agent-A-004-app-integration.md step 4).
/// </summary>
public partial class MainWindow : Window
{
    private readonly IInputSourceManager _sourceManager;
    private readonly AtemController _atemController;
    private readonly FramePumpService _framePump;
    private readonly AppConfig _config;
    private readonly ObservableCollection<SourceTileViewModel> _tiles = new();

    private WriteableBitmap? _programBitmap;
    private WriteableBitmap? _previewBitmap;
    private ProjectorWindow? _projectorWindow;

    public MainWindow(
        AppOrchestrator orchestrator,
        IInputSourceManager sourceManager,
        AtemController atemController,
        FramePumpService framePump,
        AppConfig config)
    {
        InitializeComponent();

        _sourceManager = sourceManager;
        _atemController = atemController;
        _framePump = framePump;
        _config = config;

        SourceTilesControl.ItemsSource = _tiles;

        foreach (var source in _sourceManager.GetSources())
        {
            _tiles.Add(new SourceTileViewModel(source));
        }

        _sourceManager.SourceStatusChanged += OnSourceStatusChanged;
        _atemController.ConnectionStateChanged += OnAtemConnectionStateChanged;
        orchestrator.TallyChanged += OnTallyChanged;
        _framePump.ProgramFrameReady += OnProgramFrameReady;
        _framePump.PreviewFrameReady += OnPreviewFrameReady;

        AtemStateText.Text = _atemController.State.ToString();

        Unloaded += (_, _) =>
        {
            _sourceManager.SourceStatusChanged -= OnSourceStatusChanged;
            _atemController.ConnectionStateChanged -= OnAtemConnectionStateChanged;
            orchestrator.TallyChanged -= OnTallyChanged;
            _framePump.ProgramFrameReady -= OnProgramFrameReady;
            _framePump.PreviewFrameReady -= OnPreviewFrameReady;
        };
    }

    private void OnSourceStatusChanged(object? sender, SourceInfo info)
    {
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
        });
    }

    private void OnAtemConnectionStateChanged(object? sender, AtemConnectionState state) =>
        Dispatcher.BeginInvoke(() => AtemStateText.Text = state.ToString());

    private void OnTallyChanged(object? sender, TallyState state) =>
        Dispatcher.BeginInvoke(() =>
        {
            ActivePgmText.Text = state.ActivePgm.Count == 0 ? "-" : string.Join(", ", state.ActivePgm);
            ActivePvwText.Text = state.ActivePvw.Count == 0 ? "-" : string.Join(", ", state.ActivePvw);
        });

    private void OnProgramFrameReady(object? sender, FrameData frame) =>
        Dispatcher.BeginInvoke(() =>
        {
            _programBitmap = FrameBitmapWriter.Write(_programBitmap, frame);
            ProgramImage.Source = _programBitmap;
        });

    private void OnPreviewFrameReady(object? sender, FrameData frame) =>
        Dispatcher.BeginInvoke(() =>
        {
            _previewBitmap = FrameBitmapWriter.Write(_previewBitmap, frame);
            PreviewImage.Source = _previewBitmap;
        });

    private void OnOpenProjectorClick(object sender, RoutedEventArgs e)
    {
        if (_projectorWindow is null)
        {
            _projectorWindow = new ProjectorWindow(_framePump);
            _projectorWindow.Closed += (_, _) => _projectorWindow = null;
        }

        _projectorWindow.ShowOnDisplay(_config.ProjectorDisplayIndex);
    }
}
