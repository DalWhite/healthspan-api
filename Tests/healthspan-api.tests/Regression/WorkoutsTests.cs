using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services;
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
    public async Task Workout_TargetUser_Must_Be_Self_Or_Assigned_Trainee()
    {
        var unrelatedUserId = $"unrelated-workout-{Guid.NewGuid():N}";
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Users.Add(new User
            {
                Id = unrelatedUserId,
                UserName = $"{unrelatedUserId}@healthspan.dev",
                Email = $"{unrelatedUserId}@healthspan.dev"
            });
            await db.SaveChangesAsync();
        }

        var getResponse = await Client.GetAsync(
            $"/workouts?traineeId={unrelatedUserId}");
        var postResponse = await Client.PostAsJsonAsync(
            "/workouts",
            new AddWorkoutResource
            {
                Date = new DateTime(2096, 4, 10),
                Type = "Unauthorized",
                UserId = unrelatedUserId
            });

        getResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DuplicateWorkout_Copies_Whole_Day_To_Another_Date()
    {
        var targetDate = new DateOnly(2097, 6, 15);

        var response = await Client.PostAsJsonAsync(
            $"/workouts/{TestDataSeeder.WorkoutId1}/duplicate",
            new DuplicateWorkoutResource { Date = targetDate });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content
            .ReadFromJsonAsync<DuplicatedWorkoutResource>();
        result.Should().NotBeNull();
        result!.SourceWorkoutId.Should().Be(TestDataSeeder.WorkoutId1);
        result.Workout.Id.Should().NotBe(TestDataSeeder.WorkoutId1);
        result.Workout.Date.Date.Should().Be(targetDate.ToDateTime(TimeOnly.MinValue));
        result.Workout.Type.Should().Be("Push");
        result.Workout.ExerciseCount.Should().Be(1);
        result.Workout.Exercises.Should().HaveCount(1);
        var copy = result.Workout.Exercises.Single();
        copy.TypeId.Should().Be(TestDataSeeder.BenchPressTypeId);
        copy.Series.Should().Be(4);
        copy.Repetitions.Should().Be(8);
        copy.Weight.Should().Be(80);
        copy.Id.Should().NotBe(TestDataSeeder.ExerciseId1);

        // The source workout is untouched.
        var sourceResponse = await Client.GetAsync(
            $"/workouts/{TestDataSeeder.WorkoutId1}");
        var source = await sourceResponse.Content
            .ReadFromJsonAsync<GetWorkoutResource>();
        source!.ExerciseCount.Should().Be(1);
    }

    [Fact]
    public async Task DuplicateWorkout_Copies_Selected_Exercises_Only()
    {
        var targetDate = new DateOnly(2097, 6, 20);

        var response = await Client.PostAsJsonAsync(
            $"/workouts/{TestDataSeeder.WorkoutId1}/duplicate",
            new DuplicateWorkoutResource
            {
                Date = targetDate,
                Type = "Push (partial)",
                ExerciseIds = [TestDataSeeder.ExerciseId1]
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content
            .ReadFromJsonAsync<DuplicatedWorkoutResource>();
        result!.Workout.Type.Should().Be("Push (partial)");
        result.Workout.ExerciseCount.Should().Be(1);
    }

    [Fact]
    public async Task DuplicateWorkout_Rejects_Unknown_Workout_And_Missing_Date()
    {
        var notFound = await Client.PostAsJsonAsync(
            $"/workouts/{Guid.NewGuid()}/duplicate",
            new DuplicateWorkoutResource { Date = new DateOnly(2097, 7, 1) });
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var invalid = await Client.PostAsJsonAsync(
            $"/workouts/{TestDataSeeder.WorkoutId1}/duplicate",
            new DuplicateWorkoutResource { Date = default });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DuplicateWorkout_TargetUser_Must_Be_Self_Or_Assigned_Trainee()
    {
        var unrelatedUserId = $"unrelated-dup-{Guid.NewGuid():N}";
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Users.Add(new User
            {
                Id = unrelatedUserId,
                UserName = $"{unrelatedUserId}@healthspan.dev",
                Email = $"{unrelatedUserId}@healthspan.dev"
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsJsonAsync(
            $"/workouts/{TestDataSeeder.WorkoutId1}/duplicate?traineeId={unrelatedUserId}",
            new DuplicateWorkoutResource { Date = new DateOnly(2097, 8, 1) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Assigned_Trainer_Can_Prepare_Trainee_Workout()
    {
        var traineeId = $"assigned-trainee-{Guid.NewGuid():N}";
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var trainer = await db.Users.FindAsync(
                TestDataSeeder.TestUserId);
            db.Users.Add(new User
            {
                Id = traineeId,
                UserName = $"{traineeId}@healthspan.dev",
                Email = $"{traineeId}@healthspan.dev",
                Trainer = trainer
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsJsonAsync(
            "/workouts",
            new AddWorkoutResource
            {
                Date = new DateTime(2096, 4, 11),
                Type = "Coached",
                UserId = traineeId
            });
        var workout = await response.Content
            .ReadFromJsonAsync<GetWorkoutResource>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        workout!.UserId.Should().Be(traineeId);
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
    public async Task WeeklySummary_Returns_Bounded_UserScoped_TrainingData()
    {
        var workoutId = Guid.NewGuid();
        var workoutDate = new DateTime(2099, 1, 5);
        var otherUserId = $"weekly-summary-user-{Guid.NewGuid():N}";

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Users.Add(new User
            {
                Id = otherUserId,
                UserName = $"{otherUserId}@healthspan.dev",
                NormalizedUserName =
                    $"{otherUserId}@healthspan.dev".ToUpperInvariant(),
                Email = $"{otherUserId}@healthspan.dev",
                NormalizedEmail =
                    $"{otherUserId}@healthspan.dev".ToUpperInvariant()
            });
            db.Workouts.Add(new Workout
            {
                Id = workoutId,
                Date = workoutDate,
                Type = "Weekly summary test",
                UserId = TestDataSeeder.TestUserId,
                Exercises =
                [
                    new Exercise
                    {
                        Id = Guid.NewGuid(),
                        TypeId = TestDataSeeder.BenchPressTypeId,
                        Series = 3,
                        Repetitions = 10,
                        Weight = 50
                    }
                ]
            });
            db.Workouts.Add(new Workout
            {
                Id = Guid.NewGuid(),
                Date = workoutDate.AddYears(1),
                Type = "Private weekly summary",
                UserId = otherUserId
            });
            db.Workouts.Add(new Workout
            {
                Id = Guid.NewGuid(),
                Date = workoutDate.AddYears(2),
                Type = "Empty workout shell",
                UserId = TestDataSeeder.TestUserId
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync(
            "/workouts/weekly-summary?weeks=1");
        var summary = await response.Content
            .ReadFromJsonAsync<WeeklyTrainingSummaryResource>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        summary.Should().NotBeNull();
        summary!.Weeks.Should().ContainSingle();
        var week = summary.Weeks[0];
        week.WorkoutCount.Should().Be(1);
        week.ExerciseCount.Should().Be(1);
        week.TotalLoad.Should().Be(1500);
        week.Sessions.Should().ContainSingle(session =>
            session.Key == workoutId.ToString()
            && session.Label == "Weekly summary test");
        week.Sessions[0].Exercises.Should().ContainSingle(exercise =>
            exercise.Name == "Bench Press"
            && exercise.Prescription == "3 x 10 @ 50 kg");
        week.Regions.Should().Contain(region =>
            region.Key == "chest"
            && region.Load > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    public async Task WeeklySummary_Rejects_Invalid_WeekLimit(int weeks)
    {
        var response = await Client.GetAsync(
            $"/workouts/weekly-summary?weeks={weeks}");

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
    public async Task AddWorkout_Reuses_The_First_Workout_Of_The_Day()
    {
        var date = new DateTime(2045, 5, 10, 9, 30, 0);
        var firstResponse = await Client.PostAsJsonAsync(
            "/workouts",
            new AddWorkoutResource
            {
                Date = date,
                UserId = TestDataSeeder.TestUserId,
                Type = "Morning training"
            });
        var first = await firstResponse.Content
            .ReadFromJsonAsync<GetWorkoutResource>();

        var secondResponse = await Client.PostAsJsonAsync(
            "/workouts",
            new AddWorkoutResource
            {
                Date = date.AddHours(8),
                UserId = TestDataSeeder.TestUserId,
                Type = "Duplicate attempt"
            });
        var second = await secondResponse.Content
            .ReadFromJsonAsync<GetWorkoutResource>();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        second!.Id.Should().Be(first!.Id);
        second.Type.Should().Be("Morning training");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var stored = await db.Workouts
            .IgnoreQueryFilters()
            .Where(workout =>
                workout.UserId == TestDataSeeder.TestUserId
                && workout.Date >= date.Date
                && workout.Date < date.Date.AddDays(1))
            .ToListAsync();
        stored.Should().ContainSingle();
        stored[0].DailySessionKey.Should().HaveLength(64);
    }

    [Fact]
    public async Task AddWorkout_Prefers_The_First_LegacyWorkout_WithAnExercise()
    {
        var date = new DateTime(2045, 6, 10);
        var emptyShellId = Guid.Parse(
            "10000000-0000-0000-0000-000000000001");
        var firstExerciseWorkoutId = Guid.Parse(
            "20000000-0000-0000-0000-000000000002");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Workouts.AddRange(
                new Workout
                {
                    Id = emptyShellId,
                    Date = date.AddHours(7),
                    Type = "Empty shell",
                    UserId = TestDataSeeder.TestUserId,
                    DailySessionKey = WorkoutDailySessionKey.Create(
                        TestDataSeeder.TestUserId,
                        DateOnly.FromDateTime(date))
                },
                new Workout
                {
                    Id = firstExerciseWorkoutId,
                    Date = date.AddHours(8),
                    Type = "First exercise",
                    UserId = TestDataSeeder.TestUserId,
                    Exercises =
                    [
                        new Exercise
                        {
                            Id = Guid.NewGuid(),
                            TypeId = TestDataSeeder.BenchPressTypeId,
                            Series = 3,
                            Repetitions = 8,
                            Weight = 80
                        }
                    ]
                });
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsJsonAsync(
            "/workouts",
            new AddWorkoutResource
            {
                Date = date.AddHours(20),
                UserId = TestDataSeeder.TestUserId,
                Type = "Another"
            });
        var workout = await response.Content
            .ReadFromJsonAsync<GetWorkoutResource>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        workout!.Id.Should().Be(firstExerciseWorkoutId);
        workout.Type.Should().Be("First exercise");

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<MainDbContext>();
        (await verificationDb.Workouts
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == firstExerciseWorkoutId))
            .DailySessionKey.Should().HaveLength(64);
        (await verificationDb.Workouts
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == emptyShellId))
            .DailySessionKey.Should().BeNull();
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

        var update = new
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
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
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
