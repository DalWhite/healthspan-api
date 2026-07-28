using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class HealthProfileService(MainDbContext context)
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

        return Map(profile);
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

        profile.DateOfBirth = resource.DateOfBirth;
        profile.BiologicalSex = resource.BiologicalSex;
        await context.SaveChangesAsync(cancellationToken);

        return Map(profile);
    }

    private static HealthProfileResource Map(HealthProfile profile)
        => new()
        {
            DateOfBirth = profile?.DateOfBirth,
            BiologicalSex =
                profile?.BiologicalSex ?? BiologicalSex.Unknown
        };
}
