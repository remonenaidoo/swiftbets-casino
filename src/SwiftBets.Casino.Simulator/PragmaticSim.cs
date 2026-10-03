using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Simulator;

public sealed record DrillRequest(string Session, string Scenario, string? Game = null, long Amount = 100);

/// <summary>
/// The provider side of Pragmatic Play: its launch and game-list API, real-money rounds sent as signed callbacks,
/// free demo rounds that touch no wallet, and drills that send what real providers get wrong (duplicates, refunds
/// before their bet, bad hashes) so the gate can prove the gateway handles them.
/// </summary>
public sealed class PragmaticSim(PragmaticClient client, Ledger ledger, Faults faults, IOptions<SimulatorOptions> options)
{
    private const long MinBet = 100;
    private readonly ConcurrentDictionary<string, string> _players = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _demo = new(StringComparer.Ordinal);

    /// <summary>Checks our own API's hash and operator login, as Pragmatic does.</summary>
    public bool Verify(IReadOnlyDictionary<string, string> form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var provider = options.Value.Providers.GetValueOrDefault(PragmaticClient.ProviderId);
        return provider is not null && PragmaticHash.Verify(form, provider.Secret) && form.GetValueOrDefault("secureLogin") == provider.SecureLogin;
    }

    public IResult GameUrl(IReadOnlyDictionary<string, string> form)
    {
        if (!Verify(form))
        {
            return Results.Ok(new { error = "5", description = "Invalid hash or secure login" });
        }

        var symbol = form.GetValueOrDefault("symbol");
        var token = form.GetValueOrDefault("token");
        if (PragmaticGames.Find(symbol) is null || string.IsNullOrEmpty(token))
        {
            return Results.Ok(new { error = "8", description = "Game not found" });
        }

        var url = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/pragmatic/play?session={Uri.EscapeDataString(token)}&game={Uri.EscapeDataString(symbol!)}";
        return Results.Ok(new { error = "0", description = "OK", gameURL = url });
    }

    public IResult CasinoGames(IReadOnlyDictionary<string, string> form) => !Verify(form)
        ? Results.Ok(new { error = "5", description = "Invalid hash or secure login" })
        : Results.Ok(new
        {
            error = "0",
            description = "OK",
            gameList = PragmaticGames.All.Select(g => new
            {
                gameID = g.GameId, gameName = g.GameName, gameTypeID = g.GameTypeId, typeDescription = g.TypeDescription, technology = "html5", platform = "MOBILE,WEB",
                demoGameAvailable = g.DemoGameAvailable, aspectRatio = "16:9",
            }),
        });

    public async Task<IResult> StartAsync(StartRequest request, CancellationToken cancellationToken)
    {
        if (PragmaticGames.Find(request.Game) is not { } game)
        {
            return Results.NotFound(new { status = "game_not_found" });
        }

        var auth = await AuthenticateAsync(request.Session, cancellationToken);
        return Results.Ok(new { model = "seamless", provider = "Pragmatic Play (simulated) · bets straight from your wallet", game = game.GameName, minBet = MinBet, status = Status(auth), balance = Cash(auth) });
    }

    public async Task<IResult> SpinAsync(SpinRequest request, CancellationToken cancellationToken)
    {
        if (PragmaticGames.Find(request.Game) is not { } game)
        {
            return Results.NotFound(new { status = "game_not_found" });
        }

        if (request.Bet < MinBet)
        {
            return Results.BadRequest(new { status = "bet_too_small", minBet = MinBet });
        }

        if (!_players.TryGetValue(request.Session, out var userId))
        {
            return Results.Ok(new { status = "session_invalid" });
        }

        var round = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{Random.Shared.Next(1000, 9999)}";
        var betRef = $"pp-b-{Guid.NewGuid():N}";
        var bet = await client.SendAsync("bet", Fields(userId, game.GameId, round, betRef, request.Bet), cancellationToken);
        if (faults.TakeDuplicate(PragmaticClient.ProviderId))
        {
            // Fault injection: the provider sends the same bet again, as after a lost reply.
            await client.SendAsync("bet", Fields(userId, game.GameId, round, betRef, request.Bet), cancellationToken);
        }

        if (bet.Error != 0)
        {
            return Results.Ok(new { status = Status(bet) });
        }

        await ledger.RecordAsync(PragmaticClient.ProviderId, betRef, "bet", request.Bet, options.Value.Currency, round, game.GameId, cancellationToken);
        var outcome = Games.Spin(request.Bet);
        var resultRef = $"pp-r-{Guid.NewGuid():N}";
        var result = await client.SendAsync("result", Fields(userId, game.GameId, round, resultRef, outcome.Win), cancellationToken);
        if (result.Error == 0)
        {
            await ledger.RecordAsync(PragmaticClient.ProviderId, resultRef, "win", outcome.Win, options.Value.Currency, round, game.GameId, cancellationToken);
        }

        var end = await client.SendAsync("endRound", new() { ["userId"] = userId, ["gameId"] = game.GameId, ["roundId"] = round }, cancellationToken);
        return Results.Ok(new { status = "ok", reels = outcome.Reels, win = outcome.Win, balance = Cash(end) ?? Cash(result) ?? Cash(bet) });
    }

