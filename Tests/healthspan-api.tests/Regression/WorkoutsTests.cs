using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

public class WorkoutsTests : ApiTestBase
{
    public WorkoutsTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetWorkouts_Returns_SeededWorkouts()
    {
        var response = await Client.GetAsync($"/workouts?traineeId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workouts = await response.Content.ReadFromJsonAsync<List<GetWorkoutResource>>();
        workouts.Should().NotBeNull();
        workouts!.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetWorkout_ById_Returns_Correct_Workout()
    {
        var response = await Client.GetAsync($"/workouts/{TestDataSeeder.WorkoutId1}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workout = await response.Content.ReadFromJsonAsync<GetWorkoutResource>();
        workout.Should().NotBeNull();
        workout!.Id.Should().Be(TestDataSeeder.WorkoutId1);
        workout.Type.Should().Be("Push");
    }

    [Fact]
    public async Task AddWorkout_Returns_Created()
    {
        var newWorkout = new AddWorkoutResource
        {
            Date = DateTime.UtcNow,
            UserId = TestDataSeeder.TestUserId,
            Type = "Legs"
        };

        var response = await Client.PostAsJsonAsync("/workouts", newWorkout);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdWorkout = await response.Content.ReadFromJsonAsync<GetWorkoutResource>();
        createdWorkout.Should().NotBeNull();
        createdWorkout!.Id.Should().NotBe(Guid.Empty);
        createdWorkout.Type.Should().Be("Legs");
    }

    [Fact]
    public async Task UpdateWorkout_Returns_Ok()
    {
        var update = new UpdateWorkoutResource
        {
            Id = TestDataSeeder.WorkoutId1,
            Date = DateTime.UtcNow.AddDays(-5),
            UserId = TestDataSeeder.TestUserId
        };

        var response = await Client.PutAsJsonAsync("/workouts", update);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetWorkout_NonExistent_Returns_NotFound()
    {
        var response = await Client.GetAsync($"/workouts/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTodayWorkout_WhenMissing_Returns_NoContent()
    {
        var response = await Client.GetAsync("/workouts/today?date=2099-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task StartTodayWorkout_Is_Idempotent_For_User_And_Date()
    {
        var request = new StartWorkoutSessionResource
        {
            Date = new DateOnly(2035, 1, 15),
            Type = "Training"
        };

        var firstResponse = await Client.PostAsJsonAsync("/workouts/today", request);
        var secondResponse = await Client.PostAsJsonAsync("/workouts/today", request);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstSession = await firstResponse.Content.ReadFromJsonAsync<WorkoutSessionResource>();
        var secondSession = await secondResponse.Content.ReadFromJsonAsync<WorkoutSessionResource>();

        firstSession.Should().NotBeNull();
        secondSession.Should().NotBeNull();
        secondSession!.Id.Should().Be(firstSession!.Id);
        secondSession.Date.Should().Be(request.Date);
    }

    [Fact]
    public async Task StartTodayWorkout_Rejects_Missing_Date()
    {
        var response = await Client.PostAsJsonAsync("/workouts/today", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        db.Workouts
            .IgnoreQueryFilters()
            .Should()
            .NotContain(workout => workout.Date == DateTime.MinValue);
    }

    [Fact]
    public async Task StartTodayWorkout_Concurrent_Requests_Return_One_Session()
    {
        var request = new StartWorkoutSessionResource
        {
            Date = new DateOnly(2035, 3, 12),
            Type = "Training"
        };

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => Client.PostAsJsonAsync("/workouts/today", request)));

        responses.Should().OnlyContain(response =>
            response.StatusCode == HttpStatusCode.Created
            || response.StatusCode == HttpStatusCode.OK);

        var sessions = await Task.WhenAll(
            responses.Select(response =>
                response.Content.ReadFromJsonAsync<WorkoutSessionResource>()));

        sessions.Should().NotContainNulls();
        sessions.Select(session => session!.Id).Distinct().Should().ContainSingle();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var storedSessions = await db.Workouts
            .IgnoreQueryFilters()
            .Where(workout =>
                workout.UserId == TestDataSeeder.TestUserId
                && workout.Date == request.Date.ToDateTime(TimeOnly.MinValue))
            .ToListAsync();

        storedSessions.Should().ContainSingle();
        storedSessions[0].DailySessionKey.Should().HaveLength(64);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task AddExerciseToWorkout_Returns_Updated_Session()
    {
        var startResponse = await Client.PostAsJsonAsync(
            "/workouts/today",
            new StartWorkoutSessionResource
            {
                Date = new DateOnly(2035, 2, 10),
                Type = "Training"
            });
        var session = await startResponse.Content.ReadFromJsonAsync<WorkoutSessionResource>();

        var response = await Client.PostAsJsonAsync(
            $"/workouts/{session!.Id}/exercises",
            new AddWorkoutSessionExerciseResource
            {
                TypeId = TestDataSeeder.SquatTypeId,
                Series = 3,
                Repetitions = 10,
                Weight = 60
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedSession = await response.Content.ReadFromJsonAsync<WorkoutSessionResource>();
        updatedSession.Should().NotBeNull();
        updatedSession!.Exercises.Should().ContainSingle();
        updatedSession.Exercises[0].Name.Should().Be("Squat");
        updatedSession.Exercises[0].Series.Should().Be(3);
        updatedSession.Exercises[0].Repetitions.Should().Be(10);
        updatedSession.Exercises[0].Weight.Should().Be(60);
    }

    [Fact]
    public async Task AddExerciseToWorkout_Does_Not_Expose_Another_Users_Workout()
    {
        var otherUserId = "other-workout-user";
        var otherWorkoutId = Guid.NewGuid();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Users.Add(new User
            {
                Id = otherUserId,
                UserName = "other-workout-user@healthspan.dev",
                NormalizedUserName = "OTHER-WORKOUT-USER@HEALTHSPAN.DEV",
                Email = "other-workout-user@healthspan.dev",
                NormalizedEmail = "OTHER-WORKOUT-USER@HEALTHSPAN.DEV"
            });
            db.Workouts.Add(new Workout
            {
                Id = otherWorkoutId,
                Date = new DateTime(2035, 4, 10),
                Type = "Training",
                UserId = otherUserId
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsJsonAsync(
            $"/workouts/{otherWorkoutId}/exercises",
            new AddWorkoutSessionExerciseResource
            {
                TypeId = TestDataSeeder.SquatTypeId,
                Series = 3,
                Repetitions = 10,
                Weight = 60
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MainDbContext>();
        verificationDb.Exercises
            .IgnoreQueryFilters()
            .Should()
            .NotContain(exercise => exercise.WorkoutId == otherWorkoutId);
    }
}
