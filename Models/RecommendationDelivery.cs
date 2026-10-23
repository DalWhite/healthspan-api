#nullable enable

/// <summary>
/// One organic recommendation delivery (a readiness response that actually
/// carried suggestions). Used to enforce the "1 sponsored slot per N organic
/// recommendations" frequency cap without counting sponsored impressions.
/// </summary>
public class RecommendationDelivery
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int SuggestionCount { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
