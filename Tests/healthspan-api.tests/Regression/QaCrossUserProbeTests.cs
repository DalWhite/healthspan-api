using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// QA cross-user probe (kanban t_25ae5d6c): trainer authenticated, own trainee vs
/// foreign trainee, on every endpoint carrying a user filter.
/// Acceptance: 200/204/201 for own trainee, 403 with EMPTY body for foreign trainee,
/// no data leaked, no side effects on the 403 path.
/// Verified against main @ 9ed8530 (43/43 green; full suite 365/365).
/// </summary>
public class QaCrossUserProbeTests : ApiTestBase
{
    private const string ForeignTrainerId = "qa-foreign-trainer";
    private const string ForeignTraineeId = "qa-foreign-trainee";
    private const string OrphanUserId = "qa-orphan-user";

    private const string OwnTraineeMarker = "QA-OWN-TRAINEE-DATA";
    private const string ForeignMarker = "QA-FOREIGN-DATA";
    private const string TrainerMarker = "QA-TRAINER-DATA";

    private static readonly Guid OwnTraineeWorkoutId = Guid.Parse("cccccccc-0001-0001-0001-cccccccccccc");
    private static readonly Guid OwnTraineeExerciseId = Guid.Parse("cccccccc-0002-0002-0002-cccccccccccc");
    private static readonly Guid ForeignWorkoutId = Guid.Parse("dddddddd-0001-0001-0001-dddddddddddd");
    private static readonly Guid ForeignExerciseId = Guid.Parse("dddddddd-0002-0002-0002-dddddddddddd");
    private static readonly Guid TrainerWorkoutId = Guid.Parse("eeeeeeee-0001-0001-0001-eeeeeeeeeeee");
    private static readonly Guid TrainerExerciseId = Guid.Parse("eeeeeeee-0002-0002-0002-eeeeeeeeeeee");

    public QaCrossUserProbeTests(HealthspanApiFactory factory) : base(factory) { }

    /// <summary>
    /// Runs after ApiTestBase.InitializeAsync (base seeder), so the trainer/trainee
    /// rows and exercise types already exist. Idempotent.
    /// </summary>
    private async Task SeedScenarioAsync()
    {
        await Task.CompletedTask;
        // Own trainee (Trainer = authenticated TestUserId) trained Squat only.
        SeedUser(ForeignTrainerId, null);
        SeedUser(ForeignTraineeId, ForeignTrainerId);
        SeedUser(OrphanUserId, null);

        SeedWorkout(OwnTraineeWorkoutId, OwnTraineeExerciseId, TestDataSeeder.TraineeUserId,
            TestDataSeeder.SquatTypeId, OwnTraineeMarker);
        SeedWorkout(ForeignWorkoutId, ForeignExerciseId, ForeignTraineeId,
            TestDataSeeder.BenchPressTypeId, ForeignMarker);
        SeedWorkout(TrainerWorkoutId, TrainerExerciseId, TestDataSeeder.TestUserId,
            TestDataSeeder.DeadliftTypeId, TrainerMarker);
    }

    private void SeedUser(string id, string? trainerId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        if (db.Users.Any(u => u.Id == id)) return;
        db.Users.Add(new User
        {
            Id = id,
            UserName = $"{id}@healthspan.dev",
            NormalizedUserName = $"{id}@healthspan.dev".ToUpperInvariant(),
            Email = $"{id}@healthspan.dev",
            NormalizedEmail = $"{id}@healthspan.dev".ToUpperInvariant(),
            Trainer = trainerId is null ? null : db.Users.First(u => u.Id == trainerId)
        });
        db.SaveChanges();
    }

