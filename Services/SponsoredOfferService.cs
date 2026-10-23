#nullable enable

using healthspan_api.Options;
using healthspan_api.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Services;

/// <summary>Where a sponsored slot would be inserted. Medical/marker contexts
/// are never eligible (policy: docs/decision-monetizacion-t_f625fb1c.md §3).</summary>
public enum SponsoredSlotContext
{
    Training,
    Medical
}

/// <summary>
/// Decides when a pre-approved affiliate offer may be attached to organic
/// recommendations, enforces the frequency caps, records the impression, and
/// always returns the slot with the mandatory "Patrocinado" label.
/// </summary>
public class SponsoredOfferService
{
    private readonly SponsoredOffersRepository _repository;
    private readonly MainDbContext _context;
    private readonly SponsoredOffersSettings _settings;

    public SponsoredOfferService(
        SponsoredOffersRepository repository,
        MainDbContext context,
        IOptions<SponsoredOffersSettings> settings)
    {
        _repository = repository;
        _context = context;
        _settings = settings.Value;
    }

    /// <summary>
    /// Returns a labeled sponsored slot for this user, or null when the feature
    /// is off or any eligibility rule fails. Records the impression when it
    /// returns a slot.
    /// </summary>
    public SponsoredSlotResource? TryBuildSlot(
        string userId,
        SponsoredSlotContext slotContext,
        int organicSuggestions)
    {
        if (!_settings.Enabled)
        {
            return null;
        }

        // Hard policy: never in medical / health-marker context.
        if (slotContext == SponsoredSlotContext.Medical)
        {
            return null;
        }

        // A sponsored slot accompanies organic recommendations; with none
        // delivered there is nothing to insert it into.
        if (organicSuggestions <= 0)
        {
            return null;
        }

        if (!IsAdult(userId))
        {
            return null;
        }

        // Cap 1: max sponsored impressions per user per day.
        var dayStartUtc = DateTime.UtcNow.Date;
        if (_repository.CountImpressionsSince(userId, dayStartUtc) >= _settings.MaxPerUserPerDay)
        {
            return null;
        }

        // Cap 2: at least SlotEveryRecommendations organic recommendations
        // delivered since the last sponsored impression.
        var sinceUtc = _repository.LastImpressionUtc(userId) ?? DateTime.UnixEpoch;
        if (_repository.CountDeliveriesSince(userId, sinceUtc) < _settings.SlotEveryRecommendations)
        {
            return null;
        }

        var offer = _repository.GetActiveOffers()
            .FirstOrDefault(candidate => IsDomainAllowed(candidate.ExternalUrl));

        if (offer is null)
        {
            return null;
        }

        _repository.AddEvent(new SponsoredEvent
        {
            OfferId = offer.Id,
            UserId = userId,
            EventType = SponsoredEventType.Impression,
        });

        return new SponsoredSlotResource
        {
            Slug = offer.Slug,
            DisplayName = offer.DisplayName,
            Description = offer.Description,
            Category = offer.Category.ToString().ToLowerInvariant(),
            ExternalUrl = offer.ExternalUrl,
            Label = "Patrocinado",
            Sponsored = true,
        };
    }

    /// <summary>Logs one organic recommendation delivery (counts toward the ratio cap).</summary>
    public void LogDelivery(string userId, int suggestionCount)
    {
        if (suggestionCount <= 0)
        {
            return;
        }

        _repository.AddDelivery(new RecommendationDelivery
        {
            UserId = userId,
            SuggestionCount = suggestionCount,
        });
    }

    /// <summary>Records a client-reported event (click / dismiss / conversion).</summary>
    public bool RecordEvent(string userId, string slug, SponsoredEventType eventType)
    {
        if (eventType == SponsoredEventType.Impression)
        {
            // Impressions are server-side only; clients cannot inflate them.
            return false;
        }

        var offer = _repository.GetOfferBySlug(slug);
        if (offer is null || !offer.Active)
        {
            return false;
        }

        _repository.AddEvent(new SponsoredEvent
        {
            OfferId = offer.Id,
            UserId = userId,
            EventType = eventType,
        });

        return true;
    }

    public List<SponsoredOfferMetricsRow> GetMetrics() => _repository.GetMetrics();

    public bool CanReadMetrics(string? email)
    {
        return !string.IsNullOrWhiteSpace(email)
            && _settings.MetricsEmails.Any(
                allowed => string.Equals(allowed, email, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAdult(string userId)
    {
        var user = _context.Users
            .IgnoreQueryFilters()
            .Include(candidate => candidate.HealthProfile)
            .FirstOrDefault(candidate => candidate.Id == userId);

        if (user is null)
        {
            return false;
        }

        if (user.HealthProfile?.DateOfBirth is { } dateOfBirth)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - dateOfBirth.Year;
            if (dateOfBirth.AddYears(age) > today)
            {
                age--;
            }

            return age >= _settings.MinAge;
        }

        // No DOB on file: fall back to the self-reported age; unknown age is
        // treated as not-adult (conservative).
        return user.Age.HasValue && user.Age.Value >= _settings.MinAge;
    }

    private bool IsDomainAllowed(string externalUrl)
    {
        if (!Uri.TryCreate(externalUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        return _settings.AllowedDomains.Any(
            domain => string.Equals(domain, uri.Host, StringComparison.OrdinalIgnoreCase));
    }
}
