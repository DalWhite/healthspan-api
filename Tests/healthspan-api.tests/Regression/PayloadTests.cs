using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// t_101e2b0a payload budget: history list <=15KB for 100 rows via the
/// ?fields=minimal projection, and the Today screen served by a single
/// aggregate endpoint.
/// </summary>
public sealed class PayloadTests : ApiTestBase
{
    // The API serializes with MVC defaults (camelCase); deserializing its raw
    // bodies needs the matching Web option.
    private static readonly JsonSerializerOptions WebOptions =
        new(JsonSerializerDefaults.Web);

    private readonly Xunit.Abstractions.ITestOutputHelper output;

    public PayloadTests(HealthspanApiFactory factory,
        Xunit.Abstractions.ITestOutputHelper output) : base(factory)
    {
        this.output = output;
    }

    [Fact]
    public async Task Activities_minimal_projection_drops_list_unused_fields()
    {
        for (var i = 0; i < 3; i++)
        {
            await Client.PostAsJsonAsync("/api/activities", new WellnessActivityWriteResource
            {
                Type = "sauna",
                Dimension = WellnessDimension.Health,
                OccurredOnUtc = DateTime.UtcNow.Date.AddDays(-i),
                DurationMinutes = 20,
                Notes = new string('n', 200),
                Metadata = new Dictionary<string, object?>
                {
                    ["facility"] = new string('f', 200),
                    ["humidity_percent"] = 15
                }
            });
        }

        var minimalBody = await (await Client.GetAsync(
            "/api/activities?page=1&pageSize=100&fields=minimal")).Content.ReadAsStringAsync();
        var fullBody = await (await Client.GetAsync(
            "/api/activities?page=1&pageSize=100")).Content.ReadAsStringAsync();

        minimalBody.Should().NotContain("\"notes\"");
        minimalBody.Should().NotContain("\"metadata\"");
        minimalBody.Should().NotContain("\"createdAtUtc\"");
        minimalBody.Should().Contain("\"durationMinutes\"");
        minimalBody.Should().Contain("\"occurredOnUtc\"");
        fullBody.Should().Contain("\"notes\"");
        fullBody.Should().Contain("\"metadata\"");

        // The projection is only worth shipping if it is strictly smaller.
        minimalBody.Length.Should().BeLessThan(fullBody.Length);
    }

    [Fact]
    public async Task Activities_history_100_rows_stay_under_15kb_minimal()
    {
        // Realistic history mix (the seed's own type strings and notes length);
        // the projection must hold the budget with data shaped like production,
        // not with synthetic 100-char custom types.
        var types = new[] { "sauna", "caminata", "yoga", "fuerza", "movilidad", "cold_exposure" };
        var dimensions = new[]
        {
            WellnessDimension.Health,
            WellnessDimension.Health,
            WellnessDimension.Mobility,
            WellnessDimension.Strength,
            WellnessDimension.Mobility,
            WellnessDimension.Health
        };
        for (var i = 0; i < 100; i++)
        {
            await Client.PostAsJsonAsync("/api/activities", new WellnessActivityWriteResource
            {
                Type = types[i % types.Length],
                Dimension = dimensions[i % dimensions.Length],
                OccurredOnUtc = DateTime.UtcNow.Date.AddDays(-i),
                DurationMinutes = 20 + i % 40,
                FrequencyPerWeek = i % 3 == 0 ? 3m : null,
                Intensity = i % 4 == 0 ? 7m : null,
                TemperatureCelsius = i % 10 == 0 ? 85m : null,
                Cycles = i % 5 == 0 ? 3 : null,
                Notes = "Sesión de entrenamiento registrada en la app",
                Metadata = new Dictionary<string, object?>
                {
                    ["humidity_percent"] = 15,
                    ["facility"] = "Home sauna"
                }
            });
        }

        var fullBody = await (await Client.GetAsync(
            "/api/activities?page=1&pageSize=100")).Content.ReadAsStringAsync();
        var body = await (await Client.GetAsync(
            "/api/activities?page=1&pageSize=100&fields=minimal")).Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<PagedActivitiesSummaryResource>(body, WebOptions);

        page!.Items.Should().HaveCount(100);
        page.Total.Should().BeGreaterThanOrEqualTo(100);
        fullBody.Length.Should().BeGreaterThan(body.Length);
        body.Length.Should().BeLessThanOrEqualTo(15 * 1024);
    }

    [Fact]
    public async Task Today_page_aggregate_serves_sessions_recents_and_previous_loads()
    {
        await Client.PutAsJsonAsync("/HealthProfile", new { weightKg = 75 });

        // Fixed far-future day (same convention as TrainingTrackerTests): the
        // fixture DB is shared across suites, so "today" must not depend on
        // the wall clock or on what other suites logged.
        var day = new DateOnly(2098, 3, 5);
        var created = await Client.PostAsJsonAsync(
            "/Workouts", new { date = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "T00:00:00Z" });
        created.EnsureSuccessStatusCode();
        var workoutId = JsonSerializer.Deserialize<JsonElement>(
            await created.Content.ReadAsStringAsync()).GetProperty("id").GetString();

        var posted = await Client.PostAsJsonAsync($"/Workouts/{workoutId}/exercises", new
        {
            typeId = TestDataSeeder.BenchPressTypeId,
            series = 3,
            repetitions = 10,
            bodyWeight = 71.5,
            addedWeight = 14.0,
            loadType = "Bodyweight"
        });
        posted.EnsureSuccessStatusCode();

        var response = await Client.GetAsync($"/Workouts/today/page?date={day}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<TodayWorkoutPageResource>(body, WebOptions);

        page!.Sessions.Should().HaveCount(1);
        page.Sessions[0].Id.Should().Be(Guid.Parse(workoutId!));
        page.Sessions[0].Exercises.Should().HaveCount(1);

        // Newest first: the created session is the top recent row, and its row
        // carries only the counters — the "Anterior" data travels once, as the
        // previousLoads projection, not duplicated inside every recent row.
        page.RecentWorkouts.First().Id.Should().Be(Guid.Parse(workoutId!));
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("recentWorkouts")[0]
            .TryGetProperty("exercises", out _).Should().BeFalse();

        // Bench Press is a Standard weight type: its bodyWeight/addedWeight
        // breakdown is normalized away on read (F1 read-noise rule) — the
        // aggregate carries the same projection the client used to derive.
        page.PreviousLoads.Should().ContainKey(TestDataSeeder.BenchPressTypeId);
        page.PreviousLoads[TestDataSeeder.BenchPressTypeId].Series.Should().Be(3);
        page.PreviousLoads[TestDataSeeder.BenchPressTypeId].Repetitions
            .Should().Be(10);
        page.ProfileWeightKg.Should().Be(75);
    }

    [Fact]
    public async Task Today_page_aggregate_respects_trainee_access()
    {
        var response = await Client.GetAsync(
            "/Workouts/today/page?traineeId=some-other-user");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }
}
