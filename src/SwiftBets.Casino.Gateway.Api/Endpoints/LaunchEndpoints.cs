using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Contracts.Errors;

namespace SwiftBets.Casino.Gateway.Api.Endpoints;

public static class LaunchEndpoints
{
    /// <summary>Only an absolute http(s) address is passed on as the provider's back-to-lobby link.</summary>
    private static string? Lobby(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.ToString() : null;

    public sealed record LaunchRequest(string? GameId, string? ProviderId, string? LobbyUrl = null, string? Platform = null);

    public static IEndpointRouteBuilder MapLaunchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/casino/launch", async (LaunchRequest request, LaunchGameHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(Identity.UserId(context), request.ProviderId, request.GameId, cancellationToken, Lobby(request.LobbyUrl), request.Platform == "MOBILE" ? "MOBILE" : "WEB");
            return result.Status switch
            {
                LaunchStatus.ProviderUnavailable => Error.Unavailable("provider_unavailable", "The game provider did not answer; try again.").ToHttpResult(context),
                LaunchStatus.Launched => Results.Ok(new { sessionToken = result.SessionToken, launchUrl = result.LaunchUrl, expiresAt = result.ExpiresAt }),
                LaunchStatus.Restricted => new Error("casino_restricted", "Casino play is not available on this account right now.", ErrorKind.Forbidden).ToHttpResult(context),
                LaunchStatus.RestrictionsUnavailable => Error.Unavailable("restrictions_unavailable", "Casino launch is briefly unavailable; try again.").ToHttpResult(context),
                LaunchStatus.ProviderNotFound => Error.NotFound("provider_not_found", "That game provider is not available.").ToHttpResult(context),
                _ => Error.Validation("invalid_launch", "Send a gameId and providerId in lower-case letters, digits and dashes.").ToHttpResult(context),
            };
        })
        .RequireAuthorization(Roles.Punter);

        // Free play: anyone may open a demo game; no session, no wallet, nothing at stake.
        endpoints.MapPost("/casino/demo", async (LaunchRequest request, LaunchGameHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await handler.DemoAsync(request.ProviderId, request.GameId, Lobby(request.LobbyUrl), cancellationToken);
            return result.Status switch
            {
                LaunchStatus.Launched => Results.Ok(new { launchUrl = result.LaunchUrl }),
                LaunchStatus.NoDemo => Error.NotFound("no_demo", "This game has no free demo.").ToHttpResult(context),
                LaunchStatus.ProviderNotFound => Error.NotFound("provider_not_found", "That game provider is not available.").ToHttpResult(context),
                _ => Error.Validation("invalid_launch", "Send a gameId and providerId in lower-case letters, digits and dashes.").ToHttpResult(context),
            };
        })
        .AllowAnonymous();

        endpoints.MapGet("/casino/recent", async (ICasinoStore store, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(await store.ListRecentGamesAsync(Identity.UserId(context), 12, cancellationToken)))
            .RequireAuthorization(Roles.Punter);
        return endpoints;
    }
}

internal static class Identity
{
    public static Guid UserId(HttpContext context) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out var id) ? id : throw new BadHttpRequestException("Token subject is not a user id.", 401);
}
