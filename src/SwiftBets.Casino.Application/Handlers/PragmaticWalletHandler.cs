using System.Globalization;
using System.Text.Json;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Application.Handlers;

/// <summary>Pragmatic Play's error codes; 0 is success, 100 asks the provider to retry, 120 tells it not to.</summary>
public static class PragmaticError
{
    public const int Success = 0;
    public const int InsufficientBalance = 1;
    public const int PlayerNotFound = 2;
    public const int BetNotAllowed = 3;
    public const int TokenInvalid = 4;
    public const int InvalidHash = 5;
    public const int PlayerFrozen = 6;
    public const int BadParameters = 7;
    public const int GameNotFound = 8;
    public const int BetLimitReached = 50;
    public const int InternalRetry = 100;
    public const int InternalNoRetry = 120;
    public const int TokenExpired = 130;
    public const int RegulatoryLimit = 210;
}

/// <summary>
/// Pragmatic Play's seamless wallet, translated onto our own: every call is checked against the provider's MD5 hash,
/// then applied through <see cref="WalletCallbackHandler"/>, so money only ever moves through the wallet, once per
/// <c>reference</c>. The JSON reply is stored with the transaction and a repeat gets it back byte for byte. A refund
/// pays back the stake we recorded, whatever amount the request names.
/// </summary>
public sealed class PragmaticWalletHandler(ICasinoStore store, WalletCallbackHandler callbacks, IWalletPort wallet, TimeProvider time)
{
    public static readonly IReadOnlySet<string> Methods = new HashSet<string>(StringComparer.Ordinal)
    {
        "authenticate", "balance", "bet", "result", "bonusWin", "jackpotWin", "promoWin", "endRound", "refund", "adjustment",
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<string> HandleAsync(ProviderSettings provider, string method, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(form);
        if (!PragmaticHash.Verify(form, provider.Secret))
        {
            return Error(PragmaticError.InvalidHash, "Invalid hash code");
        }

        if (!provider.Enabled)
        {
            return Error(PragmaticError.InternalNoRetry, "Provider is switched off");
        }

        if (method == "authenticate")
        {
            return await AuthenticateAsync(provider, form, cancellationToken);
        }

        if (!Guid.TryParse(Get(form, "userId"), out var punterId))
        {
            return Error(PragmaticError.PlayerNotFound, "Player not found");
        }

        var session = Get(form, "token") is { Length: > 0 } token ? await store.FindSessionAsync(SessionToken.Hash(token), cancellationToken) : null;
        session ??= await store.FindLatestSessionAsync(punterId, provider.ProviderId, cancellationToken);
        if (session is null || session.PunterId != punterId || session.ProviderId != provider.ProviderId)
        {
            return Error(PragmaticError.PlayerNotFound, "Player not found");
        }

        switch (method)
        {
            case "balance":
            case "endRound":
                return Cash(await wallet.GetBalanceAsync(punterId, cancellationToken), out var cash) is { } refused
                    ? refused
                    : Render(new() { ["currency"] = session.Currency, ["cash"] = cash, ["bonus"] = 0m }, PragmaticError.Success);
            case "refund":
                return await MoveAsync(session, WalletAction.Rollback, form, "refund:" + Get(form, "reference"), 0, Get(form, "reference"), includeCash: false, cancellationToken);
            case "adjustment" when TryAmount(form, out var adjustment) && adjustment < 0:
                return await MoveAsync(session, WalletAction.Bet, form, Get(form, "reference"), -adjustment, null, includeCash: true, cancellationToken);
            case "bet":
            case "result":
            case "bonusWin":
            case "jackpotWin":
            case "promoWin":
            case "adjustment":
                if (!TryAmount(form, out var amount) || amount < 0)
                {
                    return Error(PragmaticError.BadParameters, "Bad amount");
                }

                return await MoveAsync(session, method == "bet" ? WalletAction.Bet : WalletAction.Win, form, Get(form, "reference"), amount, null, includeCash: true, cancellationToken);
            default:
                return Error(PragmaticError.BadParameters, "Unknown method");
        }
    }

    private async Task<string> AuthenticateAsync(ProviderSettings provider, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        if (Get(form, "token") is not { Length: > 0 } token)
        {
            return Error(PragmaticError.BadParameters, "Token is required");
        }

        var session = await store.FindSessionAsync(SessionToken.Hash(token), cancellationToken);
        if (session is null || session.ProviderId != provider.ProviderId)
        {
            return Error(PragmaticError.TokenInvalid, "Token not found");
        }

        if (time.GetUtcNow() >= session.ExpiresAt)
        {
            return Error(PragmaticError.TokenExpired, "Token expired");
        }

        return Cash(await wallet.GetBalanceAsync(session.PunterId, cancellationToken), out var cash) is { } refused
            ? refused
            : Render(new()
            {
                ["userId"] = session.PunterId.ToString(), ["currency"] = session.Currency, ["cash"] = cash, ["bonus"] = 0m,
                ["token"] = token, ["country"] = "ZA", ["jurisdiction"] = "ZA",
            }, PragmaticError.Success);
    }

    private async Task<string> MoveAsync(GameSession session, WalletAction action, IReadOnlyDictionary<string, string> form, string? reference, long amount, string? refunds, bool includeCash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(reference) || reference.Length > 100)
        {
            return Error(PragmaticError.BadParameters, "Reference is required");
        }

        var round = Get(form, "roundId") is { Length: > 0 } r ? r : reference;
        var game = Get(form, "gameId") is { Length: > 0 } g ? g : session.GameId;
        var freeRound = action == WalletAction.Bet && amount == 0 && !string.IsNullOrEmpty(Get(form, "bonusCode"));
        var callback = new WalletCallback(null, reference, round, game, amount, session.Currency, freeRound, refunds);
        var reply = await callbacks.ApplyAsync(session, action, callback, ok => Render(Body(session, ok, includeCash), PragmaticError.Success), cancellationToken);
        return reply.Original ?? (reply.Status == CallbackReply.Ok ? Render(Body(session, reply, includeCash), PragmaticError.Success) : Refused(reply.Status));
    }

