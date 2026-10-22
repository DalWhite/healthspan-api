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
    public async Task Exercise_TargetUser_And_Workout_Are_Authorised()
    {
        var otherUserId = $"exercise-target-{Guid.NewGuid():N}";
        var otherWorkoutId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            db.Users.Add(new User
            {
                Id = otherUserId,
                UserName = $"{otherUserId}@healthspan.dev",
                Email = $"{otherUserId}@healthspan.dev"
            });
            db.Workouts.Add(new Workout
            {
                Id = otherWorkoutId,
                UserId = otherUserId,
                Date = new DateTime(2097, 1, 10),
                Type = "Private"
            });
            await db.SaveChangesAsync();
        }

        var listResponse = await Client.GetAsync(
            $"/exercises?userId={otherUserId}");
        var delegatedPost = await Client.PostAsJsonAsync(
            $"/exercises?userId={otherUserId}",
            new AddExerciseResource
            {
                TypeId = TestDataSeeder.BenchPressTypeId,
                WorkoutId = otherWorkoutId,
                Series = 3,
                Repetitions = 8,
                Weight = 80
            });
        var spoofedWorkoutPost = await Client.PostAsJsonAsync(
            "/exercises",
            new AddExerciseResource
            {
                TypeId = TestDataSeeder.BenchPressTypeId,
                WorkoutId = otherWorkoutId,
                Series = 3,
                Repetitions = 8,
                Weight = 80
            });

        listResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delegatedPost.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        spoofedWorkoutPost.StatusCode.Should().Be(
            HttpStatusCode.NotFound);
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
    public async Task VolumeTrend_Aggregates_ExerciseRows_PerWorkout()
    {
        var exerciseTypeId = 0;
        var workoutId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var exerciseType = new ExerciseType
            {
                Name = $"Session volume {Guid.NewGuid():N}",
                WeightType = "Standard",
                Tags = []
            };
            db.ExerciseTypes.Add(exerciseType);
            await db.SaveChangesAsync();
            exerciseTypeId = exerciseType.Id;

            db.Workouts.Add(new Workout
            {
                Id = workoutId,
                UserId = TestDataSeeder.TestUserId,
                Date = new DateTime(2096, 7, 28),
                Type = "Volume aggregation",
                Exercises =
                [
                    new Exercise
                    {
                        Id = Guid.NewGuid(),
                        TypeId = exerciseTypeId,
                        Series = 3,
                        Repetitions = 10,
                        Weight = 50,
                        Volume = 1500
                    },
                    new Exercise
                    {
                        Id = Guid.NewGuid(),
                        TypeId = exerciseTypeId,
                        Series = 2,
                        Repetitions = 10,
                        Weight = 40,
                        Volume = 800
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync(
            $"/exercises/{exerciseTypeId}/trends?type=Volume");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var trend = await response.Content
            .ReadFromJsonAsync<GetExerciseTypeTrends>();
        trend.Should().NotBeNull();
        trend!.Points.Should().ContainSingle();
        trend.Points[0].Data.Should().Be(2300);
        trend.Points[0].Time.Date.Should().Be(
            new DateTime(2096, 7, 28));
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
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetExercises_Rejects_PageSize_Above_ServerLimit()
    {
        var response = await Client.GetAsync(
            $"/exercises?userId={TestDataSeeder.TestUserId}&page=1&pageSize=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
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

    [Fact]
    public async Task Empty_Exercise_Row_Never_Mints_A_Zero_OneRM()
    {
        // t_0f19c744: "+ serie" creates an empty row (no reps, no load). If it
        // stores MaxRepetition = 0.0, the 1RM trend silently drops to 0 as soon
        // as the real set is removed. An empty row must mint no metrics at all.
        var typeId = 0;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var type = new ExerciseType
            {
                Name = $"Empty row {Guid.NewGuid():N}",
                Description = "Standard load",
                WeightType = "Standard",
                Tags = []
            };
            db.ExerciseTypes.Add(type);
            await db.SaveChangesAsync();
            typeId = type.Id;
        }

        var placeholder = await Client.PostAsJsonAsync(
            "/exercises",
            new AddExerciseResource
            {
                TypeId = typeId,
                WorkoutId = TestDataSeeder.WorkoutId1,
                Series = 1,
                Weight = 0
            });
        placeholder.StatusCode.Should().Be(HttpStatusCode.Created);
        var placeholderId = (await placeholder.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var stored = await db.Exercises
                .IgnoreQueryFilters()
                .SingleAsync(exercise => exercise.Id == placeholderId);
            stored.MaxRepetition.Should().BeNull();
            stored.Volume.Should().BeNull();
            stored.Intensity.Should().BeNull();
        }

        var filled = await Client.PostAsJsonAsync(
            "/exercises",
            new AddExerciseResource
            {
                TypeId = typeId,
                WorkoutId = TestDataSeeder.WorkoutId1,
                Series = 1,
                Repetitions = 8,
                Weight = 80
            });
        filled.StatusCode.Should().Be(HttpStatusCode.Created);
        var filledId = (await filled.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var before = await Client.GetAsync($"/exercises/{typeId}/trends?type=RM");
        var beforeTrends = await before.Content.ReadFromJsonAsync<GetExerciseTypeTrends>();
        beforeTrends!.Points.Should().ContainSingle()
            .Which.Data.Should().BeApproximately(101.3f, 0.1f);

        var kpisBefore = await Client.GetAsync($"/exercises/{typeId}/kpis");
        var kpisBeforePayload = await kpisBefore.Content
            .ReadFromJsonAsync<GetExerciseTypeKpis>();
        kpisBeforePayload!.MaxRM.Should().BeApproximately(101.3f, 0.1f);

        var removed = await Client.DeleteAsync($"/exercises/{filledId}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await Client.GetAsync($"/exercises/{typeId}/trends?type=RM");
        var afterTrends = await after.Content.ReadFromJsonAsync<GetExerciseTypeTrends>();
        afterTrends!.Points.Should().BeEmpty();

        var kpisAfter = await Client.GetAsync($"/exercises/{typeId}/kpis");
        var kpisAfterPayload = await kpisAfter.Content
            .ReadFromJsonAsync<GetExerciseTypeKpis>();
        kpisAfterPayload!.MaxRM.Should().Be(0f);
        kpisAfterPayload.Current1RM.Should().Be(0f);
    }

    [Fact]
    public async Task BodyWeight_Breakdown_RoundTrips_And_Uses_Effective_Load()
    {
        var typeId = 0;
        var workoutId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var type = new ExerciseType
            {
                Name = $"Weighted pull-up {Guid.NewGuid():N}",
                Description = "Bodyweight plus load",
                WeightType = "BodyWeight",
                Tags = []
            };
            db.ExerciseTypes.Add(type);
            db.Workouts.Add(new Workout
            {
                Id = workoutId,
                UserId = TestDataSeeder.TestUserId,
                Date = new DateTime(2097, 2, 10),
                Type = "Pull"
            });
            await db.SaveChangesAsync();
            typeId = type.Id;
        }

        var createResponse = await Client.PostAsJsonAsync(
            "/exercises",
            new AddExerciseResource
            {
                TypeId = typeId,
                WorkoutId = workoutId,
                Series = 3,
                Repetitions = 5,
                Weight = 85,
                BodyWeight = 80,
                AddedWeight = 5
            });
        var payload = await createResponse.Content
            .ReadFromJsonAsync<JsonElement>();
        var exerciseId = payload.GetProperty("id").GetGuid();

        var getResponse = await Client.GetAsync(
            $"/exercises/{exerciseId}");
        var created = await getResponse.Content
            .ReadFromJsonAsync<GetExerciseResource>();

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        created!.BodyWeight.Should().Be(80);
        created.AddedWeight.Should().Be(5);
        created.Weight.Should().Be(85);
        created.Volume.Should().Be(1275);

        var updateResponse = await Client.PutAsJsonAsync(
            "/exercises",
            new UpdateExerciseResource
            {
                Id = exerciseId,
                Series = 3,
                Repetitions = 5,
                Weight = 90,
                BodyWeight = 80,
                AddedWeight = 10,
                Type = new GetExerciseTypeResource { Id = typeId },
                Workout = new GetWorkoutResource { Id = workoutId }
            });
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<GetExerciseResource>();

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.BodyWeight.Should().Be(80);
        updated.AddedWeight.Should().Be(10);
        updated.Weight.Should().Be(90);
        updated.Volume.Should().Be(1350);
    }
}
