using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class OutputsEndpoint
{
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
