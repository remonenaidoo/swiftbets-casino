namespace SwiftBets.Casino.Application.Ports;

public interface ICasinoStore
{
    Task<bool> IsProviderEnabledAsync(string providerId, CancellationToken cancellationToken);

    Task CreateSessionAsync(GameSession session, CancellationToken cancellationToken);

    Task<GameSession?> FindSessionAsync(byte[] tokenHash, CancellationToken cancellationToken);

    Task<StoredTransaction?> FindTransactionAsync(string providerId, string providerTransactionId, CancellationToken cancellationToken);

    /// <summary>True when a rollback naming this bet was already recorded, whether the bet was seen or not.</summary>
    Task<bool> IsRolledBackAsync(string providerId, string betProviderTransactionId, CancellationToken cancellationToken);

    /// <summary>
    /// In one SQL transaction: takes a free spin when asked, inserts the row, marks a rolled-back bet, and enqueues its
    /// CasinoTransactionV1. A duplicate provider transaction id or a missing free spin changes nothing.
    /// </summary>
    Task<RecordOutcome> RecordAsync(StoredTransaction transaction, bool takeFreeSpin, string? markRolledBack, CancellationToken cancellationToken);

    Task<FreeSpinGrant> GrantFreeSpinsAsync(Guid punterId, string gameId, int spins, DateTimeOffset expiresAt, Guid operatorId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FreeSpinGrant>> ListFreeSpinsAsync(Guid punterId, CancellationToken cancellationToken);
}

public interface IWalletPort
{
    Task<WalletResult> DebitAsync(string idempotencyKey, Guid punterId, long amount, string currency, string reference, CancellationToken cancellationToken);

    Task<WalletResult> CreditAsync(string idempotencyKey, Guid punterId, long amount, string currency, string reference, CancellationToken cancellationToken);

    Task<WalletResult> GetBalanceAsync(Guid punterId, CancellationToken cancellationToken);
}

/// <summary>Responsible-gambling state from compliance; launching is refused while it is not loaded.</summary>
public interface IRestrictions
{
    bool IsReady { get; }

    bool IsCasinoRestricted(Guid punterId, DateTimeOffset now);
}
