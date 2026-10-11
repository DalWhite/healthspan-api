using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Controllers.ApiContracts.ExerciseTypes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace healthspan_api.tests.Regression;

public class ExerciseTypesTests : ApiTestBase
{
    public ExerciseTypesTests(HealthspanApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetExerciseTypes_Rejects_Unrelated_Favourite_Context()
    {
        var response = await Client.GetAsync(
            $"/exercisetypes?userId=unrelated-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetExerciseTypes_Returns_SeededTypes()
    {
        var response = await Client.GetAsync($"/exercisetypes?userId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var types = await response.Content.ReadFromJsonAsync<List<GetExerciseTypeResource>>();
        types.Should().NotBeNull();
        types!.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetExerciseTypes_WithoutUserId_UsesCurrentUserContext()
    {
        var response = await Client.GetAsync("/exercisetypes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var types = await response.Content.ReadFromJsonAsync<List<GetExerciseTypeResource>>();
        types.Should().NotBeNull();
        types!.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetExerciseTypes_WithNullTags_Returns_Ok()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var benchPress = await db.ExerciseTypes.FirstAsync(type => type.Id == TestDataSeeder.BenchPressTypeId);
            benchPress.Tags = null!;
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/exercisetypes?userId={TestDataSeeder.TestUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var types = await response.Content.ReadFromJsonAsync<List<GetExerciseTypeResource>>();
        types.Should().NotBeNull();
        types!.Single(type => type.Id == TestDataSeeder.BenchPressTypeId).Tags.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTrainedExerciseTypes_Returns_OnlyTypesWithAtLeastOneWorkoutExercise()
    {
        var response = await Client.GetAsync("/exercisetypes/trained");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var types = await response.Content.ReadFromJsonAsync<List<GetExerciseTypeResource>>();
        types.Should().NotBeNull();

        var ids = types!.Select(type => type.Id).ToList();
        ids.Should().Contain(TestDataSeeder.BenchPressTypeId);
        ids.Should().Contain(TestDataSeeder.DeadliftTypeId);
        ids.Should().NotContain(TestDataSeeder.SquatTypeId);
    }

    [Fact]
    public async Task GetTrainedExerciseTypes_Includes_Family_When_Only_Variation_Is_Trained()
    {
        int variationTypeId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
            var variation = new ExerciseType
            {
                Name = "Incline Bench Press",
                Description = "Incline variation",
                WeightType = "Standard",
                ParentExerciseTypeId = TestDataSeeder.BenchPressTypeId,
                Tags = new(),
                MuscleAssignments = new()
                {
                    new() { MuscleId = TestDataSeeder.ChestMuscleId, Role = ExerciseMuscleRole.Primary, SortOrder = 1 }
                }
            };
            db.ExerciseTypes.Add(variation);

            var exercise = new Exercise
            {
                Id = Guid.NewGuid(),
                TypeId = 0,
                Series = 3,
                Repetitions = 10,
                Weight = 40,
                WorkoutId = TestDataSeeder.WorkoutId1
            };
            db.Exercises.Add(exercise);
            await db.SaveChangesAsync();

            exercise.TypeId = variation.Id;
            await db.SaveChangesAsync();
            variationTypeId = variation.Id;
        }

        var response = await Client.GetAsync("/exercisetypes/trained");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var types = await response.Content.ReadFromJsonAsync<List<GetExerciseTypeResource>>();
        types.Should().NotBeNull();

        var benchPress = types!.Single(type => type.Id == TestDataSeeder.BenchPressTypeId);
        benchPress.Variations.Should().Contain(variation => variation.Id == variationTypeId);
    }

    [Fact]
    public async Task GetTrainedExerciseTypes_Rejects_Unrelated_User()
    {
        var response = await Client.GetAsync(
            $"/exercisetypes/trained?userId=unrelated-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetExerciseType_ById_Returns_Correct()
    {
        var response = await Client.GetAsync($"/exercisetypes/{TestDataSeeder.BenchPressTypeId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var exerciseType = await response.Content.ReadFromJsonAsync<GetExerciseTypeResource>();
        exerciseType.Should().NotBeNull();
        exerciseType!.Name.Should().Be("Bench Press");
    }

    [Fact]
    public async Task AddExerciseType_Returns_Created()
    {
        var newType = new AddExerciseTypeResource
        {
            Name = "Pull Up",
            Description = "Bodyweight pull up",
            WeightType = "BodyWeight",
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.BackMuscleId, "Primary", 1),
                (TestDataSeeder.FrontDeltsMuscleId, "Secondary", 1)),
            Tags = new()
        };

        var response = await Client.PostAsJsonAsync("/exercisetypes", newType);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AddExerciseType_VariationWithTags_IsReturned_WithParentAndTagData()
    {
        var newType = new AddExerciseTypeResource
        {
            Name = "Bench Press Incline",
            Description = "Incline variation",
            WeightType = "Standard",
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.ChestMuscleId, "Primary", 1),
                (TestDataSeeder.FrontDeltsMuscleId, "Secondary", 1),
                (TestDataSeeder.TricepsMuscleId, "Secondary", 2)),
            ParentExerciseTypeId = TestDataSeeder.BenchPressTypeId,
            Tags = new()
            {
                new ExerciseTagResource { Name = "barbell", Category = "equipment" },
                new ExerciseTagResource { Name = "variation", Category = "type" }
            }
        };

        var createResponse = await Client.PostAsJsonAsync("/exercisetypes", newType);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await Client.GetAsync($"/exercisetypes?userId={TestDataSeeder.TestUserId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var exerciseTypes = await listResponse.Content.ReadFromJsonAsync<List<GetExerciseTypeResource>>();
        exerciseTypes.Should().NotBeNull();

        var variation = exerciseTypes!.Single(type => type.Name == "Bench Press Incline");
        variation.ParentExerciseTypeId.Should().Be(TestDataSeeder.BenchPressTypeId);
        variation.Tags.Should().Contain(tag => tag.Category == "equipment" && tag.Name == "barbell");

        var mainExercise = exerciseTypes.Single(type => type.Id == TestDataSeeder.BenchPressTypeId);
        mainExercise.Variations.Should().Contain(item => item.Name == "Bench Press Incline");
    }

    [Fact]
    public async Task UpdateExerciseType_Returns_Ok()
    {
        var update = new UpdateExerciseTypeResource
        {
            Id = TestDataSeeder.BenchPressTypeId,
            Name = "Bench Press Updated",
            Description = "Updated description",
            WeightType = "Standard",
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.ChestMuscleId, "Primary", 1),
                (TestDataSeeder.FrontDeltsMuscleId, "Secondary", 1)),
            Tags = new()
        };

        var response = await Client.PutAsJsonAsync("/exercisetypes", update);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SetFavourite_UpdatesOnlyTheCurrentUsersFavouriteLink()
    {
        var createResponse = await Client.PostAsJsonAsync("/exercisetypes", new AddExerciseTypeResource
        {
            Name = $"Favourite Endpoint Test {Guid.NewGuid():N}",
            Description = "Metadata must remain unchanged",
            ImageRoute = "/images/favourite-test.jpg",
            WeightType = "Standard",
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.ChestMuscleId, "Primary", 1)),
            Tags = new()
            {
                new ExerciseTagResource { Name = "barbell", Category = "equipment" }
            }
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<GetExerciseTypeResource>();
        created.Should().NotBeNull();

        var favoriteResponse = await Client.PutAsJsonAsync(
            $"/exercisetypes/{created!.Id}/favourite",
            new SetExerciseTypeFavouriteResource { IsFavourite = true });

        favoriteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await Client.GetFromJsonAsync<List<GetExerciseTypeResource>>("/exercisetypes");
        var favorited = list!.Single(type => type.Id == created.Id);
        favorited.IsFavourite.Should().BeTrue();
        favorited.Name.Should().Be(created.Name);
        favorited.Description.Should().Be(created.Description);
        favorited.ImageRoute.Should().Be(created.ImageRoute);
        favorited.WeightType.Should().Be(created.WeightType);
        favorited.Tags.Should().BeEquivalentTo(created.Tags);
        favorited.MuscleAssignments.Should().BeEquivalentTo(created.MuscleAssignments);

        var unfavoriteResponse = await Client.PutAsJsonAsync(
            $"/exercisetypes/{created.Id}/favourite",
            new SetExerciseTypeFavouriteResource { IsFavourite = false });
        unfavoriteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        list = await Client.GetFromJsonAsync<List<GetExerciseTypeResource>>("/exercisetypes");
        list!.Single(type => type.Id == created.Id).IsFavourite.Should().BeFalse();
    }

    [Fact]
    public async Task SetFavourite_WhenExerciseTypeDoesNotExist_ReturnsNotFound()
    {
        var response = await Client.PutAsJsonAsync(
            "/exercisetypes/2147483647/favourite",
            new SetExerciseTypeFavouriteResource { IsFavourite = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
    }

    [Fact]
    public async Task DeleteExerciseType_WhenUnused_Returns_NoContent()
    {
        var newType = new AddExerciseTypeResource
        {
            Name = "Delete Me",
            Description = "Temporary exercise type",
            WeightType = "Standard",
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.QuadsMuscleId, "Primary", 1)),
            Tags = new()
        };

        var createResponse = await Client.PostAsJsonAsync("/exercisetypes", newType);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdType = await createResponse.Content.ReadFromJsonAsync<GetExerciseTypeResource>();
        createdType.Should().NotBeNull();

        var deleteResponse = await Client.DeleteAsync($"/exercisetypes/{createdType!.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteExerciseType_WhenUsedByExercises_Returns_ConflictProblem()
    {
        var response = await Client.DeleteAsync($"/exercisetypes/{TestDataSeeder.BenchPressTypeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString()
            .Should().Contain("workout history");
    }

    [Fact]
    public async Task AddExerciseType_Rejects_DuplicateMuscleAssignments()
    {
        var newType = new AddExerciseTypeResource
        {
            Name = "Duplicate Assignment Test",
            WeightType = "Standard",
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.ChestMuscleId, "Primary", 1),
                (TestDataSeeder.ChestMuscleId, "Secondary", 1))
        };

        var response = await Client.PostAsJsonAsync("/exercisetypes", newType);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");
    }

    [Fact]
    public async Task AddExerciseType_Rejects_MissingPrimaryAssignments()
    {
        var newType = new AddExerciseTypeResource
        {
            Name = "Secondary Only Test",
            WeightType = "Standard",
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.BackMuscleId, "Secondary", 1))
        };

        var response = await Client.PostAsJsonAsync("/exercisetypes", newType);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetExerciseType_WhenMissing_ReturnsTaskLanguageNotFound()
    {
        var response = await Client.GetAsync("/exercisetypes/2147483647");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Couldn't find that exercise.");
    }

    [Fact]
    public async Task UpdateExerciseType_WhenMissing_ReturnsTaskLanguageNotFound()
    {
        var response = await Client.PutAsJsonAsync("/exercisetypes", new UpdateExerciseTypeResource
        {
            Id = 2147483647,
            Name = "Missing Exercise Update",
            WeightType = "Standard",
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.ChestMuscleId, "Primary", 1))
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Couldn't find that exercise.");
    }

    [Fact]
    public async Task DeleteExerciseType_WhenHasVariations_ReturnsTaskLanguageConflict()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var parentResponse = await Client.PostAsJsonAsync("/exercisetypes", new AddExerciseTypeResource
        {
            Name = $"Copy Sweep Parent {suffix}",
            WeightType = "Standard",
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.ChestMuscleId, "Primary", 1))
        });
        parentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var parent = await parentResponse.Content.ReadFromJsonAsync<GetExerciseTypeResource>();

        var variationResponse = await Client.PostAsJsonAsync("/exercisetypes", new AddExerciseTypeResource
        {
            Name = $"Copy Sweep Variation {suffix}",
            WeightType = "Standard",
            ParentExerciseTypeId = parent!.Id,
            MuscleAssignments = TestDataSeeder.BuildAssignments(
                (TestDataSeeder.ChestMuscleId, "Primary", 1))
        });
        variationResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var deleteResponse = await Client.DeleteAsync($"/exercisetypes/{parent!.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await deleteResponse.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("This exercise has variations you train");
        problem.GetProperty("detail").GetString().Should().NotContain("exercise type");
    }
}
