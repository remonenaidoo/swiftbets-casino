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
        services.AddSingleton<IRestrictions, CompactedRestrictions>();

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
