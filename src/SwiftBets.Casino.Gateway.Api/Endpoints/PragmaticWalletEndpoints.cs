using System.Net;
using System.Text.RegularExpressions;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Gateway.Api.Endpoints;

/// <summary>
/// Pragmatic Play's seamless-wallet callbacks: form-encoded POSTs, one path per method (an <c>.html</c> suffix is
/// accepted, as their integrations add it). The caller's address is checked against the provider's allowlist before
/// anything else, then the MD5 hash, and only then is the request applied. Replies are always HTTP 200 with
/// Pragmatic's own error codes, except a refused address, which gets 403 and nothing more.
/// </summary>
public static partial class PragmaticWalletEndpoints
{
    private const int MaxBody = 16 * 1024;

    public static IEndpointRouteBuilder MapPragmaticWalletEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/providers/{providerId}/pragmatic/{method}", async (string providerId, string method, HttpContext context, IProviderDirectory providers,
            PragmaticWalletHandler handler, CancellationToken cancellationToken) =>
        {
            var provider = await providers.GetAsync(providerId, cancellationToken);
            if (provider is not { Protocol: ProviderProtocols.Pragmatic })
            {
                return Results.NotFound();
            }

            if (!await AllowedAsync(provider.AllowedAddresses, context.Connection.RemoteIpAddress, cancellationToken))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var name = method.EndsWith(".html", StringComparison.Ordinal) ? method[..^5] : method;
            if (!PragmaticWalletHandler.Methods.Contains(name))
            {
                return Results.NotFound();
            }

            if (context.Request.ContentLength > MaxBody || !context.Request.HasFormContentType)
            {
                return Json(PragmaticWalletHandler.Error(PragmaticError.BadParameters, "Send a form-encoded body"));
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var fields = form.Where(f => f.Value.Count == 1).ToDictionary(f => f.Key, f => f.Value.ToString(), StringComparer.Ordinal);
            return Json(await handler.HandleAsync(provider, name, fields, cancellationToken));
        })
        .AllowAnonymous()
        .DisableAntiforgery();
        return endpoints;
    }

    /// <summary>CIDR blocks and addresses match directly; host names (the simulator on the internal network) are resolved each time.</summary>
    public static async Task<bool> AllowedAsync(IReadOnlyList<string> entries, IPAddress? caller, CancellationToken cancellationToken)
    {
        if (caller is null || entries.Count == 0)
        {
            return false;
        }

        if (new AddressAllowlist(entries).Allows(caller))
        {
            return true;
        }

        var plain = caller.IsIPv4MappedToIPv6 ? caller.MapToIPv4() : caller;
        foreach (var host in entries.Where(e => AddressAllowlist.Parse(e) is null && HostName().IsMatch(e)))
        {
            try
            {
                if ((await Dns.GetHostAddressesAsync(host, cancellationToken)).Any(a => a.Equals(plain)))
                {
                    return true;
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                // An unknown host allows nobody.
            }
        }

        return false;
    }

    private static IResult Json(string body) => Results.Content(body, "application/json");

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,252}$")]
    public static partial Regex HostName();
}
