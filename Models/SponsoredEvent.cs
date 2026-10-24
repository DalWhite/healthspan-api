#nullable enable

/// <summary>
/// Minimum sponsored-slot metrics: impressions (recorded server-side when a
/// slot is attached), clicks and dismisses (recorded from the client), and
/// affiliate conversions (recorded manually or via affiliate postback later).
/// </summary>
public class SponsoredEvent
{
    public long Id { get; set; }

    public int OfferId { get; set; }
    public SponsoredOffer Offer { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;

    public SponsoredEventType EventType { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public enum SponsoredEventType
{
    Impression,
    Click,
    Dismiss,
    Conversion
}
