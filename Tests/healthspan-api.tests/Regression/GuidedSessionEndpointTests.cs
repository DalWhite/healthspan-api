using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// Guided-session endpoints (kanban t_94a40a37): POST /sessions/from-plan and
/// GET /sessions/{sessionId}. Acceptance: 201 created / 200 resumed idempotent
/// by DailySessionKey, 400 plan_empty, 404 routine_not_found, 409
/// session_completed (never reopens, never a second row), exact snapshot +
/// ETag "v{version}" with If-None-Match → 304, and owner-*** admin
/// authorization with 403 wrong_role for everyone else (coach included).
/// </summary>
public class GuidedSessionEndpointTests : ApiTestBase
{
    private const string PlanEmptyUserId = "gs-plan-quota-user";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public GuidedSessionEndpointTests(HealthspanApiFactory factory) : base(factory) { }

    /// <summary>Same key derivation as Services.WorkoutDailySessionKey (internal).</summary>
    private static string DailyKey(string userId, DateOnly date)
        => Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{userId}\n{date:yyyy-MM-dd}")));

    /// <summary>
    /// The class shares one factory/DB across methods and the DailySessionKey is
    /// user+day, so each test clears today's rows for both actors first.
    /// </summary>
    private async Task ClearTodaySessionsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var keys = new[]
        {
            DailyKey(TestDataSeeder.TestUserId, Today),
            DailyKey(TestDataSeeder.TraineeUserId, Today),
        };
        var stale = await db.Workouts.IgnoreQueryFilters()
            .Where(w => keys.Contains(w.DailySessionKey!))
            .ToListAsync();
        db.Workouts.RemoveRange(stale);
        await db.SaveChangesAsync();
    }

    private static RoutineResource BuildRoutine(string name, int frequency)
        => new()
        {
            Name = name,
            Description = "Guided session test routine",
            UserId = TestDataSeeder.TestUserId,
            WorkoutTemplates =
            [
                new()
                {
                    Type = "Push",
                    FrequencyPerWeek = frequency,
                    Exercises =
                    [
                        new() { ExerciseTypeId = TestDataSeeder.BenchPressTypeId, Order = 1, Sets = 4, Reps = 8, LoadKg = 80.5 },
                        new() { ExerciseTypeId = TestDataSeeder.SquatTypeId, Order = 2, Sets = 3, Reps = 10, LoadKg = 100 },
                    ],
                },
            ],
        };

    private async Task<(Guid RoutineId, int TemplateId)> CreateRoutineAsync(string name, int frequency)
    {
        var created = await Client.PostAsJsonAsync("/routines", BuildRoutine(name, frequency));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var routine = (await created.Content.ReadFromJsonAsync<RoutineResource>())!;
        var fetched = await (await Client.GetAsync($"/routines/{routine.Id}"))
            .Content.ReadFromJsonAsync<RoutineResource>();
        return (routine.Id, fetched!.WorkoutTemplates[0].Id);
    }

    private async Task<JsonElement> PostFromPlanAsync(
        Guid routineId,
        int? templateId = null,
        Guid? clientSessionId = null)
    {
        var response = await Client.PostAsJsonAsync("/sessions/from-plan", new
        {
            routineId,
            workoutTemplateId = templateId,
            clientSessionId = clientSessionId ?? Guid.NewGuid(),
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task FromPlan_Creates_201_Then_Resumes_200_Same_Session()
    {
        await ClearTodaySessionsAsync();
        var (routineId, templateId) = await CreateRoutineAsync($"GP {Guid.NewGuid():N}", 2);

        var firstResponse = await Client.PostAsJsonAsync("/sessions/from-plan", new
        {
            routineId,
            workoutTemplateId = (int?)null,
            clientSessionId = Guid.NewGuid(),
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var first = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync()).RootElement;

        first.GetProperty("state").GetString().Should().Be("active");
        first.GetProperty("version").GetInt32().Should().Be(1);
        first.GetProperty("routineId").GetGuid().Should().Be(routineId);
        first.GetProperty("templateId").GetInt32().Should().Be(templateId);
        first.GetProperty("slots").GetArrayLength().Should().Be(2);
        first.GetProperty("slots")[0].GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        first.GetProperty("slots")[0].GetProperty("sets").GetInt32().Should().Be(4);
        first.GetProperty("totals").GetProperty("setsPlanned").GetInt32().Should().Be(7);
        first.GetProperty("totals").GetProperty("setsDone").GetInt32().Should().Be(0);
        firstResponse.Headers.ETag!.ToString().Should().Be("\"v1\"");

        // Idempotent by DailySessionKey (user+day): a second call resumes the
        // same row even with a different clientSessionId.
        var secondResponse = await Client.PostAsJsonAsync("/sessions/from-plan", new
        {
            routineId,
            workoutTemplateId = (int?)null,
            clientSessionId = Guid.NewGuid(),
        });
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync()).RootElement;
        second.GetProperty("sessionId").GetGuid().Should().Be(first.GetProperty("sessionId").GetGuid());

        // Exactly one guided session row exists for today.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        db.Workouts.IgnoreQueryFilters().Count(w => w.UserId == TestDataSeeder.TestUserId
            && w.DailySessionKey == DailyKey(TestDataSeeder.TestUserId, Today))
            .Should().Be(1);
    }

    [Fact]
    public async Task FromPlan_Unknown_Routine_Returns_404_routine_not_found()
    {
        await ClearTodaySessionsAsync();
        var response = await Client.PostAsJsonAsync("/sessions/from-plan", new
        {
            routineId = Guid.NewGuid(),
            workoutTemplateId = (int?)null,
            clientSessionId = Guid.NewGuid(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(response)).Should().Be("routine_not_found");
    }

    [Fact]
    public async Task FromPlan_Quota_Met_Earlier_This_Week_Returns_400_plan_empty()
    {
        await ClearTodaySessionsAsync();
        var (routineId, _) = await CreateRoutineAsync($"GQ {Guid.NewGuid():N}", 1);

        // A completed "Push" workout earlier this week satisfies the weekly
        // quota, so nothing is planned for today.
        var weekStart = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Workouts.Add(new Workout
            {
                Id = Guid.NewGuid(),
                UserId = TestDataSeeder.TestUserId,
                Type = "Push",
                Date = weekStart.ToDateTime(TimeOnly.MinValue),
                CompletedAt = weekStart.ToDateTime(TimeOnly.MinValue.AddHours(1)),
                DailySessionKey = DailyKey(TestDataSeeder.TestUserId, weekStart),
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsJsonAsync("/sessions/from-plan", new
        {
            routineId,
            workoutTemplateId = (int?)null,
            clientSessionId = Guid.NewGuid(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("plan_empty");
    }

    [Fact]
    public async Task FromPlan_Completed_Today_Returns_409_And_Never_Reopens()
    {
        await ClearTodaySessionsAsync();
        var (routineId, _) = await CreateRoutineAsync($"GC {Guid.NewGuid():N}", 3);
        var created = await PostFromPlanAsync(routineId);
        var sessionId = created.GetProperty("sessionId").GetGuid();

        // Complete the session (apply layer owns the transition; simulate by
        // stamping CompletedAt directly).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var workout = db.Workouts.IgnoreQueryFilters().Single(w => w.Id == sessionId);
            workout.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsJsonAsync("/sessions/from-plan", new
        {
            routineId,
            workoutTemplateId = (int?)null,
            clientSessionId = Guid.NewGuid(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(response)).Should().Be("session_completed");

        using var check = Factory.Services.CreateScope();
        var verifyDb = check.ServiceProvider.GetRequiredService<MainDbContext>();
        verifyDb.Workouts.IgnoreQueryFilters().Count(w => w.UserId == TestDataSeeder.TestUserId
            && w.DailySessionKey == DailyKey(TestDataSeeder.TestUserId, Today))
            .Should().Be(1);
        verifyDb.Workouts.IgnoreQueryFilters().Single(w => w.Id == sessionId).CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSession_Returns_Snapshot_With_ETag_And_304_On_Match()
    {
        await ClearTodaySessionsAsync();
        var (routineId, _) = await CreateRoutineAsync($"GE {Guid.NewGuid():N}", 2);
        var created = await PostFromPlanAsync(routineId);
        var sessionId = created.GetProperty("sessionId").GetGuid();

        var response = await Client.GetAsync($"/sessions/{sessionId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.ToString().Should().Be("\"v1\"");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("sessionId").GetGuid().Should().Be(sessionId);
        body.GetProperty("slots").GetArrayLength().Should().Be(2);

        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/sessions/{sessionId}");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", "\"v1\"");
        var notModified = await Client.SendAsync(conditional);
        notModified.StatusCode.Should().Be(HttpStatusCode.NotModified);

        var stale = new HttpRequestMessage(HttpMethod.Get, $"/sessions/{sessionId}");
        stale.Headers.TryAddWithoutValidation("If-None-Match", "\"v99\"");
        (await Client.SendAsync(stale)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSession_Owner_OK_But_Coach_Gets_403_wrong_role()
    {
        await ClearTodaySessionsAsync();
        // Coach (TestUserId) tries to read a guided session owned by their trainee.
        var traineeSessionId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new
        {
            routineId = Guid.NewGuid(),
            templateId = 1,
            coachSigned = true,
            coachName = "Coach",
            startedAt = DateTime.UtcNow,
            slots = new[]
            {
                new { slotId = 1, order = 1, exerciseTypeId = TestDataSeeder.BenchPressTypeId,
                      name = "Bench press", sets = 3, reps = 8, loadKg = 60.0, restSeconds = 90 },
            },
        });
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Workouts.Add(new Workout
            {
                Id = traineeSessionId,
                UserId = TestDataSeeder.TraineeUserId,
                Type = "Push",
                Date = Today.ToDateTime(TimeOnly.MinValue),
                DailySessionKey = DailyKey(TestDataSeeder.TraineeUserId, Today),
                GuidedSnapshotJson = snapshot,
            });
            await db.SaveChangesAsync();
        }

        var forbidden = await Client.GetAsync($"/sessions/{traineeSessionId}");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(forbidden)).Should().Be("wrong_role");

        // The same authenticated user reading their own guided session is OK.
        var (routineId, _) = await CreateRoutineAsync($"GO {Guid.NewGuid():N}", 2);
        var mine = await PostFromPlanAsync(routineId);
        var mineId = mine.GetProperty("sessionId").GetGuid();
        (await Client.GetAsync($"/sessions/{mineId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
