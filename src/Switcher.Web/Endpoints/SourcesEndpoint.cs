using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class SourcesEndpoint
{
    public static IResult Get(IInputSourceManager sourceManager) => Results.Ok(sourceManager.GetSources());
}
