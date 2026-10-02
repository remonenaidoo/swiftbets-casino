using Microsoft.Extensions.Options;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;
using SwiftBets.Contracts.Casino;

namespace SwiftBets.Casino.Application.Handlers;

public enum WalletAction
{
    Balance,
    Bet,
    Win,
    Rollback,

    /// <summary>Transfer-wallet providers: move money from the wallet into the provider's session.</summary>
    TransferIn,

    /// <summary>Transfer-wallet providers: move the session's balance back to the wallet.</summary>
    TransferOut,
}

/// <summary>A seamless-wallet callback body, as the provider sends it.</summary>
public sealed record WalletCallback(
    string? SessionToken,
    string? ProviderTransactionId,
    string? RoundId,
    string? GameId,
    long Amount,
    string? Currency,
    bool FreeSpin = false,
    string? ReferenceTransactionId = null);

public sealed record CallbackReply(string Status, long? Balance, Guid? TransactionId)
{
    public const string Ok = "ok";
    public const string InvalidRequest = "invalid_request";
    public const string SessionInvalid = "session_invalid";
    public const string CurrencyMismatch = "currency_mismatch";
    public const string InsufficientFunds = "insufficient_funds";
    public const string LimitExceeded = "limit_exceeded";
    public const string AccountRestricted = "account_restricted";
    public const string WalletRefused = "wallet_refused";
    public const string WalletUnavailable = "wallet_unavailable";
    public const string NoFreeSpins = "no_free_spins";
    public const string BetRolledBack = "bet_rolled_back";
    public const string InvalidReference = "invalid_reference";
    public const string WalletModelMismatch = "wallet_model_mismatch";
}

/// <summary>
/// Applies a provider's bet, win or rollback exactly once, keyed on its own transaction id. Money moves through the
/// wallet under <c>casino:{provider}:{transaction}</c>, so a retry after a crash is safe; a duplicate callback
/// returns the original transaction and moves nothing. A rollback for a bet never seen is stored and acknowledged,
/// and that bet is refused if it arrives later.
/// </summary>
public sealed class WalletCallbackHandler(ICasinoStore store, IWalletPort wallet, IOptions<CasinoOptions> options, TimeProvider time)
{
    public async Task<CallbackReply> HandleAsync(string providerId, WalletAction action, WalletCallback callback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (string.IsNullOrEmpty(callback.SessionToken) || callback.Amount < 0)
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        var session = await store.FindSessionAsync(SessionToken.Hash(callback.SessionToken), cancellationToken);
        var now = time.GetUtcNow();
        if (session is null || session.ProviderId != providerId || (action is WalletAction.Bet or WalletAction.TransferIn && now >= session.ExpiresAt))
        {
            return Reply(CallbackReply.SessionInvalid);
        }

        // Seamless providers bet and win per round; transfer providers only move a session's money in and out.
        var transfer = action is WalletAction.TransferIn or WalletAction.TransferOut;
        if (action != WalletAction.Balance && await store.GetWalletModelAsync(providerId, cancellationToken) is var model && model != (transfer ? WalletModels.Transfer : WalletModels.Seamless))
        {
            return Reply(CallbackReply.WalletModelMismatch);
        }

        if (action == WalletAction.Balance)
        {
            return await WithBalanceAsync(CallbackReply.Ok, session, null, cancellationToken);
        }

        if (!IsValidId(callback.ProviderTransactionId) || !IsValidId(callback.RoundId))
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        if (callback.Currency is { } currency && currency != session.Currency)
        {
            return Reply(CallbackReply.CurrencyMismatch);
        }

        var ptx = callback.ProviderTransactionId!;
        if (await store.FindTransactionAsync(providerId, ptx, cancellationToken) is { } existing)
        {
            return await WithBalanceAsync(CallbackReply.Ok, session, existing.TransactionId, cancellationToken);
        }

        return action switch
        {
            WalletAction.Bet => await BetAsync(session, callback, ptx, now, cancellationToken),
            WalletAction.Win => await WinAsync(session, callback, ptx, now, cancellationToken),
            WalletAction.TransferIn => await TransferInAsync(session, callback, ptx, now, cancellationToken),
            WalletAction.TransferOut => await TransferOutAsync(session, callback, ptx, now, cancellationToken),
            _ => await RollbackAsync(session, callback, ptx, now, cancellationToken),
        };
    }

