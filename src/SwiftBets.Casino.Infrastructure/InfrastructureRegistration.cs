using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Resilience;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Infrastructure.Compliance;
using SwiftBets.Casino.Infrastructure.Persistence;
using SwiftBets.Casino.Infrastructure.Providers;
using SwiftBets.Casino.Infrastructure.Reconciliation;
using SwiftBets.Casino.Infrastructure.Wallet;
using SwiftBets.Contracts.Compliance;
using SwiftBets.Contracts.Messaging;
using WalletGrpc = SwiftBets.Contracts.Grpc.Wallet.V1.Wallet;

namespace SwiftBets.Casino.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddCasinoInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSqlServerPersistence(Required(configuration, "ConnectionStrings:SbCasino"));
        services.AddKafkaMessaging(configuration);
        services.AddSqlServerOutbox(configuration);
        services.AddValidatedOptions<CasinoOptions>(configuration, CasinoOptions.SectionName);
        services.AddValidatedOptions<WalletOptions>(configuration, WalletOptions.SectionName);
        services.AddSingleton<ICasinoStore, SqlCasinoStore>();
        services.AddScoped<IWalletPort, GrpcWalletPort>();
        services.AddCompactedState<RestrictionsChangedV1>(Topics.RestrictionsChanged);
        services.AddHttpClient(ComplianceRestrictions.ClientName, (sp, http) =>
        {
            http.BaseAddress = new Uri(Required(configuration, "Clients:ComplianceAddress").TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddSingleton<IRestrictions, ComplianceRestrictions>();
        services.AddHttpClient(HttpProviderReports.ClientName, http => http.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<CredentialCipher>();
        services.AddSingleton<IProviderDirectory, SqlProviderDirectory>();
        services.AddHttpClient(PragmaticGameApi.ClientName, http => http.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IProviderGameApi, PragmaticGameApi>();
        services.AddHttpClient(HttpCatalogSink.ClientName, (sp, http) =>
        {
            var address = sp.GetRequiredService<IOptions<CasinoOptions>>().Value.CatalogAddress;
            http.BaseAddress = new Uri((address.Length > 0 ? address : "http://casino-catalog:8080").TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<ICatalogSink, HttpCatalogSink>();
        if (configuration.GetValue("Casino:CatalogueSync:Enabled", true))
        {
            services.AddHostedService<CatalogueSyncWorker>();
        }
        services.AddSingleton<IProviderReports, HttpProviderReports>();
        if (configuration.GetValue("Casino:Reconciliation:Enabled", true))
        {
            services.AddHostedService<ReconciliationWorker>();
        }

        services.AddClientCredentials(configuration);
        services.AddGrpcClient<WalletGrpc.WalletClient>((sp, grpc) => grpc.Address = new Uri(sp.GetRequiredService<IOptions<WalletOptions>>().Value.GrpcAddress))
            .ConfigureChannel(channel =>
            {
                channel.ServiceConfig = GrpcResilience.KeyedServiceConfig;
                channel.UnsafeUseInsecureChannelCallCredentials = true;
            })
            .AddCallCredentials(async (context, metadata, sp) =>
                metadata.Add("Authorization", $"Bearer {await sp.GetRequiredService<ClientCredentialsTokenProvider>().GetTokenAsync(context.CancellationToken)}"))
            .AddKeyedGrpcResilience();
        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");
}
