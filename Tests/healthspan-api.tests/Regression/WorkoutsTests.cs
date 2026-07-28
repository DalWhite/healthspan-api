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
        response.Headers.GetValues("X-Total-Count").Single()
            .Should().Be(workouts.Count.ToString());
        workouts.Should().BeInDescendingOrder(workout => workout.Date);
        var seededWorkouts = workouts
            .Where(workout =>
                workout.Id == TestDataSeeder.WorkoutId1
                || workout.Id == TestDataSeeder.WorkoutId2)
            .ToList();
        seededWorkouts.Should().HaveCount(2);
        seededWorkouts.Should().OnlyContain(workout => workout.ExerciseCount == 1);
        seededWorkouts.Should().OnlyContain(workout => workout.TotalVolume > 0);
        var leadingMuscles = seededWorkouts
            .SelectMany(workout => workout.LeadingMuscles)
            .ToList();
        leadingMuscles.Should().Contain("Chest");
        leadingMuscles.Should().Contain("Upper back");
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
        workout.ExerciseCount.Should().Be(1);
        workout.TotalVolume.Should().Be(2560);
        workout.LeadingMuscles.Should().ContainSingle("Chest");
        workout.Exercises.Should().ContainSingle();
        workout.Exercises[0].Name.Should().Be("Bench Press");
        workout.Exercises[0].Muscles.Should().ContainSingle("Chest");
    }

    [Fact]
    public async Task GetWorkouts_Paginates_Workout_Sessions()
    {
        var firstResponse = await Client.GetAsync("/workouts?page=1&pageSize=1");
        var secondResponse = await Client.GetAsync("/workouts?page=2&pageSize=1");

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstPage = await firstResponse.Content.ReadFromJsonAsync<List<GetWorkoutResource>>();
        var secondPage = await secondResponse.Content.ReadFromJsonAsync<List<GetWorkoutResource>>();

        firstPage.Should().ContainSingle();
        secondPage.Should().ContainSingle();
        firstPage![0].Id.Should().NotBe(secondPage![0].Id);
        int.Parse(firstResponse.Headers.GetValues("X-Total-Count").Single())
            .Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetWorkouts_Filters_By_Date_Range()
    {
        var workoutId = Guid.NewGuid();
        var workoutDate = new DateTime(2042, 6, 15);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Workouts.Add(new Workout
            {
                Id = workoutId,
                Date = workoutDate,
                Type = "Future test",
                UserId = TestDataSeeder.TestUserId
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync(
            "/workouts?from=2042-06-15&to=2042-06-15&page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workouts = await response.Content.ReadFromJsonAsync<List<GetWorkoutResource>>();
        workouts.Should().ContainSingle(workout => workout.Id == workoutId);
        response.Headers.GetValues("X-Total-Count").Single().Should().Be("1");
    }

    [Fact]
    public async Task GetWorkouts_Rejects_Invalid_Pagination()
    {
        var response = await Client.GetAsync("/workouts?page=0&pageSize=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetWorkout_Does_Not_Expose_Another_Users_Workout()
    {
        var otherUserId = $"history-user-{Guid.NewGuid():N}";
        var workoutId = Guid.NewGuid();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Users.Add(new User
            {
                Id = otherUserId,
                UserName = $"{otherUserId}@healthspan.dev",
                NormalizedUserName = $"{otherUserId}@healthspan.dev".ToUpperInvariant(),
                Email = $"{otherUserId}@healthspan.dev",
                NormalizedEmail = $"{otherUserId}@healthspan.dev".ToUpperInvariant()
            });
            db.Workouts.Add(new Workout
            {
                Id = workoutId,
                Date = new DateTime(2042, 7, 10),
                Type = "Private workout",
                UserId = otherUserId
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/workouts/{workoutId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
    public async Task UpdateWorkout_Returns_Updated_UserScoped_Workout()
    {
        var workoutId = Guid.NewGuid();
        var originalDate = new DateTime(2043, 1, 10);
        var updatedDate = new DateTime(2043, 1, 12);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Workouts.Add(new Workout
            {
                Id = workoutId,
                Date = originalDate,
                Type = "Original",
                UserId = TestDataSeeder.TestUserId,
                Exercises =
                [
                    new Exercise
                    {
                        Id = Guid.NewGuid(),
                        TypeId = TestDataSeeder.BenchPressTypeId,
                        Series = 3,
                        Repetitions = 8,
                        Weight = 50
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var update = new UpdateWorkoutResource
        {
            Id = workoutId,
            Date = updatedDate,
            Type = "Upper body",
            UserId = "spoofed-user-id"
        };

        var response = await Client.PutAsJsonAsync("/workouts", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<GetWorkoutResource>();
        updated.Should().NotBeNull();
        updated!.Id.Should().Be(workoutId);
        updated.Date.Should().Be(updatedDate);
        updated.Type.Should().Be("Upper body");
        updated.UserId.Should().Be(TestDataSeeder.TestUserId);
        updated.Exercises.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateWorkout_Rejects_Invalid_Identity_And_Date()
    {
        var response = await Client.PutAsJsonAsync(
            "/workouts",
            new UpdateWorkoutResource());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateWorkout_Does_Not_Modify_Another_Users_Workout()
    {
        var otherUserId = $"update-workout-user-{Guid.NewGuid():N}";
        var workoutId = Guid.NewGuid();
        var originalDate = new DateTime(2043, 2, 10);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Users.Add(new User
            {
                Id = otherUserId,
                UserName = $"{otherUserId}@healthspan.dev",
                NormalizedUserName = $"{otherUserId}@healthspan.dev".ToUpperInvariant(),
                Email = $"{otherUserId}@healthspan.dev",
                NormalizedEmail = $"{otherUserId}@healthspan.dev".ToUpperInvariant()
            });
            db.Workouts.Add(new Workout
            {
                Id = workoutId,
                Date = originalDate,
                Type = "Private",
                UserId = otherUserId
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.PutAsJsonAsync(
            "/workouts",
            new UpdateWorkoutResource
            {
                Id = workoutId,
                Date = originalDate.AddDays(1),
                Type = "Changed"
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MainDbContext>();
        var stored = await verificationDb.Workouts
            .IgnoreQueryFilters()
            .SingleAsync(workout => workout.Id == workoutId);
        stored.Date.Should().Be(originalDate);
        stored.Type.Should().Be("Private");
    }

    [Fact]
    public async Task DeleteWorkout_Removes_Workout_And_Exercise_Records()
    {
        var workoutId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Workouts.Add(new Workout
            {
                Id = workoutId,
                Date = new DateTime(2043, 3, 10),
                Type = "Delete me",
                UserId = TestDataSeeder.TestUserId,
                Exercises =
                [
                    new Exercise
                    {
                        Id = exerciseId,
                        TypeId = TestDataSeeder.SquatTypeId,
                        Series = 4,
                        Repetitions = 6,
                        Weight = 80
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.DeleteAsync($"/workouts/{workoutId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await verificationDb.Workouts
            .IgnoreQueryFilters()
            .AnyAsync(workout => workout.Id == workoutId))
            .Should().BeFalse();
        (await verificationDb.Exercises
            .IgnoreQueryFilters()
            .AnyAsync(exercise => exercise.Id == exerciseId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task DeleteWorkout_Does_Not_Remove_Another_Users_Workout()
    {
        var otherUserId = $"delete-workout-user-{Guid.NewGuid():N}";
        var workoutId = Guid.NewGuid();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Users.Add(new User
            {
                Id = otherUserId,
                UserName = $"{otherUserId}@healthspan.dev",
                NormalizedUserName = $"{otherUserId}@healthspan.dev".ToUpperInvariant(),
                Email = $"{otherUserId}@healthspan.dev",
                NormalizedEmail = $"{otherUserId}@healthspan.dev".ToUpperInvariant()
            });
            db.Workouts.Add(new Workout
            {
                Id = workoutId,
                Date = new DateTime(2043, 4, 10),
                Type = "Private",
                UserId = otherUserId
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.DeleteAsync($"/workouts/{workoutId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await verificationDb.Workouts
            .IgnoreQueryFilters()
            .AnyAsync(workout => workout.Id == workoutId))
            .Should().BeTrue();
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
                Weight = 60,
                Tags = ["Bilbo"]
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedSession = await response.Content.ReadFromJsonAsync<WorkoutSessionResource>();
        updatedSession.Should().NotBeNull();
        updatedSession!.Exercises.Should().ContainSingle();
        updatedSession.Exercises[0].Name.Should().Be("Squat");
        updatedSession.Exercises[0].Series.Should().Be(3);
        updatedSession.Exercises[0].Repetitions.Should().Be(10);
        updatedSession.Exercises[0].Weight.Should().Be(60);
        updatedSession.Exercises[0].Tags.Should().Equal("Bilbo");
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
