using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class SourcesEndpoint
{
    public static IResult Get(IInputSourceManager sourceManager) => Results.Ok(sourceManager.GetSources());

    public static async Task<IResult> PostAsync(
        SourceDefinition? source,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        var errors = SourceDefinitionValidator.Validate(source);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { errors });
        }

        await configService.AddSourceAsync(source!, cancellationToken);
        return Results.NoContent();
    }

    public static async Task<IResult> PutAsync(
        string id,
        SourceDefinition? source,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        var errors = SourceDefinitionValidator.Validate(source);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { errors });
        }

        await configService.UpdateSourceAsync(id, source!, cancellationToken);
        return Results.NoContent();
    }

    public static async Task<IResult> DeleteAsync(
        string id,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        await configService.RemoveSourceAsync(id, cancellationToken);
        return Results.NoContent();
    }
}
