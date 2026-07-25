using System.ComponentModel;
using System.Runtime.CompilerServices;
using Switcher.Contracts;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Switcher.App.ViewModels;

/// <summary>
/// One tile in the multiview's input-source grid. The engine exposes composited PGM/PVW frames and
/// per-source frames via <see cref="IVideoEngine.GetFrame"/>, so each input tile shows the channel's
/// name/protocol/resolution/status alongside its preview - see the note in README.md.
/// </summary>
public sealed class SourceTileViewModel : INotifyPropertyChanged
{
    private SourceInfo _info;
    private SourceAudioMode _audioMode = SourceAudioMode.Afv;

    public SourceTileViewModel(SourceInfo info) => _info = info;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Channel => _info.Channel;

    /// <summary>The v2 OBS-style source id (docs/specs/00-system-overview.md §4.2), or <c>null</c> for
    /// a legacy int-channel source added via <c>POST /api/v1/config</c>. Only id-based sources can be
    /// removed/assigned to a multiview cell/module mapping.</summary>
    public string? Id => _info.Id;

    public string Name => _info.Name;

    public string Protocol => _info.Protocol.ToString();

    /// <summary>A mix is edited rather than reconnected — it has no device of its own.</summary>
    public bool IsMix => _info.Protocol == SourceProtocol.Mix;

    public bool IsReconnectable => !IsMix;

    public string Resolution => _info.Resolution ?? "-";

    /// <summary>AFV / ON / OFF, in the order an operator reaches for them.</summary>
    public static IReadOnlyList<SourceAudioMode> AudioModes { get; } =
        [SourceAudioMode.Afv, SourceAudioMode.On, SourceAudioMode.Off];

    /// <summary>How this source's audio reaches the program buses. The tile only holds the value for
    /// display — the orchestrator owns the policy and is told about changes by the window.</summary>
    public SourceAudioMode AudioMode
    {
        get => _audioMode;
        set
        {
            if (_audioMode == value)
            {
                return;
            }

            _audioMode = value;
            OnPropertyChanged();
        }
    }

    public SourceStatus Status => _info.Status;

    /// <summary>Plain-language state for the source row: a connected source is described by what it is
    /// actually sending, which is the thing an operator checks; anything else says what is wrong.</summary>
    public string StatusText => _info.Status switch
    {
        SourceStatus.Connected => _info.Resolution ?? "connected",
        SourceStatus.Error => "error — try Reconnect",
        _ => "no signal — check the cable, then Reconnect",
    };

    public Brush StatusBrush => _info.Status switch
    {
        SourceStatus.Connected => ConnectedBrush,
        SourceStatus.Error => ErrorBrush,
        _ => IdleBrush,
    };

    private static readonly Brush ConnectedBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0xD0, 0x7A));

    private static readonly Brush ErrorBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x2F, 0x2A));

    private static readonly Brush IdleBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x40, 0x48));

    static SourceTileViewModel()
    {
        ConnectedBrush.Freeze();
        ErrorBrush.Freeze();
        IdleBrush.Freeze();
    }

    public void Update(SourceInfo info)
    {
        _info = info;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Protocol));
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
