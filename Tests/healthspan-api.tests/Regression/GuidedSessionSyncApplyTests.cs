using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Controllers.ApiContracts.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// guided_session apply layer on POST /sync (kanban t_f2e9ecea): complete_set
/// materializes one Exercise row with SourceTemplateExerciseId lineage;
/// undo_set soft-deletes the slot's last set (Exercise.IsDeleted, hidden by
/// the global query filter). Exact failed.reason strings: slot_not_in_plan,
/// session_completed, slot_all_sets_done, not_your_session. Envelope
/// semantics (opId dedup, idMap, applied counters) must stay intact.
/// </summary>
public class GuidedSessionSyncApplyTests : ApiTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Client tap instant in epoch ms (wire type of tap_ts: long).</summary>
    private const long TapTs = 1_762_590_000_000; // 2026-11-08T15:00:00Z, ignored server-side

    public GuidedSessionSyncApplyTests(HealthspanApiFactory factory) : base(factory) { }

    /// <summary>Same key derivation as Services.WorkoutDailySessionKey (internal).</summary>
    private static string DailyKey(string userId, DateOnly date)
        => Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{userId}\n{date:yyyy-MM-dd}")));

    /// <summary>DailySessionKey is user+day and the class shares one DB.</summary>
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

    private async Task<Guid> CreateSessionFromPlanAsync()
    {
        var routineResponse = await Client.PostAsJsonAsync("/routines", new RoutineResource
        {
            Name = $"GS-Sync {Guid.NewGuid():N}",
            Description = "Guided-session sync apply tests",
            UserId = TestDataSeeder.TestUserId,
            WorkoutTemplates =
            [
                new()
                {
                    Type = "Push",
                    FrequencyPerWeek = 3,
                    Exercises =
                    [
                        new() { ExerciseTypeId = TestDataSeeder.BenchPressTypeId, Order = 1, Sets = 2, Reps = 8, LoadKg = 60 },
                        new() { ExerciseTypeId = TestDataSeeder.SquatTypeId, Order = 2, Sets = 1, Reps = 5, LoadKg = 100 },
                    ],
                },
            ],
        });
        routineResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var response = await Client.PostAsJsonAsync("/sessions/from-plan", new
        {
            routineId = (await routineResponse.Content.ReadFromJsonAsync<RoutineResource>())!.Id,
            workoutTemplateId = (int?)null,
            clientSessionId = Guid.NewGuid(),
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sessionId").GetGuid();
    }

    private async Task<SyncResponse> PostSyncAsync(
        params SyncOperationPayload[] operations)
    {
        var response = await Client.PostAsJsonAsync("/sync", new SyncRequest
        {
            Operations = operations.ToList(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SyncResponse>())!;
    }

    private static SyncOperationPayload Tap(
        string action,
        Guid opId,
        object payload,
        Guid? clientEntityId = null)
        => new()
        {
            OpId = opId,
            Entity = "guided_session",
            Action = action,
            ClientEntityId = clientEntityId,
            Payload = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };

    private static object CompleteSetPayload(
        Guid sessionWorkoutId, int slotId, int reps, double weightKg)
        => new
        {
            sessionWorkoutId,
            slotId,
            reps,
            weightKg,
            bodyweightKg = (double?)null,
            notes = (string?)null,
            tap_ts = TapTs,
        };

    private static object UndoSetPayload(Guid sessionWorkoutId, int slotId)
        => new { sessionWorkoutId, slotId };

    private async Task<JsonElement> GetSessionAsync(Guid sessionId)
        => JsonDocument.Parse(await Client.GetStringAsync($"/sessions/{sessionId}")).RootElement;

    private int? CommittedSetCount(Guid sessionId, int slotId, bool includeDeleted = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var query = db.Exercises.IgnoreQueryFilters()
            .Where(e => e.WorkoutId == sessionId && e.SourceTemplateExerciseId == slotId);
        if (!includeDeleted)
        {
            query = query.Where(e => !e.IsDeleted);
        }
        return query.Count();
    }

    [Fact]
    public async Task CompleteSet_Materializes_Exercise_With_Lineage_And_IdMap()
    {
        await ClearTodaySessionsAsync();
        var sessionId = await CreateSessionFromPlanAsync();
        var slotId = GetSessionSlotId(sessionId, index: 0);
        var clientEntityId = Guid.NewGuid();

        var response = await PostSyncAsync(Tap(
            "complete_set", Guid.NewGuid(),
            CompleteSetPayload(sessionId, slotId, reps: 12, weightKg: 77.5),
            clientEntityId));

        response.Failed.Should().BeEmpty();
        response.Applied.Should().Be(1);
        response.IdMap.Should().ContainSingle(m => m.ClientId == clientEntityId);

        var exerciseId = response.IdMap[0].ServerId;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var exercise = db.Exercises.IgnoreQueryFilters().Single(e => e.Id == exerciseId);
        exercise.WorkoutId.Should().Be(sessionId);
        exercise.SourceTemplateExerciseId.Should().Be(slotId);
        exercise.Repetitions.Should().Be(12);
        exercise.Weight.Should().Be(77.5);

        // tap_ts inside the payload must be ignored when applying: nothing
        // about the tap instant (2026-11-08T10:00:00Z) reaches the row —
        // only the training values do.
        exercise.BodyWeight.Should().Be(null);

        // The session snapshot surfaces the materialized set.
        var session = await GetSessionAsync(sessionId);
        session.GetProperty("totals").GetProperty("setsDone").GetInt32().Should().Be(1);
        session.GetProperty("progress")
            .EnumerateArray().First(p => p.GetProperty("slotId").GetInt32() == slotId)
            .GetProperty("setsDone").GetInt32().Should().Be(1);
    }

    private int GetSessionSlotId(Guid sessionId, int index)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var snapshot = Controllers.ApiContracts.Sessions.GuidedSessionSnapshotJson
            .Deserialize(db.Workouts.IgnoreQueryFilters().Single(w => w.Id == sessionId).GuidedSnapshotJson)!;
        return snapshot.Slots[index].SlotId;
    }

    [Fact]
    public async Task Same_OpId_Arriving_Twice_Is_Deduped_Not_Reapplied()
    {
        await ClearTodaySessionsAsync();
        var sessionId = await CreateSessionFromPlanAsync();
        var slotId = GetSessionSlotId(sessionId, index: 0);
        var opId = Guid.NewGuid();

        var first = await PostSyncAsync(Tap("complete_set", opId, CompleteSetPayload(sessionId, slotId, 8, 60)));
        var replay = await PostSyncAsync(Tap("complete_set", opId, CompleteSetPayload(sessionId, slotId, 8, 60)));

        first.Applied.Should().Be(1);
        first.Failed.Should().BeEmpty();
        replay.Deduped.Should().Be(1);
        replay.Applied.Should().Be(0);

        // Exactly one materialized row despite the replay.
        CommittedSetCount(sessionId, slotId).Should().Be(1);
    }

    [Fact]
    public async Task UndoSet_SoftDeletes_Last_Set_And_Hides_It_From_Reads()
    {
        await ClearTodaySessionsAsync();
        var sessionId = await CreateSessionFromPlanAsync();
        var slotId = GetSessionSlotId(sessionId, index: 0); // Sets = 2

        var taps = await PostSyncAsync(
            Tap("complete_set", Guid.NewGuid(), CompleteSetPayload(sessionId, slotId, 8, 60)),
            Tap("complete_set", Guid.NewGuid(), CompleteSetPayload(sessionId, slotId, 10, 62)));
        taps.Applied.Should().Be(2);

        var undo = await PostSyncAsync(Tap("undo_set", Guid.NewGuid(), UndoSetPayload(sessionId, slotId)));
        undo.Applied.Should().Be(1);
        undo.Failed.Should().BeEmpty();

        // Soft-deleted row persists with the marker; visible reads exclude it.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        db.Exercises.IgnoreQueryFilters()
            .Count(e => e.WorkoutId == sessionId && e.SourceTemplateExerciseId == slotId)
            .Should().Be(2);
        db.Exercises.IgnoreQueryFilters()
            .Count(e => e.WorkoutId == sessionId && e.SourceTemplateExerciseId == slotId && e.IsDeleted)
            .Should().Be(1);
        CommittedSetCount(sessionId, slotId).Should().Be(1);

        // The snapshot no longer counts the undone set.
        var session = await GetSessionAsync(sessionId);
        session.GetProperty("totals").GetProperty("setsDone").GetInt32().Should().Be(1);

        // Undo on an empty slot converges as an applied no-op.
        await PostSyncAsync(Tap("undo_set", Guid.NewGuid(), UndoSetPayload(sessionId, GetSessionSlotId(sessionId, 1))));
        var emptyUndo = await PostSyncAsync(Tap("undo_set", Guid.NewGuid(), UndoSetPayload(sessionId, slotId)));
        emptyUndo.Failed.Should().BeEmpty();
        emptyUndo.Deduped.Should().Be(0); // its own opId, not deduped: it is applied as no-op

        // Undo reopens the slot: a further complete_set applies (not slot_all_sets_done).
        var afterUndo = await PostSyncAsync(Tap("complete_set", Guid.NewGuid(), CompleteSetPayload(sessionId, slotId, 6, 55)));
        afterUndo.Failed.Should().BeEmpty();
        afterUndo.Applied.Should().Be(1);
    }

    [Fact]
    public async Task Unknown_Slot_Returns_Slot_Not_In_Plan()
    {
        await ClearTodaySessionsAsync();
        var sessionId = await CreateSessionFromPlanAsync();

        var response = await PostSyncAsync(Tap(
            "complete_set", Guid.NewGuid(), CompleteSetPayload(sessionId, slotId: 99999, 8, 60)));

        response.Applied.Should().Be(0);
        response.Failed.Should().ContainSingle(f => f.Reason == "slot_not_in_plan");
    }

    [Fact]
    public async Task Exceeding_Slot_Sets_Returns_Slot_All_Sets_Done()
    {
        await ClearTodaySessionsAsync();
        var sessionId = await CreateSessionFromPlanAsync();
        var slotId = GetSessionSlotId(sessionId, index: 1); // Sets = 1

        await PostSyncAsync(Tap("complete_set", Guid.NewGuid(), CompleteSetPayload(sessionId, slotId, 5, 100)));
        var over = await PostSyncAsync(Tap("complete_set", Guid.NewGuid(), CompleteSetPayload(sessionId, slotId, 5, 100)));

        over.Applied.Should().Be(0);
        over.Failed.Should().ContainSingle(f => f.Reason == "slot_all_sets_done");
    }

    [Fact]
    public async Task Completed_Session_Returns_Session_Completed()
    {
        await ClearTodaySessionsAsync();
        var sessionId = await CreateSessionFromPlanAsync();
        var slotId = GetSessionSlotId(sessionId, index: 0);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var workout = db.Workouts.IgnoreQueryFilters().Single(w => w.Id == sessionId);
            workout.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var complete = await PostSyncAsync(Tap("complete_set", Guid.NewGuid(), CompleteSetPayload(sessionId, slotId, 8, 60)));
        complete.Failed.Should().ContainSingle(f => f.Reason == "session_completed");

        var undo = await PostSyncAsync(Tap("undo_set", Guid.NewGuid(), UndoSetPayload(sessionId, slotId)));
        undo.Failed.Should().ContainSingle(f => f.Reason == "session_completed");
    }

    [Fact]
    public async Task CrossUser_Unknown_Or_Missing_Ids_Return_Not_Your_Session()
    {
        await ClearTodaySessionsAsync();

        // Another user's guided session, inserted directly (TestAuthHandler
        // authenticates every request as TestUserId).
        var foreignSessionId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new
        {
            routineId = Guid.NewGuid(),
            templateId = 1,
            coachSigned = false,
            coachName = (string?)null,
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
                Id = foreignSessionId,
                UserId = TestDataSeeder.TraineeUserId,
                Type = "Push",
                Date = Today.ToDateTime(TimeOnly.MinValue),
                DailySessionKey = DailyKey(TestDataSeeder.TraineeUserId, Today),
                GuidedSnapshotJson = snapshot,
            });
            await db.SaveChangesAsync();
        }

        var crossUser = await PostSyncAsync(Tap(
            "complete_set", Guid.NewGuid(), CompleteSetPayload(foreignSessionId, 1, 8, 60)));
        var unknown = await PostSyncAsync(Tap(
            "complete_set", Guid.NewGuid(), CompleteSetPayload(Guid.NewGuid(), 1, 8, 60)));
        var noIds = await PostSyncAsync(Tap(
            "complete_set", Guid.NewGuid(), new { slotId = 1, reps = 8, weightKg = 60.0, tap_ts = TapTs }));

        crossUser.Failed.Should().ContainSingle(f => f.Reason == "not_your_session");
        unknown.Failed.Should().ContainSingle(f => f.Reason == "not_your_session");
        noIds.Failed.Should().ContainSingle(f => f.Reason == "not_your_session");
    }

    [Fact]
    public async Task ClientWorkoutId_Resolves_Own_Offline_Session()
    {
        await ClearTodaySessionsAsync();
        var sessionId = await CreateSessionFromPlanAsync();
        var slotId = GetSessionSlotId(sessionId, index: 0);

        // The FE references the session by its offline client id: the workout
        // row carries that id as ClientEntityId (set here directly; the
        // offline-sync path stamps the same column).
        var clientWorkoutId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var workout = db.Workouts.IgnoreQueryFilters().Single(w => w.Id == sessionId);
            workout.ClientEntityId = clientWorkoutId;
            await db.SaveChangesAsync();
        }

        var tap = await PostSyncAsync(Tap(
            "complete_set", Guid.NewGuid(),
            new { clientWorkoutId, slotId, reps = 9, weightKg = 61.0, tap_ts = TapTs }));
        tap.Failed.Should().BeEmpty();
        tap.Applied.Should().Be(1);
        CommittedSetCount(sessionId, slotId).Should().Be(1);
    }
}
