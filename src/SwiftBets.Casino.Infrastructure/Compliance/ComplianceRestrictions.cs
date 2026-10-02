using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Contracts.Compliance;

namespace SwiftBets.Casino.Infrastructure.Compliance;

/// <summary>
/// A restriction already on the compacted topic refuses at once; otherwise compliance is asked directly, because the
/// topic can lag a break the player has just taken. Any failure to ask counts as unavailable, so launch fails closed.
/// </summary>
public sealed partial class ComplianceRestrictions(ICompactedState<RestrictionsChangedV1> topic, IHttpClientFactory http, ClientCredentialsTokenProvider tokens, ILogger<ComplianceRestrictions> logger) : IRestrictions
{
    public const string ClientName = "compliance";
    private static readonly HashSet<string> Blocking = ["selfExclusion", "coolingOff", "noBetting"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RestrictionCheck> CheckAsync(Guid punterId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (topic.IsReady && topic.TryGet(punterId.ToString(), out var known) && CasinoRestrictionRules.IsRestricted(known, now))
        {
            return RestrictionCheck.Restricted;
        }

        try
        {
            var token = await tokens.GetTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/users/{punterId}/restrictions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.CreateClient(ClientName).SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                tokens.Invalidate(token);
            }

            response.EnsureSuccessStatusCode();
            var state = await response.Content.ReadFromJsonAsync<State>(Json, cancellationToken);
            return state is null ? RestrictionCheck.Unavailable
                : state.Excluded || state.Restrictions.Any(r => Blocking.Contains(r.Kind)) ? RestrictionCheck.Restricted
                : RestrictionCheck.Clear;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(ex, punterId);
            return RestrictionCheck.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Compliance could not be asked about {PunterId}; launch refused")]
    private partial void LogUnavailable(Exception exception, Guid punterId);

    private sealed record State(bool Excluded, IReadOnlyList<Restriction> Restrictions);

    private sealed record Restriction(string Kind, DateTimeOffset? EndsAt);
}
