using System.Text.Json;
using System.Text.Json.Serialization;
using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Gateway.Api.Endpoints;
using SwiftBets.Casino.Infrastructure;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-casino");
builder.Services.AddSwiftBetsWeb();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
// Console permissions (D153): staff with the permission, or Admin; services still check every call.
foreach (var permission in new[] { ProviderAdminEndpoints.Read, ProviderAdminEndpoints.Write })
{
    builder.Services.AddAuthorizationBuilder().AddPolicy(permission, p => p.RequireRole(Roles.Operator, Roles.Admin).RequireAssertion(c =>
        c.User.IsInRole(Roles.Admin) || c.User.HasClaim("perm", permission)));
}

builder.Services.AddCasinoInfrastructure(builder.Configuration);
builder.Services.AddCasinoApplication();

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();
app.MapLaunchEndpoints();
app.MapProviderWalletEndpoints();
app.MapFreeSpinEndpoints();
app.MapReconciliationEndpoints();
app.MapPragmaticWalletEndpoints();
app.MapProviderAdminEndpoints();

await app.RunAsync();
return 0;

public partial class Program;
