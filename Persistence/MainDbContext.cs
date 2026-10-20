using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Services;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using System.Text.Json;

public class MainDbContext : IdentityDbContext<User, Role, string>
{
    public DbSet<Workout> Workouts { get; set; }
    public DbSet<Exercise> Exercises { get; set; }
    public DbSet<ExerciseType> ExerciseTypes { get; set; }
    public DbSet<ExerciseTypeMuscleAssignment> ExerciseTypeMuscleAssignments { get; set; }
    public DbSet<Muscle> Muscles { get; set; }
    public DbSet<Routine> Routines { get; set; }
    public DbSet<WorkoutTemplate> WorkoutTemplate { get; set; }
    public DbSet<WorkoutTemplateExercise> WorkoutTemplateExercises { get; set; }
    public DbSet<HealthProfile> HealthProfiles { get; set; }
    public DbSet<HealthMetricDefinition> HealthMetricDefinitions { get; set; }
    public DbSet<ReferenceRange> ReferenceRanges { get; set; }
    public DbSet<BloodTest> BloodTests { get; set; }
    public DbSet<BloodTestMarkerResult> BloodTestMarkerResults { get; set; }
    public DbSet<AnalyticsPermission> AnalyticsPermissions { get; set; }
    public DbSet<GalpinAssessment> GalpinAssessments { get; set; }
    public DbSet<GalpinAssessmentScore> GalpinAssessmentScores { get; set; }
    public DbSet<SyncOperation> SyncOperations { get; set; }
    public DbSet<WorkoutBlock> WorkoutBlocks { get; set; }
    public DbSet<Adaptation> Adaptations { get; set; }
    public DbSet<ExerciseTypeAdaptation> ExerciseTypeAdaptations { get; set; }
    public DbSet<ExerciseAdaptation> ExerciseAdaptations { get; set; }
    private CurrentUserProvider _userProvider;

