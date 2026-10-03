using Dapper;
using Microsoft.Extensions.Options;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Casino.Application;
using SwiftBets.Casino.Application.Ports;

namespace SwiftBets.Casino.Infrastructure.Providers;

/// <summary>
/// A provider's settings: the database row (on/off, and anything an operator saved) over configuration. Credentials
/// saved from the console are decrypted here and only here; nothing returns them to a caller outside the service.
/// </summary>
public sealed class SqlProviderDirectory(ISqlConnectionFactory connections, CredentialCipher cipher, IOptions<CasinoOptions> options, TimeProvider time) : IProviderDirectory
{
    private static readonly SqlResources Sql = SqlResources.For<SqlProviderDirectory>();

    public async Task<ProviderSettings?> GetAsync(string providerId, CancellationToken cancellationToken) =>
        (await QueryAsync(providerId, cancellationToken)).SingleOrDefault();

    public Task<IReadOnlyList<ProviderSettings>> ListAsync(CancellationToken cancellationToken) => QueryAsync(null, cancellationToken);

    public async Task<bool> UpdateAsync(string providerId, ProviderUpdate update, Guid operatorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Casino.UpdateProvider"), new
        {
            ProviderId = providerId, update.Enabled, SetAllowlist = update.AllowedAddresses is not null,
            AllowedAddresses = update.AllowedAddresses is { } list ? string.Join('\n', list.Select(a => a.Trim()).Where(a => a.Length > 0)) : null,
            update.ApiBaseUrl, update.DemoBaseUrl, Now = time.GetUtcNow(), OperatorId = operatorId,
        }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<bool> SetCredentialsAsync(string providerId, string? secureLogin, string? secret, Guid operatorId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(Sql.Get("Casino.SetCredentials"), new
        {
            ProviderId = providerId,
            SecureLoginCipher = string.IsNullOrEmpty(secureLogin) ? null : cipher.Encrypt(providerId, secureLogin),
            SecretCipher = string.IsNullOrEmpty(secret) ? null : cipher.Encrypt(providerId, secret),
            Now = time.GetUtcNow(), OperatorId = operatorId,
        }, cancellationToken: cancellationToken)) > 0;
    }

    private async Task<IReadOnlyList<ProviderSettings>> QueryAsync(string? providerId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(Sql.Get("Casino.ListProviders"), new { ProviderId = providerId }, cancellationToken: cancellationToken));
        return [.. rows.Select(Merge)];
    }

    private ProviderSettings Merge(Row row)
    {
        options.Value.Providers.TryGetValue(row.ProviderId, out var configured);
        configured ??= new ProviderOptions();
        var allowlist = row.AllowedAddresses is { Length: > 0 } saved ? saved.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [.. configured.AllowedAddresses];
        return new ProviderSettings(
            row.ProviderId, row.Enabled, row.WalletModel, configured.Protocol,
            cipher.Decrypt(row.ProviderId, row.SecretCipher) ?? configured.Secret,
            cipher.Decrypt(row.ProviderId, row.SecureLoginCipher) ?? configured.SecureLogin,
            configured.LaunchBaseUrl,
            Pick(row.ApiBaseUrl, configured.ApiBaseUrl),
            Pick(row.DemoBaseUrl, configured.DemoBaseUrl),
            configured.ImageBaseUrl,
            allowlist,
            row.UpdatedAt);
    }

    private static string Pick(string? saved, string configured) => string.IsNullOrWhiteSpace(saved) ? configured : saved;

    private sealed record Row(string ProviderId, string WalletModel, bool Enabled, string? AllowedAddresses, string? ApiBaseUrl, string? DemoBaseUrl,
        byte[]? SecureLoginCipher, byte[]? SecretCipher, DateTimeOffset? UpdatedAt);
}
