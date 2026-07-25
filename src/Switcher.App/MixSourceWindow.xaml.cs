using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Switcher.App.Rendering;
using Switcher.App.ViewModels;
using Switcher.Contracts;

// WindowsForms is enabled in this project, so Point is ambiguous without this.
using Point = System.Windows.Point;

namespace Switcher.App;

/// <summary>
/// Editor for a MIX source: several sources arranged on one canvas and saved as a single source that
/// can then be put on a bus, in a multiview cell or on a module switch like any camera.
///
/// The canvas is drawn at the mix's real pixel size inside a <c>Viewbox</c>, so every coordinate in this
/// window — drag deltas included — is already in canvas pixels and needs no scale conversion. Layers are
/// live thumbnails, using the operator window's shared bitmap cache, so the operator composes against
/// what the sources are actually showing rather than against placeholder boxes.
/// </summary>
public partial class MixSourceWindow : Window
{
    private readonly ObservableCollection<MixLayerViewModel> _layers = [];
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;

    private MixLayerViewModel? _dragLayer;
    private Point _dragOrigin;
    private double _dragStartX, _dragStartY, _dragStartW, _dragStartH;
    private bool _resizing;

    /// <param name="existing">The mix being edited, or <c>null</c> to create a new one.</param>
    /// <param name="availableSources">Sources that can become layers — the caller filters out the mix
    /// itself so it can't contain itself.</param>
    public MixSourceWindow(
        SourceDefinition? existing,
        string suggestedName,
        IReadOnlyList<(string Id, string Name)> availableSources)
    {
        ArgumentNullException.ThrowIfNull(availableSources);

        InitializeComponent();

        var config = existing?.Mix;
        _canvasWidth = config?.CanvasWidth is > 0 ? config.CanvasWidth : EngineDefaults.CanvasWidth;
        _canvasHeight = config?.CanvasHeight is > 0 ? config.CanvasHeight : EngineDefaults.CanvasHeight;

        EditingId = existing?.Id;
        MixNameBox.Text = existing?.Name ?? suggestedName;
        CanvasSizeText.Text = $"{_canvasWidth} x {_canvasHeight}";
        StageBorder.Width = _canvasWidth;
        StageBorder.Height = _canvasHeight;

        AvailableSourcesControl.ItemsSource = availableSources
            .Select(s => new { s.Id, s.Name })
            .ToList();

        LayersControl.ItemsSource = _layers;

        // Layer order in the list is the draw order, so load bottom-up by z.
        foreach (var layer in (config?.Layers ?? []).OrderBy(l => l.ZOrder))
        {
            var name = availableSources.FirstOrDefault(s => s.Id == layer.SourceId).Name ?? layer.SourceId;
            _layers.Add(new MixLayerViewModel(layer.SourceId, name, layer));
        }

        SaveButton.Content = existing is null ? "Create mix" : "Save mix";
        UpdateSelectionPanel();
    }

    /// <summary>Id of the mix being edited, or <c>null</c> when creating a new one.</summary>
    public string? EditingId { get; }

    /// <summary>The mix the operator saved. Set only when the dialog closes with <c>true</c>.</summary>
    public MixConfig? Result { get; private set; }

    /// <summary>The name the operator gave the mix.</summary>
    public string MixName => MixNameBox.Text.Trim();

    /// <summary>Refreshes the layer thumbnails; driven by the operator window's frame-pump tick so the
    /// whole app converts each source's frame once (see <see cref="PreviewBitmapCache"/>).</summary>
    public void RefreshPreviews(PreviewBitmapCache bitmaps)
    {
        ArgumentNullException.ThrowIfNull(bitmaps);
        foreach (var layer in _layers)
        {
            layer.Image = bitmaps.Get(layer.Token);
        }
    }

    private MixLayerViewModel? Selected => _layers.FirstOrDefault(l => l.Selected);

    // ── adding / removing / ordering ────────────────────────────────────────────

