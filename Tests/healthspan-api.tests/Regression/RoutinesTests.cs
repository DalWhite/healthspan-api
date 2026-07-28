using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class RoutinesTests : ApiTestBase
{
    public RoutinesTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task AddRoutine_Then_GetRoutines_Returns_It()
    {
        var newRoutine = new AddRoutineResource
        {
            Name = "Push Pull Legs",
            Description = "Classic PPL split",
            UserId = TestDataSeeder.TestUserId,
            WorkoutTemplates = new List<WorkoutTemplateResource>
            {
                new()
                {
                    Type = "Push",
                    Frequency = 2,
                    ExerciseTypesIds = new List<int> { TestDataSeeder.BenchPressTypeId }
                },
                new()
                {
                    Type = "Pull",
                    Frequency = 2,
                    ExerciseTypesIds = new List<int> { TestDataSeeder.DeadliftTypeId }
                }
            }
        };

        var createResponse = await Client.PostAsJsonAsync("/routines", newRoutine);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await Client.GetAsync($"/routines?traineeId={TestDataSeeder.TestUserId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var routines = await listResponse.Content.ReadFromJsonAsync<List<GetRoutineResource>>();
        routines.Should().NotBeNull();
        routines!.Should().Contain(r => r.Name == "Push Pull Legs");
    }

    [Fact]
    public async Task GetRoutines_NoTraineeId_Returns_Ok()
    {
        var response = await Client.GetAsync("/routines?traineeId=");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Routines_Reject_Unrelated_Target_User()
    {
        var unrelatedUserId = $"routine-user-{Guid.NewGuid():N}";

        var getResponse = await Client.GetAsync(
            $"/routines?traineeId={unrelatedUserId}");
        var postResponse = await Client.PostAsJsonAsync(
            "/routines",
            new AddRoutineResource
            {
                Name = "Unauthorized",
                UserId = unrelatedUserId,
                WorkoutTemplates = []
            });

        getResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddRoutine_WithSingleWorkoutTemplate_Returns_Created()
    {
        var routine = new AddRoutineResource
        {
            Name = "Full Body",
            Description = "Full body 3x/week",
            UserId = TestDataSeeder.TestUserId,
            WorkoutTemplates = new List<WorkoutTemplateResource>
            {
                new()
                {
                    Type = "Full Body",
                    Frequency = 3,
                    ExerciseTypesIds = new List<int>
                    {
                        TestDataSeeder.BenchPressTypeId,
                        TestDataSeeder.SquatTypeId,
                        TestDataSeeder.DeadliftTypeId
                    }
                }
            }
        };

        var response = await Client.PostAsJsonAsync("/routines", routine);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetRoutines_Returns_RoutineWithWorkoutTemplates()
    {
        // First ensure a routine exists
        var routine = new AddRoutineResource
        {
            Name = "Template Check",
            Description = "Verify templates returned",
            UserId = TestDataSeeder.TestUserId,
            WorkoutTemplates = new List<WorkoutTemplateResource>
            {
                new()
                {
                    Type = "Upper",
                    Frequency = 2,
                    ExerciseTypesIds = new List<int> { TestDataSeeder.BenchPressTypeId }
                }
            }
        };
        await Client.PostAsJsonAsync("/routines", routine);

        var response = await Client.GetAsync($"/routines?traineeId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var routines = await response.Content.ReadFromJsonAsync<List<GetRoutineResource>>();
        routines.Should().NotBeNull();
        var found = routines!.FirstOrDefault(r => r.Name == "Template Check");
        found.Should().NotBeNull();
        found!.WorkoutTemplates.Should().HaveCount(1);
        found.WorkoutTemplates.First().Type.Should().Be("Upper");
    }
}
