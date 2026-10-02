using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace SwiftBets.Casino.Simulator;

public sealed record StartRequest(string Session, string Game, long TransferIn = 0);

public sealed record SpinRequest(string Session, string Game, long Bet);

public sealed record CashoutRequest(string Session, string Game);

/// <summary>A transfer-wallet session's money held at the provider, from transfer-in until cash out.</summary>
public sealed class TransferSession
{
    public long Balance { get; set; }

    public bool Open { get; set; } = true;
}

/// <summary>
/// The provider side of a round. Seamless games bet and win against the player's wallet on every round; transfer
/// games move a chosen amount in once, play against it here, and move what is left back on cash out. Every call the
/// gateway accepts goes into the ledger, so the daily report is the provider's honest view.
/// </summary>
public sealed class Play(GatewayClient gateway, Ledger ledger, IOptions<SimulatorOptions> options)
{
    private readonly ConcurrentDictionary<string, TransferSession> _transfers = new(StringComparer.Ordinal);

    public async Task<IResult> StartAsync(StartRequest request, CancellationToken cancellationToken)
    {
        if (await ledger.GameAsync(request.Game, cancellationToken) is not { } game)
        {
            return Results.NotFound(new { status = "game_not_found" });
        }

        if (game.WalletModel == "seamless")
        {
            var balance = await gateway.CallAsync(game.ProviderId, "balance", Body(request.Session, game, $"bal-{Guid.NewGuid():N}", 0), cancellationToken);
            return Results.Ok(new { model = game.WalletModel, game = game.Name, minBet = game.MinBet, status = balance.Status, balance = balance.Balance });
        }

        if (request.TransferIn <= 0)
        {
            return Results.Ok(new { model = game.WalletModel, game = game.Name, minBet = game.MinBet, status = "transfer_needed" });
        }

        var ptx = $"ti-{Guid.NewGuid():N}";
        var reply = await gateway.CallAsync(game.ProviderId, "transfer-in", Body(request.Session, game, ptx, request.TransferIn), cancellationToken);
        if (reply.Status != "ok")
        {
            return Results.Ok(new { model = game.WalletModel, status = reply.Status });
        }

        await ledger.RecordAsync(game.ProviderId, ptx, "transferIn", request.TransferIn, options.Value.Currency, ptx, game.GameId, cancellationToken);
        var session = _transfers.GetOrAdd(request.Session, _ => new TransferSession());
        session.Balance += request.TransferIn;
        return Results.Ok(new { model = game.WalletModel, game = game.Name, minBet = game.MinBet, status = "ok", sessionBalance = session.Balance, balance = reply.Balance });
    }

    public async Task<IResult> SpinAsync(SpinRequest request, CancellationToken cancellationToken)
    {
        if (await ledger.GameAsync(request.Game, cancellationToken) is not { } game)
        {
            return Results.NotFound(new { status = "game_not_found" });
        }

        if (request.Bet < game.MinBet)
        {
            return Results.BadRequest(new { status = "bet_too_small", minBet = game.MinBet });
        }

        return game.WalletModel == "seamless" ? await SeamlessRoundAsync(request, game, cancellationToken) : TransferRound(request);
    }

    public async Task<IResult> CashoutAsync(CashoutRequest request, CancellationToken cancellationToken)
    {
        if (await ledger.GameAsync(request.Game, cancellationToken) is not { WalletModel: "transfer" } game || !_transfers.TryGetValue(request.Session, out var session) || !session.Open)
        {
            return Results.BadRequest(new { status = "no_open_transfer_session" });
        }

        var ptx = $"to-{Guid.NewGuid():N}";
        var amount = session.Balance;
        var reply = await gateway.CallAsync(game.ProviderId, "transfer-out", Body(request.Session, game, ptx, amount), cancellationToken);
        if (reply.Status == "ok")
        {
            await ledger.RecordAsync(game.ProviderId, ptx, "transferOut", amount, options.Value.Currency, ptx, game.GameId, cancellationToken);
            session.Open = false;
            session.Balance = 0;
        }

        return Results.Ok(new { status = reply.Status, cashedOut = amount, balance = reply.Balance });
    }

    private async Task<IResult> SeamlessRoundAsync(SpinRequest request, SimGame game, CancellationToken cancellationToken)
    {
        var round = $"r-{Guid.NewGuid():N}";
        var betPtx = $"b-{Guid.NewGuid():N}";
        var bet = await gateway.CallAsync(game.ProviderId, "bet", Body(request.Session, game, betPtx, request.Bet, round), cancellationToken);
        if (bet.Status != "ok")
        {
            return Results.Ok(new { status = bet.Status, balance = bet.Balance });
        }

        await ledger.RecordAsync(game.ProviderId, betPtx, "bet", request.Bet, options.Value.Currency, round, game.GameId, cancellationToken);
        var outcome = Games.Spin(request.Bet);
        var balance = bet.Balance;
        if (outcome.Win > 0)
        {
            var winPtx = $"w-{Guid.NewGuid():N}";
            var win = await gateway.CallAsync(game.ProviderId, "win", Body(request.Session, game, winPtx, outcome.Win, round), cancellationToken);
            if (win.Status == "ok")
            {
                await ledger.RecordAsync(game.ProviderId, winPtx, "win", outcome.Win, options.Value.Currency, round, game.GameId, cancellationToken);
                balance = win.Balance;
            }
        }

        return Results.Ok(new { status = "ok", reels = outcome.Reels, win = outcome.Win, balance });
    }

    private IResult TransferRound(SpinRequest request)
    {
        if (!_transfers.TryGetValue(request.Session, out var session) || !session.Open)
        {
            return Results.BadRequest(new { status = "transfer_needed" });
        }

        if (request.Bet > session.Balance)
        {
            return Results.Ok(new { status = "insufficient_session_balance", sessionBalance = session.Balance });
        }

        var outcome = Games.SpinWheel(request.Bet);
        session.Balance += outcome.Win - request.Bet;
        return Results.Ok(new { status = "ok", segment = outcome.Segment, multiplier = outcome.Multiplier, win = outcome.Win, sessionBalance = session.Balance });
    }

    private object Body(string session, SimGame game, string ptx, long amount, string? round = null) =>
        new { sessionToken = session, providerTransactionId = ptx, roundId = round ?? ptx, gameId = game.GameId, amount, currency = options.Value.Currency };
}
