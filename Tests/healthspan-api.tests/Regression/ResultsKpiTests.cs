using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// Monthly health-result KPIs (kanban t_4882e760, spec t_358e09e9).
/// All KPI fixtures use FIXED past dates (2026-05..2026-08) so the numbers are
/// deterministic regardless of when CI runs. Only the "future month" guard is
/// evaluated relative to the live clock.
/// </summary>
public sealed class ResultsKpiTests : ApiTestBase
{
    public ResultsKpiTests(HealthspanApiFactory factory) : base(factory)
    {
    }

    // ── Full-data trainee: K1/K2/K3 numeric assertions ───────────────────────

    [Fact]
    public async Task Month_ComputesAllThreeKpis_Deterministically()
    {
        var traineeId = await SeedFullDataTraineeAsync();

        // K1 consistency (weeks Monday-start, UTC).
        var may = await GetMonthAsync(traineeId, "2026-05");
        var mayConsistency = Element(may, "consistency");
        mayConsistency.GetProperty("state").GetString().Should().Be("ok");
        mayConsistency.GetProperty("score").GetDouble().Should().Be(0.4);
        mayConsistency.GetProperty("activeSessions").GetInt32().Should().Be(2);
        // 4 May + legacy 18 May (18 May row has no CompletedAt but has exercises).
        mayConsistency.GetProperty("deltaPp").GetDouble().Should().Be(40.0);
        // 4/5 May keys may4+may5 are ONE session (DailySessionKey dedup).

        var june = await GetMonthAsync(traineeId, "2026-06");
        var juneConsistency = Element(june, "consistency");
        juneConsistency.GetProperty("score").GetDouble().Should().Be(0.4);
        // Legitimate plateau: delta 0.0 is a real value, NOT null.
        juneConsistency.GetProperty("deltaPp").GetDouble().Should().Be(0.0);

        var july = await GetMonthAsync(traineeId, "2026-07");
        var julyConsistency = Element(july, "consistency");
        julyConsistency.GetProperty("score").GetDouble().Should().Be(0.8);
        julyConsistency.GetProperty("activeSessions").GetInt32().Should().Be(5);
        julyConsistency.GetProperty("deltaPp").GetDouble().Should().Be(40.0);

        // K2 weight.
        var mayWeight = Element(may, "weight");
        mayWeight.GetProperty("state").GetString().Should().Be("current_only");
        mayWeight.GetProperty("lastKg").GetDouble().Should().Be(80.0);
        mayWeight.GetProperty("deltaKg").ValueKind.Should().Be(JsonValueKind.Null);

        var juneWeight = Element(june, "weight");
        juneWeight.GetProperty("state").GetString().Should().Be("ok");
        juneWeight.GetProperty("lastKg").GetDouble().Should().Be(82.5);
        juneWeight.GetProperty("deltaKg").GetDouble().Should().Be(2.5);

        var julyWeight = Element(july, "weight");
        julyWeight.GetProperty("state").GetString().Should().Be("ok");
        julyWeight.GetProperty("lastKg").GetDouble().Should().Be(81.0);
        julyWeight.GetProperty("deltaKg").GetDouble().Should().Be(-1.5);

        // K3 strength (basics: squat + bench press have data; others excluded).
        var mayStrength = Element(may, "strength");
        mayStrength.GetProperty("state").GetString().Should().Be("insufficient_data");
        mayStrength.GetProperty("indexPct").ValueKind.Should().Be(JsonValueKind.Null);
        // First month of data has no previous observation → per-type insufficient.
        mayStrength.GetProperty("byType").GetArrayLength().Should().Be(2);
        ByType(mayStrength, 1).GetProperty("state").GetString().Should().Be("insufficient_data");
        ByType(mayStrength, 2).GetProperty("state").GetString().Should().Be("insufficient_data");

        var juneStrength = Element(june, "strength");
        juneStrength.GetProperty("state").GetString().Should().Be("ok");
        // bench +2/80 = 2.5 pct, squat +5/100 = 5.0 pct → mean 3.75.
        juneStrength.GetProperty("indexPct").GetDouble().Should().Be(3.75);

        var julyStrength = Element(july, "strength");
        julyStrength.GetProperty("state").GetString().Should().Be("ok");
        // Only squat qualifies (bench press has no July session) → index 5.0.
        julyStrength.GetProperty("indexPct").GetDouble().Should().Be(5.0);
        var julySquat = ByType(julyStrength, 2);
        julySquat.GetProperty("state").GetString().Should().Be("ok");
        // The 20 Jul row has MaxRepetition 0 (not a measurement) and must NOT
        // become the month's last session (E5): delta stays +5, not −110.
        julySquat.GetProperty("delta").GetDouble().Should().Be(5.0);
        julySquat.GetProperty("deltaPct").GetDouble().Should().Be(5.0);
        ByType(julyStrength, 1).GetProperty("state").GetString().Should().Be("insufficient_data");
        // typeId ascending: bench press (1) listed before squat (2).
        julyStrength.GetProperty("byType")[0].GetProperty("typeId").GetInt32().Should().Be(1);
        julyStrength.GetProperty("byType")[1].GetProperty("typeId").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Month_WeightGapsAndPlateau_GapIsFine_PlateauIsZeroNotNull()
    {
        var traineeId = await SeedTraineeAsync("gap");
        await SeedReadingAsync(traineeId, new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc), 80.0m);
        await SeedReadingAsync(traineeId, new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc), 81.5m);
        await SeedReadingAsync(traineeId, new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc), 81.5m);

        // June has no reading; its current value is still the May one (E4-ish),
        // and July's delta is computed against the last previous month WITH a
        // reading — the gap does not dilute or fake anything.
        var june = Element(await GetMonthAsync(traineeId, "2026-06"), "weight");
        june.GetProperty("state").GetString().Should().Be("current_only");
        june.GetProperty("lastKg").GetDouble().Should().Be(80.0);

        var july = Element(await GetMonthAsync(traineeId, "2026-07"), "weight");
        july.GetProperty("state").GetString().Should().Be("ok");
        july.GetProperty("deltaKg").GetDouble().Should().Be(1.5);

        // E8: flat month → delta 0.0, explicitly NOT null.
        var august = Element(await GetMonthAsync(traineeId, "2026-08"), "weight");
        august.GetProperty("state").GetString().Should().Be("ok");
        august.GetProperty("deltaKg").GetDouble().Should().Be(0.0);

        // Before any reading: insufficient_data with explicit nulls.
        var april = Element(await GetMonthAsync(traineeId, "2026-04"), "weight");
        april.GetProperty("state").GetString().Should().Be("insufficient_data");
        april.GetProperty("lastKg").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Month_NoData_ExplicitStatesAndNullKeys_NeverFakeZero()
    {
        var traineeId = await SeedTraineeAsync("empty");

        var root = await GetMonthAsync(traineeId, "2026-06");

        var consistency = Element(root, "consistency");
        consistency.GetProperty("state").GetString().Should().Be("not_started");
        consistency.GetProperty("score").ValueKind.Should().Be(JsonValueKind.Null);
        consistency.TryGetProperty("deltaPp", out _).Should().BeTrue();
        consistency.GetProperty("deltaPp").ValueKind.Should().Be(JsonValueKind.Null);
        consistency.GetProperty("activeSessions").GetInt32().Should().Be(0);

        var weight = Element(root, "weight");
        weight.GetProperty("state").GetString().Should().Be("insufficient_data");
        weight.GetProperty("lastKg").ValueKind.Should().Be(JsonValueKind.Null);
        weight.GetProperty("deltaKg").ValueKind.Should().Be(JsonValueKind.Null);

        var strength = Element(root, "strength");
        strength.GetProperty("state").GetString().Should().Be("insufficient_data");
        strength.GetProperty("indexPct").ValueKind.Should().Be(JsonValueKind.Null);
        strength.GetProperty("byType").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Month_SameRequestTwice_IdenticalPayload()
    {
        var traineeId = await SeedTraineeAsync("determinism");
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), "det-jun1", true);
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc), "det-jun2", true);
        await SeedReadingAsync(traineeId, new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), 70.0m);
        await SeedReadingAsync(traineeId, new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc), 69.5m);

        var first = await GetMonthAsync(traineeId, "2026-06");
        var second = await GetMonthAsync(traineeId, "2026-06");

        // GeneratedAtUtc may differ; everything else must be byte-identical.
        Normalize(first).Should().Be(Normalize(second));
    }

    // ── Access, validation, guards ────────────────────────────────────────────

    [Fact]
    public async Task Month_ForeignTrainee_403_EmptyBody_ActionNeverRuns()
    {
        var foreignTraineeId = await SeedTraineeAsync("foreign", linkToTrainer: false);

        var response = await Client.GetAsync(
            $"Results/{foreignTraineeId}/month?month=2026-06");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Month_InvalidOrMissingMonth_400()
    {
        var response = await Client.GetAsync(
            $"Results/{TestDataSeeder.TraineeUserId}/month");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        response = await Client.GetAsync(
            $"Results/{TestDataSeeder.TraineeUserId}/month?month=2026-13");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Month_FutureMonth_400()
    {
        var future = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM");

        var response = await Client.GetAsync(
            $"Results/{TestDataSeeder.TraineeUserId}/month?month={future}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("month_in_future");
    }

    [Fact]
    public async Task Month_SelfAccess_Owner_200()
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM");

        var response = await Client.GetAsync(
            $"Results/{HealthspanApiFactory.TestUserId}/month?month={month}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Series ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Series_DefaultWindowSixMonths_AlignedAscending()
    {
        var traineeId = await SeedFullDataTraineeAsync();

        var root = await GetJsonAsync($"Results/{traineeId}/series");

        var months = root.GetProperty("months");
        months.GetArrayLength().Should().Be(6);
        var labels = months.EnumerateArray().Select(item => item.GetString()!).ToList();
        labels.Should().BeInAscendingOrder();
        root.GetProperty("consistency").GetArrayLength().Should().Be(6);
        root.GetProperty("weight").GetArrayLength().Should().Be(6);

        var strength = root.GetProperty("strength");
        strength.GetProperty("indexPct").GetArrayLength().Should().Be(6);

        // Every loaded type carries one point per month (explicit states, even
        // for months without observations), ordered by typeId asc.
        var byType = strength.GetProperty("byType");
        byType.GetArrayLength().Should().Be(2);
        byType[0].GetProperty("typeId").GetInt32().Should().Be(1);
        byType[1].GetProperty("typeId").GetInt32().Should().Be(2);
        foreach (var type in byType.EnumerateArray())
        {
            var points = type.GetProperty("points").EnumerateArray().ToList();
            points.Count.Should().Be(6);
            points.Select(p => p.GetProperty("month").GetString())
                .Should().Equal(labels);
        }
    }

    [Fact]
    public async Task Series_ClampsWindowToOneAndTwelveMonths()
    {
        var traineeId = await SeedTraineeAsync("clamp");

        var capped = await GetJsonAsync($"Results/{traineeId}/series?months=99");
        capped.GetProperty("months").GetArrayLength().Should().Be(12);

        var floored = await GetJsonAsync($"Results/{traineeId}/series?months=0");
        floored.GetProperty("months").GetArrayLength().Should().Be(1);
    }

    // ── Weight ingest (HealthProfile → reading atomically) ────────────────────

    [Fact]
    public async Task HealthProfile_Put_WeightKg_WritesReading_VisibleInResults()
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM");

        var response = await Client.PutAsJsonAsync("/healthprofile", new UpdateHealthProfileResource
        {
            DateOfBirth = new DateOnly(1990, 1, 1),
            BiologicalSex = BiologicalSex.Male,
            HeightCm = 175,
            WeightKg = 68
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Single observation so far → current_only, never a fake 0 delta.
        var weight = Element(await GetMonthAsync(
            HealthspanApiFactory.TestUserId, month), "weight");
        weight.GetProperty("state").GetString().Should().Be("current_only");
        weight.GetProperty("lastKg").GetDouble().Should().Be(68.0);

        // Out-of-range weights are an explicit 400 (spec §2 K2 ingest rule).
        response = await Client.PutAsJsonAsync("/healthprofile", new UpdateHealthProfileResource
        {
            DateOfBirth = new DateOnly(1990, 1, 1),
            BiologicalSex = BiologicalSex.Male,
            HeightCm = 175,
            WeightKg = 500
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("weight_kg_invalid");

        // The rejected value never reached the series.
        weight = Element(await GetMonthAsync(
            HealthspanApiFactory.TestUserId, month), "weight");
        weight.GetProperty("lastKg").GetDouble().Should().Be(68.0);
    }

    // ── Legacy KPI bug fix: missing data is null, never 0 ─────────────────────

    [Fact]
    public async Task ExerciseTypeKpis_NotEnoughDataInWindow_RMDiferentialIsNull()
    {
        // Trainee with exactly ONE deadlift session (June 2026, older than the
        // 3-month window whenever CI runs) → previously reported 0, now null.
        var traineeId = await SeedTraineeAsync("rmdiff");
        await SeedWorkoutAsync(
            traineeId,
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            "rmdiff-1",
            true,
            [(TestDataSeeder.DeadliftTypeId, 150.0)]);

        var response = await Client.GetAsync(
            $"Exercises/{TestDataSeeder.DeadliftTypeId}/kpis?userId={traineeId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var property = body.RootElement.EnumerateObject()
            .FirstOrDefault(p => p.Name.EndsWith("iferentialInLast3Months"));
        property.Should().NotBeNull();
        property.Value.ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Seeding helpers ───────────────────────────────────────────────────────

    private async Task<string> SeedTraineeAsync(
        string label,
        bool linkToTrainer = true)
    {
        var traineeId = $"results-trainee-{label}-{Guid.NewGuid():N}";

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var trainer = db.Users.IgnoreQueryFilters()
            .Single(user => user.Id == HealthspanApiFactory.TestUserId);

        db.Users.Add(new User
        {
            Id = traineeId,
            UserName = $"{traineeId}@healthspan.dev",
            Email = $"{traineeId}@healthspan.dev",
            Name = $"Results {label}",
            Trainer = linkToTrainer ? trainer : null
        });
        await db.SaveChangesAsync();

        return traineeId;
    }

    private async Task<string> SeedFullDataTraineeAsync()
    {
        var traineeId = await SeedTraineeAsync("full");

        // K1 sessions (dedup + legacy-completion fixture).
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 5, 4), "full-may", true,
            [(TestDataSeeder.SquatTypeId, 100.0), (TestDataSeeder.BenchPressTypeId, 80.0)]);
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 5, 5), "full-may", true);
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 5, 18), "", false,
            [(TestDataSeeder.BenchPressTypeId, null)]);
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 6, 1), "full-jun1", true,
            [(TestDataSeeder.SquatTypeId, 105.0), (TestDataSeeder.BenchPressTypeId, 82.0)]);
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 6, 15), "full-jun2", true);
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 7, 6), "full-jul1", true);
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 7, 13), "full-jul2", true);
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 7, 15), "full-jul3", true,
            [(TestDataSeeder.SquatTypeId, 110.0)]);
        // E5: a 1RM of 0 is not a measurement — excluded everywhere.
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 7, 20), "full-jul4", true,
            [(TestDataSeeder.SquatTypeId, 0.0)]);
        await SeedWorkoutAsync(traineeId, new DateTime(2026, 7, 27), "full-jul5", true);

        // K2 readings.
        await SeedReadingAsync(traineeId, new DateTime(2026, 5, 10), 80.0m);
        await SeedReadingAsync(traineeId, new DateTime(2026, 6, 12), 82.5m);
        await SeedReadingAsync(traineeId, new DateTime(2026, 7, 8), 81.0m);

        return traineeId;
    }

    private async Task SeedWorkoutAsync(
        string traineeId,
        DateTime dateUtc,
        string dailySessionKey,
        bool completed,
        (int TypeId, double? MaxRepetition)[]? exercises = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

        var workout = new Workout
        {
            Id = Guid.NewGuid(),
            UserId = traineeId,
            Date = DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc),
            Type = "Strength",
            DailySessionKey = dailySessionKey,
            CompletedAt = completed
                ? DateTime.SpecifyKind(dateUtc.AddHours(10), DateTimeKind.Utc)
                : null
        };

        if (exercises is not null)
        {
            foreach (var (typeId, maxRepetition) in exercises)
            {
                workout.Exercises.Add(new Exercise
                {
                    Id = Guid.NewGuid(),
                    TypeId = typeId,
                    MaxRepetition = maxRepetition
                });
            }
        }

        db.Workouts.Add(workout);
        await db.SaveChangesAsync();
    }

    private async Task SeedReadingAsync(
        string traineeId,
        DateTime occurredOnUtc,
        decimal kg)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

        var definition = db.HealthMetricDefinitions
            .IgnoreQueryFilters()
            .Single(item => item.Key == BodyCompositionCatalog.WeightKgKey);

        db.BodyCompositionReadings.Add(new BodyCompositionReading
        {
            Id = Guid.NewGuid(),
            UserId = traineeId,
            HealthMetricDefinitionId = definition.Id,
            Value = kg,
            Source = BodyCompositionReadingSources.Manual,
            OccurredOnUtc = DateTime.SpecifyKind(occurredOnUtc, DateTimeKind.Utc),
            CreatedAtUtc = DateTime.SpecifyKind(occurredOnUtc, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();
    }

    // ── Response helpers ──────────────────────────────────────────────────────

    private async Task<JsonElement> GetMonthAsync(string traineeId, string month)
        => await GetJsonAsync($"Results/{traineeId}/month?month={month}");

    private async Task<JsonElement> GetJsonAsync(string uri)
    {
        var response = await Client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static JsonElement Element(JsonElement root, string name)
        => root.GetProperty(name);

    private static JsonElement ByType(JsonElement strength, int typeId)
        => strength.GetProperty("byType").EnumerateArray()
            .First(item => item.GetProperty("typeId").GetInt32() == typeId);

    private static string Normalize(JsonElement root)
    {
        var copy = new Dictionary<string, JsonElement>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name == "generatedAtUtc")
            {
                continue;
            }
            copy[property.Name] = property.Value;
        }
        return JsonSerializer.Serialize(copy);
    }
}
