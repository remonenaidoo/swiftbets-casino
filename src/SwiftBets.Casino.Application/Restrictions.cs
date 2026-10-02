using SwiftBets.Contracts.Compliance;

namespace SwiftBets.Casino.Application;

/// <summary>Which compliance restrictions keep a player out of the casino.</summary>
public static class CasinoRestrictionRules
{
    private static readonly HashSet<RestrictionKind> Blocking = [RestrictionKind.SelfExclusion, RestrictionKind.CoolingOff, RestrictionKind.NoBetting];

    public static bool IsRestricted(RestrictionsChangedV1 state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Restrictions.Any(r => Blocking.Contains(r.Kind) && r.StartsAt <= now && (r.EndsAt is null || r.EndsAt > now));
    }
}
