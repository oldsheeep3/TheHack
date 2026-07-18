using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class ProgramEndpoint
{
    public static async Task<IResult> PostAsync(
        ProgramRequest? request,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        var errors = ProgramRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { errors });
        }

        await configService.ApplyProgramAsync(request!, cancellationToken);
        return Results.NoContent();
    }
}
