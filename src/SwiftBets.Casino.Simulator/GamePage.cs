using System.Security.Cryptography;

namespace SwiftBets.Casino.Simulator;

/// <summary>
/// The playable game page. It is HTML meant to be framed by the site, so it replaces the JSON API baseline headers
/// (no framing, no scripts) with its own: framed only by the same origin, and running only its own inline script.
/// </summary>
public static class GamePage
{
    public static (string Html, string Policy) Render(string page, string nonce)
    {
        ArgumentNullException.ThrowIfNull(page);
        var html = page.Replace("<script>", $"<script nonce=\"{nonce}\">", StringComparison.Ordinal);
        var policy = $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; form-action 'none'; frame-ancestors 'self'";
        return (html, policy);
    }

    public static IResult Serve(string page, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (html, policy) = Render(page, Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)));
        context.Response.Headers.ContentSecurityPolicy = policy;
        context.Response.Headers.XFrameOptions = "SAMEORIGIN";
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
