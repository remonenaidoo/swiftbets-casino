using Npgsql;
using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Catalog.Api;
using SwiftBets.Contracts.Errors;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-casino-catalog");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
var connectionString = builder.Configuration["ConnectionStrings:SbCasinoCatalog"] is { Length: > 0 } value
    ? value
    : throw new InvalidOperationException("Configuration 'ConnectionStrings:SbCasinoCatalog' is required.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<CatalogStore>();
// The market a lobby is served for; one market per deployment in v1.
var market = builder.Configuration["Casino:Market"] ?? "ZA";

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();

app.MapGet("/casino/lobby", async (CatalogStore store, HttpContext context, CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "public, max-age=30";
    return Results.Ok(await store.LobbyAsync(market, cancellationToken));
}).AllowAnonymous();

app.MapGet("/casino/games/{gameId}", async (string gameId, CatalogStore store, HttpContext context, CancellationToken cancellationToken) =>
    await store.GameAsync(gameId, market, cancellationToken) is { } game
        ? Results.Ok(game)
        : Error.NotFound("game_not_found", $"No game {gameId}.").ToHttpResult(context)).AllowAnonymous();

app.MapPost("/admin/casino/games/{gameId}/availability", async (string gameId, AvailabilityRequest request, CatalogStore store, HttpContext context, CancellationToken cancellationToken) =>
    await store.SetAvailabilityAsync(gameId, market, request.Enabled, cancellationToken)
        ? Results.NoContent()
        : Error.NotFound("game_not_found", $"No game {gameId}.").ToHttpResult(context)).RequireAuthorization(Roles.Operator);

await app.RunAsync();
return 0;

public partial class Program;
