using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class OutputsEndpoint
{
    /// <summary>The output table currently in force. The <c>PUT</c> replaces the whole table, so a client
    /// that could not read it first would overwrite sinks it never knew about.</summary>
    public static async Task<IResult> GetAsync(
        ISwitcherConfigService configService,
        CancellationToken cancellationToken) =>
        Results.Ok(await configService.GetOutputsAsync(cancellationToken));

    public static async Task<IResult> PutAsync(
        OutputsRequest? request,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        var errors = OutputsRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { errors });
        }

        await configService.ApplyOutputsAsync(request!, cancellationToken);
        return Results.NoContent();
    }
}
