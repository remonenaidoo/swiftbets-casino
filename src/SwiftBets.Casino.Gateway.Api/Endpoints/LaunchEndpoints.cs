using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Contracts.Errors;

namespace SwiftBets.Casino.Gateway.Api.Endpoints;

public static class LaunchEndpoints
{
    public sealed record LaunchRequest(string? GameId, string? ProviderId);

    public static IEndpointRouteBuilder MapLaunchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/casino/launch", async (LaunchRequest request, LaunchGameHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(Identity.UserId(context), request.ProviderId, request.GameId, cancellationToken);
            return result.Status switch
            {
                LaunchStatus.Launched => Results.Ok(new { sessionToken = result.SessionToken, launchUrl = result.LaunchUrl, expiresAt = result.ExpiresAt }),
                LaunchStatus.Restricted => new Error("casino_restricted", "Casino play is not available on this account right now.", ErrorKind.Forbidden).ToHttpResult(context),
                LaunchStatus.RestrictionsUnavailable => Error.Unavailable("restrictions_unavailable", "Casino launch is briefly unavailable; try again.").ToHttpResult(context),
                LaunchStatus.ProviderNotFound => Error.NotFound("provider_not_found", "That game provider is not available.").ToHttpResult(context),
                _ => Error.Validation("invalid_launch", "Send a gameId and providerId in lower-case letters, digits and dashes.").ToHttpResult(context),
            };
        })
        .RequireAuthorization(Roles.Punter);
        return endpoints;
    }
}

internal static class Identity
{
    public static Guid UserId(HttpContext context) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out var id) ? id : throw new BadHttpRequestException("Token subject is not a user id.", 401);
}