    /// <summary>Demo rounds: R1000 of play money per demo session, held here; no callback is ever sent.</summary>
    public IResult DemoStart(StartRequest request) => PragmaticGames.Find(request.Game) is not { } game
        ? Results.NotFound(new { status = "game_not_found" })
        : Results.Ok(new { model = "seamless", provider = "Free demo · play money, nothing at stake", game = game.GameName, minBet = MinBet, status = "ok", balance = _demo.GetOrAdd(request.Session, 100_000) });

    public IResult DemoSpin(SpinRequest request)
    {
        if (PragmaticGames.Find(request.Game) is null || request.Bet < MinBet || !_demo.TryGetValue(request.Session, out var balance))
        {
            return Results.Ok(new { status = "session_invalid" });
        }

        if (request.Bet > balance)
        {
            return Results.Ok(new { status = "insufficient_funds" });
        }

        var outcome = Games.Spin(request.Bet);
        _demo[request.Session] = balance - request.Bet + outcome.Win;
        return Results.Ok(new { status = "ok", reels = outcome.Reels, win = outcome.Win, balance = _demo[request.Session] });
    }

    public string NewDemoSession()
    {
        var session = $"demo-{Guid.NewGuid():N}";
        _demo[session] = 100_000;
        return session;
    }

    /// <summary>Sends one misbehaving sequence and returns every exchange, for the gate to judge.</summary>
    public async Task<IResult> DrillAsync(DrillRequest request, CancellationToken cancellationToken)
    {
        var game = request.Game ?? PragmaticGames.All[0].GameId;
        var auth = await AuthenticateAsync(request.Session, cancellationToken);
        if (auth.Error != 0 || !_players.TryGetValue(request.Session, out var userId))
        {
            return Results.Ok(new { scenario = request.Scenario, exchanges = new[] { Show(auth) } });
        }

        var round = $"drill-{Guid.NewGuid():N}";
        var reference = $"pp-drill-{Guid.NewGuid():N}";
        var exchanges = new List<PragmaticExchange>();
        switch (request.Scenario)
        {
            case "duplicate-bet":
                exchanges.Add(await client.SendAsync("bet", Fields(userId, game, round, reference, request.Amount), cancellationToken));
                exchanges.Add(await client.SendAsync("bet", Fields(userId, game, round, reference, request.Amount), cancellationToken));
                break;
            case "refund-before-bet":
                exchanges.Add(await client.SendAsync("refund", new() { ["userId"] = userId, ["gameId"] = game, ["roundId"] = round, ["reference"] = reference, ["amount"] = PragmaticClient.Amount(request.Amount) }, cancellationToken));
                exchanges.Add(await client.SendAsync("bet", Fields(userId, game, round, reference, request.Amount), cancellationToken));
                break;
            case "refund-inflated":
                // A refund naming far more than the stake: only the recorded stake may come back.
                exchanges.Add(await client.SendAsync("bet", Fields(userId, game, round, reference, request.Amount), cancellationToken));
                exchanges.Add(await client.SendAsync("refund", new() { ["userId"] = userId, ["gameId"] = game, ["roundId"] = round, ["reference"] = reference, ["amount"] = PragmaticClient.Amount(request.Amount * 100) }, cancellationToken));
                break;
            case "bad-hash":
                exchanges.Add(await client.SendAsync("bet", Fields(userId, game, round, reference, request.Amount), cancellationToken, badHash: true));
                break;
            default:
                exchanges.Add(await client.SendAsync("bet", Fields(userId, game, round, reference, request.Amount), cancellationToken));
                break;
        }

        return Results.Ok(new { scenario = request.Scenario, reference, exchanges = exchanges.Select(Show) });
    }

    private async Task<PragmaticExchange> AuthenticateAsync(string session, CancellationToken cancellationToken)
    {
        var auth = await client.SendAsync("authenticate", new() { ["token"] = session }, cancellationToken);
        if (auth.Error == 0 && auth.Json.TryGetProperty("userId", out var userId) && userId.GetString() is { } id)
        {
            _players[session] = id;
        }

        return auth;
    }

    private static object Show(PragmaticExchange e) => new { method = e.Method, http = e.Http, error = e.Error, body = e.Body };

    private static Dictionary<string, string> Fields(string userId, string gameId, string round, string reference, long amount) => new()
    {
        ["userId"] = userId, ["gameId"] = gameId, ["roundId"] = round, ["reference"] = reference, ["amount"] = PragmaticClient.Amount(amount), ["roundDetails"] = "spin",
    };

    private static long? Cash(PragmaticExchange exchange) =>
        exchange.Error == 0 && exchange.Json.TryGetProperty("cash", out var cash) && cash.TryGetDecimal(out var major) ? (long)(major * 100m) : null;

    /// <summary>Pragmatic's error codes in the words the game page shows.</summary>
    private static string Status(PragmaticExchange exchange) => exchange.Error switch
    {
        0 => "ok",
        1 => "insufficient_funds",
        4 or 130 => "session_invalid",
        6 => "account_restricted",
        50 or 210 => "limit_exceeded",
        _ => "error_" + exchange.Error.ToString(CultureInfo.InvariantCulture),
    };
}
