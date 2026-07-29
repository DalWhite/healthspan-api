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
