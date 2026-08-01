using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class SourcesEndpoint
{
    public static IResult Get(IVideoEngine videoEngine) => Results.Ok(videoEngine.GetSources());

    /// <summary>
    /// The full <see cref="SourceDefinition"/> list, which <see cref="Get"/> deliberately does not carry.
    /// A settings client needs the per-type config (NDI name, webcam device, SRT URL, ...) to populate an
    /// edit form; <c>PUT /api/v1/sources/{id}</c> replaces the whole definition, so without this it could
    /// only overwrite the fields it never had.
    /// </summary>
    public static async Task<IResult> GetDefinitionsAsync(
        ISwitcherConfigService configService,
        CancellationToken cancellationToken) =>
        Results.Ok(await configService.GetSourceDefinitionsAsync(cancellationToken));

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
