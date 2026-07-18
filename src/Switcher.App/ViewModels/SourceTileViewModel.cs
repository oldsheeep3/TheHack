using System.ComponentModel;
using System.Runtime.CompilerServices;
using Switcher.Contracts;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Switcher.App.ViewModels;

/// <summary>
/// One tile in the multiview's input-source grid. The compositor only exposes composited PGM/PVW
/// frames (not a per-channel raw frame) through <see cref="ICompositorEngine"/>, so each input tile
/// shows the channel's name/protocol/resolution/status rather than live per-channel video - see the
/// note in README.md.
/// </summary>
public sealed class SourceTileViewModel : INotifyPropertyChanged
{
    private SourceInfo _info;

    public SourceTileViewModel(SourceInfo info) => _info = info;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Channel => _info.Channel;

    /// <summary>The v2 OBS-style source id (docs/specs/00-system-overview.md §4.2), or <c>null</c> for
    /// a legacy int-channel source added via <c>POST /api/v1/config</c>. Only id-based sources can be
    /// removed/assigned to a multiview cell/module mapping.</summary>
    public string? Id => _info.Id;

    public string Name => _info.Name;

    public string Protocol => _info.Protocol.ToString();

    public string Resolution => _info.Resolution ?? "-";

    public SourceStatus Status => _info.Status;

    public Brush StatusBrush => _info.Status switch
    {
        SourceStatus.Connected => Brushes.LimeGreen,
        SourceStatus.Error => Brushes.Crimson,
        _ => Brushes.Gray,
    };

    public void Update(SourceInfo info)
    {
        _info = info;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Protocol));
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusBrush));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
