using System.Net;
using System.Net.Http.Json;
using System.Text;
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
        created.TemperatureCelsius.Should().Be(85d);
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
    public async Task GetActivities_Returns_Paged_Envelope_And_Filters_By_Type_Dimension_And_Date()
    {
        await Client.PostAsJsonAsync("/api/activities", NewActivity("sauna", WellnessDimension.Health));
        await Client.PostAsJsonAsync("/api/activities", NewActivity("cold_exposure", WellnessDimension.Health));
        await Client.PostAsJsonAsync(
            "/api/activities",
            NewActivity("tenis", WellnessDimension.Strength, occurredOn: DateTime.UtcNow.Date.AddDays(-40)));

        var byType = await Client.GetAsync("/api/activities?type=cold_exposure");
        byType.StatusCode.Should().Be(HttpStatusCode.OK);
        var typePage = await byType.Content.ReadFromJsonAsync<PagedActivitiesResource>();
        typePage!.Items.Should().OnlyContain(a => a.Type == "cold_exposure");
        typePage.Total.Should().BeGreaterThanOrEqualTo(1);
        typePage.Page.Should().Be(1);
        typePage.PageSize.Should().Be(10);

        var byDimension = await Client.GetAsync("/api/activities?dimension=strength");
        var dimensionPage = await byDimension.Content.ReadFromJsonAsync<PagedActivitiesResource>();
        dimensionPage!.Items.Should().NotBeEmpty();
        dimensionPage.Items.Should().OnlyContain(a => a.Dimension == WellnessDimension.Strength);

        var inRange = await Client.GetAsync(
            $"/api/activities?from={DateTime.UtcNow.Date.AddDays(-10):yyyy-MM-dd}&to={DateTime.UtcNow.Date:yyyy-MM-dd}");
        var rangePage = await inRange.Content.ReadFromJsonAsync<PagedActivitiesResource>();
        rangePage!.Items.Should().NotBeEmpty();
        rangePage.Items.Should().NotContain(a => a.Type == "tenis");
    }

    [Fact]
    public async Task GetActivities_Paginates_With_Items_Total_HasMore()
    {
        for (var i = 0; i < 3; i++)
        {
            var activity = NewActivity($"pagetest{i}");
            activity.OccurredOnUtc = DateTime.UtcNow.Date.AddDays(-i);
            await Client.PostAsJsonAsync("/api/activities", activity);
        }

        var firstPage = await (await Client.GetAsync("/api/activities?page=1&pageSize=2"))
            .Content.ReadFromJsonAsync<PagedActivitiesResource>();
        firstPage!.Items.Count.Should().Be(2);
        firstPage.PageSize.Should().Be(2);
        firstPage.HasMore.Should().BeTrue();

        var secondPage = await (await Client.GetAsync("/api/activities?page=2&pageSize=2"))
            .Content.ReadFromJsonAsync<PagedActivitiesResource>();
        secondPage!.Items.Count.Should().Be(1);
        secondPage.HasMore.Should().BeFalse();
        secondPage.Total.Should().Be(firstPage.Total);
    }

    [Theory]
    [InlineData("page=0", "activities_page_invalid")]
    [InlineData("pageSize=0", "activities_page_size_invalid")]
    [InlineData("pageSize=101", "activities_page_size_invalid")]
    public async Task GetActivities_Rejects_Invalid_Paging(string query, string expectedCode)
    {
        var response = await Client.GetAsync($"/api/activities?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        problem!.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task CreateActivity_Accepts_Dimension_As_String_And_Returns_It_As_String()
    {
        var response = await Client.PostAsync(
            "/api/activities",
            new StringContent(
                """
                {"type":"sauna","dimension":"health","occurredOnUtc":"2026-10-20T10:00:00Z","durationMinutes":20}
                """,
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"dimension\":\"health\"");
    }

    [Fact]
    public async Task CreateActivity_Rejects_Unknown_TopLevel_Fields_With_Field_Errors()
    {
        // The register prototype's as-is payload: date/temperatureC/extra are not
        // part of the contract and must 400 instead of being silently dropped.
        var response = await Client.PostAsync(
            "/api/activities",
            new StringContent(
                """
                {"type":"other","dimension":"health","date":"2026-10-20","name":"Padel",
                 "style":"Guiada","breathsPerRound":30,"extra":{"pistas":2},"temperatureC":85}
                """,
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("errors");
    }

    [Fact]
    public async Task CreateActivity_Maps_Dynamic_Fields_Through_Metadata()
    {
        var response = await Client.PostAsync(
            "/api/activities",
            new StringContent(
                """
                {"type":"other","dimension":"health","occurredOnUtc":"2026-10-20T10:00:00Z",
                 "durationMinutes":60,
                 "metadata":{"name":"Padel","style":"Guiada","breathsPerRound":30,"pistas":2}}
                """,
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<WellnessActivityResource>();
        created!.Metadata!.Should().ContainKey("name");
        created.Metadata["name"]!.ToString().Should().Be("Padel");
        created.Metadata.Should().ContainKey("breathsPerRound");
    }

    [Fact]
    public async Task CreateActivity_Rejects_Missing_Dimension()
    {
        var response = await Client.PostAsync(
            "/api/activities",
            new StringContent(
                """
                {"type":"sauna","occurredOnUtc":"2026-10-20T10:00:00Z"}
                """,
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        problem!.Code.Should().Be("activity_dimension_required");
    }

    [Fact]
    public async Task CreateActivity_Normalizes_DateOnly_To_Utc_With_Z()
    {
        var response = await Client.PostAsync(
            "/api/activities",
            new StringContent(
                """
                {"type":"sauna","dimension":"health","occurredOnUtc":"2026-10-20"}
                """,
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"occurredOnUtc\":\"2026-10-20T00:00:00Z\"");
    }

    [Fact]
    public async Task CreateActivity_Rejects_OutOfRange_Frequency_Duration_Cycles()
    {
        var zeroFreq = NewActivity(); zeroFreq.FrequencyPerWeek = 0m;
        var zeroFreqProblem = await PostAndReadProblem(zeroFreq);
        zeroFreqProblem.Code.Should().Be("activity_frequency_invalid");

        var halfFreq = NewActivity(); halfFreq.FrequencyPerWeek = 0.5m;
        (await PostAndReadProblem(halfFreq)).Code.Should().Be("activity_frequency_invalid");

        var freq99 = NewActivity(); freq99.FrequencyPerWeek = 99m;
        (await PostAndReadProblem(freq99)).Code.Should().Be("activity_frequency_invalid");

        var zeroDur = NewActivity(); zeroDur.DurationMinutes = 0;
        (await PostAndReadProblem(zeroDur)).Code.Should().Be("activity_duration_invalid");

        var cycles999 = NewActivity(); cycles999.Cycles = 999;
        (await PostAndReadProblem(cycles999)).Code.Should().Be("activity_cycles_invalid");
    }

    private async Task<ProblemDetailsLite> PostAndReadProblem(WellnessActivityWriteResource resource)
    {
        var response = await Client.PostAsJsonAsync("/api/activities", resource);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsLite>();
        problem.Should().NotBeNull();
        return problem!;
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
        resource.Intensity.Should().Be(4d);
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
        var page = await listResponse.Content.ReadFromJsonAsync<PagedActivitiesResource>();
        page!.Items.Should().NotContain(a => a.Id == created.Id);
    }

    private sealed class ProblemDetailsLite
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public string? Code { get; set; }
    }
}
