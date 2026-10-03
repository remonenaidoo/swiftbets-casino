namespace SwiftBets.Casino.Simulator;

/// <summary>A game in Pragmatic's getCasinoGames shape.</summary>
public sealed record PragmaticGame(string GameId, string GameName, string GameTypeId, string TypeDescription, bool DemoGameAvailable, string Tint);

/// <summary>The simulated Pragmatic catalogue: slot-style games played with the simulator's slot rules.</summary>
public static class PragmaticGames
{
    public static readonly IReadOnlyList<PragmaticGame> All =
    [
        new("vs20sunwolf", "Sun Wolf Megaways", "vs", "Video Slots", true, "#f59e0b"),
        new("vs25goldenreef", "Golden Reef", "vs", "Video Slots", true, "#0ea5e9"),
        new("vs10pharaohcoins", "Pharaoh Coins", "vs", "Video Slots", true, "#eab308"),
        new("vs20candyburst", "Candy Burst", "vs", "Video Slots", true, "#ec4899"),
        new("vs5fruitstack", "Fruit Stack 5", "cs", "Classic Slots", true, "#22c55e"),
        new("vs40wildbison", "Wild Bison Run", "vs", "Video Slots", false, "#b45309"),
    ];

    public static PragmaticGame? Find(string? symbol) => All.FirstOrDefault(g => g.GameId == symbol);

    /// <summary>Simple poster artwork, so tiles have real images served by URL.</summary>
    public static string Poster(PragmaticGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var name = System.Net.WebUtility.HtmlEncode(game.GameName);
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="325" height="434" viewBox="0 0 325 434">
            <defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{game.Tint}"/><stop offset="1" stop-color="#111827"/></linearGradient></defs>
            <rect width="325" height="434" fill="url(#g)"/>
            <circle cx="162" cy="170" r="88" fill="#ffffff" opacity="0.16"/>
            <text x="162" y="190" font-family="Arial, sans-serif" font-size="64" font-weight="900" fill="#ffffff" text-anchor="middle">7</text>
            <text x="162" y="360" font-family="Arial, sans-serif" font-size="26" font-weight="800" fill="#ffffff" text-anchor="middle">{name}</text>
            </svg>
            """;
    }
}
