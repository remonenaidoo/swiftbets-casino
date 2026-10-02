using Microsoft.Extensions.DependencyInjection;
using SwiftBets.Casino.Application.Handlers;

namespace SwiftBets.Casino.Application;

public static class ApplicationRegistration
{
    public static IServiceCollection AddCasinoApplication(this IServiceCollection services)
    {
        services.AddScoped<LaunchGameHandler>();
        services.AddScoped<WalletCallbackHandler>();
        services.AddScoped<FreeSpinsHandler>();
        return services;
    }
}
