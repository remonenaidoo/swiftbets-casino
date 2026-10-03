using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Handlers;

namespace SwiftBets.Casino.Infrastructure.Providers;

/// <summary>Syncs each Pragmatic-protocol provider's game list shortly after start and then daily; a failed sync retries every minute.</summary>
public sealed partial class CatalogueSyncWorker(IServiceScopeFactory scopes, IOptions<CasinoOptions> options, ILogger<CatalogueSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var failed = false;
                foreach (var providerId in options.Value.Providers.Where(p => p.Value.Protocol == ProviderProtocols.Pragmatic).Select(p => p.Key))
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<SyncCatalogueHandler>().HandleAsync(providerId, stoppingToken);
                    LogSynced(providerId, result.Listed, result.Added, result.Error);
                    failed |= result.Error is not null and not SyncResult.ProviderUnknown;
                }

                await Task.Delay(failed ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(24), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue sync for {ProviderId}: {Listed} listed, {Added} new, error {Error}")]
    private partial void LogSynced(string providerId, int? listed, int? added, string? error);
}
