#nullable enable

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using healthspan_api.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Xunit;

namespace healthspan_api.tests;

/// <summary>
/// Sponsored offers (t_45c5213f): feature flag OFF by default, mandatory
/// "Patrocinado" label, frequency caps, minors/medical exclusion, domain
/// allowlist and minimum metrics.
/// </summary>
public class SponsoredOffersTests : ApiTestBase
{
    public SponsoredOffersTests(HealthspanApiFactory factory) : base(factory)
    {
    }

    // ---------- Feature flag OFF (default) ----------

    [Fact]
    public async Task Readiness_Has_No_Sponsored_Slot_When_Flag_Off()
    {
        var readiness = await Client.GetFromJsonAsync<ReadinessResource>(
            $"/readiness?traineeId={TestDataSeeder.TestUserId}");

        readiness.Should().NotBeNull();
        readiness!.Sponsored.Should().BeNull();
    }

    // ---------- Enabled variants ----------

    public class EnabledFactory : HealthspanApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("SponsoredOffersEnabled", "true");
            builder.UseSetting("SponsoredOffersAllowedDomains:0", "store.example.com");
            builder.UseSetting("SponsoredOffersMetricsEmails:0", TestUserEmail);
        }
    }

    public class MinAge31Factory : EnabledFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("SponsoredOffersMinAge", "31");
        }
    }

    public class RatioFactory : EnabledFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Raise the daily cap so the ratio cap is the only thing under test.
            builder.UseSetting("SponsoredOffersMaxPerUserPerDay", "3");
        }
    }

    public static async Task SeedSponsoredFixtureAsync(HealthspanApiFactory factory, int deliveries)
    {
        // Custom factories have their own InMemory DB: seed the standard
        // user/tracker fixture first, then the sponsored fixture on top.
        using (var seedScope = factory.Services.CreateScope())
        {
            await TestDataSeeder.SeedAsync(seedScope.ServiceProvider);
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

        if (!db.SponsoredOffers.Any())
        {
            db.SponsoredOffers.Add(new SponsoredOffer
            {
                Slug = "creatina-monohidrato",
                DisplayName = "Creatina monohidrato Creapure® 300 g",
                Description = "Compra en la tienda de nuestro afiliado.",
                Category = SponsoredOfferCategory.Supplement,
                ExternalUrl = "https://store.example.com/creatina",
                Active = true,
            });
        }

        for (var i = 0; i < deliveries; i++)
        {
            db.RecommendationDeliveries.Add(new RecommendationDelivery
            {
                UserId = TestDataSeeder.TestUserId,
                SuggestionCount = 1,
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-30 - i),
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<ReadinessResource?> GetReadinessAsync(HttpClient client)
        => await client.GetFromJsonAsync<ReadinessResource>($"/readiness?traineeId={TestDataSeeder.TestUserId}");

    // ---------- Eligibility + labeling ----------

    [Fact]
    public async Task Readiness_Returns_Labeled_Sponsored_Slot_When_Enabled_And_Eligible()
    {
        using var factory = new EnabledFactory();
        await SeedSponsoredFixtureAsync(factory, deliveries: 10);
        using var client = factory.CreateClient();

        var readiness = await GetReadinessAsync(client);

        readiness.Should().NotBeNull();
        readiness!.Suggestions.Should().NotBeEmpty(
            "the seeded tracker data must produce at least one organic suggestion for the slot to attach");
        readiness.Sponsored.Should().NotBeNull();
        readiness.Sponsored!.Label.Should().Be("Patrocinado");
        readiness.Sponsored.Sponsored.Should().BeTrue();
        readiness.Sponsored.Slug.Should().Be("creatina-monohidrato");
        readiness.Sponsored.ExternalUrl.Should().Be("https://store.example.com/creatina");
        // Never mixed into organic suggestions.
        readiness.Suggestions.Should().NotContain(s => s.Name == readiness.Sponsored!.DisplayName);
    }

    [Fact]
    public async Task Sponsored_Slot_Respects_Daily_Cap()
    {
        using var factory = new EnabledFactory();
        await SeedSponsoredFixtureAsync(factory, deliveries: 10);
        using var client = factory.CreateClient();

        var first = await GetReadinessAsync(client);
        var second = await GetReadinessAsync(client);

        first!.Sponsored.Should().NotBeNull();
        second!.Sponsored.Should().BeNull("MaxPerUserPerDay is 1");
    }

    [Fact]
    public async Task Sponsored_Slot_Respects_Ratio_Cap_Against_Organic_Deliveries()
    {
        using var factory = new RatioFactory();
        await SeedSponsoredFixtureAsync(factory, deliveries: 10);
        using var client = factory.CreateClient();

        var first = await GetReadinessAsync(client);
        first!.Sponsored.Should().NotBeNull();

        // Daily cap is 3 here, so only the ratio cap can block the second slot.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.RecommendationDeliveries.Add(new RecommendationDelivery
            {
                UserId = TestDataSeeder.TestUserId,
                SuggestionCount = 1,
            });
            await db.SaveChangesAsync();
        }

        var second = await GetReadinessAsync(client);
        second!.Sponsored.Should().BeNull(
            "only 1 organic delivery has been made since the last impression (ratio cap is 10)");

        // Seed the remaining deliveries and the slot becomes eligible again.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            for (var i = 0; i < 9; i++)
            {
                db.RecommendationDeliveries.Add(new RecommendationDelivery
                {
                    UserId = TestDataSeeder.TestUserId,
                    SuggestionCount = 1,
                });
            }
            await db.SaveChangesAsync();
        }

        var third = await GetReadinessAsync(client);
        third!.Sponsored.Should().NotBeNull();
    }

    [Fact]
    public async Task Sponsored_Slot_Is_Not_Served_To_Minors()
    {
        using var factory = new MinAge31Factory();
        await SeedSponsoredFixtureAsync(factory, deliveries: 10);
        using var client = factory.CreateClient();

        var readiness = await GetReadinessAsync(client);

        readiness!.Sponsored.Should().BeNull(
            "the test user is 30 and the configured minimum age is 31");
    }

    [Fact]
    public void Sponsored_Slot_Is_Rejected_In_Medical_Context()
    {
        using var factory = new EnabledFactory();
        SeedSponsoredFixtureAsync(factory, deliveries: 10).GetAwaiter().GetResult();

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SponsoredOfferService>();

        var slot = service.TryBuildSlot(TestDataSeeder.TestUserId, SponsoredSlotContext.Medical, organicSuggestions: 5);

        slot.Should().BeNull();
    }

    [Fact]
    public async Task Sponsored_Slot_Is_Not_Served_When_Offer_Domain_Not_Allowed()
    {
        using var factory = new EnabledFactory();
        await SeedSponsoredFixtureAsync(factory, deliveries: 10);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var offer = db.SponsoredOffers.Single();
            offer.ExternalUrl = "https://evil-not-allowlisted.example.org/creatina";
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var readiness = await GetReadinessAsync(client);

        readiness!.Sponsored.Should().BeNull();
    }

    // ---------- Metrics ----------

    [Fact]
    public async Task Sponsored_Events_Are_Recorded_And_Aggregated_In_Metrics()
    {
        using var factory = new EnabledFactory();
        await SeedSponsoredFixtureAsync(factory, deliveries: 10);
        using var client = factory.CreateClient();

        var readiness = await GetReadinessAsync(client);
        readiness!.Sponsored.Should().NotBeNull(); // 1 impression

        var click = await client.PostAsJsonAsync("/sponsored/events",
            new SponsoredEventRequest { Slug = "creatina-monohidrato", Type = "click" });
        click.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dismiss = await client.PostAsJsonAsync("/sponsored/events",
            new SponsoredEventRequest { Slug = "creatina-monohidrato", Type = "dismiss" });
        dismiss.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var metricsResponse = await client.GetAsync("/sponsored/metrics");
        metricsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var metrics = await metricsResponse.Content.ReadFromJsonAsync<List<SponsoredMetricsResource>>();
        var row = metrics!.Single(m => m.Slug == "creatina-monohidrato");
        row.Impressions.Should().Be(1);
        row.Clicks.Should().Be(1);
        row.Dismisses.Should().Be(1);
        row.Conversions.Should().Be(0);
    }

    [Fact]
    public async Task Sponsored_Event_Rejects_Invalid_Type_And_Unknown_Slug()
    {
        using var factory = new EnabledFactory();
        await SeedSponsoredFixtureAsync(factory, deliveries: 1);
        using var client = factory.CreateClient();

        var invalidType = await client.PostAsJsonAsync("/sponsored/events",
            new SponsoredEventRequest { Slug = "creatina-monohidrato", Type = "impression" });
        invalidType.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unknownSlug = await client.PostAsJsonAsync("/sponsored/events",
            new SponsoredEventRequest { Slug = "no-existe", Type = "click" });
        unknownSlug.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sponsored_Metrics_Are_Forbidden_For_Non_Allowlisted_Users()
    {
        using var factory = new MetricsLockedFactory();
        await SeedSponsoredFixtureAsync(factory, deliveries: 1);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sponsored/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public class MetricsLockedFactory : EnabledFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Remove the metrics allowlist entry.
            builder.UseSetting("SponsoredOffersMetricsEmails:0", "someone-else@healthspan.dev");
        }
    }
}
