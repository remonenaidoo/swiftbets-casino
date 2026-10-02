using System.Text.Json;
using Microsoft.Extensions.Options;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Handlers;
using SwiftBets.Casino.Domain;

namespace SwiftBets.Casino.Gateway.Api.Endpoints;

/// <summary>
/// The seamless-wallet API providers call back into. Not behind user auth: each request is authenticated by the
/// provider's HMAC over the exact body, checked before anything is parsed.
/// </summary>
public static class ProviderWalletEndpoints
{
    public const string SignatureHeader = "X-Provider-Signature";
    private const int MaxBody = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProviderWalletEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/providers/{providerId}/wallet/{action}", async (string providerId, string action, HttpContext context, WalletCallbackHandler handler,
            IOptions<CasinoOptions> options, CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<WalletAction>(action, ignoreCase: true, out var walletAction) || !Enum.IsDefined(walletAction))
            {
                return Results.NotFound();
            }

            var body = await ReadBodyAsync(context.Request, cancellationToken);
            options.Value.Providers.TryGetValue(providerId, out var provider);
            if (body is null || !ProviderSignature.Verify(provider?.Secret, body, context.Request.Headers[SignatureHeader]))
            {
                return Results.Json(new CallbackReply("signature_invalid", null, null), statusCode: StatusCodes.Status401Unauthorized);
            }

            WalletCallback? callback;
            try
            {
                callback = JsonSerializer.Deserialize<WalletCallback>(body, Json);
            }
            catch (JsonException)
            {
                callback = null;
            }

            if (callback is null)
            {
                return Results.Json(new CallbackReply(CallbackReply.InvalidRequest, null, null), statusCode: StatusCodes.Status400BadRequest);
            }

            var reply = await handler.HandleAsync(providerId, walletAction, callback, cancellationToken);
            var status = reply.Status switch
            {
                CallbackReply.InvalidRequest => StatusCodes.Status400BadRequest,
                CallbackReply.WalletUnavailable => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status200OK,
            };
            return Results.Json(reply, statusCode: status);
        })
        .AllowAnonymous();
        return endpoints;
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBody)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        return buffer.Length > MaxBody ? null : buffer.ToArray();
    }
}
