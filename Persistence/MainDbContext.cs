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
    public DbSet<HealthProfile> HealthProfiles { get; set; }
    public DbSet<HealthMetricDefinition> HealthMetricDefinitions { get; set; }
    public DbSet<ReferenceRange> ReferenceRanges { get; set; }
    public DbSet<BloodTest> BloodTests { get; set; }
    public DbSet<BloodTestMarkerResult> BloodTestMarkerResults { get; set; }
    public DbSet<AnalyticsPermission> AnalyticsPermissions { get; set; }
    public DbSet<GalpinAssessment> GalpinAssessments { get; set; }
    public DbSet<GalpinAssessmentScore> GalpinAssessmentScores { get; set; }
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
}
