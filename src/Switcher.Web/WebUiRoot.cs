namespace Switcher.Web;

/// <summary>
/// Locates the built settings UI (<c>apps/phone-bridge</c>'s <c>dist</c>, deployed as <c>wwwroot</c>
/// beside the app) so <see cref="WebHost"/> can serve it from the same origin as <c>/api/v1/*</c>.
/// <para>
/// Same origin is the point. The UI is a browser page that calls this API, and a page served from
/// anywhere else — a Vite dev server, a file:// path, another machine — is blocked by the browser
/// unless the API hands out CORS headers. Serving the page from the API's own port keeps every request
/// same-origin, so no cross-origin permission has to be granted to a host that binds 0.0.0.0.
/// </para>
/// </summary>
internal static class WebUiRoot
{
    /// <summary>Directory name the UI is deployed under, next to the executable.</summary>
    public const string DirectoryName = "wwwroot";

    /// <summary>
    /// The directory to serve, or <c>null</c> when no UI is deployed. A missing directory is normal —
    /// CI and the test host run the API without ever building the front end — so it is not an error;
    /// the API simply serves no pages.
    /// </summary>
    public static string? Resolve(string? explicitPath = null)
    {
        var candidate = string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(AppContext.BaseDirectory, DirectoryName)
            : explicitPath;

        return Directory.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }
}
