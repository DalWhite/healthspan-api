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
}
