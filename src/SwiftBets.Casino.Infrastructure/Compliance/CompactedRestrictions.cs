using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Contracts.Compliance;

namespace SwiftBets.Casino.Infrastructure.Compliance;

/// <summary>Compliance's compacted restrictions topic, held in memory and keyed by user id.</summary>
public sealed class CompactedRestrictions(ICompactedState<RestrictionsChangedV1> state) : IRestrictions
{
    public bool IsReady => state.IsReady;

    public bool IsCasinoRestricted(Guid punterId, DateTimeOffset now) =>
        state.TryGet(punterId.ToString(), out var restrictions) && CasinoRestrictionRules.IsRestricted(restrictions, now);
}
