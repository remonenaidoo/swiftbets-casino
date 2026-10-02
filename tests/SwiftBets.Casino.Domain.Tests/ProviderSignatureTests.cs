using System.Text;

namespace SwiftBets.Casino.Domain.Tests;

public sealed class ProviderSignatureTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"providerTransactionId":"p-1","amount":500}""");

    [Fact]
    public void A_body_signed_with_the_shared_secret_verifies() =>
        ProviderSignature.Verify("secret", Body, ProviderSignature.Compute("secret", Body)).ShouldBeTrue();

    [Fact]
    public void A_tampered_body_or_missing_signature_does_not_verify()
    {
        var signature = ProviderSignature.Compute("secret", Body);

        ProviderSignature.Verify("secret", Encoding.UTF8.GetBytes("""{"providerTransactionId":"p-1","amount":50000}"""), signature).ShouldBeFalse();
        ProviderSignature.Verify("secret", Body, null).ShouldBeFalse();
        ProviderSignature.Verify(null, Body, signature).ShouldBeFalse();
    }

    [Fact]
    public void A_session_token_hashes_the_same_every_time_and_tokens_differ()
    {
        var (token, hash) = SessionToken.New();

        SessionToken.Hash(token).ShouldBe(hash);
        SessionToken.New().Token.ShouldNotBe(token);
    }
}
