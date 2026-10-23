#nullable enable

using Microsoft.EntityFrameworkCore;

namespace healthspan_api.Persistence.Repositories;

public class SponsoredOffersRepository(MainDbContext context)
{
    public SponsoredOffer? GetOfferBySlug(string slug)
    {
        return context.SponsoredOffers
            .FirstOrDefault(offer => offer.Slug == slug);
    }

    /// <summary>Active offers ordered so the least-shown one wins (simple rotation).</summary>
    public List<SponsoredOffer> GetActiveOffers()
    {
        var offers = context.SponsoredOffers
            .Where(offer => offer.Active)
            .ToList();

        var impressionCounts = context.SponsoredEvents
            .IgnoreQueryFilters()
            .Where(e => e.EventType == SponsoredEventType.Impression)
            .GroupBy(e => e.OfferId)
            .Select(group => new { OfferId = group.Key, Count = group.Count() })
            .ToList();

        return offers
            .OrderBy(offer => impressionCounts
                .Where(count => count.OfferId == offer.Id)
                .Select(count => (int?)count.Count)
                .FirstOrDefault() ?? 0)
            .ThenBy(offer => offer.Id)
            .ToList();
    }

    public DateTime? LastImpressionUtc(string userId)
    {
        return context.SponsoredEvents
            .IgnoreQueryFilters()
            .Where(e => e.UserId == userId && e.EventType == SponsoredEventType.Impression)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Select(e => (DateTime?)e.OccurredAtUtc)
            .FirstOrDefault();
    }

    public int CountImpressionsSince(string userId, DateTime sinceUtc)
    {
        return context.SponsoredEvents
            .IgnoreQueryFilters()
            .Count(e => e.UserId == userId
                && e.EventType == SponsoredEventType.Impression
                && e.OccurredAtUtc >= sinceUtc);
    }

    public int CountDeliveriesSince(string userId, DateTime sinceUtc)
    {
        return context.RecommendationDeliveries
            .IgnoreQueryFilters()
            .Count(d => d.UserId == userId && d.OccurredAtUtc >= sinceUtc);
    }

    public void AddEvent(SponsoredEvent sponsoredEvent)
    {
        context.SponsoredEvents.Add(sponsoredEvent);
        context.SaveChanges();
    }

    public void AddDelivery(RecommendationDelivery delivery)
    {
        context.RecommendationDeliveries.Add(delivery);
        context.SaveChanges();
    }

    /// <summary>Aggregated minimum metrics per offer (cross-user; admin path only).</summary>
    public List<SponsoredOfferMetricsRow> GetMetrics()
    {
        return context.SponsoredOffers
            .Select(offer => new SponsoredOfferMetricsRow
            {
                Slug = offer.Slug,
                DisplayName = offer.DisplayName,
                Active = offer.Active,
                Impressions = offer.Events.Count(e => e.EventType == SponsoredEventType.Impression),
                Clicks = offer.Events.Count(e => e.EventType == SponsoredEventType.Click),
                Dismisses = offer.Events.Count(e => e.EventType == SponsoredEventType.Dismiss),
                Conversions = offer.Events.Count(e => e.EventType == SponsoredEventType.Conversion),
            })
            .OrderBy(row => row.Slug)
            .ToList();
    }
}

public sealed class SponsoredOfferMetricsRow
{
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Active { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public int Dismisses { get; set; }
    public int Conversions { get; set; }
}
