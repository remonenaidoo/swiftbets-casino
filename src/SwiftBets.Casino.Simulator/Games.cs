using System.Security.Cryptography;

namespace SwiftBets.Casino.Simulator;

public sealed record SpinOutcome(IReadOnlyList<string> Reels, long Win);

public sealed record WheelOutcome(int Segment, int Multiplier, long Win);

/// <summary>
/// The simulated games, drawn with a cryptographic RNG on the server. Slot pays on the middle line:
/// three 7s 50x, three BARs 20x, three bells 10x, three cherries 5x, two cherries anywhere 2x. The wheel's
/// 12 segments pay 0, 0, 0, 0, 1, 1, 1, 2, 2, 3, 5 and 10 times the stake.
/// </summary>
public static class Games
{
    private static readonly string[] Symbols = ["7", "BAR", "BELL", "CHERRY", "LEMON", "LEMON", "CHERRY", "BELL", "LEMON", "CHERRY"];
    private static readonly int[] Wheel = [0, 0, 0, 0, 1, 1, 1, 2, 2, 3, 5, 10];

    public static SpinOutcome Spin(long stake, Func<int, int>? draw = null)
    {
        draw ??= RandomNumberGenerator.GetInt32;
        string[] reels = [Symbols[draw(Symbols.Length)], Symbols[draw(Symbols.Length)], Symbols[draw(Symbols.Length)]];
        return new SpinOutcome(reels, stake * SlotMultiplier(reels));
    }

    public static int SlotMultiplier(IReadOnlyList<string> reels)
    {
        if (reels.Distinct().Count() == 1)
        {
            return reels[0] switch { "7" => 50, "BAR" => 20, "BELL" => 10, "CHERRY" => 5, _ => 0 };
        }

        return reels.Count(r => r == "CHERRY") >= 2 ? 2 : 0;
    }

    public static WheelOutcome SpinWheel(long stake, Func<int, int>? draw = null)
    {
        var segment = (draw ?? RandomNumberGenerator.GetInt32)(Wheel.Length);
        return new WheelOutcome(segment, Wheel[segment], stake * Wheel[segment]);
    }
}
