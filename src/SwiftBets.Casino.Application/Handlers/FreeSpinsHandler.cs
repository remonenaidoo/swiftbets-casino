using SwiftBets.Casino.Application.Ports;

namespace SwiftBets.Casino.Application.Handlers;

/// <summary>Operators grant free spins on one game; each free-spin bet from the provider takes one.</summary>
public sealed class FreeSpinsHandler(ICasinoStore store, TimeProvider time)
{
    public sealed record Grant(Guid PunterId, string? GameId, int Spins, DateTimeOffset ExpiresAt);

    public async Task<(FreeSpinGrant? Grant, string? Error)> GrantAsync(Grant request, Guid operatorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PunterId == Guid.Empty || string.IsNullOrWhiteSpace(request.GameId) || request.GameId.Length > 80)
        {
            return (null, "invalid_grant");
        }

        if (request.Spins is < 1 or > 1000)
        {
            return (null, "spins_out_of_range");
        }

        if (request.ExpiresAt <= time.GetUtcNow())
        {
            return (null, "expiry_in_past");
        }

        return (await store.GrantFreeSpinsAsync(request.PunterId, request.GameId, request.Spins, request.ExpiresAt, operatorId, cancellationToken), null);
    }

    public Task<IReadOnlyList<FreeSpinGrant>> ListAsync(Guid punterId, CancellationToken cancellationToken) => store.ListFreeSpinsAsync(punterId, cancellationToken);
}
