namespace SwiftBets.Casino.Domain;

public enum TransactionStatus
{
    Applied = 1,

    /// <summary>A bet the provider later rolled back; its stake went back to the player.</summary>
    RolledBack = 2,

    /// <summary>A rollback for a bet we never saw: stored and acknowledged, no money moved.</summary>
    AcceptedUnseenRollback = 3,
}
