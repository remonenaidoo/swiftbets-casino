using SwiftBets.Casino.Domain;
using SwiftBets.Contracts.Casino;

namespace SwiftBets.Casino.Application.Ports;

public sealed record GameSession(Guid SessionId, byte[] TokenHash, Guid PunterId, string ProviderId, string GameId, string Currency, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record StoredTransaction(
    Guid TransactionId, string ProviderId, string ProviderTransactionId, string RoundId, Guid PunterId, string GameId,
    CasinoTransactionKind Kind, long Amount, string Currency, TransactionStatus Status, string? ReferencesProviderTransactionId, DateTimeOffset CreatedAt);

public sealed record FreeSpinGrant(Guid GrantId, Guid PunterId, string GameId, int Granted, int Remaining, DateTimeOffset ExpiresAt);

public enum RecordOutcome
{
    Recorded,

    /// <summary>The provider transaction id was already recorded; nothing changed.</summary>
    Duplicate,

    /// <summary>A free-spin bet with no grant left for the game; nothing changed.</summary>
    NoFreeSpins,
}

public enum WalletStatus
{
    Succeeded,
    Refused,
    Unavailable,
}

public sealed record WalletResult(WalletStatus Status, string? FailureCode, long? Available);

public static class WalletModels
{
    public const string Seamless = "seamless";
    public const string Transfer = "transfer";
}

/// <summary>One transaction as either side records it, for reconciliation.</summary>
public sealed record ReportedTransaction(string ProviderTransactionId, CasinoTransactionKind Kind, long Amount);

public sealed record ReconciliationRun(
    Guid RunId, string ProviderId, DateOnly BusinessDate, long OurNet, long ProviderNet, long Drift, int MissingOnOurSide, int MissingOnProviderSide,
    ReconciliationStatus Status, string Currency, DateTimeOffset ReconciledAt);