    private static Dictionary<string, object> Body(GameSession session, CallbackReply reply, bool includeCash)
    {
        var body = new Dictionary<string, object> { ["transactionId"] = reply.TransactionId?.ToString() ?? string.Empty };
        if (includeCash)
        {
            body["currency"] = session.Currency;
            body["cash"] = Major(reply.Balance ?? 0);
            body["bonus"] = 0m;
            body["usedPromo"] = 0m;
        }

        return body;
    }

    /// <summary>Our refusal, in the provider's codes.</summary>
    public static string Refused(string status) => status switch
    {
        CallbackReply.InsufficientFunds => Error(PragmaticError.InsufficientBalance, "Insufficient balance"),
        CallbackReply.SessionInvalid => Error(PragmaticError.TokenExpired, "Session expired"),
        CallbackReply.AccountRestricted => Error(PragmaticError.PlayerFrozen, "Player cannot play right now"),
        CallbackReply.LimitExceeded => Error(PragmaticError.RegulatoryLimit, "Player limit reached"),
        CallbackReply.WalletUnavailable => Error(PragmaticError.InternalRetry, "Temporarily unavailable"),
        CallbackReply.InvalidRequest or CallbackReply.CurrencyMismatch or CallbackReply.InvalidReference => Error(PragmaticError.BadParameters, "Bad parameters"),
        CallbackReply.BetRolledBack => Error(PragmaticError.BetNotAllowed, "Bet already refunded"),
        CallbackReply.NoFreeSpins => Error(PragmaticError.BetNotAllowed, "No free rounds left"),
        _ => Error(PragmaticError.InternalNoRetry, "Refused"),
    };

    public static string Error(int code, string description) => Render([], code, description);

    private static string Render(Dictionary<string, object> body, int code, string description = "Success")
    {
        body["error"] = code;
        body["description"] = description;
        return JsonSerializer.Serialize(body, Json);
    }

    /// <summary>Amounts arrive in major units with at most two decimals; anything finer is refused, never rounded.</summary>
    private static bool TryAmount(IReadOnlyDictionary<string, string> form, out long minor)
    {
        minor = 0;
        if (!decimal.TryParse(Get(form, "amount"), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var major)
            || decimal.Abs(major) > 1_000_000_000m)
        {
            return false;
        }

        var scaled = major * 100m;
        if (scaled != decimal.Truncate(scaled))
        {
            return false;
        }

        minor = (long)scaled;
        return true;
    }

    private static decimal Major(long minor) => minor / 100m;

    /// <summary>A player with no wallet account yet has nothing in it; anything else the wallet refuses is not a balance.</summary>
    private static string? Cash(WalletResult balance, out decimal cash)
    {
        cash = Major(balance.Available ?? 0);
        return balance switch
        {
            { Status: WalletStatus.Succeeded } or { Status: WalletStatus.Refused, FailureCode: "AccountNotFound" } => null,
            { Status: WalletStatus.Refused, FailureCode: "AccountRestricted" or "AccountBlacklisted" } => Error(PragmaticError.PlayerFrozen, "Player cannot play right now"),
            { Status: WalletStatus.Refused } => Error(PragmaticError.InternalNoRetry, "Wallet refused"),
            _ => Error(PragmaticError.InternalRetry, "Wallet unavailable"),
        };
    }

    private static string? Get(IReadOnlyDictionary<string, string> form, string key) => form.TryGetValue(key, out var value) ? value : null;
}
