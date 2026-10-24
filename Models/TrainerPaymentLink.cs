#nullable enable

/// <summary>
/// External payment link a trainer publishes so trainees can pay them directly,
/// outside the platform (Fase 1 decision: the platform never touches that money).
/// The URL is validated against a domain allowlist before it is stored.
/// </summary>
public class TrainerPaymentLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public string Url { get; set; } = string.Empty;
    public string VerifiedDomain { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
