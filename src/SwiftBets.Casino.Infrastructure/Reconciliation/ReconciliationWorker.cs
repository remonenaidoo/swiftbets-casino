using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Contracts.Casino;

namespace SwiftBets.Casino.Infrastructure.Reconciliation;

/// <summary>From 00:30 UTC, reconciles yesterday once per provider that serves a report; checks every 15 minutes.</summary>
public sealed partial class ReconciliationWorker(IServiceScopeFactory scopes, IOptions<CasinoOptions> options, TimeProvider time, ILogger<ReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15), time);
        do
        {
            var now = time.GetUtcNow();
            if (now.TimeOfDay < TimeSpan.FromMinutes(30))
            {
                continue;
            }

            var yesterday = DateOnly.FromDateTime(now.UtcDateTime.AddDays(-1));
            foreach (var providerId in options.Value.Providers.Where(p => p.Value.ReportUrl.Length > 0).Select(p => p.Key))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    if (await scope.ServiceProvider.GetRequiredService<ICasinoStore>().HasReconciliationAsync(providerId, yesterday, stoppingToken))
                    {
                        continue;
                    }

                    var (run, error) = await scope.ServiceProvider.GetRequiredService<ReconcileProviderHandler>().HandleAsync(providerId, yesterday, stoppingToken);
                    if (run is { Status: ReconciliationStatus.Drift })
                    {
                        LogDrift(providerId, yesterday, run.Drift, run.MissingOnOurSide, run.MissingOnProviderSide);
                    }
                    else if (error is not null)
                    {
                        LogSkipped(providerId, yesterday, error);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    LogFailed(ex, providerId, yesterday);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconciliation drift with {ProviderId} on {BusinessDate}: {Drift} minor units, {MissingOurs} missing on our side, {MissingTheirs} on theirs")]
    private partial void LogDrift(string providerId, DateOnly businessDate, long drift, int missingOurs, int missingTheirs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconciliation with {ProviderId} for {BusinessDate} skipped: {Reason}")]
    private partial void LogSkipped(string providerId, DateOnly businessDate, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation with {ProviderId} for {BusinessDate} failed; retrying next check")]
    private partial void LogFailed(Exception exception, string providerId, DateOnly businessDate);
}
