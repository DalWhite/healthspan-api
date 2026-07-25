using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace healthspan_api.tests;

/// <summary>
/// Seeds the InMemory database with test data shared by all regression tests.
/// Call <see cref="SeedAsync"/> inside an IServiceScope from the factory.
/// </summary>
public static class TestDataSeeder
{
    public static readonly string TestUserId = HealthspanApiFactory.TestUserId;

    // Exercise types
    public static readonly int BenchPressTypeId = 1;
    public static readonly int SquatTypeId = 2;
    public static readonly int DeadliftTypeId = 3;

    // Muscles
    public static readonly int ChestMuscleId = 1;
    public static readonly int FrontDeltsMuscleId = 2;
    public static readonly int BackMuscleId = 6;
    public static readonly int TricepsMuscleId = 9;
    public static readonly int GlutesMuscleId = 15;
    public static readonly int QuadsMuscleId = 16;
    public static readonly int HamstringsMuscleId = 17;

    // Known GUIDs for workouts & blood tests
    public static readonly Guid WorkoutId1 = Guid.Parse("aaaaaaaa-0001-0001-0001-aaaaaaaaaaaa");
    public static readonly Guid WorkoutId2 = Guid.Parse("aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa");
    public static readonly Guid ExerciseId1 = Guid.Parse("bbbbbbbb-0001-0001-0001-bbbbbbbbbbbb");
    public static readonly Guid ExerciseId2 = Guid.Parse("bbbbbbbb-0002-0002-0002-bbbbbbbbbbbb");
    public static readonly Guid BloodTestId1 = Guid.Parse("cccccccc-0001-0001-0001-cccccccccccc");

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<MainDbContext>();
        var userManager = services.GetRequiredService<UserManager<User>>();

        await SeedUser(userManager);
        await SeedMusclesAsync(db);
        SeedExerciseTypes(db);
        SeedWorkoutsAndExercises(db);
        SeedBloodTests(db);

        await db.SaveChangesAsync();
    }

    private static async Task SeedUser(UserManager<User> userManager)
    {
        if (await userManager.FindByIdAsync(TestUserId) is not null)
            return;

        var user = new User
        {
            Id = TestUserId,
            UserName = HealthspanApiFactory.TestUserEmail,
            Email = HealthspanApiFactory.TestUserEmail,
            Name = "Test User",
            Age = 30,
            Weight = 80,
            Height = 180
        };

        await userManager.CreateAsync(user, "TestPassword123!");
    }

    private static async Task SeedMusclesAsync(MainDbContext db)
    {
        if (db.Muscles.Any())
            return;

        await MuscleCatalogSeeder.UpsertAsync(db);
    }

    private static void SeedExerciseTypes(MainDbContext db)
    {
        if (db.ExerciseTypes.Any())
            return;

        db.ExerciseTypes.AddRange(
            new ExerciseType
            {
                Id = BenchPressTypeId,
                Name = "Bench Press",
                Description = "Barbell bench press",
                WeightType = "Standard",
                Tags = new(),
                MuscleAssignments = new()
                {
                    new() { ExerciseTypeId = BenchPressTypeId, MuscleId = ChestMuscleId, Role = ExerciseMuscleRole.Primary, SortOrder = 1 },
                    new() { ExerciseTypeId = BenchPressTypeId, MuscleId = FrontDeltsMuscleId, Role = ExerciseMuscleRole.Secondary, SortOrder = 1 },
                    new() { ExerciseTypeId = BenchPressTypeId, MuscleId = TricepsMuscleId, Role = ExerciseMuscleRole.Secondary, SortOrder = 2 }
                }
            },
            new ExerciseType
            {
                Id = SquatTypeId,
                Name = "Squat",
                Description = "Barbell back squat",
                WeightType = "Standard",
                Tags = new(),
                MuscleAssignments = new()
                {
                    new() { ExerciseTypeId = SquatTypeId, MuscleId = QuadsMuscleId, Role = ExerciseMuscleRole.Primary, SortOrder = 1 },
                    new() { ExerciseTypeId = SquatTypeId, MuscleId = GlutesMuscleId, Role = ExerciseMuscleRole.Secondary, SortOrder = 1 }
                }
            },
            new ExerciseType
            {
                Id = DeadliftTypeId,
                Name = "Deadlift",
                Description = "Conventional deadlift",
                WeightType = "Standard",
                Tags = new(),
                MuscleAssignments = new()
                {
                    new() { ExerciseTypeId = DeadliftTypeId, MuscleId = BackMuscleId, Role = ExerciseMuscleRole.Primary, SortOrder = 1 },
                    new() { ExerciseTypeId = DeadliftTypeId, MuscleId = HamstringsMuscleId, Role = ExerciseMuscleRole.Secondary, SortOrder = 1 },
                    new() { ExerciseTypeId = DeadliftTypeId, MuscleId = GlutesMuscleId, Role = ExerciseMuscleRole.Secondary, SortOrder = 2 }
                }
            }
        );
    }

    public static List<ExerciseTypeMuscleAssignmentResource> BuildAssignments(params (int MuscleId, string Role, int SortOrder)[] assignments)
        => assignments
            .Select(assignment => new ExerciseTypeMuscleAssignmentResource
            {
                MuscleId = assignment.MuscleId,
                Role = assignment.Role,
                SortOrder = assignment.SortOrder
            })
            .ToList();

    private static void SeedWorkoutsAndExercises(MainDbContext db)
    {
        if (db.Workouts.Any())
            return;

        var workout1 = new Workout
        {
            Id = WorkoutId1,
            Date = DateTime.UtcNow.AddDays(-7),
            Type = "Push",
            UserId = TestUserId,
            Exercises = new List<Exercise>
            {
                new()
                {
                    Id = ExerciseId1,
                    TypeId = BenchPressTypeId,
                    Series = 4,
                    Repetitions = 8,
                    Weight = 80,
                    Comments = "Felt good",
                    WorkoutId = WorkoutId1
                }
            }
        };

        var workout2 = new Workout
        {
            Id = WorkoutId2,
            Date = DateTime.UtcNow.AddDays(-3),
            Type = "Pull",
            UserId = TestUserId,
            Exercises = new List<Exercise>
            {
                new()
                {
                    Id = ExerciseId2,
                    TypeId = DeadliftTypeId,
                    Series = 5,
                    Repetitions = 5,
                    Weight = 120,
                    Comments = "New PR",
                    WorkoutId = WorkoutId2
                }
            }
        };

        db.Workouts.AddRange(workout1, workout2);
    }

    private static void SeedBloodTests(MainDbContext db)
    {
        if (db.BloodTests.Any())
            return;

        var bloodTest = new BloodTest
        {
            Id = BloodTestId1,
            UserId = TestUserId,
            TestDate = DateTime.UtcNow.AddDays(-30),
            LabName = "Test Lab",
            ProcessingStatus = BloodTestProcessingStatus.Processed,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-30),
            MarkerResults = new List<BloodTestMarkerResult>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    MetricKey = "glucose",
                    DisplayName = "Glucose",
                    Unit = "mg/dL",
                    Value = 90,
                    NormalizedValue = 90,
                    Status = MarkerStatus.InRange,
                    ReferenceMinValue = 70,
                    ReferenceMaxValue = 110
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    MetricKey = "cholesterol_total",
                    DisplayName = "Total Cholesterol",
                    Unit = "mg/dL",
                    Value = 220,
                    NormalizedValue = 220,
                    Status = MarkerStatus.High,
                    ReferenceMinValue = 0,
                    ReferenceMaxValue = 200
                }
            }
        };

        db.BloodTests.Add(bloodTest);
    }
}
