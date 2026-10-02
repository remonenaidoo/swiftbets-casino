using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Contracts.Errors;

namespace SwiftBets.Casino.Gateway.Api.Endpoints;

public static class FreeSpinEndpoints
{
    public static IEndpointRouteBuilder MapFreeSpinEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/casino/free-spins").RequireAuthorization(Roles.Operator);
        admin.MapPost("/", async (FreeSpinsHandler.Grant request, FreeSpinsHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        {
            var (grant, error) = await handler.GrantAsync(request, Identity.UserId(context), cancellationToken);
            return grant is not null
                ? Results.Created($"/admin/casino/free-spins/{grant.GrantId}", grant)
                : Error.Validation(error!, "Give a punter, a game, 1 to 1000 spins and an expiry in the future.").ToHttpResult(context);
        });
        admin.MapGet("/", async (Guid punterId, FreeSpinsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.ListAsync(punterId, cancellationToken)));
        return endpoints;
    }
}
