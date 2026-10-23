#nullable enable

using System.Security.Cryptography;
using System.Text;

namespace Services;

/// <summary>
/// Stripe webhook signature verification (docs.stripe.com/webhooks#verify-official-libraries):
/// signed payload is "{timestamp}.{payload}", HMAC-SHA256 with the webhook signing
/// secret; the Stripe-Signature header carries "t=...,v1=sig,v1=sig...".
/// Kept as pure static logic so it is unit-testable without any HTTP.
/// </summary>
public static class StripeWebhookVerifier
{
    public const int DefaultToleranceSeconds = 300;

    public static void Verify(
        string payload,
        string? signatureHeader,
        string signingSecret,
        int toleranceSeconds = DefaultToleranceSeconds,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            throw new GatewayWebhookException("Missing Stripe-Signature header.");
        }

        long? timestamp = null;
        var candidateSignatures = new List<string>();

        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = part[..separatorIndex];
            var value = part[(separatorIndex + 1)..];

            switch (key)
            {
                case "t":
                    if (long.TryParse(value, out var parsedTimestamp))
                    {
                        timestamp = parsedTimestamp;
                    }
                    break;
                case "v1":
                    candidateSignatures.Add(value);
                    break;
            }
        }

        if (timestamp is null || candidateSignatures.Count == 0)
        {
            throw new GatewayWebhookException("Malformed Stripe-Signature header.");
        }

        var reference = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now is not null)
        {
            reference = now.Value.ToUnixTimeSeconds();
        }

        if (Math.Abs(reference - timestamp.Value) > toleranceSeconds)
        {
            throw new GatewayWebhookException("Stripe-Signature timestamp is outside the tolerance window.");
        }

        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)));

        var matches = candidateSignatures.Any(candidate =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(candidate)));

        if (!matches)
        {
            throw new GatewayWebhookException("Stripe-Signature does not match the payload.");
        }
    }
}