    public MainDbContext(DbContextOptions<MainDbContext> options, CurrentUserProvider userProvider)
        : base(options)
    {
        _userProvider = userProvider;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Workout>().HasQueryFilter(w => w.User.Id == _userProvider.GetCurrentUserId());
        builder.Entity<Exercise>().HasQueryFilter(e => e.Workout.User.Id == _userProvider.GetCurrentUserId());
        builder.Entity<HealthProfile>().HasQueryFilter(profile => profile.UserId == _userProvider.GetCurrentUserId());
        builder.Entity<BloodTest>().HasQueryFilter(test => test.UserId == _userProvider.GetCurrentUserId());
        builder.Entity<BloodTestMarkerResult>().HasQueryFilter(result => result.BloodTest.UserId == _userProvider.GetCurrentUserId());
        builder.Entity<GalpinAssessment>().HasQueryFilter(assessment => assessment.UserId == _userProvider.GetCurrentUserId());
        builder.Entity<GalpinAssessmentScore>().HasQueryFilter(score => score.GalpinAssessment.UserId == _userProvider.GetCurrentUserId());
        builder.Entity<AnalyticsPermission>().HasQueryFilter(permission => permission.OwnerUserId == _userProvider.GetCurrentUserId() || permission.ViewerUserId == _userProvider.GetCurrentUserId());

        builder.Entity<Workout>()
            .Property(workout => workout.DailySessionKey)
            .HasMaxLength(64);

        builder.Entity<Workout>()
            .HasIndex(workout => workout.DailySessionKey)
            .IsUnique();

        builder.Entity<Workout>()
            .HasIndex(workout => workout.ClientEntityId)
            .IsUnique();

        builder.Entity<Exercise>()
            .HasIndex(exercise => exercise.ClientEntityId)
            .IsUnique();

        builder.Entity<SyncOperation>()
            .HasIndex(entry => new { entry.UserId, entry.OpId })
            .IsUnique();

        builder.Entity<Muscle>()
            .Property(e => e.Side)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, default(MuscleSide)));
        
        builder.Entity<Muscle>()
            .Property(e => e.MuscleGroup)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, default(MuscleGroup)));

        builder.Entity<Muscle>()
            .HasIndex(e => e.Slug)
            .IsUnique();

        builder.Entity<ExerciseTypeMuscleAssignment>()
            .Property(e => e.Role)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, ExerciseMuscleRole.Primary));

        builder.Entity<ExerciseTypeMuscleAssignment>()
            .HasIndex(e => new { e.ExerciseTypeId, e.MuscleId })
            .IsUnique();

        builder.Entity<ExerciseTypeMuscleAssignment>()
            .HasOne(e => e.ExerciseType)
            .WithMany(e => e.MuscleAssignments)
            .HasForeignKey(e => e.ExerciseTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ExerciseTypeMuscleAssignment>()
            .HasOne(e => e.Muscle)
            .WithMany(e => e.ExerciseTypeAssignments)
            .HasForeignKey(e => e.MuscleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<HealthProfile>()
            .HasKey(profile => profile.UserId);

        builder.Entity<HealthProfile>()
            .HasOne(profile => profile.User)
            .WithOne(user => user.HealthProfile)
            .HasForeignKey<HealthProfile>(profile => profile.UserId);

        builder.Entity<HealthProfile>()
            .Property(profile => profile.BiologicalSex)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, default(BiologicalSex)));

        builder.Entity<HealthMetricDefinition>()
            .HasIndex(metric => metric.Key)
            .IsUnique();

        builder.Entity<HealthMetricDefinition>()
            .Property(metric => metric.Category)
            .HasConversion(v => v.ToString(), v => (HealthMetricCategory)Enum.Parse(typeof(HealthMetricCategory), v));

        builder.Entity<ReferenceRange>()
            .Property(range => range.BiologicalSex)
            .HasConversion(
                value => value.HasValue ? value.Value.ToString() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : ParseEnumOrDefault(value, default(BiologicalSex)));

        builder.Entity<BloodTest>()
            .Property(test => test.ProcessingStatus)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, default(BloodTestProcessingStatus)));

        builder.Entity<BloodTest>()
            .HasIndex(test => test.SourceBlobName);

        builder.Entity<BloodTest>()
            .Property(test => test.SourceContentHash)
            .HasMaxLength(64);

        builder.Entity<BloodTest>()
            .HasIndex(test => new
            {
                test.UserId,
                test.SourceContentHash
            });

        builder.Entity<BloodTest>()
            .HasMany(test => test.MarkerResults)
            .WithOne(result => result.BloodTest)
            .HasForeignKey(result => result.BloodTestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<BloodTestMarkerResult>()
            .Property(result => result.Status)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, default(MarkerStatus)));

        builder.Entity<BloodTestMarkerResult>()
            .HasIndex(result => new { result.BloodTestId, result.MetricKey });

        builder.Entity<AnalyticsPermission>()
            .HasOne(permission => permission.OwnerUser)
            .WithMany()
            .HasForeignKey(permission => permission.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AnalyticsPermission>()
            .HasOne(permission => permission.ViewerUser)
            .WithMany()
            .HasForeignKey(permission => permission.ViewerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GalpinAssessment>()
            .HasMany(assessment => assessment.Scores)
            .WithOne(score => score.GalpinAssessment)
            .HasForeignKey(score => score.GalpinAssessmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GalpinAssessmentScore>()
            .Property(score => score.Adaptation)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, default(GalpinAdaptationType)));

        // Configure ExerciseType parent-child relationship
        builder.Entity<ExerciseType>()
            .HasOne(e => e.ParentExerciseType)
            .WithMany(e => e.Variations)
            .HasForeignKey(e => e.ParentExerciseTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Tags property with JSON serialization
        builder.Entity<ExerciseType>()
            .Property(e => e.Tags)
            .HasConversion(
                value => SerializeExerciseTags(value),
                value => DeserializeExerciseTags(value)
            )
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<ExerciseTag>>(
                (left, right) => ExerciseTagsEqual(left, right),
                value => GetExerciseTagsHashCode(value),
                value => CloneExerciseTags(value)
            ));

        var trainingTagsProperty = builder.Entity<Exercise>()
            .Property(exercise => exercise.Tags)
            .HasConversion(
                value => SerializeTrainingTags(value),
                value => DeserializeTrainingTags(value));
        trainingTagsProperty.IsRequired();
        trainingTagsProperty.Metadata.SetValueComparer(
            new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                (left, right) => TrainingTagsEqual(left, right),
                value => GetTrainingTagsHashCode(value),
                value => CloneTrainingTags(value)));

        // Ordered exercise slots inside a template (routine-prescriptions design).
        builder.Entity<WorkoutTemplateExercise>()
            .HasOne(wte => wte.WorkoutTemplate)
            .WithMany(wt => wt.Exercises)
            .HasForeignKey(wte => wte.WorkoutTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<WorkoutTemplateExercise>()
            .HasOne(wte => wte.ExerciseType)
            .WithMany()
            .HasForeignKey(wte => wte.ExerciseTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<WorkoutTemplateExercise>()
            .HasIndex(wte => new { wte.WorkoutTemplateId, wte.Order })
            .IsUnique();

        builder.Entity<WorkoutTemplateExercise>()
            .HasIndex(wte => wte.ExerciseTypeId);

        // Logged exercise -> routine slot it came from; logged history survives
        // template/prescription deletion.
        builder.Entity<Exercise>()
            .HasOne<WorkoutTemplateExercise>()
            .WithMany()
            .HasForeignKey(exercise => exercise.SourceTemplateExerciseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Routine>()
            .HasOne(routine => routine.Trainer)
            .WithMany()
            .HasForeignKey(routine => routine.TrainerId)
            .OnDelete(DeleteBehavior.SetNull);

        // ------------------------------------------------------------------
        // Universal training tracker (docs/training-tracker-universal-design.md)
        // ------------------------------------------------------------------

        builder.Entity<Workout>()
            .Property(workout => workout.Goal)
            .HasConversion(
                value => value.HasValue ? value.Value.ToString() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : ParseEnumOrDefault(value, default(GalpinAdaptationType)));

        builder.Entity<Workout>()
            .Property(workout => workout.Modality)
            .HasConversion(
                value => value.HasValue ? value.Value.ToString() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : ParseEnumOrDefault(value, default(WorkoutModality)));

        var secondaryGoalsProperty = builder.Entity<Workout>()
            .Property(workout => workout.SecondaryGoals)
            .HasConversion(
                value => SerializeAdaptationGoals(value),
                value => DeserializeAdaptationGoals(value));
        secondaryGoalsProperty.Metadata.SetValueComparer(
            new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<GalpinAdaptationType>>(
                (left, right) => AdaptationGoalsEqual(left, right),
                value => GetAdaptationGoalsHashCode(value),
                value => CloneAdaptationGoals(value)));

        builder.Entity<WorkoutBlock>()
            .HasIndex(block => block.ClientEntityId)
            .IsUnique();

        builder.Entity<WorkoutBlock>()
            .HasOne(block => block.Workout)
            .WithMany(workout => workout.Blocks)
            .HasForeignKey(block => block.WorkoutId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<WorkoutBlock>()
            .HasIndex(block => new { block.WorkoutId, block.Order })
            .IsUnique();

        builder.Entity<WorkoutBlock>()
            .Property(block => block.Label)
            .HasMaxLength(100);

        builder.Entity<WorkoutBlock>()
            .Property(block => block.Type)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, default(WorkoutBlockType)));

        builder.Entity<Exercise>()
            .HasOne(exercise => exercise.Block)
            .WithMany(block => block.Exercises)
            .HasForeignKey(exercise => exercise.BlockId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Exercise>()
            .Property(exercise => exercise.QualityIntent)
            .HasMaxLength(200);

        builder.Entity<Exercise>()
            .Property(exercise => exercise.LoadType)
            .HasConversion(
                value => value.HasValue ? value.Value.ToString() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : ParseEnumOrDefault(value, default(ExerciseLoadType)));

        builder.Entity<Adaptation>()
            .Property(adaptation => adaptation.Id)
            .ValueGeneratedNever();

        builder.Entity<Adaptation>()
            .HasIndex(adaptation => adaptation.Type)
            .IsUnique();

        builder.Entity<Adaptation>()
            .Property(adaptation => adaptation.Name)
            .HasMaxLength(100);

        builder.Entity<Adaptation>()
            .Property(adaptation => adaptation.Color)
            .HasMaxLength(16);

        builder.Entity<Adaptation>()
            .Property(adaptation => adaptation.Type)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, default(GalpinAdaptationType)));

        builder.Entity<ExerciseTypeAdaptation>()
            .HasOne(mapping => mapping.ExerciseType)
            .WithMany()
            .HasForeignKey(mapping => mapping.ExerciseTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ExerciseTypeAdaptation>()
            .HasOne(mapping => mapping.Adaptation)
            .WithMany(adaptation => adaptation.ExerciseTypeAdaptations)
            .HasForeignKey(mapping => mapping.AdaptationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ExerciseTypeAdaptation>()
            .HasIndex(mapping => new { mapping.ExerciseTypeId, mapping.AdaptationId })
            .IsUnique();

        builder.Entity<ExerciseTypeAdaptation>()
            .Property(mapping => mapping.Role)
            .HasConversion(
                value => value.ToString(),
                value => ParseEnumOrDefault(value, default(AdaptationRole)));

        builder.Entity<ExerciseAdaptation>()
            .HasQueryFilter(adaptation => adaptation.Exercise.Workout.User.Id == _userProvider.GetCurrentUserId());

        builder.Entity<ExerciseAdaptation>()
            .HasOne(adaptation => adaptation.Exercise)
            .WithMany(exercise => exercise.Adaptations)
            .HasForeignKey(adaptation => adaptation.ExerciseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ExerciseAdaptation>()
            .HasOne(adaptation => adaptation.Adaptation)
            .WithMany()
            .HasForeignKey(adaptation => adaptation.AdaptationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ExerciseAdaptation>()
            .HasIndex(adaptation => new { adaptation.ExerciseId, adaptation.AdaptationId })
            .IsUnique();

        base.OnModelCreating(builder);
    }

    private static TEnum ParseEnumOrDefault<TEnum>(string value, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;

    private static string SerializeExerciseTags(List<ExerciseTag> tags)
        => JsonSerializer.Serialize(CloneExerciseTags(tags), (JsonSerializerOptions)null);

    private static List<ExerciseTag> DeserializeExerciseTags(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return CloneExerciseTags(JsonSerializer.Deserialize<List<ExerciseTag>>(value, (JsonSerializerOptions)null));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool ExerciseTagsEqual(List<ExerciseTag> left, List<ExerciseTag> right)
    {
        var normalizedLeft = CloneExerciseTags(left);
        var normalizedRight = CloneExerciseTags(right);

        if (normalizedLeft.Count != normalizedRight.Count)
        {
            return false;
        }

        for (var index = 0; index < normalizedLeft.Count; index++)
        {
            if (!string.Equals(normalizedLeft[index].Name, normalizedRight[index].Name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(normalizedLeft[index].Category, normalizedRight[index].Category, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetExerciseTagsHashCode(List<ExerciseTag> tags)
        => CloneExerciseTags(tags)
            .Aggregate(0, (hash, tag) => HashCode.Combine(
                hash,
                StringComparer.OrdinalIgnoreCase.GetHashCode(tag.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(tag.Category)));

    private static List<ExerciseTag> CloneExerciseTags(List<ExerciseTag> tags)
        => (tags ?? [])
            .Where(tag => tag is not null)
            .Select(tag => new ExerciseTag
            {
                Name = tag.Name ?? string.Empty,
                Category = tag.Category ?? string.Empty
            })
            .ToList();

    private static string SerializeTrainingTags(List<string> tags)
        => JsonSerializer.Serialize(CloneTrainingTags(tags), (JsonSerializerOptions)null);

    private static List<string> DeserializeTrainingTags(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return CloneTrainingTags(
                JsonSerializer.Deserialize<List<string>>(
                    value,
                    (JsonSerializerOptions)null));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TrainingTagsEqual(
        List<string> left,
        List<string> right)
        => CloneTrainingTags(left)
            .SequenceEqual(
                CloneTrainingTags(right),
                StringComparer.OrdinalIgnoreCase);

    private static int GetTrainingTagsHashCode(List<string> tags)
        => CloneTrainingTags(tags)
            .Aggregate(
                17,
                (hash, tag) =>
                    HashCode.Combine(
                        hash,
                        StringComparer.OrdinalIgnoreCase.GetHashCode(tag)));

    private static List<string> CloneTrainingTags(IEnumerable<string> tags)
        => ExerciseTagNormalizer.Normalize(tags);

    private static readonly JsonSerializerOptions AdaptationGoalJsonOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static string SerializeAdaptationGoals(List<GalpinAdaptationType> goals)
        => JsonSerializer.Serialize(CloneAdaptationGoals(goals), AdaptationGoalJsonOptions);

    private static List<GalpinAdaptationType> DeserializeAdaptationGoals(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return CloneAdaptationGoals(
                JsonSerializer.Deserialize<List<GalpinAdaptationType>>(value, AdaptationGoalJsonOptions));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<GalpinAdaptationType> CloneAdaptationGoals(List<GalpinAdaptationType> goals)
        => (goals ?? []).Distinct().ToList();

    private static bool AdaptationGoalsEqual(
        List<GalpinAdaptationType> left,
        List<GalpinAdaptationType> right)
        => CloneAdaptationGoals(left).OrderBy(goal => goal).SequenceEqual(CloneAdaptationGoals(right).OrderBy(goal => goal));

    private static int GetAdaptationGoalsHashCode(List<GalpinAdaptationType> goals)
        => CloneAdaptationGoals(goals)
            .OrderBy(goal => goal)
            .Aggregate(17, (hash, goal) => HashCode.Combine(hash, goal));
}
