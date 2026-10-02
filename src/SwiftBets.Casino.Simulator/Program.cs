using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Options;
using Npgsql;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Simulator;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-casino-sim");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
builder.Services.AddValidatedOptions<SimulatorOptions>(builder.Configuration, SimulatorOptions.SectionName);
var connectionString = builder.Configuration["ConnectionStrings:SbCasinoCatalog"] is { Length: > 0 } value
    ? value
    : throw new InvalidOperationException("Configuration 'ConnectionStrings:SbCasinoCatalog' is required.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<Ledger>();
builder.Services.AddSingleton<Faults>();
builder.Services.AddSingleton<Play>();
builder.Services.AddHttpClient<GatewayClient>(http => http.Timeout = TimeSpan.FromSeconds(15));
var faultsEnabled = builder.Configuration.GetValue("FaultInjection:Enabled", false);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();

var page = ReadPage();
app.MapGet("/play", (string? session, string? game, HttpContext context) =>
    string.IsNullOrEmpty(session) || string.IsNullOrEmpty(game) ? Results.BadRequest("session and game are required") : GamePage.Serve(page, context))
    .AllowAnonymous();
app.MapPost("/play/start", (StartRequest request, Play play, CancellationToken cancellationToken) => play.StartAsync(request, cancellationToken)).AllowAnonymous();
app.MapPost("/play/spin", (SpinRequest request, Play play, CancellationToken cancellationToken) => play.SpinAsync(request, cancellationToken)).AllowAnonymous();
app.MapPost("/play/cashout", (CashoutRequest request, Play play, CancellationToken cancellationToken) => play.CashoutAsync(request, cancellationToken)).AllowAnonymous();

// The casino gateway fetches each provider's daily report for reconciliation; the request line is signed.
app.MapGet("/reports/{providerId}/{date}", async (string providerId, string date, HttpContext context, Ledger ledger, Faults faults, IOptions<SimulatorOptions> options, CancellationToken cancellationToken) =>
{
    options.Value.Providers.TryGetValue(providerId, out var provider);
    if (!GatewayClient.VerifyReport(provider?.Secret, providerId, date, context.Request.Headers[GatewayClient.SignatureHeader]))
    {
        return Results.Unauthorized();
    }

    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
    {
        return Results.BadRequest();
    }

    var lines = await ledger.ReportAsync(providerId, day, cancellationToken);
    var drop = faults.TakeDrop(providerId);
    return Results.Ok(new { providerId, businessDate = date, transactions = lines.Skip(Math.Min(drop, lines.Count)).ToList() });
}).AllowAnonymous();

if (faultsEnabled)
{
    var drill = app.MapGroup("/faults").RequireAuthorization(Roles.OperatorOrService);
    drill.MapPost("/{providerId}/drop-from-report", (string providerId, int? count, Faults faults) =>
    {
        faults.DropFromNextReport(providerId, Math.Clamp(count ?? 1, 1, 100));
        return Results.Accepted();
    });
    drill.MapPost("/{providerId}/duplicate-next-callback", (string providerId, Faults faults) =>
    {
        faults.DuplicateNextWin(providerId);
        return Results.Accepted();
    });
}

await app.RunAsync();
return 0;

static string ReadPage()
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SwiftBets.Casino.Simulator.wwwroot.play.html")!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

public partial class Program;
