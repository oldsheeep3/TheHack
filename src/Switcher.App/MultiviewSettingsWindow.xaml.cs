using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Switcher.App.Display;
using Switcher.App.Multiview;
using Switcher.App.Orchestration;
using Switcher.App.Rendering;

using Switcher.App.ViewModels;
using Switcher.Contracts;

namespace Switcher.App;

/// <summary>
/// The multiview editor, split out of the operator window (the operator window now only *shows* the
/// multiview). Arranging cells is a setup-time job done once per venue, not something touched during a
/// show, so it lives behind "Multiview settings…" and gets room to explain itself instead of competing
/// with the program/preview monitors for space.
///
/// Edits go through <see cref="AppOrchestrator.ApplyMultiviewAsync"/>, which persists the layout and
/// pushes it to the engine; the operator window follows via
/// <see cref="AppOrchestrator.MultiviewChanged"/>, so the two views never drift.
/// </summary>
public partial class MultiviewSettingsWindow : Window
{
    private readonly AppOrchestrator _orchestrator;
    private readonly IVideoEngine _engine;
    private readonly Action<int> _openFullscreen;

    private readonly ObservableCollection<MultiviewCellViewModel> _cells = [];
    private readonly ObservableCollection<string> _availableTokens = [];
    private readonly MultiviewRegionModel _regionModel = new();

    private bool _suppressApply;
    private bool _isDraggingSelection;
    private bool _isEditing;
    private bool _uiReady;

    /// <summary>True while this window is the one applying a layout change, so the operator window
    /// doesn't echo that change straight back and reset the editor mid-edit.</summary>
    public bool IsEditing => _isEditing || _isDraggingSelection;

    public MultiviewSettingsWindow(
        AppOrchestrator orchestrator,
        IVideoEngine engine,
        IReadOnlyList<DisplayOption> displays,
        Action<int> openFullscreen)
    {
        ArgumentNullException.ThrowIfNull(displays);

        InitializeComponent();

        _orchestrator = orchestrator;
        _engine = engine;
        _openFullscreen = openFullscreen;

        MultiviewControl.ItemsSource = _cells;
        FullscreenDisplayCombo.ItemsSource = displays;
        FullscreenDisplayCombo.SelectedItem = displays.FirstOrDefault();

        var sizes = Enumerable.Range(MultiviewRegionModel.MinSize,
            MultiviewRegionModel.MaxSize - MultiviewRegionModel.MinSize + 1).ToList();
        GridRowsCombo.ItemsSource = sizes;
        GridColsCombo.ItemsSource = sizes;

        RefreshTokens();
        LoadLayout(_orchestrator.CurrentMultiviewLayout);
        _uiReady = true;
    }

    /// <summary>Rebuilds the assignable token list from the current sources. Mirrors the operator
    /// window's guard: clearing a shared ItemsSource transiently nulls every bound selection, which
    /// would otherwise be written back into the region model as a real operator choice.</summary>
    private void RefreshTokens()
    {
        _suppressApply = true;
        try
        {
            _availableTokens.Clear();
            foreach (var token in new[] { "EMPTY", "PGM1", "PGM2", "PVW1", "PVW2" })
            {
                _availableTokens.Add(token);
            }

            foreach (var source in _engine.GetSources())
            {
                if (source.Id is { } id)
                {
                    _availableTokens.Add($"SRC:{id}");
                }
            }
        }
        finally
        {
            _suppressApply = false;
        }

        TokenLegendControl.ItemsSource = new[]
        {
            "EMPTY — nothing",
            "PGM1 / PGM2 — what each bus is sending out",
            "PVW1 / PVW2 — what each bus will send next",
        }
        .Concat(_engine.GetSources()
            .Where(s => s.Id is not null)
            .Select(s => $"SRC:{s.Id} — {s.Name}"))
        .ToList();

        RebuildCells();
    }

    private void LoadLayout(MultiviewLayout layout)
    {
        _regionModel.Load(layout);
        RebuildCells();
    }

    private void RebuildCells()
    {
        _suppressApply = true;

        // Dimensions before items: the panel binds through Tag, so setting it afterwards would arrange
        // the new cells against the previous grid for a frame.
        MultiviewControl.Tag = new MultiviewGridSize(_regionModel.Rows, _regionModel.Cols);
        SyncGridCombos();

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

        _isDraggingSelection = false;
        _suppressApply = false;
    }

    /// <summary>Mirrors the model's dimensions into the pickers without re-entering
    /// <see cref="OnGridSizeChanged"/>.</summary>
    private void SyncGridCombos()
    {
        var wasReady = _uiReady;
        _uiReady = false;
        GridRowsCombo.SelectedItem = _regionModel.Rows;
        GridColsCombo.SelectedItem = _regionModel.Cols;
        _uiReady = wasReady;
    }

