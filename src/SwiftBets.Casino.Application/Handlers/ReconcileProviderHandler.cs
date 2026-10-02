using Microsoft.Extensions.Options;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Contracts.Casino;

namespace SwiftBets.Casino.Application.Handlers;

/// <summary>
/// Compares a provider's report for a UTC business day with our ledger: by provider transaction id for what is missing
/// on either side, and by the player's net money (wins, refunds and transfers out less bets and transfers in) for drift.
/// Any missing transaction or any drift makes the day a Drift, which Steward raises as an incident.
/// </summary>
public sealed class ReconcileProviderHandler(ICasinoStore store, IProviderReports reports, IOptions<CasinoOptions> options, TimeProvider time)
{
    public const string ProviderUnknown = "provider_unknown";
    public const string ReportUnavailable = "report_unavailable";

    public async Task<(ReconciliationRun? Run, string? Error)> HandleAsync(string providerId, DateOnly businessDate, CancellationToken cancellationToken)
    {
        if (await store.GetWalletModelAsync(providerId, cancellationToken) is null)
        {
            return (null, ProviderUnknown);
        }

        if (await reports.GetAsync(providerId, businessDate, cancellationToken) is not { } theirs)
        {
            return (null, ReportUnavailable);
        }

        var from = new DateTimeOffset(businessDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var ours = await store.ListTransactionsAsync(providerId, from, from.AddDays(1), cancellationToken);
        var ourIds = ours.Select(t => t.ProviderTransactionId).ToHashSet(StringComparer.Ordinal);
        var theirIds = theirs.Select(t => t.ProviderTransactionId).ToHashSet(StringComparer.Ordinal);
        var ourNet = Net(ours);
        var providerNet = Net(theirs);
        var missingOurs = theirIds.Count(id => !ourIds.Contains(id));
        var missingTheirs = ourIds.Count(id => !theirIds.Contains(id));
        var drift = ourNet - providerNet;
        var status = drift == 0 && missingOurs == 0 && missingTheirs == 0 ? ReconciliationStatus.Matched : ReconciliationStatus.Drift;

        var run = new ReconciliationRun(Guid.CreateVersion7(), providerId, businessDate, ourNet, providerNet, drift, missingOurs, missingTheirs, status,
            options.Value.Currency, time.GetUtcNow());
        await store.RecordReconciliationAsync(run, cancellationToken);
        return (run, null);
    }

    /// <summary>The player's net: money they received less money they staked.</summary>
    public static long Net(IEnumerable<ReportedTransaction> transactions) => transactions.Sum(t => t.Kind switch
    {
        CasinoTransactionKind.Bet or CasinoTransactionKind.TransferIn => -t.Amount,
        CasinoTransactionKind.Win or CasinoTransactionKind.Rollback or CasinoTransactionKind.TransferOut => t.Amount,
        _ => 0,
    });
}
