using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace healthspan_api.tests.Regression;

public class RoutinesTests : ApiTestBase
{
    public RoutinesTests(HealthspanApiFactory factory) : base(factory) { }

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
                    Type = "Push",
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
    public async Task CreateRoutine_WithOrderedExercises_Returns_Created_And_Get_Returns_Them_In_Order()
    {
        var response = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine(
                "Ordered PPL",
                2,
                (TestDataSeeder.BenchPressTypeId, 4, 8, 80.5),
                (TestDataSeeder.SquatTypeId, 3, 10, null)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<RoutineResource>();
        created.Should().NotBeNull();
        created!.Id.Should().NotBeEmpty();
        created.Version.Should().Be(1);

        var getResponse = await Client.GetAsync($"/routines/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Headers.ETag.Should().NotBeNull();

        var routine = await getResponse.Content.ReadFromJsonAsync<RoutineResource>();
        routine!.WorkoutTemplates.Should().HaveCount(1);
        var exercises = routine.WorkoutTemplates[0].Exercises;
        exercises.Should().HaveCount(2);
        exercises[0].ExerciseTypeId.Should().Be(TestDataSeeder.BenchPressTypeId);
        exercises[0].Order.Should().Be(1);
        exercises[0].Sets.Should().Be(4);
        exercises[0].Reps.Should().Be(8);
        exercises[0].LoadKg.Should().Be(80.5);
        exercises[1].ExerciseTypeId.Should().Be(TestDataSeeder.SquatTypeId);
        exercises[1].Order.Should().Be(2);
        exercises[1].LoadKg.Should().BeNull();
    }

    [Fact]
    public async Task CreateRoutine_WithUnknownExerciseType_Returns_Field_Error()
    {
        var response = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine("Bad exercise", 1, (9999, null, null, null)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("workoutTemplates[0].exercises[0].exerciseTypeId");
    }

    [Fact]
    public async Task CreateRoutine_WithNegativeTargets_Returns_Field_Error()
    {
        var response = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine("Negative load", 1, (TestDataSeeder.SquatTypeId, 3, 10, -5)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("loadKg");
    }

    [Fact]
    public async Task UpdateRoutine_With_Stale_IfMatch_Returns_Conflict()
    {
        var createResponse = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine("Conflict test", 1, (TestDataSeeder.BenchPressTypeId, 3, 5, 60)));
        var created = await createResponse.Content.ReadFromJsonAsync<RoutineResource>();

        var update = BuildRoutine("Conflict test renamed", 1, (TestDataSeeder.SquatTypeId, 1, 1, 20));
        Client.DefaultRequestHeaders.Remove("If-Match");
        Client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "W/\"v99\"");
        var conflictResponse = await Client.PutAsJsonAsync($"/routines/{created!.Id}", update);

        Client.DefaultRequestHeaders.Remove("If-Match");
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateRoutine_With_Current_IfMatch_Replaces_Structure_And_Bumps_Version()
    {
        var createResponse = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine("Versioned routine", 1, (TestDataSeeder.BenchPressTypeId, 3, 5, 60)));
        var created = await createResponse.Content.ReadFromJsonAsync<RoutineResource>();

        var getResponse = await Client.GetAsync($"/routines/{created!.Id}");
        var etag = getResponse.Headers.ETag!.ToString();

        var update = BuildRoutine(
            "Versioned routine v2",
            3,
            (TestDataSeeder.SquatTypeId, 5, 5, 100),
            (TestDataSeeder.DeadliftTypeId, 2, 12, null));

        Client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", etag);
        var putResponse = await Client.PutAsJsonAsync($"/routines/{created.Id}", update);
        Client.DefaultRequestHeaders.Remove("If-Match");

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putResponse.Content.ReadFromJsonAsync<RoutineResource>();
        updated!.Version.Should().Be(2);
        updated.Name.Should().Be("Versioned routine v2");
        updated.WorkoutTemplates[0].Exercises.Should().HaveCount(2);
        updated.WorkoutTemplates[0].Exercises[0].ExerciseTypeId.Should().Be(TestDataSeeder.SquatTypeId);
    }

    [Fact]
    public async Task DeleteRoutine_Returns_NoContent_Then_Get_Returns_NotFound()
    {
        var createResponse = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine("Doomed routine", 1, (TestDataSeeder.BenchPressTypeId, 3, 5, 60)));
        var created = await createResponse.Content.ReadFromJsonAsync<RoutineResource>();

        var deleteResponse = await Client.DeleteAsync($"/routines/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await Client.GetAsync($"/routines/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

        var getResponse = await Client.GetAsync($"/routines?traineeId={unrelatedUserId}");
        var response = await Client.PostAsJsonAsync(
            "/routines",
            new RoutineResource
            {
                Name = "Unauthorized",
                UserId = unrelatedUserId,
                WorkoutTemplates = []
            });

        getResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Plan_Quota_Makes_Template_Due_Until_Weekly_Completions_Met()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow)
            .AddYears(3)
            .AddDays(Random.Shared.Next(1, 300));
        var createResponse = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine($"Quota {Guid.NewGuid():N}", 1, (TestDataSeeder.BenchPressTypeId, 3, 8, 60)));
        var created = await createResponse.Content.ReadFromJsonAsync<RoutineResource>();

        var planResponse = await Client.GetAsync(
            $"/routines/plan?date={date:yyyy-MM-dd}");
        planResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await planResponse.Content.ReadFromJsonAsync<List<RoutinePlanResource>>();
        plan!.Should().Contain(entry => entry.RoutineId == created!.Id);
        plan.First(entry => entry.RoutineId == created.Id).Exercises
            .Should().ContainSingle()
            .Which.ExerciseTypeId.Should().Be(TestDataSeeder.BenchPressTypeId);

        // Complete one workout for the template: the weekly quota (1) is met,
        // so the template is no longer due for any day of that week.
        var applyResponse = await Client.PostAsJsonAsync(
            "/workouts/apply-template",
            new ApplyTemplateResource
            {
                RoutineId = created.Id,
                TemplateId = created.WorkoutTemplates[0].Id,
                Date = date.ToString("yyyy-MM-dd")
            });
        var session = await applyResponse.Content.ReadFromJsonAsync<WorkoutSessionResource>();
        var completeResponse = await Client.PostAsync($"/workouts/{session!.Id}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterResponse = await Client.GetAsync($"/routines/plan?date={date:yyyy-MM-dd}");
        var afterPlan = await afterResponse.Content.ReadFromJsonAsync<List<RoutinePlanResource>>();
        afterPlan!.Should().NotContain(entry => entry.RoutineId == created.Id);
    }

    [Fact]
    public async Task Plan_Rejects_Invalid_Date()
    {
        var response = await Client.GetAsync("/routines/plan?date=not-a-date");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ApplyTemplate_Creates_Session_Exercises_And_Is_Idempotent()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow)
            .AddYears(1)
            .AddDays(Random.Shared.Next(1, 300));
        var createResponse = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine(
                $"Apply {Guid.NewGuid():N}",
                2,
                (TestDataSeeder.BenchPressTypeId, 4, 8, 80),
                (TestDataSeeder.DeadliftTypeId, 5, 5, 120)));
        var created = await createResponse.Content.ReadFromJsonAsync<RoutineResource>();
        var templateId = created!.WorkoutTemplates[0].Id;