    /// <summary>
    /// Applies a new grid size (4x4 … 6x6). Merged regions that no longer fit inside a smaller grid are
    /// dropped, so the operator is warned before that happens rather than discovering it afterwards.
    /// </summary>
    private void OnGridSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || GridRowsCombo.SelectedItem is not int rows || GridColsCombo.SelectedItem is not int cols)
        {
            return;
        }

        if (rows == _regionModel.Rows && cols == _regionModel.Cols)
        {
            return;
        }

        var losing = _regionModel.Regions.Count(r =>
            (r.RowSpan > 1 || r.ColSpan > 1) && (r.Row + r.RowSpan > rows || r.Col + r.ColSpan > cols));
        if (losing > 0)
        {
            var answer = System.Windows.MessageBox.Show(
                $"{losing} merged cell{(losing == 1 ? string.Empty : "s")} won't fit in a {rows} x {cols} grid "
                + "and will be split back into single cells. Continue?",
                "Resize multiview",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK)
            {
                SyncGridCombos();
                return;
            }
        }

        _regionModel.Resize(rows, cols);
        StatusText.Text = $"Grid is now {rows} x {cols}.";
        ApplyFromModel();
    }

    /// <summary>
    /// Repaints the cell previews and their red/green tally frames. Driven by the operator window's
    /// frame-pump tick, using its bitmap cache, rather than by a subscription of this window's own: one
    /// subscriber converts each token's frame once and every open grid shares the result. Rendering the
    /// same cells twice per tick doubled the per-frame work on the single UI thread and stalled the
    /// whole console.
    /// </summary>
    public void RefreshPreviews(PreviewBitmapCache bitmaps) =>
        MainWindow.RefreshCellPreviews(_cells, bitmaps, _orchestrator);

    /// <summary>Reloads the layout after it changed elsewhere (Web API, or the operator window).</summary>
    public void ReloadLayout(MultiviewLayout layout) => LoadLayout(layout);

    /// <summary>Rebuilds the assignable token list after a source was added or removed.</summary>
    public void ReloadSources() => RefreshTokens();

    // --- rectangular merge drag -------------------------------------------------

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
            StatusText.Text = error;
            return;
        }

        StatusText.Text = "Cells merged.";
        ApplyFromModel();
    }

    private void OnSplitSelectedClick(object sender, RoutedEventArgs e)
    {
        var targets = _cells.Where(c => c.Selected).Select(c => (c.Row, c.Col)).ToList();
        if (targets.Count == 0)
        {
            StatusText.Text = "Select a merged cell first, then Split.";
            return;
        }

        foreach (var (row, col) in targets)
        {
            _regionModel.Split(row, col);
        }

        StatusText.Text = "Cells split.";
        ApplyFromModel();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        _isDraggingSelection = false;
        foreach (var cell in _cells)
        {
            cell.Selected = false;
        }
    }

    private void OnResetLayoutClick(object sender, RoutedEventArgs e)
    {
        _regionModel.Reset();
        StatusText.Text = $"All {_regionModel.Rows * _regionModel.Cols} cells emptied.";
        ApplyFromModel();
    }

    private void ApplyFromModel()
    {
        var layout = _regionModel.ToLayout();
        RebuildCells();

        _isEditing = true;
        try
        {
            _ = _orchestrator.ApplyMultiviewAsync(layout);
        }
        finally
        {
            _isEditing = false;
        }
    }

    private void OnMultiviewCellSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressApply || sender is not FrameworkElement { DataContext: MultiviewCellViewModel vm })
        {
            return;
        }

        // A ComboBox whose selected item leaves its ItemsSource writes a null Token back through the
        // TwoWay binding; that is a transient UI deselection, not an operator choice.
        if (vm.Token is null)
        {
            return;
        }

        // WPF materialises the item containers lazily, *after* RebuildCells has cleared its suppression
        // flag, so every combo raises SelectionChanged once just for existing. Re-applying the layout on
        // those raises MultiviewChanged, which rebuilds the cells, which materialises new combos - a
        // feedback loop that saturates the dispatcher and stalls every other UI update. Comparing
        // against the model makes the handler act only on a real change, whenever it arrives.
        if (_regionModel.RegionAt(vm.Row, vm.Col)?.Content == vm.Token)
        {
            (vm.Tally, vm.TallyBus) = MultiviewTally.ResolveDetailed(vm.Token, _orchestrator);
            return;
        }

        _regionModel.SetContent(vm.Row, vm.Col, vm.Token);
        (vm.Tally, vm.TallyBus) = MultiviewTally.ResolveDetailed(vm.Token, _orchestrator);

        _isEditing = true;
        try
        {
            _ = _orchestrator.ApplyMultiviewAsync(_regionModel.ToLayout());
        }
        finally
        {
            _isEditing = false;
        }
    }

    private void OnShowFullscreenClick(object sender, RoutedEventArgs e)
    {
        if (FullscreenDisplayCombo.SelectedItem is DisplayOption display)
        {
            _openFullscreen(display.Index);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
