using Grpc.Core;
using Microsoft.Extensions.Options;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Casino.Application.Ports;
using SwiftBets.Contracts.Grpc.Wallet.V1;
using WalletGrpc = SwiftBets.Contracts.Grpc.Wallet.V1.Wallet;

namespace SwiftBets.Casino.Infrastructure.Wallet;

/// <summary>Debits and credits over the wallet's gRPC API. A transport failure is Unavailable: the key makes the provider's retry safe.</summary>
public sealed class GrpcWalletPort(WalletGrpc.WalletClient client, IOptions<WalletOptions> options, ClientCredentialsTokenProvider tokens) : IWalletPort
{
    public Task<WalletResult> DebitAsync(string idempotencyKey, Guid punterId, long amount, string currency, string reference, CancellationToken cancellationToken) =>
        PostAsync(deadline => client.DebitAsync(Request(idempotencyKey, punterId, amount, currency, reference, "casino-bet"), deadline: deadline, cancellationToken: cancellationToken).ResponseAsync);

    public Task<WalletResult> CreditAsync(string idempotencyKey, Guid punterId, long amount, string currency, string reference, CancellationToken cancellationToken) =>
        PostAsync(deadline => client.CreditAsync(Request(idempotencyKey, punterId, amount, currency, reference, "casino-win"), deadline: deadline, cancellationToken: cancellationToken).ResponseAsync);

    public async Task<WalletResult> GetBalanceAsync(Guid punterId, CancellationToken cancellationToken) =>
        await CallAsync(async deadline =>
        {
            var reply = await client.GetBalanceAsync(new GetBalanceRequest { AccountId = punterId.ToString() }, deadline: deadline, cancellationToken: cancellationToken);
            return reply.OutcomeCase == BalanceReply.OutcomeOneofCase.Balance
                ? new WalletResult(WalletStatus.Succeeded, null, reply.Balance.Available.MinorUnits)
                : new WalletResult(WalletStatus.Refused, reply.Failure.Code.ToString(), null);
        });

    private static PostingRequest Request(string key, Guid punterId, long amount, string currency, string reference, string reason) => new()
    {
        IdempotencyKey = key, AccountId = punterId.ToString(), Amount = new Money { MinorUnits = amount, Currency = currency }, Reference = reference, Reason = reason,
    };

    private Task<WalletResult> PostAsync(Func<DateTime, Task<PostingReply>> call) =>
        CallAsync(async deadline =>
        {
            var reply = await call(deadline);
            return reply.OutcomeCase == PostingReply.OutcomeOneofCase.Posting
                ? new WalletResult(WalletStatus.Succeeded, null, reply.Posting.Balance?.Available?.MinorUnits)
                : new WalletResult(WalletStatus.Refused, reply.Failure.Code.ToString(), null);
        });

    private async Task<WalletResult> CallAsync(Func<DateTime, Task<WalletResult>> call)
    {
        try
        {
            return await call(DateTime.UtcNow.AddSeconds(options.Value.DeadlineSeconds));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
        {
            // The identity service rotated its key: drop the dead token so the provider's retry gets a fresh one.
            tokens.Invalidate(await tokens.GetTokenAsync(CancellationToken.None));
            return new WalletResult(WalletStatus.Unavailable, null, null);
        }
        catch (RpcException ex) when (ex.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied))
        {
            return new WalletResult(WalletStatus.Unavailable, null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            return new WalletResult(WalletStatus.Unavailable, null, null);
        }
    }
}
