using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace healthspan_api.tests.Regression;

public class MuscleCatalogSeederTests
{
    [Fact]
    public async Task UpsertAsync_ReusesLegacyMatchesWithoutKeyConflicts_AndQuarantinesDuplicates()
    {
        var options = new DbContextOptionsBuilder<MainDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new MainDbContext(
            options,
            new CurrentUserProvider(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }));

        db.Muscles.AddRange(
            new Muscle
            {
                Id = 1,
                Name = "Biceps",
                Side = MuscleSide.front,
                MuscleGroup = MuscleGroup.Arms
            },
            new Muscle
            {
                Id = 99,
                Name = "Biceps",
                Side = MuscleSide.front,
                MuscleGroup = MuscleGroup.Arms
            });

        await db.SaveChangesAsync();

        await MuscleCatalogSeeder.UpsertAsync(db);

        var muscles = await db.Muscles
            .OrderBy(muscle => muscle.Id)
            .ToListAsync();

        muscles.Should().ContainSingle(muscle => muscle.Slug == "biceps");
        muscles.Should().ContainSingle(muscle => muscle.Slug == "chest");
        muscles.Single(muscle => muscle.Slug == "biceps").Id.Should().Be(1);
        muscles.Single(muscle => muscle.Slug == "legacy-99").SortOrder.Should().Be(0);
        muscles.Count(muscle => muscle.SortOrder > 0).Should().Be(MuscleCatalogSeeder.CanonicalMuscles.Count);
    }
}
