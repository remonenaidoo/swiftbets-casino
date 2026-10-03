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
// Games new in a provider's list are hidden until an operator shows them, unless this deployment says otherwise (the preview).
var showNewGames = builder.Configuration.GetValue("Casino:ShowNewGames", false);
var currency = builder.Configuration["Casino:Currency"] ?? "ZAR";
const string CasinoRead = "casino.read";
const string CasinoWrite = "casino.write";
foreach (var permission in new[] { CasinoRead, CasinoWrite })
{
    builder.Services.AddAuthorizationBuilder().AddPolicy(permission, p => p.RequireRole(Roles.Operator, Roles.Admin).RequireAssertion(c =>
        c.User.IsInRole(Roles.Admin) || c.User.HasClaim("perm", permission)));
}

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

app.MapGet("/casino/favourites", async (CatalogStore store, HttpContext context, CancellationToken cancellationToken) =>
    Results.Ok(await store.FavouritesAsync(UserId(context), cancellationToken))).RequireAuthorization(Roles.Punter);
app.MapPut("/casino/favourites/{gameId}", async (string gameId, CatalogStore store, HttpContext context, CancellationToken cancellationToken) =>
    await store.SetFavouriteAsync(UserId(context), gameId, true, cancellationToken)
        ? Results.NoContent()
        : Error.NotFound("game_not_found", $"No game {gameId}.").ToHttpResult(context)).RequireAuthorization(Roles.Punter);
app.MapDelete("/casino/favourites/{gameId}", async (string gameId, CatalogStore store, HttpContext context, CancellationToken cancellationToken) =>
{
    await store.SetFavouriteAsync(UserId(context), gameId, false, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(Roles.Punter);

// The casino gateway pushes each provider's game list here; it owns the provider credentials, the catalogue owns the lobby.
app.MapPost("/internal/casino/providers/{providerId}/games", async (string providerId, SyncRequest request, CatalogStore store, CancellationToken cancellationToken) =>
    Results.Ok(new { added = await store.SyncAsync(providerId, market, request.Games ?? [], showNewGames, 100, currency, cancellationToken) }))
    .RequireAuthorization(Roles.OperatorOrService);

var admin = app.MapGroup("/admin/casino/catalog");
admin.MapGet("/games", async (string? providerId, CatalogStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.AdminGamesAsync(market, providerId, cancellationToken))).RequireAuthorization(CasinoRead);
admin.MapGet("/categories", async (CatalogStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.CategoriesAsync(cancellationToken))).RequireAuthorization(CasinoRead);
admin.MapPatch("/games/{gameId}", async (string gameId, GameUpdate update, CatalogStore store, HttpContext context, CancellationToken cancellationToken) =>
{
    if (update.Position is < 0 or > 100_000 || update.Category is { } category && !(await store.CategoriesAsync(cancellationToken)).Any(c => c.Key == category))
    {
        return Error.Validation("game_update_invalid", "Pick a listed category and a position from 0 to 100000.").ToHttpResult(context);
    }

    return await store.UpdateGameAsync(gameId, market, update, cancellationToken)
        ? Results.NoContent()
        : Error.NotFound("game_not_found", $"No game {gameId}.").ToHttpResult(context);
}).RequireAuthorization(CasinoWrite);

await app.RunAsync();
return 0;

static Guid UserId(HttpContext context) =>
    Guid.TryParse(context.User.FindFirst("sub")?.Value, out var id) ? id : throw new BadHttpRequestException("Token subject is not a user id.", 401);

public partial class Program;