    private async Task<CallbackReply> TransferInAsync(GameSession session, WalletCallback callback, string ptx, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (callback.Amount == 0)
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        var debit = await wallet.DebitAsync(Key(session, ptx), session.PunterId, callback.Amount, session.Currency, $"casino transfer in {ptx}", cancellationToken);
        return debit.Status != WalletStatus.Succeeded
            ? Reply(Refusal(debit))
            : await RecordAsync(session, Row(session, callback, ptx, CasinoTransactionKind.TransferIn, callback.Amount, TransactionStatus.Applied, null, now), null, debit.Available, cancellationToken);
    }

    private async Task<CallbackReply> TransferOutAsync(GameSession session, WalletCallback callback, string ptx, DateTimeOffset now, CancellationToken cancellationToken)
    {
        long? balance = null;
        if (callback.Amount > 0)
        {
            var credit = await wallet.CreditAsync(Key(session, ptx), session.PunterId, callback.Amount, session.Currency, $"casino transfer out {ptx}", cancellationToken);
            if (credit.Status != WalletStatus.Succeeded)
            {
                return Reply(Refusal(credit));
            }

            balance = credit.Available;
        }

        return await RecordAsync(session, Row(session, callback, ptx, CasinoTransactionKind.TransferOut, callback.Amount, TransactionStatus.Applied, null, now), null, balance, cancellationToken);
    }

    private async Task<CallbackReply> BetAsync(GameSession session, WalletCallback callback, string ptx, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await store.IsRolledBackAsync(session.ProviderId, ptx, cancellationToken))
        {
            return Reply(CallbackReply.BetRolledBack);
        }

        if (callback.FreeSpin)
        {
            var spin = Row(session, callback, ptx, CasinoTransactionKind.FreeSpinBet, 0, TransactionStatus.Applied, null, now);
            return await store.RecordAsync(spin, takeFreeSpin: true, markRolledBack: null, cancellationToken) switch
            {
                RecordOutcome.NoFreeSpins => Reply(CallbackReply.NoFreeSpins),
                RecordOutcome.Duplicate => await DuplicateAsync(session, ptx, cancellationToken),
                _ => await WithBalanceAsync(CallbackReply.Ok, session, spin.TransactionId, cancellationToken),
            };
        }

        if (callback.Amount == 0)
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        var debit = await wallet.DebitAsync(Key(session, ptx), session.PunterId, callback.Amount, session.Currency, $"casino bet {ptx}", cancellationToken);
        if (debit.Status != WalletStatus.Succeeded)
        {
            return Reply(Refusal(debit));
        }

