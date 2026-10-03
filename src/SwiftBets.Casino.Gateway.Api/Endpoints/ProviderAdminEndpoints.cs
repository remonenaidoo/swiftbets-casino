using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;
using SwiftBets.Casino.Infrastructure.Providers;
using SwiftBets.Contracts.Errors;

namespace SwiftBets.Casino.Gateway.Api.Endpoints;

/// <summary>
/// The console's provider settings. Credentials go in and never come out: the list says only whether each is set.
/// Reading needs <c>casino.read</c>, changing needs <c>casino.write</c> (Admin always has both).
/// </summary>
public static class ProviderAdminEndpoints
{
    public const string Read = "casino.read";
    public const string Write = "casino.write";

    public sealed record ProviderView(string ProviderId, bool Enabled, string WalletModel, string Protocol, bool SecureLoginSet, bool SecretSet,
        IReadOnlyList<string> AllowedAddresses, string ApiBaseUrl, string DemoBaseUrl, DateTimeOffset? UpdatedAt);

    public sealed record ProvidersView(bool CredentialKeyConfigured, IReadOnlyList<ProviderView> Providers);

    public sealed record UpdateRequest(bool? Enabled, IReadOnlyList<string>? AllowedAddresses, string? ApiBaseUrl, string? DemoBaseUrl);

    public sealed record CredentialsRequest(string? SecureLogin, string? Secret);

    public static IEndpointRouteBuilder MapProviderAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/casino/providers");
        admin.MapGet("/", async (IProviderDirectory providers, CredentialCipher cipher, CancellationToken cancellationToken) =>
            Results.Ok(new ProvidersView(cipher.IsConfigured, [.. (await providers.ListAsync(cancellationToken)).Select(p => new ProviderView(p.ProviderId, p.Enabled, p.WalletModel,
                p.Protocol, p.SecureLogin.Length > 0, p.Secret.Length > 0, p.AllowedAddresses, p.ApiBaseUrl, p.DemoBaseUrl, p.UpdatedAt))])))
            .RequireAuthorization(Read);

        admin.MapPut("/{providerId}", async (string providerId, UpdateRequest request, IProviderDirectory providers, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (request.AllowedAddresses is { } list && (list.Count > 50 || list.Any(e => AddressAllowlist.Parse(e) is null && !PragmaticWalletEndpoints.HostName().IsMatch(e.Trim()))))
            {
                return Error.Validation("allowlist_invalid", "List up to 50 entries, each a CIDR block, an address or an internal host name.").ToHttpResult(context);
            }

            if (!IsUrlOrEmpty(request.ApiBaseUrl) || !IsUrlOrEmpty(request.DemoBaseUrl))
            {
                return Error.Validation("url_invalid", "Addresses must be http or https URLs.").ToHttpResult(context);
            }

            return await providers.UpdateAsync(providerId, new ProviderUpdate(request.Enabled, request.AllowedAddresses, request.ApiBaseUrl, request.DemoBaseUrl), Identity.UserId(context), cancellationToken)
                ? Results.NoContent()
                : Error.NotFound("provider_not_found", $"No provider {providerId}.").ToHttpResult(context);
        }).RequireAuthorization(Write);

        admin.MapPut("/{providerId}/credentials", async (string providerId, CredentialsRequest request, IProviderDirectory providers, CredentialCipher cipher, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!cipher.IsConfigured)
            {
                return Error.Unavailable("credential_key_missing", "Credentials cannot be stored until the casino's credential key is configured.").ToHttpResult(context);
            }

            if (string.IsNullOrEmpty(request.SecureLogin) && string.IsNullOrEmpty(request.Secret) || request.SecureLogin?.Length > 200 || request.Secret?.Length > 200)
            {
                return Error.Validation("credentials_invalid", "Send a secure login, a secret key, or both, up to 200 characters each.").ToHttpResult(context);
            }

            return await providers.SetCredentialsAsync(providerId, request.SecureLogin, request.Secret, Identity.UserId(context), cancellationToken)
                ? Results.NoContent()
                : Error.NotFound("provider_not_found", $"No provider {providerId}.").ToHttpResult(context);
        }).RequireAuthorization(Write);

        admin.MapPost("/{providerId}/sync", async (string providerId, SyncCatalogueHandler handler, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(providerId, cancellationToken);
            return result.Error switch
            {
                null => Results.Ok(result),
                SyncResult.ProviderUnknown => Error.NotFound(result.Error, $"{providerId} has no game list to sync.").ToHttpResult(context),
                _ => Error.Unavailable(result.Error, "The game list could not be synced; try again.").ToHttpResult(context),
            };
        }).RequireAuthorization(Write);
        return endpoints;
    }

    private static bool IsUrlOrEmpty(string? value) =>
        string.IsNullOrEmpty(value) || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