    private void OnAddLayerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string id } button ||
            button.DataContext is not { } context)
        {
            return;
        }

        var name = context.GetType().GetProperty("Name")?.GetValue(context) as string ?? id;

        // New layers land at a quarter size, offset per existing layer, so successive adds don't stack
        // exactly on top of each other and become impossible to grab.
        var offset = _layers.Count * (_canvasWidth / 24);
        var layer = new MixLayer(
            id,
            Math.Min(offset, _canvasWidth / 2),
            Math.Min(offset, _canvasHeight / 2),
            _canvasWidth / 2,
            _canvasHeight / 2);

        var vm = new MixLayerViewModel(id, name, layer);
        _layers.Add(vm);
        Select(vm);
        StatusText.Text = $"Added {name}.";
    }

    private void OnRemoveLayerClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } layer)
        {
            _layers.Remove(layer);
            UpdateSelectionPanel();
            StatusText.Text = $"Removed {layer.Name}.";
        }
    }

    private void OnBringForwardClick(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void OnSendBackClick(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveSelected(int delta)
    {
        if (Selected is not { } layer)
        {
            return;
        }

        var index = _layers.IndexOf(layer);
        var target = index + delta;
        if (target < 0 || target >= _layers.Count)
        {
            return;
        }

        _layers.Move(index, target);
        StatusText.Text = $"{layer.Name} is now layer {target + 1} of {_layers.Count}.";
    }

    // ── drag to move / resize ───────────────────────────────────────────────────

    private void OnLayerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MixLayerViewModel layer } element)
        {
            return;
        }

        Select(layer);
        BeginDrag(layer, element, e, resizing: false);
        e.Handled = true;
    }

    private void OnResizeGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MixLayerViewModel layer } element)
        {
            return;
        }

        Select(layer);
        BeginDrag(layer, element, e, resizing: true);
        e.Handled = true;  // don't let the move-drag underneath also start
    }

    private void BeginDrag(MixLayerViewModel layer, FrameworkElement element, MouseButtonEventArgs e, bool resizing)
    {
        _dragLayer = layer;
        _resizing = resizing;
        _dragOrigin = e.GetPosition(StageBorder);
        _dragStartX = layer.X;
        _dragStartY = layer.Y;
        _dragStartW = layer.Width;
        _dragStartH = layer.Height;

        // Capture on the item root so the pointer can leave the (possibly small) grip mid-drag.
        var target = resizing ? element : element;
        target.CaptureMouse();
    }

    private void OnLayerMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragLayer is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(StageBorder);
        var dx = position.X - _dragOrigin.X;
        var dy = position.Y - _dragOrigin.Y;

        if (_resizing)
        {
            _dragLayer.Width = _dragStartW + dx;
            _dragLayer.Height = _dragStartH + dy;
        }
        else
        {
            // Clamp so a layer always keeps a grabbable corner on the canvas.
            _dragLayer.X = Math.Clamp(_dragStartX + dx, -_dragLayer.Width + 40, _canvasWidth - 40);
            _dragLayer.Y = Math.Clamp(_dragStartY + dy, -_dragLayer.Height + 40, _canvasHeight - 40);
        }

        SyncFieldsFromSelection();
    }

    private void OnLayerMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.ReleaseMouseCapture();
        }

        _dragLayer = null;
        _resizing = false;
    }

    private void Select(MixLayerViewModel layer)
    {
        foreach (var other in _layers)
        {
            other.Selected = ReferenceEquals(other, layer);
        }

        UpdateSelectionPanel();
    }

    // ── numeric fields ──────────────────────────────────────────────────────────

    private bool _syncingFields;

    private void UpdateSelectionPanel()
    {
        var layer = Selected;
        SelectedNameText.Text = layer?.Name ?? "Nothing selected — click a layer";
        SelectedLayerPanel.IsEnabled = layer is not null;
        SyncFieldsFromSelection();
    }

    private void SyncFieldsFromSelection()
    {
        if (Selected is not { } layer)
        {
            return;
        }

        _syncingFields = true;
        LayerXBox.Text = layer.X.ToString(CultureInfo.InvariantCulture);
        LayerYBox.Text = layer.Y.ToString(CultureInfo.InvariantCulture);
        LayerWBox.Text = layer.Width.ToString(CultureInfo.InvariantCulture);
        LayerHBox.Text = layer.Height.ToString(CultureInfo.InvariantCulture);
        _syncingFields = false;
    }

    private void OnLayerFieldChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_syncingFields || Selected is not { } layer)
        {
            return;
        }

        if (double.TryParse(LayerXBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
        {
            layer.X = x;
        }

        if (double.TryParse(LayerYBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            layer.Y = y;
        }

        if (double.TryParse(LayerWBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
        {
            layer.Width = w;
        }

        if (double.TryParse(LayerHBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
        {
            layer.Height = h;
        }
    }

    // ── presets ─────────────────────────────────────────────────────────────────

    private void OnFullFrameClick(object sender, RoutedEventArgs e) =>
        PlaceSelected(0, 0, _canvasWidth, _canvasHeight);

    private void OnHalfLeftClick(object sender, RoutedEventArgs e) =>
        PlaceSelected(0, _canvasHeight / 4, _canvasWidth / 2, _canvasHeight / 2);

    private void OnHalfRightClick(object sender, RoutedEventArgs e) =>
        PlaceSelected(_canvasWidth / 2, _canvasHeight / 4, _canvasWidth / 2, _canvasHeight / 2);

    private void OnCornerInsetClick(object sender, RoutedEventArgs e)
    {
        var w = _canvasWidth / 4;
        var h = _canvasHeight / 4;
        var margin = _canvasWidth / 40;
        PlaceSelected(_canvasWidth - w - margin, _canvasHeight - h - margin, w, h);
    }

    private void PlaceSelected(int x, int y, int width, int height)
    {
        if (Selected is not { } layer)
        {
            return;
        }

        layer.X = x;
        layer.Y = y;
        layer.Width = width;
        layer.Height = height;
        SyncFieldsFromSelection();
    }

    // ── save ────────────────────────────────────────────────────────────────────

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MixName))
        {
            System.Windows.MessageBox.Show("Give the mix a name first.", "Mix source",
                MessageBoxButton.OK, MessageBoxImage.Information);
            MixNameBox.Focus();
            return;
        }

        if (_layers.Count == 0)
        {
            System.Windows.MessageBox.Show("Add at least one layer.", "Mix source",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result = new MixConfig(
            _layers.Select((l, index) => l.ToLayer(index)).ToList(),
            _canvasWidth,
            _canvasHeight);

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
