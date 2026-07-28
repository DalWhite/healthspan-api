using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

public sealed class HealthProfileTests : ApiTestBase
{
    public HealthProfileTests(HealthspanApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Get_Returns_Default_When_Profile_Does_Not_Exist()
    {
        var otherUserId = $"health-profile-user-{Guid.NewGuid():N}";

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var existing = db.HealthProfiles
                .IgnoreQueryFilters()
                .SingleOrDefault(item =>
                    item.UserId == TestDataSeeder.TestUserId);
            if (existing is not null)
            {
                db.HealthProfiles.Remove(existing);
            }

            db.Users.Add(new User
            {
                Id = otherUserId,
                UserName = $"{otherUserId}@healthspan.dev",
                NormalizedUserName =
                    $"{otherUserId}@healthspan.dev".ToUpperInvariant(),
                Email = $"{otherUserId}@healthspan.dev",
                NormalizedEmail =
                    $"{otherUserId}@healthspan.dev".ToUpperInvariant()
            });
            db.HealthProfiles.Add(new HealthProfile
            {
                UserId = otherUserId,
                DateOfBirth = new DateOnly(1970, 1, 1),
                BiologicalSex = BiologicalSex.Male
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync("/healthprofile");
        var resource = await response.Content
            .ReadFromJsonAsync<HealthProfileResource>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        resource.Should().NotBeNull();
        resource!.DateOfBirth.Should().BeNull();
        resource.BiologicalSex.Should().Be(BiologicalSex.Unknown);
    }

    [Fact]
    public async Task Put_Upserts_Current_Users_Profile()
    {
        var request = new UpdateHealthProfileResource
        {
            Name = "Updated User",
            DateOfBirth = new DateOnly(1989, 6, 15),
            BiologicalSex = BiologicalSex.Female,
            HeightCm = 172,
            WeightKg = 68
        };

        var response = await Client.PutAsJsonAsync(
            "/healthprofile",
            request);
        var resource = await response.Content
            .ReadFromJsonAsync<HealthProfileResource>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        resource.Should().BeEquivalentTo(request);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var stored = db.HealthProfiles
            .IgnoreQueryFilters()
            .Single(item =>
                item.UserId == TestDataSeeder.TestUserId);
        stored.DateOfBirth.Should().Be(request.DateOfBirth);
        stored.BiologicalSex.Should().Be(request.BiologicalSex);

        var storedUser = db.Users
            .IgnoreQueryFilters()
            .Single(item => item.Id == TestDataSeeder.TestUserId);
        storedUser.Name.Should().Be(request.Name);
        storedUser.Height.Should().Be(request.HeightCm);
        storedUser.Weight.Should().Be(request.WeightKg);
    }

    [Fact]
    public async Task Put_Rejects_Future_Date_Of_Birth()
    {
        var response = await Client.PutAsJsonAsync(
            "/healthprofile",
            new UpdateHealthProfileResource
            {
                DateOfBirth = DateOnly.FromDateTime(
                    DateTime.UtcNow.AddDays(1)),
                BiologicalSex = BiologicalSex.Unknown
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
