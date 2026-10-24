#nullable enable

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Processed gateway webhook event. <see cref="GatewayEventId"/> is unique, which
/// is what makes webhook processing idempotent (Stripe retries are deduped here).
/// Only a SHA-256 hash of the payload is kept — no raw event data, no personal
/// data, is persisted.
/// </summary>
public class BillingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GatewayEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    public static string HashPayload(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}
