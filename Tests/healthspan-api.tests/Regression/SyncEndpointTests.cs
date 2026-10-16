using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using healthspan_api.Persistence;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// Integration tests for POST /sync: the endpoint that replays the FE's
/// offline queue. Idempotency (OpId dedupe + ClientEntityId) is the core
/// guarantee being verified here.
/// </summary>
public class SyncEndpointTests : ApiTestBase
{
    public SyncEndpointTests(HealthspanApiFactory factory) : base(factory) { }

    private static StringContent SyncJson(object operations)
        => new(
            JsonSerializer.Serialize(
                new { operations },
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
            System.Text.Encoding.UTF8,
            "application/json");

    private static object Operation(
        Guid opId,
        string entity,
        string action,
        Guid? clientEntityId,
        object payload)
        => new
        {
            opId,
            entity,
            action,
            clientEntityId,
            payload
        };

    [Fact]
    public async Task Sync_CreatesWorkout_AndMapsClientIdToServerId()
    {
        var opId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        var response = await Client.PostAsync("/sync", SyncJson(new[]
        {
            Operation(opId, "workout", "create", clientId,
                new { date = "2026-10-16", type = "Synced fuerza" })
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("applied").GetInt32().Should().Be(1);
        result.GetProperty("deduped").GetInt32().Should().Be(0);
        result.GetProperty("failed").GetArrayLength().Should().Be(0);

        var mapping = result.GetProperty("idMap")[0];
        mapping.GetProperty("clientId").GetString().Should().Be(clientId.ToString());
        var serverId = Guid.Parse(mapping.GetProperty("serverId").GetString()!);

        var workouts = await Client.GetFromJsonAsync<List<GetWorkoutResource>>(
            $"/workouts?traineeId={HealthspanApiFactory.TestUserId}");
        workouts!.Select(workout => workout.Id).Should().Contain(serverId);
    }

    [Fact]
    public async Task Sync_ReplayingSameOpId_IsDeduped_NotAppliedTwice()
    {
        var opId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var operations = new[]
        {
            Operation(opId, "workout", "create", clientId,
                new { date = "2026-10-16", type = "Dedupe me" })
        };

        var first = await Client.PostAsync("/sync", SyncJson(operations));
        var firstResult = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstResult.GetProperty("applied").GetInt32().Should().Be(1);

        // A dropped response makes the client replay the exact same batch.
        var replay = await Client.PostAsync("/sync", SyncJson(operations));
        var replayResult = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayResult.GetProperty("applied").GetInt32().Should().Be(0);
        replayResult.GetProperty("deduped").GetInt32().Should().Be(1);

        // The replay still answers with the id map so the client can remap.
        replayResult.GetProperty("idMap")[0]
            .GetProperty("clientId").GetString().Should().Be(clientId.ToString());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var workouts = await db.Workouts
            .IgnoreQueryFilters()
            .Where(workout => workout.ClientEntityId == clientId)
            .CountAsync();
        workouts.Should().Be(1);
    }

    [Fact]
    public async Task Sync_BatchWithWorkoutThenExercise_ResolvesParentByClientId()
    {
        var workoutOpId = Guid.NewGuid();
        var workoutClientId = Guid.NewGuid();
        var exerciseClientId = Guid.NewGuid();

        var response = await Client.PostAsync("/sync", SyncJson(new[]
        {
            Operation(workoutOpId, "workout", "create", workoutClientId,
                new { date = "2026-10-16", type = "Empuje offline" }),
            Operation(Guid.NewGuid(), "exercise", "create", exerciseClientId,
                new
                {
                    workoutId = workoutClientId.ToString(),
                    typeId = 7,
                    series = 4,
                    repetitions = 8,
                    weight = 60.0
                })
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("applied").GetInt32().Should().Be(2);
        result.GetProperty("failed").GetArrayLength().Should().Be(0);
        result.GetProperty("idMap").GetArrayLength().Should().Be(2);

        var exerciseServerId = Guid.Parse(
            result.GetProperty("idMap")[1].GetProperty("serverId").GetString()!);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var exercise = await db.Exercises
            .IgnoreQueryFilters()
            .SingleAsync(candidate =>
                candidate.ClientEntityId == exerciseClientId);
        exercise.Id.Should().Be(exerciseServerId);
        exercise.Series.Should().Be(4);
        exercise.Repetitions.Should().Be(8);
        exercise.Weight.Should().Be(60.0);

        var workout = await db.Workouts
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ClientEntityId == workoutClientId);
        exercise.WorkoutId.Should().Be(workout.Id);
    }

    [Fact]
    public async Task Sync_UnknownEntity_IsReportedAsFailed_NotThrown()
    {
        var opId = Guid.NewGuid();

        var response = await Client.PostAsync("/sync", SyncJson(new[]
        {
            Operation(opId, "routines", "create", Guid.NewGuid(), new { name = "x" })
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("applied").GetInt32().Should().Be(0);
        result.GetProperty("failed").GetArrayLength().Should().Be(1);
        result.GetProperty("failed")[0].GetProperty("opId").GetString()
            .Should().Be(opId.ToString());
    }

    [Fact]
    public async Task Sync_DeleteWorkout_RemovesServerEntity()
    {
        // Create through the normal endpoint first (future date so it does
        // not collide with the seeded daily session), then delete via sync.
        var created = await Client.PostAsJsonAsync("/workouts", new
        {
            date = new DateTime(2026, 12, 1, 12, 0, 0),
            type = "Para borrar"
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var serverId = Guid.Parse(createdBody.GetProperty("id").GetString()!);

        var response = await Client.PostAsync("/sync", SyncJson(new[]
        {
            Operation(Guid.NewGuid(), "workout", "delete", serverId, new { id = serverId })
        }));

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("applied").GetInt32().Should().Be(1);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await db.Workouts.IgnoreQueryFilters()
            .AnyAsync(workout => workout.Id == serverId)).Should().BeFalse();
    }

    [Fact]
    public async Task Sync_EmptyBatch_IsAccepted_AsNoOp()
    {
        var response = await Client.PostAsync("/sync", SyncJson(Array.Empty<object>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("applied").GetInt32().Should().Be(0);
        result.GetProperty("deduped").GetInt32().Should().Be(0);
        result.GetProperty("failed").GetArrayLength().Should().Be(0);
    }
}
