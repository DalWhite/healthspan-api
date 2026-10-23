#nullable enable

namespace healthspan_api.Options;

/// <summary>
/// Stripe configuration. Keys are provided at deploy time (user secrets / env);
/// nothing Stripe-related is committed.
/// </summary>
public class StripeSettings
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSigningSecret { get; set; } = string.Empty;

    /// <summary>Stripe price id per plan slug (e.g. "premium-monthly" → "price_...").</summary>
    public Dictionary<string, string> PriceIds { get; set; } = new();

    /// <summary>Where Stripe Checkout redirects after a successful payment.</summary>
    public string SuccessUrl { get; set; } = "https://localhost:5001/billing/success?session_id={CHECKOUT_SESSION_ID}";
    public string CancelUrl { get; set; } = "https://localhost:5001/billing/cancel";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SecretKey) &&
        !string.IsNullOrWhiteSpace(WebhookSigningSecret);
}
