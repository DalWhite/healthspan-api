using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

/// <summary>
/// The trainee routine view: exercise explanations inside routine workouts and
/// the next workout the user should do (t_bbd0674b).
/// </summary>
public class RoutineViewTests : ApiTestBase
{
    public RoutineViewTests(HealthspanApiFactory factory) : base(factory) { }

    private static RoutineResource BuildRoutine(
        string name,
        int frequency,
        params (int TypeId, int? Sets, int? Reps, double? Load)[] exercises)
        => new()
        {
            Name = name,
            Description = "Test routine",
            UserId = TestDataSeeder.TestUserId,
            WorkoutTemplates =
            [
                new()
                {
                    Type = "Full body",
                    FrequencyPerWeek = frequency,
                    Exercises = exercises
                        .Select((exercise, index) => new ExercisePrescriptionResource
                        {
                            ExerciseTypeId = exercise.TypeId,
                            Order = index + 1,
                            Sets = exercise.Sets,
                            Reps = exercise.Reps,
                            LoadKg = exercise.Load
                        })
                        .ToList()
                }
            ]
        };

    [Fact]
    public async Task Routine_Exercises_Carry_The_Exercise_Explanation()
    {
        var createResponse = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine(
                $"Explained {Guid.NewGuid():N}",
                2,
                (TestDataSeeder.BenchPressTypeId, 4, 8, 80),
                (TestDataSeeder.SquatTypeId, 3, 10, null)));
        var created = await createResponse.Content.ReadFromJsonAsync<RoutineResource>();

        var getResponse = await Client.GetAsync($"/routines/{created!.Id}");
        var routine = await getResponse.Content.ReadFromJsonAsync<RoutineResource>();

        var exercises = routine!.WorkoutTemplates[0].Exercises;
        exercises[0].ExerciseTypeName.Should().Be("Bench Press");
        exercises[0].Description.Should().Be("Barbell bench press");
        exercises[1].ExerciseTypeName.Should().Be("Squat");
        exercises[1].Description.Should().Be("Barbell back squat");
    }

    [Fact]
    public async Task Next_Workout_Is_Pending_Today_When_Quota_Not_Met()
    {
        await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine(
                $"Pending {Guid.NewGuid():N}",
                3,
                (TestDataSeeder.BenchPressTypeId, 4, 8, 80)));

        var response = await Client.GetAsync("/routines/next");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var next = await response.Content.ReadFromJsonAsync<RoutineNextWorkoutResource>();
        next.Should().NotBeNull();
        next!.Date.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        next.PendingNow.Should().BeTrue();
        next.FrequencyPerWeek.Should().BeGreaterThan(0);
        next.Type.Should().NotBeNullOrWhiteSpace();
        next.Exercises.Should().NotBeEmpty();
        next.Exercises[0].ExerciseTypeName.Should().NotBeNullOrWhiteSpace();
        next.Exercises[0].Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Next_Workout_Rejects_Unrelated_Target_User()
    {
        var unrelatedUserId = $"routine-view-{Guid.NewGuid():N}";

        var response = await Client.GetAsync($"/routines/next?traineeId={unrelatedUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Next_Workout_Moves_To_Next_Week_When_Weekly_Quota_Is_Met()
    {
        var (userId, _) = await SeedIsolatedRoutineAsync(frequency: 1, completedThisWeek: 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var next = await ResolveNextAsync(userId, today);

        next.Should().NotBeNull();
        next!.PendingNow.Should().BeFalse();
        next.Date.Should().Be(RoutinePlanService.WeekStart(today).AddDays(7));
    }

    [Fact]
    public async Task Next_Workout_Stays_Pending_While_Quota_Has_Room()
    {
        var (userId, _) = await SeedIsolatedRoutineAsync(frequency: 3, completedThisWeek: 2);

        var next = await ResolveNextAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow));

        next.Should().NotBeNull();
        next!.PendingNow.Should().BeTrue();
    }

    [Fact]
    public async Task Next_Workout_Skips_Workout_Days_Without_Exercises()
    {
        var (userId, _) = await SeedIsolatedRoutineAsync(
            frequency: 3,
            completedThisWeek: 0,
            withExercises: false);

        var next = await ResolveNextAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow));

        next.Should().BeNull();
    }

    [Fact]
    public async Task Next_Workout_Is_Null_When_The_User_Has_No_Routines()
    {
        var userId = $"routine-view-empty-{Guid.NewGuid():N}";

        var next = await ResolveNextAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow));

        next.Should().BeNull();
    }

    /// <summary>
    /// Seeds a routine for a brand-new user id so the weekly-quota rules are
    /// asserted without interference from routines other tests created.
    /// </summary>
    private async Task<(string UserId, Guid RoutineId)> SeedIsolatedRoutineAsync(
        int frequency,
        int completedThisWeek,
        bool withExercises = true)
    {
        var userId = $"routine-view-{Guid.NewGuid():N}";
        var routine = new Routine
        {
            Name = $"Isolated {Guid.NewGuid():N}",
            Description = "Isolated test routine",
            UserId = userId,
            WorkoutTemplates =
            [
                new WorkoutTemplate
                {
                    Type = "Isolated day",
                    DaysOfWeek = frequency,
                    Exercises = withExercises
                        ?
                        [
                            new WorkoutTemplateExercise
                            {
                                ExerciseTypeId = TestDataSeeder.BenchPressTypeId,
                                Order = 1,
                                Sets = 3,
                                Reps = 8
                            }
                        ]
                        : []
                }
            ]
        };

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        db.Routines.Add(routine);

        for (var index = 0; index < completedThisWeek; index++)
        {
            db.Workouts.Add(new Workout
            {
                Id = Guid.NewGuid(),
                Date = DateTime.UtcNow,
                Type = "Isolated day",
                UserId = userId,
                DailySessionKey = $"isolated-{index}",
                CompletedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return (userId, routine.Id);
    }

    private async Task<RoutineNextWorkoutResource?> ResolveNextAsync(
        string userId,
        DateOnly today)
    {
        using var scope = Factory.Services.CreateScope();
        var planService = scope.ServiceProvider.GetRequiredService<RoutinePlanService>();
        return await planService.GetNextDueAsync(userId, today);
    }
}