    private void SeedWorkout(Guid workoutId, Guid exerciseId, string userId, int typeId, string marker)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        if (db.Workouts.IgnoreQueryFilters().Any(w => w.Id == workoutId)) return;
        db.Workouts.Add(new Workout
        {
            Id = workoutId,
            Date = new DateTime(2044, 1, 1),
            Type = "QA",
            UserId = userId,
            Exercises =
            [
                new Exercise
                {
                    Id = exerciseId,
                    TypeId = typeId,
                    Series = 3,
                    Repetitions = 8,
                    Weight = 70,
                    Comments = marker,
                    WorkoutId = workoutId
                }
            ]
        });
        db.SaveChanges();
    }

    private string ExerciseOwnerId(Guid exerciseId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        return db.Exercises.IgnoreQueryFilters()
            .Where(e => e.Id == exerciseId)
            .Select(e => e.Workout.UserId)
            .FirstOrDefault() ?? "<missing>";
    }

    private static async Task AssertForbiddenEmptyBodyAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty(
            "a 403 must not carry a body (no data may be exposed)");
    }

    // ==================== GET /exercises (list) ====================

    [Fact]
    public async Task GetExercises_List_OwnTrainee_Returns_Trainee_Data_Only()
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync($"/exercises?userId={TestDataSeeder.TraineeUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(OwnTraineeMarker);
        body.Should().NotContain(ForeignMarker);
        body.Should().NotContain(TrainerMarker);
    }

    [Theory]
    [InlineData(ForeignTraineeId)]
    [InlineData(ForeignTrainerId)]
    [InlineData(OrphanUserId)]
    [InlineData("qa-does-not-exist")]
    public async Task GetExercises_List_ForeignOrUnknownUser_Returns403Empty(string userId)
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync($"/exercises?userId={userId}");
        await AssertForbiddenEmptyBodyAsync(response);
    }

    [Fact]
    public async Task GetExercises_List_NoUserId_ReturnsTrainerOwnData()
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync("/exercises");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(TrainerMarker);
        body.Should().NotContain(OwnTraineeMarker);
        body.Should().NotContain(ForeignMarker);
    }

    // ==================== GET /exercises/{id} ====================

    [Fact]
    public async Task GetExercise_ByOwnTraineeId_ReturnsOk()
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync($"/exercises/{OwnTraineeExerciseId}?userId={TestDataSeeder.TraineeUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(OwnTraineeMarker);
    }

    [Fact]
    public async Task GetExercise_ByForeignTraineeId_Returns403Empty()
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync($"/exercises/{ForeignExerciseId}?userId={ForeignTraineeId}");
        await AssertForbiddenEmptyBodyAsync(response);
    }

    [Fact]
    public async Task GetExercise_ForeignExerciseIdWithOwnTrainee_Returns404_NotForeignData()
    {
        await SeedScenarioAsync();
        // The id belongs to a foreign user but the filter param is the own trainee:
        // the action runs scoped to the own trainee and must 404, never leak.
        var response = await Client.GetAsync($"/exercises/{ForeignExerciseId}?userId={TestDataSeeder.TraineeUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(ForeignMarker);
    }

    [Fact]
    public async Task GetExercise_TraineeExerciseWithoutUserId_ResolvesToTrainer_404()
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync($"/exercises/{OwnTraineeExerciseId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(OwnTraineeMarker);
    }

    [Fact]
    public async Task GetExercise_TrainerExerciseWithoutUserId_ReturnsOk()
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync($"/exercises/{TrainerExerciseId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(TrainerMarker);
    }

    // ==================== POST /exercises ====================

    [Fact]
    public async Task AddExercise_ForOwnTrainee_CreatesUnderTrainee()
    {
        await SeedScenarioAsync();
        var response = await Client.PostAsJsonAsync(
            $"/exercises?userId={TestDataSeeder.TraineeUserId}",
            new AddExerciseResource
            {
                TypeId = TestDataSeeder.BenchPressTypeId,
                WorkoutId = OwnTraineeWorkoutId,
                Series = 3,
                Repetitions = 8,
                Weight = 80,
                Comments = "QA-CREATED-FOR-TRAINEE"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var created = await db.Exercises.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Comments == "QA-CREATED-FOR-TRAINEE");
        created.Should().NotBeNull();
        (await db.Workouts.IgnoreQueryFilters()
            .FirstAsync(w => w.Id == OwnTraineeWorkoutId)).UserId.Should().Be(TestDataSeeder.TraineeUserId);
    }

    [Fact]
    public async Task AddExercise_ForForeignTrainee_Returns403Empty_AndCreatesNothing()
    {
        await SeedScenarioAsync();
        var response = await Client.PostAsJsonAsync(
            $"/exercises?userId={ForeignTraineeId}",
            new AddExerciseResource
            {
                TypeId = TestDataSeeder.BenchPressTypeId,
                WorkoutId = ForeignWorkoutId,
                Series = 3,
                Repetitions = 8,
                Weight = 80,
                Comments = "QA-SHOULD-NOT-EXIST"
            });

        await AssertForbiddenEmptyBodyAsync(response);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await db.Exercises.IgnoreQueryFilters()
            .AnyAsync(e => e.Comments == "QA-SHOULD-NOT-EXIST")).Should().BeFalse();
    }

    [Fact]
    public async Task AddExercise_SpoofedForeignWorkoutWithoutUserId_Returns404()
    {
        await SeedScenarioAsync();
        var response = await Client.PostAsJsonAsync(
            "/exercises",
            new AddExerciseResource
            {
                TypeId = TestDataSeeder.BenchPressTypeId,
                WorkoutId = ForeignWorkoutId,
                Series = 3,
                Repetitions = 8,
                Weight = 80,
                Comments = "QA-SPOOF"
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await db.Exercises.IgnoreQueryFilters()
            .AnyAsync(e => e.Comments == "QA-SPOOF")).Should().BeFalse();
    }

    // ==================== PUT /exercises ====================

    [Fact]
    public async Task UpdateExercise_ForOwnTrainee_ReturnsOk()
    {
        await SeedScenarioAsync();
        var response = await Client.PutAsJsonAsync(
            $"/exercises?userId={TestDataSeeder.TraineeUserId}",
            new UpdateExerciseResource
            {
                Id = OwnTraineeExerciseId,
                Series = 5,
                Repetitions = 5,
                Weight = 90
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(OwnTraineeMarker);
    }

    [Fact]
    public async Task UpdateExercise_ForForeignTrainee_Returns403Empty_AndLeavesRowUntouched()
    {
        await SeedScenarioAsync();
        var response = await Client.PutAsJsonAsync(
            $"/exercises?userId={ForeignTraineeId}",
            new UpdateExerciseResource
            {
                Id = ForeignExerciseId,
                Series = 99,
                Repetitions = 99,
                Weight = 999
            });

        await AssertForbiddenEmptyBodyAsync(response);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var row = await db.Exercises.IgnoreQueryFilters().FirstAsync(e => e.Id == ForeignExerciseId);
        row.Series.Should().Be(3);
        row.Weight.Should().Be(70);
    }

    [Fact]
    public async Task UpdateExercise_ForeignExerciseIdWithoutUserId_Returns404_AndLeavesRowUntouched()
    {
        await SeedScenarioAsync();
        var response = await Client.PutAsJsonAsync(
            "/exercises",
            new UpdateExerciseResource
            {
                Id = ForeignExerciseId,
                Series = 99,
                Repetitions = 99,
                Weight = 999
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var row = await db.Exercises.IgnoreQueryFilters().FirstAsync(e => e.Id == ForeignExerciseId);
        row.Series.Should().Be(3);
    }

    // ==================== DELETE /exercises/{id} ====================

    [Fact]
    public async Task DeleteExercise_ForOwnTrainee_Returns204()
    {
        await SeedScenarioAsync();
        var victim = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Workouts.Add(new Workout
            {
                Id = Guid.NewGuid(),
                Date = new DateTime(2044, 2, 1),
                Type = "QA",
                UserId = TestDataSeeder.TraineeUserId,
                Exercises = [new Exercise { Id = victim, TypeId = TestDataSeeder.SquatTypeId, Series = 1, Repetitions = 1, Weight = 10 }]
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.DeleteAsync($"/exercises/{victim}?userId={TestDataSeeder.TraineeUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ExerciseOwnerId(victim).Should().Be("<missing>");
    }

    [Fact]
    public async Task DeleteExercise_ForForeignTrainee_Returns403Empty_AndRowSurvives()
    {
        await SeedScenarioAsync();
        var response = await Client.DeleteAsync($"/exercises/{ForeignExerciseId}?userId={ForeignTraineeId}");

        await AssertForbiddenEmptyBodyAsync(response);
        ExerciseOwnerId(ForeignExerciseId).Should().Be(ForeignTraineeId);
    }

    [Fact]
    public async Task DeleteExercise_ForeignExerciseIdWithoutUserId_Returns404_AndRowSurvives()
    {
        await SeedScenarioAsync();
        var response = await Client.DeleteAsync($"/exercises/{ForeignExerciseId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ExerciseOwnerId(ForeignExerciseId).Should().Be(ForeignTraineeId);
    }

    // ==================== metrics / kpis / trends ====================

    [Fact]
    public async Task ExerciseMetrics_OwnTrainee_Ok_Foreign403()
    {
        await SeedScenarioAsync();
        var own = await Client.GetAsync($"/exercises/{OwnTraineeExerciseId}/metrics?userId={TestDataSeeder.TraineeUserId}");
        own.StatusCode.Should().Be(HttpStatusCode.OK);

        var foreign = await Client.GetAsync($"/exercises/{ForeignExerciseId}/metrics?userId={ForeignTraineeId}");
        await AssertForbiddenEmptyBodyAsync(foreign);
    }

    [Fact]
    public async Task ExerciseKpis_OwnTrainee_Ok_Foreign403()
    {
        await SeedScenarioAsync();
        var own = await Client.GetAsync($"/exercises/{TestDataSeeder.SquatTypeId}/kpis?userId={TestDataSeeder.TraineeUserId}");
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        (await own.Content.ReadAsStringAsync()).Should().NotContain(ForeignMarker);

        var foreign = await Client.GetAsync($"/exercises/{TestDataSeeder.BenchPressTypeId}/kpis?userId={ForeignTraineeId}");
        await AssertForbiddenEmptyBodyAsync(foreign);
    }

    [Fact]
    public async Task ExerciseTrends_OwnTrainee_Ok_Foreign403()
    {
        await SeedScenarioAsync();
        var own = await Client.GetAsync($"/exercises/{TestDataSeeder.SquatTypeId}/trends?type=RM&userId={TestDataSeeder.TraineeUserId}");
        own.StatusCode.Should().Be(HttpStatusCode.OK);

        var foreign = await Client.GetAsync($"/exercises/{TestDataSeeder.BenchPressTypeId}/trends?type=RM&userId={ForeignTraineeId}");
        await AssertForbiddenEmptyBodyAsync(foreign);
    }

    // ==================== /exerciseTypes ====================

    [Fact]
    public async Task GetExerciseTypes_OwnTrainee_Ok_Foreign403Empty()
    {
        await SeedScenarioAsync();
        var own = await Client.GetAsync($"/exercisetypes?userId={TestDataSeeder.TraineeUserId}");
        own.StatusCode.Should().Be(HttpStatusCode.OK);

        var foreign = await Client.GetAsync($"/exercisetypes?userId={ForeignTraineeId}");
        await AssertForbiddenEmptyBodyAsync(foreign);

        var orphan = await Client.GetAsync($"/exercisetypes?userId={OrphanUserId}");
        await AssertForbiddenEmptyBodyAsync(orphan);
    }

    [Fact]
    public async Task GetTrainedExerciseTypes_RewritesUserId_ToTheTrainee()
    {
        await SeedScenarioAsync();
        // Trainee trained Squat only; trainer trained Bench Press + Deadlift (seed data).
        var trainee = await Client.GetAsync($"/exercisetypes/trained?userId={TestDataSeeder.TraineeUserId}");
        trainee.StatusCode.Should().Be(HttpStatusCode.OK);
        var traineeBody = await trainee.Content.ReadAsStringAsync();
        traineeBody.Should().Contain("Squat");
        traineeBody.Should().NotContain("Bench Press");
        traineeBody.Should().NotContain("Deadlift");

        var trainer = await Client.GetAsync("/exercisetypes/trained");
        trainer.StatusCode.Should().Be(HttpStatusCode.OK);
        var trainerBody = await trainer.Content.ReadAsStringAsync();
        trainerBody.Should().Contain("Bench Press");
        trainerBody.Should().Contain("Deadlift");
        trainerBody.Should().NotContain("Squat");
    }

    [Fact]
    public async Task GetTrainedExerciseTypes_Foreign403Empty()
    {
        await SeedScenarioAsync();
        var foreign = await Client.GetAsync($"/exercisetypes/trained?userId={ForeignTraineeId}");
        await AssertForbiddenEmptyBodyAsync(foreign);
    }

    // ==================== edge cases ====================

    [Fact]
    public async Task RepeatedUserIdQueryParameter_DoesNotBypassFilter()
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync(
            $"/exercises?userId={TestDataSeeder.TraineeUserId}&userId={ForeignTraineeId}");

        // Whatever MVC binds, it must never resolve to the foreign user's data.
        if (response.StatusCode == HttpStatusCode.OK)
        {
            (await response.Content.ReadAsStringAsync()).Should().NotContain(ForeignMarker);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task WhitespaceUserId_ResolvesToAuthenticatedTrainer()
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync("/exercises?userId=%20%20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(TrainerMarker);
        body.Should().NotContain(OwnTraineeMarker);
        body.Should().NotContain(ForeignMarker);
    }

    [Fact]
    public async Task TraineeIdQueryParameter_IsNotHonouredOnExercisesEndpoints()
    {
        await SeedScenarioAsync();
        // /exercises declares no traineeId parameter; sending one must not widen access.
        var response = await Client.GetAsync(
            $"/exercises/{ForeignExerciseId}?userId={TestDataSeeder.TraineeUserId}&traineeId={ForeignTraineeId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(ForeignMarker);
    }

    [Fact]
    public async Task ExerciseTypeCatalogEndpoints_DoNotExposeUserData()
    {
        await SeedScenarioAsync();
        // GET /exerciseTypes/{id} is catalog data (no user filter by design).
        var response = await Client.GetAsync($"/exercisetypes/{TestDataSeeder.BenchPressTypeId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(OwnTraineeMarker).And.NotContain(ForeignMarker);
    }

    // ==================== filter ordering / id-vs-param mismatch ====================

    [Fact]
    public async Task UpdateExercise_ForeignExerciseIdWithOwnTraineeParam_Returns404_AndRowUntouched()
    {
        await SeedScenarioAsync();
        // Filter passes (own trainee) but the row id belongs to a foreign user:
        // the action must 404 scoped to the own trainee, never touch the foreign row.
        var response = await Client.PutAsJsonAsync(
            $"/exercises?userId={TestDataSeeder.TraineeUserId}",
            new UpdateExerciseResource
            {
                Id = ForeignExerciseId,
                Series = 99,
                Repetitions = 99,
                Weight = 999
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var row = await db.Exercises.IgnoreQueryFilters().FirstAsync(e => e.Id == ForeignExerciseId);
        row.Series.Should().Be(3);
        row.Weight.Should().Be(70);
    }

    [Fact]
    public async Task DeleteExercise_OwnTraineeExerciseWithForeignTrainerParam_Returns403_AndRowSurvives()
    {
        await SeedScenarioAsync();
        var response = await Client.DeleteAsync($"/exercises/{OwnTraineeExerciseId}?userId={ForeignTrainerId}");

        await AssertForbiddenEmptyBodyAsync(response);
        ExerciseOwnerId(OwnTraineeExerciseId).Should().Be(TestDataSeeder.TraineeUserId);
    }

    [Fact]
    public async Task AddExercise_InvalidBodyWithForeignUserId_FilterOrderingBehaviour()
    {
        await SeedScenarioAsync();
        // MVC model validation short-circuits before action filters: document what
        // an invalid payload returns for a foreign userId (must never be a success).
        var response = await Client.PostAsJsonAsync(
            $"/exercises?userId={ForeignTraineeId}",
            new AddExerciseResource
            {
                TypeId = TestDataSeeder.BenchPressTypeId,
                WorkoutId = ForeignWorkoutId,
                Series = 99999,
                Repetitions = -5,
                Weight = 999999,
                Comments = "QA-INVALID"
            });

        response.StatusCode.Should().NotBe(HttpStatusCode.Created);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await db.Exercises.IgnoreQueryFilters()
            .AnyAsync(e => e.Comments == "QA-INVALID")).Should().BeFalse();
    }

    [Fact]
    public async Task ForbiddenResponses_CarryNoContentTypePayload()
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync($"/exercises?userId={ForeignTraineeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().BeEmpty();
        response.Headers.WwwAuthenticate.Should().BeEmpty(
            "a 403 must not advertise an authentication scheme");
    }

    // ==================== other user-filtered endpoints (inline guards) ====================

    [Theory]
    [InlineData("/workouts")]
    [InlineData("/routines")]
    [InlineData("/routines/plan")]
    [InlineData("/readiness")]
    [InlineData("/adaptations/coverage")]
    public async Task OtherUserFilteredEndpoints_ForeignTrainee_Returns403Empty(string path)
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync($"{path}?traineeId={ForeignTraineeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{path} must reject a trainee that is not the caller's");
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty($"{path} 403 body");
    }

    [Theory]
    [InlineData("/workouts")]
    [InlineData("/routines")]
    [InlineData("/readiness")]
    [InlineData("/adaptations/coverage")]
    public async Task OtherUserFilteredEndpoints_OwnTrainee_Returns200(string path)
    {
        await SeedScenarioAsync();
        var response = await Client.GetAsync($"{path}?traineeId={TestDataSeeder.TraineeUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} with own trainee");
    }
}
