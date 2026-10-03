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

    /// <summary>The reply body stored with the transaction the first time; a repeat returns it unchanged.</summary>
    public string? Original { get; init; }
}

/// <summary>
/// Applies a provider's bet, win or rollback exactly once, keyed on its own transaction id. Money moves through the
/// wallet under <c>casino:{provider}:{transaction}</c>, so a retry after a crash is safe; a duplicate callback
/// returns the original reply and moves nothing. A rollback for a bet never seen is stored and acknowledged,
/// and that bet is refused if it arrives later, checked again under the insert's lock so a race cannot slip past.
/// Bets are refused while compliance says the player may not play, or cannot say.
/// </summary>
public sealed class WalletCallbackHandler(ICasinoStore store, IWalletPort wallet, IRestrictions restrictions, IOptions<CasinoOptions> options, TimeProvider time)
{
    public async Task<CallbackReply> HandleAsync(string providerId, WalletAction action, WalletCallback callback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (string.IsNullOrEmpty(callback.SessionToken) || callback.Amount < 0)
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        var session = await store.FindSessionAsync(SessionToken.Hash(callback.SessionToken), cancellationToken);
        if (session is null || session.ProviderId != providerId)
        {
            return Reply(CallbackReply.SessionInvalid);
        }

        return await ApplyAsync(session, action, callback, null, cancellationToken);
    }

    /// <summary>
    /// Applies a callback to a session the caller already resolved. <paramref name="replyBody"/> renders the reply in
    /// the provider's own format; it is stored with the transaction so a repeat gets exactly the same answer.
    /// </summary>
    public async Task<CallbackReply> ApplyAsync(GameSession session, WalletAction action, WalletCallback callback, Func<CallbackReply, string>? replyBody, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(callback);
        if (callback.Amount < 0)
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        var now = time.GetUtcNow();
        if (action is WalletAction.Bet or WalletAction.TransferIn && now >= session.ExpiresAt)
        {
            return Reply(CallbackReply.SessionInvalid);
        }

        // Seamless providers bet and win per round; transfer providers only move a session's money in and out.
        var providerId = session.ProviderId;
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
            return await RepeatAsync(session, existing, cancellationToken);
        }