        return await RecordAsync(session, Row(session, callback, ptx, CasinoTransactionKind.Bet, callback.Amount, TransactionStatus.Applied, null, now), null, debit.Available, cancellationToken);
    }

    private async Task<CallbackReply> WinAsync(GameSession session, WalletCallback callback, string ptx, DateTimeOffset now, CancellationToken cancellationToken)
    {
        long? balance = null;
        if (callback.Amount > 0)
        {
            var credit = await wallet.CreditAsync(Key(session, ptx), session.PunterId, callback.Amount, session.Currency, $"casino win {ptx}", cancellationToken);
            if (credit.Status != WalletStatus.Succeeded)
            {
                return Reply(Refusal(credit));
            }

            balance = credit.Available;
        }

        return await RecordAsync(session, Row(session, callback, ptx, CasinoTransactionKind.Win, callback.Amount, TransactionStatus.Applied, null, now), null, balance, cancellationToken);
    }

    private async Task<CallbackReply> RollbackAsync(GameSession session, WalletCallback callback, string ptx, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!IsValidId(callback.ReferenceTransactionId))
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        var reference = callback.ReferenceTransactionId!;
        var bet = await store.FindTransactionAsync(session.ProviderId, reference, cancellationToken);
        if (bet is null)
        {
            // Store-and-accept: the bet may still be in flight; when it arrives it is refused, so no money ever moves.
            var unseen = Row(session, callback, ptx, CasinoTransactionKind.Rollback, 0, TransactionStatus.AcceptedUnseenRollback, reference, now);
            return await RecordAsync(session, unseen, null, null, cancellationToken);
        }

        if (bet.Kind is not (CasinoTransactionKind.Bet or CasinoTransactionKind.FreeSpinBet))
        {
            return Reply(CallbackReply.InvalidReference);
        }

        var refund = bet.Status == TransactionStatus.Applied ? bet.Amount : 0;
        long? balance = null;
        if (refund > 0)
        {
            var credit = await wallet.CreditAsync(Key(session, ptx), session.PunterId, refund, bet.Currency, $"casino rollback {ptx} of {reference}", cancellationToken);
            if (credit.Status != WalletStatus.Succeeded)
            {
                return Reply(Refusal(credit));
            }

            balance = credit.Available;
        }

        var rollback = Row(session, callback, ptx, CasinoTransactionKind.Rollback, refund, TransactionStatus.Applied, reference, now);
        return await RecordAsync(session, rollback, bet.Status == TransactionStatus.Applied ? reference : null, balance, cancellationToken);
    }

    private async Task<CallbackReply> RecordAsync(GameSession session, StoredTransaction row, string? markRolledBack, long? balance, CancellationToken cancellationToken) =>
        await store.RecordAsync(row, takeFreeSpin: false, markRolledBack, cancellationToken) == RecordOutcome.Duplicate
            ? await DuplicateAsync(session, row.ProviderTransactionId, cancellationToken)
            : balance is { } known ? new CallbackReply(CallbackReply.Ok, known, row.TransactionId) : await WithBalanceAsync(CallbackReply.Ok, session, row.TransactionId, cancellationToken);

    private async Task<CallbackReply> DuplicateAsync(GameSession session, string ptx, CancellationToken cancellationToken) =>
        await WithBalanceAsync(CallbackReply.Ok, session, (await store.FindTransactionAsync(session.ProviderId, ptx, cancellationToken))?.TransactionId, cancellationToken);

    private async Task<CallbackReply> WithBalanceAsync(string status, GameSession session, Guid? transactionId, CancellationToken cancellationToken)
    {
        var balance = await wallet.GetBalanceAsync(session.PunterId, cancellationToken);
        return new CallbackReply(status, balance.Available, transactionId);
    }

    private StoredTransaction Row(GameSession session, WalletCallback callback, string ptx, CasinoTransactionKind kind, long amount, TransactionStatus status, string? reference, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), session.ProviderId, ptx, callback.RoundId!, session.PunterId, callback.GameId ?? session.GameId, kind, amount,
            callback.Currency ?? options.Value.Currency, status, reference, now);

    private static string Key(GameSession session, string ptx) => $"casino:{session.ProviderId}:{ptx}";

    private static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 100;

    private static string Refusal(WalletResult result) => result.Status == WalletStatus.Unavailable
        ? CallbackReply.WalletUnavailable
        : result.FailureCode switch
        {
            "InsufficientFunds" => CallbackReply.InsufficientFunds,
            "LimitExceeded" => CallbackReply.LimitExceeded,
            "AccountRestricted" => CallbackReply.AccountRestricted,
            _ => CallbackReply.WalletRefused,
        };

    private static CallbackReply Reply(string status) => new(status, null, null);
}
