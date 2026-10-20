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
/// Regression tests for the universal training tracker: session blocks, goal /
/// modality / fatigue metadata, the canonical adaptation catalog, coverage and
/// readiness analytics, and the offline sync path for blocks.
/// </summary>
public class TrainingTrackerTests : ApiTestBase
{
    public TrainingTrackerTests(HealthspanApiFactory factory) : base(factory) { }

    private static StringContent SyncJson(object operations)
        => new(
            JsonSerializer.Serialize(
                new { operations },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            System.Text.Encoding.UTF8,
            "application/json");

    private static object Operation(Guid opId, string entity, string action, Guid? clientId, object payload)
        => new { opId, entity, action, clientEntityId = clientId, payload };

    private async Task<GetWorkoutResource> CreateWorkoutAsync(AddWorkoutResource resource)
    {
        var response = await Client.PostAsJsonAsync("/workouts", resource);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<GetWorkoutResource>();
        created.Should().NotBeNull();
        return created!;
    }

    [Fact]
    public async Task Catalog_Has_The_9_Adaptations_In_Canonical_Order()
    {
        var catalog = await Client.GetFromJsonAsync<List<AdaptationResource>>("/adaptations");

        catalog.Should().NotBeNull();
        catalog!.Should().HaveCount(9);
        catalog.Select(adaptation => adaptation.Order)
            .Should().BeInAscendingOrder()
            .And.BeEquivalentTo(Enumerable.Range(1, 9));
        catalog.Select(adaptation => adaptation.Type).Should().OnlyHaveUniqueItems();
        catalog.Should().OnlyContain(adaptation => !string.IsNullOrWhiteSpace(adaptation.Color));
        catalog.First(adaptation => adaptation.Order == 1).Type.Should().Be("Skill");
    }

    [Fact]
    public async Task AddWorkout_With_Tracker_Payload_Persists_Goal_Blocks_And_Fatigue()
    {
        var created = await CreateWorkoutAsync(new AddWorkoutResource
        {
            Date = new DateTime(2098, 3, 4),
            Type = "Empuje",
            Goal = "Strength",
            SecondaryGoals = ["Hypertrophy"],
            Modality = "Fuerza",
            PerceivedFatigue = 3,
            Blocks =
            [
                new WorkoutBlockInputResource
                {
                    ClientEntityId = Guid.NewGuid(),
                    Order = 1,
                    Type = "Superset",
                    Label = "Pecho + tríceps",
                    Exercises =
                    [
                        new SessionExerciseInputResource
                        {
                            TypeId = TestDataSeeder.BenchPressTypeId,
                            Order = 1,
                            Series = 3,
                            Repetitions = 8,
                            Weight = 60
                        },
                        new SessionExerciseInputResource
                        {
                            TypeId = TestDataSeeder.SquatTypeId,
                            Order = 2,
                            Series = 3,
                            Repetitions = 10,
                            Weight = 40
                        }
                    ]
                }
            ]
        });

        created.Goal.Should().Be("Strength");
        created.SecondaryGoals.Should().Contain("Hypertrophy");
        created.Modality.Should().Be("Fuerza");
        created.PerceivedFatigue.Should().Be(3);
        created.Blocks.Should().HaveCount(1);
        created.Blocks[0].Type.Should().Be("Superset");
        created.Blocks[0].Label.Should().Be("Pecho + tríceps");
        created.Blocks[0].Exercises.Should().HaveCount(2);
        created.Exercises.Should().HaveCount(2);
        created.Exercises.Should().OnlyContain(exercise => exercise.BlockId == created.Blocks[0].Id);

        var fetched = await Client.GetFromJsonAsync<GetWorkoutResource>(
            $"/workouts/{created.Id}?traineeId={TestDataSeeder.TestUserId}");
        fetched!.Goal.Should().Be("Strength");
        fetched.Blocks.Should().HaveCount(1);
        fetched.Blocks[0].Exercises.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddWorkout_Without_Tracker_Payload_Keeps_Legacy_Shape()
    {
        var created = await CreateWorkoutAsync(new AddWorkoutResource
        {
            Date = new DateTime(2098, 3, 5),
            Type = "Legs legacy"
        });

        created.Goal.Should().BeNull();
        created.Modality.Should().BeNull();
        created.PerceivedFatigue.Should().BeNull();
        created.SecondaryGoals.Should().BeEmpty();
        created.Blocks.Should().BeEmpty();

        // Legacy workouts keep working through the session endpoints.
        var sessions = await Client.GetFromJsonAsync<List<WorkoutSessionResource>>(
            $"/workouts/today/sessions?traineeId={TestDataSeeder.TestUserId}&date=2098-03-05");
        sessions.Should().NotBeNull();
        sessions!.Single().Blocks.Should().BeEmpty();
        sessions.Single().Exercises.Should().BeEmpty();
    }

    [Fact]
    public async Task Seeded_Legacy_Workouts_Have_No_Blocks()
    {
        var workouts = await Client.GetFromJsonAsync<List<GetWorkoutResource>>(
            $"/workouts?traineeId={TestDataSeeder.TestUserId}");

        workouts!.Should().Contain(workout =>
            workout.Id == TestDataSeeder.WorkoutId1 && workout.Blocks.Count == 0);
    }

    [Fact]
    public async Task Unknown_Goal_Or_BlockType_Is_Rejected_With_400()
    {
        var badGoal = await Client.PostAsJsonAsync("/workouts", new AddWorkoutResource
        {
            Date = new DateTime(2098, 3, 6),
            Goal = "NotAnAdaptation"
        });
        badGoal.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badBlock = await Client.PostAsJsonAsync("/workouts", new AddWorkoutResource
        {
            Date = new DateTime(2098, 3, 6),
            Blocks = [new WorkoutBlockInputResource { Order = 1, Type = "Crossfit" }]
        });
        badBlock.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddBlock_Then_Exercise_Lands_Inside_That_Block()
    {
        var created = await CreateWorkoutAsync(new AddWorkoutResource
        {
            Date = new DateTime(2098, 3, 7),
            Type = "Circuito"
        });

        var blockResponse = await Client.PostAsJsonAsync(
            $"/workouts/{created.Id}/blocks?traineeId={TestDataSeeder.TestUserId}",
            new WorkoutBlockInputResource
            {
                ClientEntityId = Guid.NewGuid(),
                Order = 1,
                Type = "Circuito",
                Label = "Metcon",
                Rounds = 4
            });
        blockResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var withBlock = await blockResponse.Content.ReadFromJsonAsync<WorkoutSessionResource>();
        withBlock!.Blocks.Should().HaveCount(1);
        var blockId = withBlock.Blocks[0].Id;

        var exerciseResponse = await Client.PostAsJsonAsync(
            $"/workouts/{created.Id}/exercises?traineeId={TestDataSeeder.TestUserId}",
            new AddWorkoutSessionExerciseResource
            {
                TypeId = TestDataSeeder.DeadliftTypeId,
                BlockId = blockId,
                Order = 1,
                Series = 3,
                Repetitions = 12,
                Weight = 80
            });
        exerciseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await exerciseResponse.Content.ReadFromJsonAsync<WorkoutSessionResource>();

        session!.Blocks.Should().HaveCount(1);
        session.Blocks[0].Exercises.Should().HaveCount(1);
        session.Blocks[0].Exercises[0].BlockId.Should().Be(blockId);
    }

    [Fact]
    public async Task UpsertBlock_With_Same_ClientEntityId_Is_Idempotent()
    {
        var created = await CreateWorkoutAsync(new AddWorkoutResource
        {
            Date = new DateTime(2098, 3, 8),
            Type = "Idempotent blocks"
        });

        var clientId = Guid.NewGuid();
        var payload = new WorkoutBlockInputResource
        {
            ClientEntityId = clientId,
            Order = 1,
            Type = "EMOM",
            Label = "10 min EMOM",
            Rounds = 10
        };

        var first = await Client.PostAsJsonAsync(
            $"/workouts/{created.Id}/blocks?traineeId={TestDataSeeder.TestUserId}", payload);
        var second = await Client.PostAsJsonAsync(
            $"/workouts/{created.Id}/blocks?traineeId={TestDataSeeder.TestUserId}", payload);

        var firstSession = await first.Content.ReadFromJsonAsync<WorkoutSessionResource>();
        var secondSession = await second.Content.ReadFromJsonAsync<WorkoutSessionResource>();

        firstSession!.Blocks.Should().HaveCount(1);
        secondSession!.Blocks.Should().HaveCount(1);
        secondSession.Blocks[0].Id.Should().Be(firstSession.Blocks[0].Id);
    }

    [Fact]
    public async Task ExerciseTypeMapping_Can_Be_Edited_And_Is_Not_Overwritten_By_Backfill()
    {
        var updated = await Client.PutAsJsonAsync(
            $"/adaptations/exercise-types/{TestDataSeeder.BenchPressTypeId}/mapping",
            new AdaptationMappingInputResource
            {
                Primary = ["Power"],
                Secondary = ["Strength"]
            });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var mapping = await updated.Content.ReadFromJsonAsync<AdaptationMappingResource>();
        mapping!.Primary.Should().BeEquivalentTo(["Power"]);
        mapping.Secondary.Should().BeEquivalentTo(["Strength"]);

        // The backfill only touches types with no mapping yet.
        using (var scope = Factory.Services.CreateScope())
        {
            await AdaptationCatalogSeeder.BackfillExerciseTypeAdaptationsAsync(
                scope.ServiceProvider.GetRequiredService<MainDbContext>());
        }

        var fetched = await Client.GetFromJsonAsync<AdaptationMappingResource>(
            $"/adaptations/exercise-types/{TestDataSeeder.BenchPressTypeId}/mapping");
        fetched!.Primary.Should().BeEquivalentTo(["Power"]);
        fetched.Secondary.Should().BeEquivalentTo(["Strength"]);
    }

    [Fact]
    public async Task Mapping_For_Unknown_Exercise_Type_Returns_404()
    {
        var response = await Client.GetAsync("/adaptations/exercise-types/999999/mapping");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unstructured_Functional_Session_Flows_Through_Create_Read_And_Sync()
    {
        // The design's reference case: a superset of 4 rounds, 8 attempts with
        // 0 completed, perceived fatigue 2/5, logged without sets/reps/weight.
        var clientId = Guid.NewGuid();
        var created = await CreateWorkoutAsync(new AddWorkoutResource
        {
            Date = new DateTime(2098, 3, 9),
            Type = "Funcional",
            Goal = "Skill",
            SecondaryGoals = ["AnaerobicCapacity"],
            Modality = "Funcional",
            PerceivedFatigue = 2,
            Blocks =
            [
                new WorkoutBlockInputResource
                {
                    ClientEntityId = clientId,
                    Order = 1,
                    Type = "Superset",
                    Label = "4 rondas",
                    Rounds = 4,
                    Exercises =
                    [
                        new SessionExerciseInputResource
                        {
                            TypeId = TestDataSeeder.SquatTypeId,
                            Order = 1,
                            Attempts = 8,
                            CompletedAttempts = 0,
                            QualityIntent = "Técnica fresca",
                            LoadType = "Bodyweight"
                        }
                    ]
                }
            ]
        });

        created.Blocks[0].Rounds.Should().Be(4);
        created.Blocks[0].Exercises[0].Attempts.Should().Be(8);
        created.Blocks[0].Exercises[0].CompletedAttempts.Should().Be(0);
        created.Blocks[0].Exercises[0].LoadType.Should().Be("Bodyweight");
        created.PerceivedFatigue.Should().Be(2);

        var fetched = await Client.GetFromJsonAsync<GetWorkoutResource>(
            $"/workouts/{created.Id}?traineeId={TestDataSeeder.TestUserId}");
        fetched!.Blocks[0].Exercises[0].QualityIntent.Should().Be("Técnica fresca");

        // The same tracker fields must flow through the offline sync route
        // (op `workout`) on the idempotent path, keyed by the entity id.
        var sync = await Client.PostAsync("/sync", SyncJson(new[]
        {
            Operation(Guid.NewGuid(), "workout", "update", created.Id, new
            {
                date = new DateTime(2098, 3, 9).ToString("yyyy-MM-dd"),
                type = "Funcional",
                goal = "Speed",
                secondaryGoals = new[] { "AnaerobicCapacity", "Power" },
                modality = "Funcional",
                perceivedFatigue = 5
            })
        }));

        sync.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await sync.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("applied").GetInt32().Should().Be(1);
        result.GetProperty("failed").GetArrayLength().Should().Be(0);

        var synced = await Client.GetFromJsonAsync<GetWorkoutResource>(
            $"/workouts/{created.Id}?traineeId={TestDataSeeder.TestUserId}");
        synced!.Goal.Should().Be("Speed");
        synced.SecondaryGoals.Should().BeEquivalentTo(["AnaerobicCapacity", "Power"]);
        synced.PerceivedFatigue.Should().Be(5);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var blockCount = await db.WorkoutBlocks.CountAsync(block =>
                block.WorkoutId == created.Id);
            blockCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task PatchWorkout_Updates_Tracker_Meta_And_Keeps_What_Is_Not_Sent()
    {
        var created = await CreateWorkoutAsync(new AddWorkoutResource
        {
            Date = new DateTime(2098, 3, 10),
            Type = "Patch meta",
            Goal = "Strength",
            Modality = "Fuerza"
        });

        var response = await Client.SendAsync(new HttpRequestMessage(
            new HttpMethod("PATCH"),
            $"/workouts/{created.Id}?traineeId={TestDataSeeder.TestUserId}")
        {
            Content = JsonContent.Create(new PatchWorkoutSessionResource { PerceivedFatigue = 4 })
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = await response.Content.ReadFromJsonAsync<GetWorkoutResource>();
        patched!.PerceivedFatigue.Should().Be(4);
        patched.Goal.Should().Be("Strength");
        patched.Modality.Should().Be("Fuerza");

        var badPatch = await Client.SendAsync(new HttpRequestMessage(
            new HttpMethod("PATCH"),
            $"/workouts/{created.Id}?traineeId={TestDataSeeder.TestUserId}")
        {
            Content = JsonContent.Create(new PatchWorkoutSessionResource { Goal = "Nope" })
        });
        badPatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Coverage_Uses_Tonnage_For_Muscular_Adaptations_And_Attempts_For_Neural_Ones()
    {
        // Type 1 -> Hypertrophy (tonnage), type 2 -> Skill (attempts).
        await Client.PutAsJsonAsync(
            $"/adaptations/exercise-types/{TestDataSeeder.BenchPressTypeId}/mapping",
            new AdaptationMappingInputResource { Primary = ["Hypertrophy"] });
        await Client.PutAsJsonAsync(
            $"/adaptations/exercise-types/{TestDataSeeder.SquatTypeId}/mapping",
            new AdaptationMappingInputResource { Primary = ["Skill"] });

        var from = DateOnly.FromDateTime(DateTime.Today).AddDays(-2);
        var created = await CreateWorkoutAsync(new AddWorkoutResource
        {
            Date = DateTime.Today.AddDays(-1),
            Type = "Coverage probe",
            Blocks =
            [
                new WorkoutBlockInputResource
                {
                    Order = 1,
                    Type = "Normal",
                    Exercises =
                    [
                        new SessionExerciseInputResource
                        {
                            TypeId = TestDataSeeder.BenchPressTypeId,
                            Order = 1,
                            Series = 3,
                            Repetitions = 10,
                            Weight = 50
                        },
                        new SessionExerciseInputResource
                        {
                            TypeId = TestDataSeeder.SquatTypeId,
                            Order = 2,
                            Attempts = 5,
                            CompletedAttempts = 3,
                            QualityIntent = "Skilled, fresh"
                        }
                    ]
                }
            ]
        });
        created.Blocks[0].Exercises.Should().HaveCount(2);

        var coverage = await Client.GetFromJsonAsync<AdaptationCoverageResource>(
            $"/adaptations/coverage?traineeId={TestDataSeeder.TestUserId}"
            + $"&from={from:yyyy-MM-dd}&to={DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}");

        coverage.Should().NotBeNull();
        coverage!.Sessions.Should().BeGreaterThanOrEqualTo(1);

        var hypertrophy = coverage.Items.Single(item => item.Type == "Hypertrophy");
        hypertrophy.Volume.Should().Be(1500);
        hypertrophy.Sessions.Should().Be(1);

        var skill = coverage.Items.Single(item => item.Type == "Skill");
        skill.Volume.Should().Be(5);
        skill.Sessions.Should().Be(1);
    }

    [Fact]
    public async Task Readiness_Score_Is_In_Range_And_Suggestions_Respect_Interference()
    {
        var readiness = await Client.GetFromJsonAsync<ReadinessResource>(
            $"/readiness?traineeId={TestDataSeeder.TestUserId}");

        readiness.Should().NotBeNull();
        readiness!.Score.Should().BeInRange(0, 10);
        readiness.PerAdaptation.Should().HaveCount(9);
        readiness.Suggestions.Count.Should().BeLessThanOrEqualTo(2);

        foreach (var suggestion in readiness.Suggestions)
        {
            suggestion.Reason.Should().NotBeNullOrWhiteSpace();
        }

        // Two suggestions must be at least 4 canonical positions apart.
        foreach (var left in readiness.Suggestions)
        {
            foreach (var right in readiness.Suggestions)
            {
                Math.Abs(left.Order - right.Order).Should().BeGreaterThanOrEqualTo(4);
            }
        }
    }

    [Fact]
    public async Task Readiness_High_Fatigue_Hides_High_Neural_Cost_Suggestions()
    {
        await CreateWorkoutAsync(new AddWorkoutResource
        {
            Date = DateTime.Today,
            Type = "Sesión muy dura",
            Goal = "Strength",
            PerceivedFatigue = 5,
            Blocks =
            [
                new WorkoutBlockInputResource
                {
                    Order = 1,
                    Type = "Normal",
                    Exercises =
                    [
                        new SessionExerciseInputResource
                        {
                            TypeId = TestDataSeeder.DeadliftTypeId,
                            Order = 1,
                            Series = 5,
                            Repetitions = 5,
                            Weight = 120
                        }
                    ]
                }
            ]
        });

        var readiness = await Client.GetFromJsonAsync<ReadinessResource>(
            $"/readiness?traineeId={TestDataSeeder.TestUserId}&date={DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}");

        readiness!.Score.Should().BeLessThan(10);
        readiness.Suggestions.Should().OnlyContain(suggestion =>
            suggestion.Type != "Speed"
            && suggestion.Type != "Power"
            && suggestion.Type != "Strength");
    }

    [Fact]
    public async Task Sync_Creates_Block_And_Attaches_Exercise_By_ClientId()
    {
        var workoutClientId = Guid.NewGuid();
        var blockClientId = Guid.NewGuid();
        var exerciseClientId = Guid.NewGuid();

        var response = await Client.PostAsync("/sync", SyncJson(new[]
        {
            Operation(Guid.NewGuid(), "workout", "create", workoutClientId,
                new { date = DateTime.Today.ToString("yyyy-MM-dd"), type = "Sync tracker", goal = "Strength" }),
            Operation(Guid.NewGuid(), "workout_block", "create", blockClientId,
                new { workoutClientId, type = "Superset", order = 1, label = "Sync superset", rounds = 3 }),
            Operation(Guid.NewGuid(), "exercise", "create", exerciseClientId,
                new
                {
                    workoutClientId,
                    blockClientId,
                    typeId = TestDataSeeder.BenchPressTypeId,
                    order = 1,
                    series = 4,
                    repetitions = 6,
                    weight = 70,
                    attempts = 4,
                    completedAttempts = 4,
                    loadType = "External"
                })
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("applied").GetInt32().Should().Be(3);
        result.GetProperty("failed").GetArrayLength().Should().Be(0);

        var serverWorkoutId = Guid.Parse(
            result.GetProperty("idMap")[0].GetProperty("serverId").GetString()!);

        var workout = await Client.GetFromJsonAsync<GetWorkoutResource>(
            $"/workouts/{serverWorkoutId}?traineeId={TestDataSeeder.TestUserId}");

        workout!.Goal.Should().Be("Strength");
        workout.Blocks.Should().HaveCount(1);
        workout.Blocks[0].Type.Should().Be("Superset");
        workout.Blocks[0].Rounds.Should().Be(3);
        workout.Blocks[0].Exercises.Should().HaveCount(1);
        workout.Blocks[0].Exercises[0].Attempts.Should().Be(4);
        workout.Blocks[0].Exercises[0].LoadType.Should().Be("External");
    }

    [Fact]
    public async Task Sync_ReplayingBlockOps_Is_Deduped_And_Keeps_One_Block()
    {
        var workoutClientId = Guid.NewGuid();
        var blockClientId = Guid.NewGuid();
        var blockOpId = Guid.NewGuid();

        var operations = new[]
        {
            Operation(Guid.NewGuid(), "workout", "create", workoutClientId,
                new { date = DateTime.Today.ToString("yyyy-MM-dd"), type = "Sync dedupe" }),
            Operation(blockOpId, "workout_block", "create", blockClientId,
                new { workoutClientId, type = "Ronda", order = 1, label = "Ronda 1" })
        };

        var first = await Client.PostAsync("/sync", SyncJson(operations));
        var second = await Client.PostAsync("/sync", SyncJson(operations));

        var firstResult = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondResult = await second.Content.ReadFromJsonAsync<JsonElement>();

        firstResult.GetProperty("applied").GetInt32().Should().Be(2);
        secondResult.GetProperty("applied").GetInt32().Should().Be(0);
        secondResult.GetProperty("deduped").GetInt32().Should().Be(2);

        var serverWorkoutId = Guid.Parse(
            firstResult.GetProperty("idMap")[0].GetProperty("serverId").GetString()!);
        var workout = await Client.GetFromJsonAsync<GetWorkoutResource>(
            $"/workouts/{serverWorkoutId}?traineeId={TestDataSeeder.TestUserId}");
        workout!.Blocks.Should().HaveCount(1);
    }

    [Fact]
    public void Volume_Proxy_Uses_Tonnage_For_Muscular_Work_And_Attempts_For_Neural_Work()
    {
        var strength = new Exercise { Series = 5, Repetitions = 5, Weight = 100 };
        TrainingVolume.ForAdaptation(strength, GalpinAdaptationType.Strength)
            .Should().Be(2500);

        var bodyweightSkill = new Exercise { Attempts = 8, CompletedAttempts = 2 };
        TrainingVolume.ForAdaptation(bodyweightSkill, GalpinAdaptationType.Skill)
            .Should().Be(8);

        var bodyweightHypertrophy = new Exercise { Series = 3, Repetitions = 12, BodyWeight = 70 };
        TrainingVolume.ForAdaptation(bodyweightHypertrophy, GalpinAdaptationType.Hypertrophy)
            .Should().Be(2520);

        // A session logged without numbers still counts as exposure.
        var unstructured = new Exercise();
        TrainingVolume.ForAdaptation(unstructured, GalpinAdaptationType.LongDurationEndurance)
            .Should().Be(0);
    }
}
