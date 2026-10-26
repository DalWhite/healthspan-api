#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class HealthProfileService(
    MainDbContext context,
    BloodAnalyticsService bloodAnalyticsService)
{
    public async Task<HealthProfileResource> GetAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var profile = await context.HealthProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.UserId == userId,
                cancellationToken);
        var user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == userId,
                cancellationToken);

        return Map(profile, user);
    }

    public async Task<HealthProfileResource> UpdateAsync(
        string userId,
        UpdateHealthProfileResource resource,
        CancellationToken cancellationToken)
    {
        var profile = await context.HealthProfiles
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.UserId == userId,
                cancellationToken);

        if (profile is null)
        {
            profile = new HealthProfile { UserId = userId };
            context.HealthProfiles.Add(profile);
        }

        var user = await context.Users
            .SingleAsync(
                item => item.Id == userId,
                cancellationToken);

        profile.DateOfBirth = resource.DateOfBirth;
        profile.BiologicalSex = resource.BiologicalSex;
        if (resource.Name is not null)
        {
            user.Name = string.IsNullOrWhiteSpace(resource.Name)
                ? null
                : resource.Name.Trim();
        }
        if (resource.HeightCm.HasValue)
        {
            user.Height = resource.HeightCm;
        }
        if (resource.WeightKg.HasValue)
        {
            user.Weight = resource.WeightKg;

            // K2 time series (t_358e09e9): every registered weight also writes
            // a BodyCompositionReading in the SAME SaveChanges — the scalar and
            // the series can never diverge. No synthetic seeding otherwise.
            var definition = await BodyCompositionCatalog.EnsureWeightKgDefinitionAsync(
                context,
                cancellationToken);

            var now = DateTime.UtcNow;
            context.BodyCompositionReadings.Add(new BodyCompositionReading
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                HealthMetricDefinitionId = definition.Id,
                Value = resource.WeightKg.Value,
                Source = BodyCompositionReadingSources.Manual,
                OccurredOnUtc = now,
                CreatedAtUtc = now
            });
        }
        await context.SaveChangesAsync(cancellationToken);
        await bloodAnalyticsService
            .ReclassifyMarkersForUserAsync(
                userId,
                cancellationToken);

        return Map(profile, user);
    }

    private static HealthProfileResource Map(
        HealthProfile? profile,
        User? user)
        => new()
        {
            Name = user?.Name,
            DateOfBirth = profile?.DateOfBirth,
            BiologicalSex =
                profile?.BiologicalSex ?? BiologicalSex.Unknown,
            HeightCm = user?.Height,
            WeightKg = user?.Weight
        };
}
