#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using healthspan_api.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Xunit;

namespace healthspan_api.tests.Regression;

public class DevQaAccountSeederTests
{
    private static (HealthspanApiFactory Factory, MainDbContext Db, UserManager<User> UserManager) CreateScope()
    {
        // Testing environment with the committed defaults: DevQaSeed is not
        // enabled anywhere in tests, so the app startup itself seeds nothing.
        var factory = new HealthspanApiFactory();
        var scope = factory.Services.CreateScope();
        return (
            factory,
            scope.ServiceProvider.GetRequiredService<MainDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<User>>());
    }

    private static DevQaSeedSettings EnabledSettings() => new()
    {
        Enabled = true,
        Email = "qa@dev.local",
        Password = "QaDev!2026test",
    };

    [Fact]
    public async Task AppStartup_CommittedDefaults_DoNotSeedAnything()
    {
        // Regression guard for the prod safety gate: with appsettings.json as
        // committed (DevQaSeed.Enabled=false), a full app startup creates no
        // QA account and no QA activities.
        var (factory, db, _) = CreateScope();

        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == "qa@dev.local"))
            .Should().BeFalse();
        (await db.WellnessActivities.IgnoreQueryFilters().AnyAsync())
            .Should().BeFalse();

        factory.Dispose();
    }

    [Fact]
    public async Task UpsertAsync_IsDisabledByDefault_CreatesNothing()
    {
        var (factory, db, userManager) = CreateScope();

        await DevQaAccountSeeder.UpsertAsync(db, userManager, new DevQaSeedSettings());

        (await db.Users.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.WellnessActivities.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();

        factory.Dispose();
    }

    [Fact]
    public async Task UpsertAsync_Enabled_CreatesQaUserWithValidPasswordAndSeedActivities()
    {
        var (factory, db, userManager) = CreateScope();

        await DevQaAccountSeeder.UpsertAsync(db, userManager, EnabledSettings());

        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "qa@dev.local");
        user.EmailConfirmed.Should().BeTrue();

        (await userManager.CheckPasswordAsync(user, "QaDev!2026test"))
            .Should().BeTrue("the QA account must be usable via POST /login (no Google)");
        (await userManager.CheckPasswordAsync(user, "wrong-password"))
            .Should().BeFalse();

        var activities = await db.WellnessActivities.IgnoreQueryFilters().ToListAsync();
        activities.Should().HaveCount(3);
        activities.Select(a => a.Dimension).Should().Contain([
            WellnessDimension.Strength,
            WellnessDimension.Health,
            WellnessDimension.Mobility,
        ]);
        activities.Should().OnlyContain(a => a.UserId == user.Id);
        activities.Should().OnlyContain(a => a.Metadata != null && a.Metadata.Count > 0);

        var strength = activities.Single(a => a.Dimension == WellnessDimension.Strength);
        strength.Type.Should().Be("strength_session");
        strength.Metadata!.Should().ContainKey("exercises");
        strength.Metadata.Should().ContainKey("rpe");

        factory.Dispose();
    }

    [Fact]
    public async Task UpsertAsync_IsIdempotent_AndDoesNotResurrectMutatedSeedRows()
    {
        var (factory, db, userManager) = CreateScope();
        var settings = EnabledSettings();

        await DevQaAccountSeeder.UpsertAsync(db, userManager, settings);

        // QA mutates a seeded row during verification (e.g. the F1 round-trip).
        var mutated = await db.WellnessActivities.IgnoreQueryFilters()
            .FirstAsync(a => a.Dimension == WellnessDimension.Health);
        mutated.Notes = "mutated by QA";
        mutated.Intensity = 1.25m;
        await db.SaveChangesAsync();

        await DevQaAccountSeeder.UpsertAsync(db, userManager, settings);
        await DevQaAccountSeeder.UpsertAsync(db, userManager, settings);

        (await db.Users.IgnoreQueryFilters().CountAsync(u => u.Email == "qa@dev.local"))
            .Should().Be(1);
        (await db.WellnessActivities.IgnoreQueryFilters().CountAsync())
            .Should().Be(3);

        var stillMutated = await db.WellnessActivities.IgnoreQueryFilters()
            .FirstAsync(a => a.Dimension == WellnessDimension.Health);
        stillMutated.Notes.Should().Be("mutated by QA");
        stillMutated.Intensity.Should().Be(1.25m);

        factory.Dispose();
    }

    [Fact]
    public async Task UpsertAsync_EnabledWithoutPassword_ThrowsExplicitly()
    {
        var (factory, db, userManager) = CreateScope();
        var settings = EnabledSettings();
        settings.Password = "";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DevQaAccountSeeder.UpsertAsync(db, userManager, settings));

        factory.Dispose();
    }
}
