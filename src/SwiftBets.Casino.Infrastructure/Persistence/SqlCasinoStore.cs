using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;
using SwiftBets.Contracts.Casino;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Money;

namespace SwiftBets.Casino.Infrastructure.Persistence;

public sealed class SqlCasinoStore(ISqlConnectionFactory connections, IOutbox outbox, TimeProvider time) : ICasinoStore
{
    private const int UniqueViolation = 2627;
    private static readonly SqlResources Sql = SqlResources.For<SqlCasinoStore>();

    public async Task<bool> IsProviderEnabledAsync(string providerId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(Sql.Get("Casino.ProviderEnabled"), new { ProviderId = providerId }, cancellationToken: cancellationToken));
    }

    public async Task CreateSessionAsync(GameSession session, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Casino.CreateSession"), session, cancellationToken: cancellationToken));
    }

    public async Task<GameSession?> FindSessionAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<GameSession>(new CommandDefinition(Sql.Get("Casino.FindSession"), new { TokenHash = tokenHash }, cancellationToken: cancellationToken));
    }

    public async Task<StoredTransaction?> FindTransactionAsync(string providerId, string providerTransactionId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TransactionRow>(new CommandDefinition(Sql.Get("Casino.FindTransaction"),
            new { ProviderId = providerId, ProviderTransactionId = providerTransactionId }, cancellationToken: cancellationToken));
        return row?.ToStored();
    }

    public async Task<bool> IsRolledBackAsync(string providerId, string betProviderTransactionId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(Sql.Get("Casino.IsRolledBack"), new { ProviderId = providerId, Reference = betProviderTransactionId }, cancellationToken: cancellationToken));
    }

    public async Task<RecordOutcome> RecordAsync(StoredTransaction transaction, bool takeFreeSpin, string? markRolledBack, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        if (takeFreeSpin && await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Casino.TakeFreeSpin"),
                new { transaction.PunterId, transaction.GameId, Now = time.GetUtcNow() }, tx, cancellationToken: cancellationToken)) == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return RecordOutcome.NoFreeSpins;
        }

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Casino.InsertTransaction"), new
            {
                transaction.TransactionId, transaction.ProviderId, transaction.ProviderTransactionId, transaction.RoundId, transaction.PunterId, transaction.GameId,
                Kind = (byte)transaction.Kind, transaction.Amount, transaction.Currency, Status = (byte)transaction.Status, transaction.ReferencesProviderTransactionId, transaction.CreatedAt,
            }, tx, cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (ex.Number == UniqueViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return RecordOutcome.Duplicate;
        }

        if (markRolledBack is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Casino.MarkRolledBack"), new { transaction.ProviderId, Reference = markRolledBack }, tx, cancellationToken: cancellationToken));
        }

        var payload = new CasinoTransactionV1(transaction.TransactionId, transaction.ProviderId, transaction.ProviderTransactionId, transaction.RoundId, transaction.PunterId,
            transaction.GameId, transaction.Kind, new Money(transaction.Amount, transaction.Currency), transaction.CreatedAt);
        await outbox.EnqueueAsync(tx, Topics.CasinoTransaction, transaction.PunterId.ToString(),
            EventEnvelope<CasinoTransactionV1>.Create(payload, time.GetUtcNow(), CorrelationContext.CorrelationId ?? CorrelationContext.NewId()), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return RecordOutcome.Recorded;
    }

    public async Task<FreeSpinGrant> GrantFreeSpinsAsync(Guid punterId, string gameId, int spins, DateTimeOffset expiresAt, Guid operatorId, CancellationToken cancellationToken)
    {
        var grant = new FreeSpinGrant(Guid.CreateVersion7(), punterId, gameId, spins, spins, expiresAt);
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Casino.GrantFreeSpins"),
            new { grant.GrantId, PunterId = punterId, GameId = gameId, Spins = spins, ExpiresAt = expiresAt, CreatedBy = operatorId, CreatedAt = time.GetUtcNow() }, cancellationToken: cancellationToken));
        return grant;
    }

    public async Task<IReadOnlyList<FreeSpinGrant>> ListFreeSpinsAsync(Guid punterId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<FreeSpinGrant>(new CommandDefinition(Sql.Get("Casino.ListFreeSpins"), new { PunterId = punterId }, cancellationToken: cancellationToken))];
    }

    private sealed record TransactionRow(Guid TransactionId, string ProviderId, string ProviderTransactionId, string RoundId, Guid PunterId, string GameId,
        byte Kind, long Amount, string Currency, byte Status, string? ReferencesProviderTransactionId, DateTimeOffset CreatedAt)
    {
        public StoredTransaction ToStored() => new(TransactionId, ProviderId, ProviderTransactionId, RoundId, PunterId, GameId, (CasinoTransactionKind)Kind, Amount, Currency,
            (TransactionStatus)Status, ReferencesProviderTransactionId, CreatedAt);
    }
}
