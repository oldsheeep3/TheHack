using System.Runtime.InteropServices;

namespace Switcher.Engine;

/// <summary>
/// P/Invoke declarations mirroring <c>native/switcher-engine/include/engine.h</c>. The native
/// library links libobs and is built separately via CMake (Windows + OBS only). All strings cross the
/// boundary as UTF-8 JSON; buffers handed to callbacks are only valid for the duration of the callback.
/// </summary>
internal static partial class NativeMethods
{
    private const string Lib = "switcher-engine";

    internal delegate void FrameCallback(IntPtr user, [MarshalAs(UnmanagedType.LPUTF8Str)] string target, IntPtr bgra, int width, int height, int stride);

    internal delegate void StateCallback(IntPtr user, [MarshalAs(UnmanagedType.LPUTF8Str)] string stateJson);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr engine_startup(string optionsJson);

    [LibraryImport(Lib)]
    internal static partial void engine_shutdown(IntPtr ctx);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int engine_add_source(IntPtr ctx, string id, string type, string settingsJson);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int engine_remove_source(IntPtr ctx, string id);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void engine_set_preview(IntPtr ctx, int bus, string? sourceId);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void engine_set_source_enabled(IntPtr ctx, int bus, string sourceId, [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int engine_apply_program(IntPtr ctx, string programJson);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void engine_set_pip(IntPtr ctx, int bus, string sourceId, string pipJson);

    [LibraryImport(Lib)]
    internal static partial void engine_take(IntPtr ctx, int bus, int transitionKind, int durationMs);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int engine_apply_outputs(IntPtr ctx, string outputsJson);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int engine_apply_multiview(IntPtr ctx, string layoutJson);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int engine_start_display(IntPtr ctx, string target, IntPtr hwnd, int displayId);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void engine_stop_display(IntPtr ctx, string target);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void engine_set_tap(IntPtr ctx, string target, [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [LibraryImport(Lib)]
    internal static partial void engine_set_frame_cb(IntPtr ctx, IntPtr callback, IntPtr user);

    [LibraryImport(Lib)]
    internal static partial void engine_set_state_cb(IntPtr ctx, IntPtr callback, IntPtr user);
}
