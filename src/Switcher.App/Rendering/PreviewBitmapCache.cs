using System.Windows.Media.Imaging;
using Switcher.App.Services;

namespace Switcher.App.Rendering;

/// <summary>
/// One <see cref="WriteableBitmap"/> per multiview token (<c>PGM1</c>/<c>PVW2</c>/<c>SRC:&lt;id&gt;</c>/…),
/// refreshed once per frame-pump tick and shared by every window that shows that token.
///
/// A <see cref="WriteableBitmap"/> can back any number of <c>Image</c> elements, so the operator window
/// and the multiview editor showing the same cell must not each convert the frame: doing that work twice
/// doubles the per-tick cost on the single UI thread, which is what made opening the editor stall the
/// whole console. Callers ask for a token's bitmap; the first ask in a tick does the conversion and the
/// rest get the same instance.
///
/// UI-thread only, like the bitmaps it owns.
/// </summary>
public sealed class PreviewBitmapCache
{
    private readonly FramePumpService _framePump;
    private readonly Dictionary<string, WriteableBitmap> _bitmaps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _refreshedThisTick = new(StringComparer.Ordinal);

    public PreviewBitmapCache(FramePumpService framePump) => _framePump = framePump;

    /// <summary>Starts a new tick: the next <see cref="Get"/> per token re-reads the pump.</summary>
    public void BeginTick() => _refreshedThisTick.Clear();

    /// <summary>The current bitmap for <paramref name="token"/>, or <c>null</c> while the engine has not
    /// rendered that target yet.</summary>
    public WriteableBitmap? Get(string token)
    {
        _bitmaps.TryGetValue(token, out var bitmap);

        if (_refreshedThisTick.Add(token) && _framePump.TryGetCellFrame(token) is { } frame)
        {
            var updated = FrameBitmapWriter.Write(bitmap, frame);
            if (updated is not null)
            {
                _bitmaps[token] = updated;
                bitmap = updated;
            }
        }

        return bitmap;
    }

    /// <summary>Drops a removed source's bitmap so a re-added id never shows the old device's last frame.</summary>
    public void Forget(string token) => _bitmaps.Remove(token);
}
