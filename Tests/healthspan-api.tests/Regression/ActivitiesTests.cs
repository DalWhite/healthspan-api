using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

public class ActivitiesTests : ApiTestBase
{
    public ActivitiesTests(HealthspanApiFactory factory) : base(factory) { }

    private static WellnessActivityWriteResource NewActivity(
        string type = "sauna",
        WellnessDimension dimension = WellnessDimension.Health,
        DateTime? occurredOn = null)
        => new()
        {
            Type = type,
            Dimension = dimension,
            OccurredOnUtc = occurredOn ?? DateTime.UtcNow.Date,
            DurationMinutes = 20,
            FrequencyPerWeek = 3m,
            Intensity = 7m,
            TemperatureCelsius = 85m,
            Cycles = 3,
            Notes = "Session notes",
            Metadata = new Dictionary<string, object?>
            {
                ["humidity_percent"] = 15,
                ["facility"] = "Home sauna"
            }
        };

    [Fact]
    public async Task CreateActivity_Returns_Created_And_Metadata_RoundTrips()
    {
        var response = await Client.PostAsJsonAsync("/api/activities", NewActivity());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<WellnessActivityResource>();
        created.Should().NotBeNull();
        created!.Id.Should().NotBeEmpty();
        created.Type.Should().Be("sauna");
        created.Dimension.Should().Be(WellnessDimension.Health);
        created.DurationMinutes.Should().Be(20);
        created.TemperatureCelsius.Should().Be(85m);
        created.Cycles.Should().Be(3);
        created.Metadata.Should().NotBeNull();
        created.Metadata!.Should().ContainKey("humidity_percent");
        created.Metadata!["facility"]!.ToString().Should().Be("Home sauna");
    }

    [Fact]
    public async Task CreateActivity_Accepts_Custom_Type()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/activities",
            NewActivity(type: "Yoga Flow", dimension: WellnessDimension.Mobility));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<WellnessActivityResource>();
        created!.Type.Should().Be("yoga flow");
    }

    [Fact]
    public async Task GetActivities_Filters_By_Type_Dimension_And_Date()
    {
        await Client.PostAsJsonAsync("/api/activities", NewActivity("sauna", WellnessDimension.Health));
        await Client.PostAsJsonAsync("/api/activities", NewActivity("cold_exposure", WellnessDimension.Health));
        await Client.PostAsJsonAsync(
            "/api/activities",
            NewActivity("tenis", WellnessDimension.Strength, occurredOn: DateTime.UtcNow.Date.AddDays(-40)));

        var byType = await Client.GetAsync("/api/activities?type=cold_exposure");
        byType.StatusCode.Should().Be(HttpStatusCode.OK);
        var typeList = await byType.Content.ReadFromJsonAsync<List<WellnessActivityResource>>();
        typeList!.Should().OnlyContain(a => a.Type == "cold_exposure");
        typeList.Count.Should().BeGreaterThanOrEqualTo(1);

        var byDimension = await Client.GetAsync("/api/activities?dimension=strength");
        var dimensionList = await byDimension.Content.ReadFromJsonAsync<List<WellnessActivityResource>>();
        dimensionList!.Should().NotBeEmpty();
        dimensionList!.Should().OnlyContain(a => a.Dimension == WellnessDimension.Strength);

        var inRange = await Client.GetAsync(
            $"/api/activities?from={DateTime.UtcNow.Date.AddDays(-10):yyyy-MM-dd}&to={DateTime.UtcNow.Date:yyyy-MM-dd}");
        var rangeList = await inRange.Content.ReadFromJsonAsync<List<WellnessActivityResource>>();
        rangeList!.Should().NotBeEmpty();
        rangeList!.Should().NotContain(a => a.Type == "tenis");
    }

    [Fact]
    public async Task GetActivity_By_Id_Returns_It_And_Unknown_Id_Returns_404()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/activities", NewActivity("wim_hof"));
        var created = await createResponse.Content.ReadFromJsonAsync<WellnessActivityResource>();

        var found = await Client.GetAsync($"/api/activities/{created!.Id}");
        found.StatusCode.Should().Be(HttpStatusCode.OK);

        var missing = await Client.GetAsync($"/api/activities/{Guid.NewGuid()}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateActivity_Changes_Fields_Then_Delete_Removes_It()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/activities", NewActivity("meditation"));
        var created = await createResponse.Content.ReadFromJsonAsync<WellnessActivityResource>();

        var updated = NewActivity("meditation", WellnessDimension.Mobility);
        updated.DurationMinutes = 45;
        updated.Intensity = 4m;
        updated.Metadata = new Dictionary<string, object?> { ["technique"] = "body scan" };

        var putResponse = await Client.PutAsJsonAsync($"/api/activities/{created!.Id}", updated);
        putResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await Client.GetAsync($"/api/activities/{created.Id}");
        var resource = await getResponse.Content.ReadFromJsonAsync<WellnessActivityResource>();
        resource!.DurationMinutes.Should().Be(45);
        resource.Intensity.Should().Be(4m);
        resource.Dimension.Should().Be(WellnessDimension.Mobility);
        resource.Metadata!["technique"]!.ToString().Should().Be("body scan");

        var deleteResponse = await Client.DeleteAsync($"/api/activities/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await Client.GetAsync($"/api/activities/{created.Id}");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("", WellnessDimension.Health, "activity_type_invalid")]
    [InlineData("sauna!!", WellnessDimension.Health, "activity_type_invalid")]
    public async Task CreateActivity_Rejects_Invalid_Type(string type, WellnessDimension dimension, string expectedCode)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/activities",
            NewActivity(type, dimension));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        problem!.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task CreateActivity_Rejects_Future_Date_And_OutOfRange_Intensity()
    {
        var future = NewActivity(occurredOn: DateTime.UtcNow.Date.AddDays(2));
        var futureResponse = await Client.PostAsJsonAsync("/api/activities", future);
        futureResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var futureProblem = await futureResponse.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        futureProblem!.Code.Should().Be("activity_date_invalid");

        var hot = NewActivity();
        hot.Intensity = 11m;
        var hotResponse = await Client.PostAsJsonAsync("/api/activities", hot);
        hotResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var hotProblem = await hotResponse.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        hotProblem!.Code.Should().Be("activity_intensity_invalid");
    }

    [Fact]
    public async Task Activities_Are_Isolated_Between_Users()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/activities", NewActivity("sauna"));
        var created = await createResponse.Content.ReadFromJsonAsync<WellnessActivityResource>();

        // Re-home the activity to another user directly in the database.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var stored = db.WellnessActivities
                .IgnoreQueryFilters()
                .First(a => a.Id == created!.Id);
            stored.UserId = TestDataSeeder.TraineeUserId;
            await db.SaveChangesAsync();
        }

        var getResponse = await Client.GetAsync($"/api/activities/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listResponse = await Client.GetAsync("/api/activities");
        var list = await listResponse.Content.ReadFromJsonAsync<List<WellnessActivityResource>>();
        list!.Should().NotContain(a => a.Id == created.Id);
    }

    private sealed class ProblemDetailsLite
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public string? Code { get; set; }
    }
}
