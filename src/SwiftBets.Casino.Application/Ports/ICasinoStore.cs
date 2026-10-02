namespace SwiftBets.Casino.Application.Ports;

public interface ICasinoStore
{
    Task<bool> IsProviderEnabledAsync(string providerId, CancellationToken cancellationToken);

    /// <summary><see cref="WalletModels.Seamless"/> or <see cref="WalletModels.Transfer"/>; null for an unknown provider.</summary>
    Task<string?> GetWalletModelAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>Our transactions with a provider created in [from, to): what reconciliation compares with its report.</summary>
    Task<IReadOnlyList<ReportedTransaction>> ListTransactionsAsync(string providerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Stores the run and enqueues its ProviderReconciliationV1 in one transaction.</summary>
    Task RecordReconciliationAsync(ReconciliationRun run, CancellationToken cancellationToken);

    Task<bool> HasReconciliationAsync(string providerId, DateOnly businessDate, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReconciliationRun>> ListReconciliationsAsync(string? providerId, int limit, CancellationToken cancellationToken);

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

/// <summary>A provider's own report of what it sent us for a business day.</summary>
public interface IProviderReports
{
    /// <summary>The report, or null when the provider could not be reached or refused us.</summary>
    Task<IReadOnlyList<ReportedTransaction>?> GetAsync(string providerId, DateOnly businessDate, CancellationToken cancellationToken);
}

/// <summary>Responsible-gambling state from compliance; launching is refused while it is not loaded.</summary>
public interface IRestrictions
{
    bool IsReady { get; }

    bool IsCasinoRestricted(Guid punterId, DateTimeOffset now);
}
