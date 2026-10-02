using Dapper;
using Npgsql;
using SwiftBets.BuildingBlocks.Persistence;

namespace SwiftBets.Casino.Simulator;

public sealed record ReportLine(string ProviderTransactionId, string Kind, long Amount);

public sealed record SimGame(string GameId, string Name, string ProviderId, string WalletModel, long MinBet);

/// <summary>The simulated providers' own books: every wallet call the gateway accepted, which daily reports are built from.</summary>
public sealed class Ledger(NpgsqlDataSource dataSource, TimeProvider time)
{
    private static readonly SqlResources Sql = SqlResources.For<Ledger>();

    public async Task RecordAsync(string providerId, string ptx, string kind, long amount, string currency, string roundId, string gameId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Sim.Record"), new
        {
            ProviderId = providerId, ProviderTransactionId = ptx, Kind = kind, Amount = amount, Currency = currency, RoundId = roundId, GameId = gameId, SentAt = time.GetUtcNow().UtcDateTime,
        }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ReportLine>> ReportAsync(string providerId, DateOnly day, CancellationToken cancellationToken)
    {
        var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<ReportLine>(new CommandDefinition(Sql.Get("Sim.Report"), new { ProviderId = providerId, From = from, To = from.AddDays(1) }, cancellationToken: cancellationToken))];
    }

    public async Task<SimGame?> GameAsync(string gameId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<SimGame>(new CommandDefinition(Sql.Get("Sim.Game"), new { GameId = gameId }, cancellationToken: cancellationToken));
    }
}
