using SwiftBets.Casino.Application.Ports;

namespace SwiftBets.Casino.Infrastructure.Tests;

/// <summary>An in-memory wallet that, like the real one, applies each idempotency key once.</summary>
internal sealed class FakeWallet(long opening) : IWalletPort
{
    private readonly HashSet<string> _applied = [];

    public long Balance { get; private set; } = opening;

    public int Postings { get; private set; }

    public Task<WalletResult> DebitAsync(string idempotencyKey, Guid punterId, long amount, string currency, string reference, CancellationToken cancellationToken)
    {
        if (_applied.Contains(idempotencyKey))
        {
            return Ok();
        }

        if (amount > Balance)
        {
            return Task.FromResult(new WalletResult(WalletStatus.Refused, "InsufficientFunds", null));
        }

        _applied.Add(idempotencyKey);
        Balance -= amount;
        Postings++;
        return Ok();
    }

    public Task<WalletResult> CreditAsync(string idempotencyKey, Guid punterId, long amount, string currency, string reference, CancellationToken cancellationToken)
    {
        if (_applied.Add(idempotencyKey))
        {
            Balance += amount;
            Postings++;
        }

        return Ok();
    }

    public Task<WalletResult> GetBalanceAsync(Guid punterId, CancellationToken cancellationToken) => Ok();

    private Task<WalletResult> Ok() => Task.FromResult(new WalletResult(WalletStatus.Succeeded, null, Balance));
}
