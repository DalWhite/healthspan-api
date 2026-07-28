using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

public class ExercisesTests : ApiTestBase
{
    public ExercisesTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetExercises_Returns_SeededExercises()
    {
        var response = await Client.GetAsync($"/exercises?userId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var exercises = await response.Content.ReadFromJsonAsync<List<GetExerciseResource>>();
        exercises.Should().NotBeNull();
        exercises!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetExercises_WithoutUserId_UsesCurrentUserContext()
    {
        var response = await Client.GetAsync("/exercises?page=1&pageSize=20&sortBy=date&descending=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("items");
        payload.Should().Contain("totalCount");
    }

    [Fact]
    public async Task GetExercises_WithNullExerciseTypeTags_Returns_Ok()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var benchPress = await db.ExerciseTypes.FirstAsync(type => type.Id == TestDataSeeder.BenchPressTypeId);
            benchPress.Tags = null!;
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/exercises?userId={TestDataSeeder.TestUserId}&page=1&pageSize=20&sortBy=date&descending=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("items");
    }

    [Fact]
    public async Task GetExercises_WithMissingExerciseType_Returns_Ok()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Exercises.Add(new Exercise
            {
                Id = Guid.NewGuid(),
                WorkoutId = TestDataSeeder.WorkoutId1,
                Series = 3,
                Repetitions = 12,
                Weight = 25,
                Comments = "No type assigned",
                TypeId = null
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/exercises?userId={TestDataSeeder.TestUserId}&sortBy=name&descending=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var exercises = await response.Content.ReadFromJsonAsync<List<GetExerciseResource>>();
        exercises.Should().NotBeNull();
    }

    [Fact]
    public async Task GetExercises_ByType_Filters_Correctly()
    {
        var response = await Client.GetAsync($"/exercises?exerciseTypeId={TestDataSeeder.BenchPressTypeId}&userId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var exercises = await response.Content.ReadFromJsonAsync<List<GetExerciseResource>>();
        exercises.Should().NotBeNull();
        exercises!.Should().AllSatisfy(e => e.Type.Id.Should().Be(TestDataSeeder.BenchPressTypeId));
    }

    [Fact]
    public async Task GetExercises_ByMuscleGroup_Filters_Through_Assignments()
    {
        var response = await Client.GetAsync($"/exercises?muscleGroup=Chest&userId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var exercises = await response.Content.ReadFromJsonAsync<List<GetExerciseResource>>();
        exercises.Should().NotBeNull();
        exercises!.Should().NotBeEmpty();
        exercises.Should().OnlyContain(exercise => exercise.Type.Name == "Bench Press");
    }

    [Fact]
    public async Task GetExercises_Paginated_Returns_PageInfo()
    {
        var response = await Client.GetAsync($"/exercises?userId={TestDataSeeder.TestUserId}&page=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("totalCount");
        json.Should().Contain("items");
    }

    [Fact]
    public async Task GetExercise_ById_Returns_Exercise()
    {
        var response = await Client.GetAsync($"/exercises/{TestDataSeeder.ExerciseId1}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var exercise = await response.Content.ReadFromJsonAsync<GetExerciseResource>();
        exercise.Should().NotBeNull();
        exercise!.Id.Should().Be(TestDataSeeder.ExerciseId1);
    }

    [Fact]
    public async Task AddExercise_Returns_Created()
    {
        var newExercise = new AddExerciseResource
        {
            TypeId = TestDataSeeder.SquatTypeId,
            WorkoutId = TestDataSeeder.WorkoutId1,
            Series = 3,
            Repetitions = 10,
            Weight = 60,
            Comments = "Test add"
        };

        var response = await Client.PostAsJsonAsync($"/exercises?userId={TestDataSeeder.TestUserId}", newExercise);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
        payload.EnumerateObject().Should().ContainSingle();
    }

    [Fact]
    public async Task TrainingTags_Are_Normalized_Editable_And_Filter_Analysis()
    {
        var createResponse = await Client.PostAsJsonAsync(
            "/exercises",
            new AddExerciseResource
            {
                TypeId = TestDataSeeder.BenchPressTypeId,
                WorkoutId = TestDataSeeder.WorkoutId1,
                Series = 1,
                Repetitions = 30,
                Weight = 50,
                Tags = [" #Bilbo ", "bilbo"]
            });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await createResponse.Content
            .ReadFromJsonAsync<JsonElement>();
        var exerciseId = payload.GetProperty("id").GetGuid();

        var getResponse = await Client.GetAsync(
            $"/exercises/{exerciseId}");
        var created = await getResponse.Content
            .ReadFromJsonAsync<GetExerciseResource>();
        created!.Tags.Should().Equal("Bilbo");

        var filteredResponse = await Client.GetAsync(
            $"/exercises?exerciseTypeId={TestDataSeeder.BenchPressTypeId}&tag=bilbo");
        var filtered = await filteredResponse.Content
            .ReadFromJsonAsync<List<GetExerciseResource>>();
        filtered.Should().ContainSingle(exercise => exercise.Id == exerciseId);
        filtered.Should().OnlyContain(exercise =>
            exercise.Tags.Contains(
                "Bilbo",
                StringComparer.OrdinalIgnoreCase));

        var trendResponse = await Client.GetAsync(
            $"/exercises/{TestDataSeeder.BenchPressTypeId}/trends?type=RM&tag=Bilbo");
        var trend = await trendResponse.Content
            .ReadFromJsonAsync<GetExerciseTypeTrends>();
        trend!.Points.Should().ContainSingle();

        var updateResponse = await Client.PutAsJsonAsync(
            "/exercises",
            new
            {
                Id = exerciseId,
                Tags = new[] { "Tempo" }
            });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<GetExerciseResource>();
        updated!.Tags.Should().Equal("Tempo");

        var oldFilterResponse = await Client.GetAsync(
            $"/exercises?exerciseTypeId={TestDataSeeder.BenchPressTypeId}&tag=Bilbo");
        var oldFilter = await oldFilterResponse.Content
            .ReadFromJsonAsync<List<GetExerciseResource>>();
        oldFilter.Should().NotContain(exercise => exercise.Id == exerciseId);
    }

    [Fact]
    public async Task AddExercise_Rejects_TooManyTrainingTags()
    {
        var response = await Client.PostAsJsonAsync(
            "/exercises",
            new AddExerciseResource
            {
                TypeId = TestDataSeeder.SquatTypeId,
                WorkoutId = TestDataSeeder.WorkoutId1,
                Series = 3,
                Repetitions = 10,
                Weight = 60,
                Tags = ["One", "Two", "Three", "Four", "Five", "Six"]
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("Use no more than 5 training tags");
    }

    [Fact]
    public async Task DeleteExercise_Returns_NoContent()
    {
        var deleteResponse = await Client.DeleteAsync($"/exercises/{TestDataSeeder.ExerciseId2}");

        deleteResponse.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateExercise_Returns_Ok()
    {
        var update = new
        {
            Id = TestDataSeeder.ExerciseId1,
            Series = 5,
            Repetitions = 6,
            Weight = 85m,
            Comments = "Updated via test",
            Workout = new { Id = TestDataSeeder.WorkoutId1 },
            Type = new { Id = TestDataSeeder.BenchPressTypeId }
        };

        var response = await Client.PutAsJsonAsync($"/exercises?userId={TestDataSeeder.TestUserId}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<GetExerciseResource>();
        updated.Should().NotBeNull();
        updated!.Series.Should().Be(5);
        updated.Repetitions.Should().Be(6);
        updated.Weight.Should().Be(85);
    }

    [Fact]
    public async Task GetExercise_NonExistent_Returns_NotFound()
    {
        var response = await Client.GetAsync($"/exercises/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetExerciseMetrics_Returns_Metrics()
    {
        var response = await Client.GetAsync($"/exercises/{TestDataSeeder.ExerciseId1}/metrics?userId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetExerciseTypeKpis_Returns_Ok()
    {
        var response = await Client.GetAsync($"/exercises/{TestDataSeeder.BenchPressTypeId}/kpis?userId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetExerciseTypeTrends_Returns_Ok()
    {
        var response = await Client.GetAsync($"/exercises/{TestDataSeeder.BenchPressTypeId}/trends?type=RM&userId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetExercises_SortByName_Returns_Ok()
    {
        var response = await Client.GetAsync($"/exercises?userId={TestDataSeeder.TestUserId}&sortBy=name&descending=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var exercises = await response.Content.ReadFromJsonAsync<List<GetExerciseResource>>();
        exercises.Should().NotBeNull();
    }

    [Fact]
    public async Task GetExercises_Paginated_Returns_CorrectPageSize()
    {
        var response = await Client.GetAsync($"/exercises?userId={TestDataSeeder.TestUserId}&page=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("pageSize").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("currentPage").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetExercises_WithInvalidPagination_Returns_BadRequest()
    {
        var response = await Client.GetAsync($"/exercises?userId={TestDataSeeder.TestUserId}&page=1&pageSize=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateExercise_NonExistent_Returns_NotFound()
    {
        var update = new
        {
            Id = Guid.NewGuid(),
            Series = 1,
            Repetitions = 1,
            Weight = 10m,
            Comments = "Non-existent"
        };

        var response = await Client.PutAsJsonAsync($"/exercises?userId={TestDataSeeder.TestUserId}", update);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateExercise_Moves_Only_The_Exercise_To_TargetWorkout()
    {
        var sourceWorkoutId = Guid.NewGuid();
        var targetWorkoutId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        var sourceDate = new DateTime(2044, 1, 10);
        var targetDate = new DateTime(2044, 1, 11);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Workouts.AddRange(
                new Workout
                {
                    Id = sourceWorkoutId,
                    Date = sourceDate,
                    Type = "Source",
                    UserId = TestDataSeeder.TestUserId,
                    Exercises =
                    [
                        new Exercise
                        {
                            Id = exerciseId,
                            TypeId = TestDataSeeder.BenchPressTypeId,
                            Series = 3,
                            Repetitions = 8,
                            Weight = 70
                        },
                        new Exercise
                        {
                            Id = siblingId,
                            TypeId = TestDataSeeder.SquatTypeId,
                            Series = 3,
                            Repetitions = 10,
                            Weight = 60
                        }
                    ]
                },
                new Workout
                {
                    Id = targetWorkoutId,
                    Date = targetDate,
                    Type = "Target",
                    UserId = TestDataSeeder.TestUserId
                });
            await db.SaveChangesAsync();
        }

        var response = await Client.PutAsJsonAsync(
            "/exercises",
            new
            {
                Id = exerciseId,
                Series = 4,
                Repetitions = 6,
                Weight = 75m,
                Workout = new { Id = targetWorkoutId, Date = targetDate },
                Type = new { Id = TestDataSeeder.BenchPressTypeId }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<GetExerciseResource>();
        updated!.Workout.Id.Should().Be(targetWorkoutId);

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MainDbContext>();
        var sourceWorkout = await verificationDb.Workouts
            .IgnoreQueryFilters()
            .SingleAsync(workout => workout.Id == sourceWorkoutId);
        var movedExercise = await verificationDb.Exercises
            .IgnoreQueryFilters()
            .SingleAsync(exercise => exercise.Id == exerciseId);
        var sibling = await verificationDb.Exercises
            .IgnoreQueryFilters()
            .SingleAsync(exercise => exercise.Id == siblingId);

        sourceWorkout.Date.Should().Be(sourceDate);
        movedExercise.WorkoutId.Should().Be(targetWorkoutId);
        sibling.WorkoutId.Should().Be(sourceWorkoutId);
    }

    [Fact]
    public async Task UpdateExercise_WithUnavailableWorkout_Returns_Conflict()
    {
        var response = await Client.PutAsJsonAsync(
            "/exercises",
            new
            {
                Id = TestDataSeeder.ExerciseId1,
                Series = 4,
                Repetitions = 8,
                Weight = 80m,
                Workout = new { Id = Guid.NewGuid() },
                Type = new { Id = TestDataSeeder.BenchPressTypeId }
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Exercise_ById_Update_And_Delete_Do_Not_Expose_Another_User()
    {
        var otherUserId = $"exercise-owner-{Guid.NewGuid():N}";
        var workoutId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();

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
                Date = new DateTime(2044, 2, 10),
                Type = "Private",
                UserId = otherUserId,
                Exercises =
                [
                    new Exercise
                    {
                        Id = exerciseId,
                        TypeId = TestDataSeeder.BenchPressTypeId,
                        Series = 3,
                        Repetitions = 8,
                        Weight = 70
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var getResponse = await Client.GetAsync($"/exercises/{exerciseId}");
        var updateResponse = await Client.PutAsJsonAsync(
            "/exercises",
            new
            {
                Id = exerciseId,
                Series = 1,
                Repetitions = 1,
                Weight = 1m
            });
        var deleteResponse = await Client.DeleteAsync($"/exercises/{exerciseId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MainDbContext>();
        (await verificationDb.Exercises
            .IgnoreQueryFilters()
            .AnyAsync(exercise => exercise.Id == exerciseId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AddExercise_ForNoWeightType_DoesNotCreate_LoadMetrics()
    {
        var typeId = 0;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var type = new ExerciseType
            {
                Name = $"Mobility {Guid.NewGuid():N}",
                Description = "No external load",
                WeightType = "None",
                Tags = []
            };
            db.ExerciseTypes.Add(type);
            await db.SaveChangesAsync();
            typeId = type.Id;
        }

        var response = await Client.PostAsJsonAsync(
            "/exercises",
            new AddExerciseResource
            {
                TypeId = typeId,
                WorkoutId = TestDataSeeder.WorkoutId1,
                Series = 3,
                Repetitions = 30,
                Weight = 99
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var exerciseId = payload.GetProperty("id").GetGuid();

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<MainDbContext>();
        var stored = await verificationDb.Exercises
            .IgnoreQueryFilters()
            .SingleAsync(exercise => exercise.Id == exerciseId);
        stored.Weight.Should().Be(0);
        stored.MaxRepetition.Should().BeNull();
        stored.Volume.Should().BeNull();
        stored.Intensity.Should().BeNull();

        var kpisResponse = await Client.GetAsync(
            $"/exercises/{typeId}/kpis");
        var trendResponse = await Client.GetAsync(
            $"/exercises/{typeId}/trends?type=RM");

        kpisResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        trendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var trend = await trendResponse.Content
            .ReadFromJsonAsync<GetExerciseTypeTrends>();
        trend!.Points.Should().BeEmpty();
    }
}