        var firstApply = await Client.PostAsJsonAsync(
            "/workouts/apply-template",
            new ApplyTemplateResource
            {
                RoutineId = created.Id,
                TemplateId = templateId,
                Date = date.ToString("yyyy-MM-dd")
            });
        var diagBody = await firstApply.Content.ReadAsStringAsync();
        firstApply.StatusCode.Should().Be(HttpStatusCode.Created, "response body: {0}", diagBody);
        var session = await firstApply.Content.ReadFromJsonAsync<WorkoutSessionResource>();
        session!.Exercises.Should().HaveCount(2);
        session.Exercises[0].Series.Should().Be(4);
        session.Exercises[0].Repetitions.Should().Be(8);

        var secondApply = await Client.PostAsJsonAsync(
            "/workouts/apply-template",
            new ApplyTemplateResource
            {
                RoutineId = created.Id,
                TemplateId = templateId,
                Date = date.ToString("yyyy-MM-dd")
            });
        secondApply.StatusCode.Should().Be(HttpStatusCode.OK);
        var sameSession = await secondApply.Content.ReadFromJsonAsync<WorkoutSessionResource>();
        sameSession!.Id.Should().Be(session.Id);
        sameSession.Exercises.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyTemplate_On_Unknown_Routine_Returns_NotFound()
    {
        var response = await Client.PostAsJsonAsync(
            "/workouts/apply-template",
            new ApplyTemplateResource
            {
                RoutineId = Guid.NewGuid(),
                TemplateId = 1
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CompleteWorkout_Then_Adherence_Counts_It_As_Completed()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow)
            .AddYears(2)
            .AddDays(Random.Shared.Next(1, 300));
        var createResponse = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine(
                $"Adherence {Guid.NewGuid():N}",
                1,
                (TestDataSeeder.BenchPressTypeId, 3, 8, 60)));
        var created = await createResponse.Content.ReadFromJsonAsync<RoutineResource>();

        var applyResponse = await Client.PostAsJsonAsync(
            "/workouts/apply-template",
            new ApplyTemplateResource
            {
                RoutineId = created!.Id,
                TemplateId = created.WorkoutTemplates[0].Id,
                Date = today.ToString("yyyy-MM-dd")
            });
        var session = await applyResponse.Content.ReadFromJsonAsync<WorkoutSessionResource>();

        // Before completing: planned but not completed.
        var beforeResponse = await Client.GetAsync(
            $"/workouts/adherence?routineId={created.Id}&from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        beforeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await beforeResponse.Content.ReadFromJsonAsync<RoutineAdherenceResource>();
        before!.PlannedWorkouts.Should().Be(1);
        before.CompletedWorkouts.Should().Be(0);
        before.Days.Should().ContainSingle().Which.Completed.Should().BeFalse();

        var completeResponse = await Client.PostAsync($"/workouts/{session!.Id}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterResponse = await Client.GetAsync(
            $"/workouts/adherence?routineId={created.Id}&from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        var after = await afterResponse.Content.ReadFromJsonAsync<RoutineAdherenceResource>();
        after!.PlannedWorkouts.Should().Be(1);
        after.CompletedWorkouts.Should().Be(1);
        var day = after.Days.Should().ContainSingle().Which;
        day.Completed.Should().BeTrue();
        day.PlannedExerciseCount.Should().Be(1);
        day.CompletedExerciseCount.Should().Be(1);
    }

    [Fact]
    public async Task SaveAsRoutine_From_Logged_Workout_Uses_Logged_Values()
    {
        var response = await Client.PostAsJsonAsync(
            $"/workouts/{TestDataSeeder.WorkoutId1}/save-as-routine",
            new SaveAsRoutineResource
            {
                Name = "From logged push"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var routine = await response.Content.ReadFromJsonAsync<RoutineResource>();
        routine!.WorkoutTemplates.Should().HaveCount(1);
        var exercises = routine.WorkoutTemplates[0].Exercises;
        exercises.Should().ContainSingle();
        exercises[0].ExerciseTypeId.Should().Be(TestDataSeeder.BenchPressTypeId);
        exercises[0].Sets.Should().Be(4);
        exercises[0].Reps.Should().Be(8);
        exercises[0].LoadKg.Should().Be(80);
    }

    [Fact]
    public async Task SaveAsRoutine_From_Unknown_Workout_Returns_NotFound()
    {
        var response = await Client.PostAsJsonAsync(
            $"/workouts/{Guid.NewGuid()}/save-as-routine",
            new SaveAsRoutineResource
            {
                Name = "Ghost workout"
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Routine_Validation_Rejects_Bad_Frequency_Name_And_Order()
    {
        var badFrequency = BuildRoutine("Bad frequency", 8, (TestDataSeeder.BenchPressTypeId, 3, 8, 60));
        var frequencyResponse = await Client.PostAsJsonAsync("/routines", badFrequency);
        frequencyResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var frequencyErrors = await ReadValidationErrors(frequencyResponse);
        frequencyErrors.Should().ContainKey("workoutTemplates[0].frequencyPerWeek");

        var badName = BuildRoutine("x", 1, (TestDataSeeder.BenchPressTypeId, 3, 8, 60));
        var nameResponse = await Client.PostAsJsonAsync("/routines", badName);
        nameResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var nameErrors = await ReadValidationErrors(nameResponse);
        nameErrors.Should().ContainKey("name");

        var badOrder = BuildRoutine("Bad order", 1, (TestDataSeeder.BenchPressTypeId, 3, 8, 60));
        badOrder.WorkoutTemplates[0].Exercises[0].Order = 2;
        var orderResponse = await Client.PostAsJsonAsync("/routines", badOrder);
        orderResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var orderErrors = await ReadValidationErrors(orderResponse);
        orderErrors.Should().ContainKey("workoutTemplates[0].exercises");
    }

    [Fact]
    public async Task Routine_Name_Is_Unique_Per_User()
    {
        var name = $"Unique {Guid.NewGuid():N}";
        var first = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine(name, 1, (TestDataSeeder.BenchPressTypeId, 3, 8, 60)));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await Client.PostAsJsonAsync(
            "/routines",
            BuildRoutine(name, 2, (TestDataSeeder.SquatTypeId, 3, 8, 60)));
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await ReadValidationErrors(duplicate);
        errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task Trainer_Creates_Routine_For_Trainee_Sets_TrainerId_And_Trainee_Sees_It()
    {
        var routine = new RoutineResource
        {
            Name = $"Coach plan {Guid.NewGuid():N}",
            Description = "Personal plan from your coach",
            UserId = TestDataSeeder.TraineeUserId,
            WorkoutTemplates =
            [
                new()
                {
                    Type = "Push",
                    FrequencyPerWeek = 2,
                    Exercises =
                    [
                        new ExercisePrescriptionResource
                        {
                            ExerciseTypeId = TestDataSeeder.BenchPressTypeId,
                            Order = 1,
                            Sets = 4,
                            Reps = 8,
                            LoadKg = 60,
                            Notes = "Control the descent, 2 seconds down."
                        }
                    ]
                }
            ]
        };

        var createResponse = await Client.PostAsJsonAsync("/routines", routine);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<RoutineResource>();
        created!.UserId.Should().Be(TestDataSeeder.TraineeUserId);
        created.TrainerId.Should().Be(TestDataSeeder.TestUserId);

        // The coach can list the trainee's routines and sees the explanations.
        var listResponse = await Client.GetAsync(
            $"/routines?traineeId={TestDataSeeder.TraineeUserId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<List<RoutineResource>>();
        var assigned = list!.Single(entry => entry.Id == created.Id);
        assigned.TrainerId.Should().Be(TestDataSeeder.TestUserId);
        assigned.WorkoutTemplates[0].Exercises[0].Notes
            .Should().Be("Control the descent, 2 seconds down.");

        // The coach opens one of them by id. The routine belongs to the trainee, so the
        // lookup has to be scoped with traineeId — that is what the trainer editor does.
        var coachGet = await Client.GetAsync(
            $"/routines/{created.Id}?traineeId={TestDataSeeder.TraineeUserId}");
        coachGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await coachGet.Content.ReadFromJsonAsync<RoutineResource>();
        fetched!.Id.Should().Be(created.Id);
        fetched.TrainerId.Should().Be(TestDataSeeder.TestUserId);
        fetched.WorkoutTemplates[0].Exercises[0].Notes
            .Should().Be("Control the descent, 2 seconds down.");

        // Unscoped, the same routine resolves against the trainer and is not theirs.
        var unscoped = await Client.GetAsync($"/routines/{created.Id}");
        unscoped.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<Dictionary<string, string[]>> ReadValidationErrors(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var errors = new Dictionary<string, string[]>();
        if (body is not null && body.TryGetValue("errors", out var raw) && raw is JsonElement element)
        {
            foreach (var entry in element.EnumerateObject())
            {
                errors[entry.Name] = entry.Value.EnumerateArray().Select(v => v.GetString()).ToArray();
            }
        }

        return errors;
    }
}
