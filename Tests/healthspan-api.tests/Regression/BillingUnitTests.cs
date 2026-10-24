using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Services;
using Xunit;

namespace healthspan_api.tests.Regression;

public class StripeWebhookVerifierTests
{
    private const string Secret = "whsec_unit_test_0123456789";
    private const string Payload = """{"id":"evt_unit_1","type":"ping","data":{}}""";

    private static string Sign(string payload, long timestamp, string secret = Secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        // Stripe emits v1 signatures as lowercase hex; sign the way Stripe signs.
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
    }

    // QA-B1 regression (t_c95d1623): a Stripe-style lowercase-hex v1 must verify.
    // Before the fix the verifier compared its uppercase Convert.ToHexString() byte-by-byte
    // against the received candidate, so every real Stripe webhook was rejected with 400.
    [Fact]
    public void StripeStyleLowercaseHexSignature_IsAccepted()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var lowercase = Sign(Payload, timestamp);
        lowercase.Should().MatchRegex("^[0-9a-f]{64}$");

        var act = () => StripeWebhookVerifier.Verify(
            Payload,
            $"t={timestamp},v1={lowercase}",
            Secret);

        act.Should().NotThrow(
            "Stripe sends v1 signatures as lowercase hex; a genuine Stripe webhook must verify");
    }

    // Uppercase hex must also verify (case-insensitive comparison), so no caller regresses.
    [Fact]
    public void UppercaseHexSignature_IsAlsoAccepted()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var act = () => StripeWebhookVerifier.Verify(
            Payload,
            $"t={timestamp},v1={Sign(Payload, timestamp).ToUpperInvariant()}",
            Secret);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidSignature_DoesNotThrow()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var act = () => StripeWebhookVerifier.Verify(
            Payload,
            $"t={timestamp},v1={Sign(Payload, timestamp)}",
            Secret);

        act.Should().NotThrow();
    }

    [Fact]
    public void MultipleV1Signatures_OneValid_IsAccepted()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var act = () => StripeWebhookVerifier.Verify(
            Payload,
            $"t={timestamp},v1={new string('0', 64)},v1={Sign(Payload, timestamp)}",
            Secret);

        act.Should().NotThrow();
    }

    [Fact]
    public void WrongSecret_Throws()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var act = () => StripeWebhookVerifier.Verify(
            Payload,
            $"t={timestamp},v1={Sign(Payload, timestamp, "whsec_other")}",
            Secret);

        act.Should().Throw<GatewayWebhookException>();
    }

    [Fact]
    public void TamperedPayload_Throws()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var act = () => StripeWebhookVerifier.Verify(
            Payload + " ",
            $"t={timestamp},v1={Sign(Payload, timestamp)}",
            Secret);

        act.Should().Throw<GatewayWebhookException>();
    }

    [Fact]
    public void TimestampOutsideTolerance_Throws()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10_000;

        var act = () => StripeWebhookVerifier.Verify(
            Payload,
            $"t={timestamp},v1={Sign(Payload, timestamp)}",
            Secret);

        act.Should().Throw<GatewayWebhookException>()
            .WithMessage("*tolerance*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v1=abcdef")]
    public void MissingOrMalformedHeader_Throws(string? header)
    {
        var act = () => StripeWebhookVerifier.Verify(Payload, header, Secret);

        act.Should().Throw<GatewayWebhookException>();
    }
}

public class TrainerPaymentLinkValidatorTests
{
    [Theory]
    [InlineData("https://checkout.stripe.com/b/pay/pl_123", "checkout.stripe.com")]
    [InlineData("https://buy.stripe.com/abc123", "buy.stripe.com")]
    [InlineData("https://paypal.me/mytrainer", "paypal.me")]
    [InlineData("https://www.paypal.com/cgi-bin/webscr?cmd=x", "paypal.com")]
    [InlineData("https://paypal.com/pay", "paypal.com")]
    public void AllowlistedUrls_AreAccepted(string url, string expectedDomain)
    {
        TrainerPaymentLinkValidator.Validate(url).Should().Be(expectedDomain);
    }

    [Theory]
    [InlineData("https://checkout.stripe.com.evil.test/pay")]
    [InlineData("https://evil.test/?next=https://checkout.stripe.com")]
    [InlineData("http://paypal.me/trainer")]
    [InlineData("https://paypal.me@evil.test/")]
    [InlineData("https://checkout.stripe.com:8443/pay")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void NonAllowlistedUrls_AreRejected(string? url)
    {
        TrainerPaymentLinkValidator.Validate(url).Should().BeNull();
    }
}
