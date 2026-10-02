using System.Globalization;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Contracts.Errors;

namespace SwiftBets.Casino.Gateway.Api.Endpoints;

public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/casino/reconciliation").RequireAuthorization(Roles.Operator);
        admin.MapPost("/{providerId}/{date}", async (string providerId, string date, ReconcileProviderHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var businessDate))
            {
                return Error.Validation("date_invalid", "Give the business date as yyyy-MM-dd.").ToHttpResult(context);
            }

            var (run, error) = await handler.HandleAsync(providerId, businessDate, cancellationToken);
            return run is not null
                ? Results.Ok(run)
                : error == ReconcileProviderHandler.ProviderUnknown
                    ? Error.NotFound(error, $"No provider {providerId}.").ToHttpResult(context)
                    : Error.Unavailable(error!, "The provider's report could not be fetched; try again.").ToHttpResult(context);
        });
        admin.MapGet("/", async (string? providerId, int? limit, ICasinoStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.ListReconciliationsAsync(providerId, limit ?? 30, cancellationToken)));
        return endpoints;
    }
}
