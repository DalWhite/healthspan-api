#nullable enable

namespace Services;

/// <summary>
/// Validates trainer payment links against the Fase 1 allowlist. Only HTTPS URLs
/// whose host is exactly (or a subdomain of) an allowlisted domain are accepted,
/// so lookalikes like "checkout.stripe.com.evil.tld" are rejected.
/// </summary>
public static class TrainerPaymentLinkValidator
{
    public static readonly IReadOnlyList<string> AllowedDomains = new[]
    {
        "checkout.stripe.com",
        "buy.stripe.com",
        "paypal.me",
        "paypal.com",
    };

    /// <summary>
    /// Returns the verified allowlisted domain, or null when the URL is not acceptable.
    /// </summary>
    public static string? Validate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048)
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo) || parsed.Port is not (0 or 443))
        {
            return null;
        }

        var host = parsed.Host.ToLowerInvariant();

        foreach (var allowed in AllowedDomains)
        {
            if (host == allowed || host.EndsWith("." + allowed, StringComparison.Ordinal))
            {
                return allowed;
            }
        }

        return null;
    }
}