        var context = new Call(session, callback, ptx, now, replyBody);
        return action switch
        {
            WalletAction.Bet => await BetAsync(context, cancellationToken),
            WalletAction.Win => await WinAsync(context, cancellationToken),
            WalletAction.TransferIn => await TransferInAsync(context, cancellationToken),
            WalletAction.TransferOut => await TransferOutAsync(context, cancellationToken),
            _ => await RollbackAsync(context, retried: false, cancellationToken),
        };
    }

    private sealed record Call(GameSession Session, WalletCallback Callback, string Ptx, DateTimeOffset Now, Func<CallbackReply, string>? ReplyBody);

    private async Task<CallbackReply> TransferInAsync(Call call, CancellationToken cancellationToken)
    {
        if (call.Callback.Amount == 0)
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        if (await RestrictionAsync(call.Session, cancellationToken) is { } refused)
        {
            return refused;
        }

        var debit = await wallet.DebitAsync(Key(call.Session, call.Ptx), call.Session.PunterId, call.Callback.Amount, call.Session.Currency, $"casino transfer in {call.Ptx}", cancellationToken);
        return debit.Status != WalletStatus.Succeeded
            ? Reply(Refusal(debit))
            : await RecordAsync(call, Row(call, CasinoTransactionKind.TransferIn, call.Callback.Amount, TransactionStatus.Applied, null), null, debit.Available, cancellationToken);
    }

    private async Task<CallbackReply> TransferOutAsync(Call call, CancellationToken cancellationToken)
    {
        var (refusal, balance) = await CreditAsync(call, call.Callback.Amount, $"casino transfer out {call.Ptx}", cancellationToken);
        return refusal ?? await RecordAsync(call, Row(call, CasinoTransactionKind.TransferOut, call.Callback.Amount, TransactionStatus.Applied, null), null, balance, cancellationToken);
    }

    private async Task<CallbackReply> BetAsync(Call call, CancellationToken cancellationToken)
    {
        if (await store.IsRolledBackAsync(call.Session.ProviderId, call.Ptx, cancellationToken))
        {
            return Reply(CallbackReply.BetRolledBack);
        }

        if (!call.Callback.FreeSpin && call.Callback.Amount == 0)
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        if (await RestrictionAsync(call.Session, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (call.Callback.FreeSpin)
        {
            return await RecordAsync(call, Row(call, CasinoTransactionKind.FreeSpinBet, 0, TransactionStatus.Applied, null), null, null, cancellationToken, takeFreeSpin: true);
        }

        var debit = await wallet.DebitAsync(Key(call.Session, call.Ptx), call.Session.PunterId, call.Callback.Amount, call.Session.Currency, $"casino bet {call.Ptx}", cancellationToken);
        if (debit.Status != WalletStatus.Succeeded)
        {
            return Reply(Refusal(debit));
        }

        return await RecordAsync(call, Row(call, CasinoTransactionKind.Bet, call.Callback.Amount, TransactionStatus.Applied, null), null, debit.Available, cancellationToken);
    }

    private async Task<CallbackReply> WinAsync(Call call, CancellationToken cancellationToken)
    {
        var (refusal, balance) = await CreditAsync(call, call.Callback.Amount, $"casino win {call.Ptx}", cancellationToken);
        return refusal ?? await RecordAsync(call, Row(call, CasinoTransactionKind.Win, call.Callback.Amount, TransactionStatus.Applied, null), null, balance, cancellationToken);
    }

    private async Task<CallbackReply> RollbackAsync(Call call, bool retried, CancellationToken cancellationToken)
    {
        if (!IsValidId(call.Callback.ReferenceTransactionId))
        {
            return Reply(CallbackReply.InvalidRequest);
        }

        var reference = call.Callback.ReferenceTransactionId!;
        var bet = await store.FindTransactionAsync(call.Session.ProviderId, reference, cancellationToken);
        if (bet is null)
        {
            // Store-and-accept: the bet may still be in flight; when it arrives it is refused, so no money ever moves.
            var unseen = Row(call, CasinoTransactionKind.Rollback, 0, TransactionStatus.AcceptedUnseenRollback, reference);
            var reply = await RecordOrNullAsync(call, unseen, null, null, cancellationToken);
            return reply is null && !retried ? await RollbackAsync(call, retried: true, cancellationToken) : reply ?? Reply(CallbackReply.WalletUnavailable);
        }

        if (bet.Kind is not (CasinoTransactionKind.Bet or CasinoTransactionKind.FreeSpinBet))
        {
            return Reply(CallbackReply.InvalidReference);
        }

        // The refund is the stake we recorded, never an amount the provider sends.
        var refund = bet.Status == TransactionStatus.Applied ? bet.Amount : 0;
        var (refusal, balance) = await CreditAsync(call, refund, $"casino rollback {call.Ptx} of {reference}", cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var rollback = Row(call, CasinoTransactionKind.Rollback, refund, TransactionStatus.Applied, reference);
        return await RecordAsync(call, rollback, bet.Status == TransactionStatus.Applied ? reference : null, balance, cancellationToken);
    }

    private async Task<(CallbackReply? Refusal, long? Balance)> CreditAsync(Call call, long amount, string reference, CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            return (null, null);
        }

        var credit = await wallet.CreditAsync(Key(call.Session, call.Ptx), call.Session.PunterId, amount, call.Session.Currency, reference, cancellationToken);
        return credit.Status == WalletStatus.Succeeded ? (null, credit.Available) : (Reply(Refusal(credit)), null);
    }

    private async Task<CallbackReply?> RestrictionAsync(GameSession session, CancellationToken cancellationToken) =>
        await restrictions.CheckAsync(session.PunterId, time.GetUtcNow(), cancellationToken) switch
        {
            RestrictionCheck.Clear => null,
            RestrictionCheck.Restricted => Reply(CallbackReply.AccountRestricted),
            _ => Reply(CallbackReply.WalletUnavailable),
        };

    /// <summary>Records the row with its rendered reply; null only for a refund marker whose bet arrived meanwhile.</summary>
    private async Task<CallbackReply> RecordAsync(Call call, StoredTransaction row, string? markRolledBack, long? balance, CancellationToken cancellationToken, bool takeFreeSpin = false) =>
        await RecordOrNullAsync(call, row, markRolledBack, balance, cancellationToken, takeFreeSpin) ?? Reply(CallbackReply.WalletUnavailable);

    private async Task<CallbackReply?> RecordOrNullAsync(Call call, StoredTransaction row, string? markRolledBack, long? balance, CancellationToken cancellationToken, bool takeFreeSpin = false)
    {
        balance ??= (await wallet.GetBalanceAsync(call.Session.PunterId, cancellationToken)).Available;
        var reply = new CallbackReply(CallbackReply.Ok, balance, row.TransactionId);
        switch (await store.RecordAsync(row with { Reply = call.ReplyBody?.Invoke(reply) }, takeFreeSpin, markRolledBack, cancellationToken))
        {
            case RecordOutcome.Recorded:
                return reply;
            case RecordOutcome.NoFreeSpins:
                return Reply(CallbackReply.NoFreeSpins);
            case RecordOutcome.BetAlreadyRecorded:
                return null;
            case RecordOutcome.RolledBack:
                // The refund landed between our check and the insert: hand the stake straight back.
                if (row.Amount > 0)
                {
                    await wallet.CreditAsync(Key(call.Session, call.Ptx) + ":void", call.Session.PunterId, row.Amount, row.Currency, $"casino bet {call.Ptx} refused after its refund", cancellationToken);
                }

                return Reply(CallbackReply.BetRolledBack);
            default:
                return await store.FindTransactionAsync(call.Session.ProviderId, call.Ptx, cancellationToken) is { } existing
                    ? await RepeatAsync(call.Session, existing, cancellationToken)
                    : await WithBalanceAsync(CallbackReply.Ok, call.Session, null, cancellationToken);
        }
    }

    private async Task<CallbackReply> RepeatAsync(GameSession session, StoredTransaction existing, CancellationToken cancellationToken) =>
        existing.Reply is { } original
            ? new CallbackReply(CallbackReply.Ok, null, existing.TransactionId) { Original = original }
            : await WithBalanceAsync(CallbackReply.Ok, session, existing.TransactionId, cancellationToken);

    private async Task<CallbackReply> WithBalanceAsync(string status, GameSession session, Guid? transactionId, CancellationToken cancellationToken)
    {
        var balance = await wallet.GetBalanceAsync(session.PunterId, cancellationToken);
        return new CallbackReply(status, balance.Available, transactionId);
    }

    private StoredTransaction Row(Call call, CasinoTransactionKind kind, long amount, TransactionStatus status, string? reference) =>
        new(Guid.CreateVersion7(), call.Session.ProviderId, call.Ptx, call.Callback.RoundId!, call.Session.PunterId, call.Callback.GameId ?? call.Session.GameId, kind, amount,
            call.Callback.Currency ?? options.Value.Currency, status, reference, call.Now);

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
