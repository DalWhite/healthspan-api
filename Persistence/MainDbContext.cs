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

        builder.Entity<Muscle>()
            .Property(e => e.Side).HasConversion(v => v.ToString(), v => (MuscleSide)Enum.Parse(typeof(MuscleSide), v));
        
        builder.Entity<Muscle>()
            .Property(e => e.MuscleGroup).HasConversion(v => v.ToString(), v => (MuscleGroup)Enum.Parse(typeof(MuscleGroup), v));

        builder.Entity<HealthProfile>()
            .HasKey(profile => profile.UserId);

        builder.Entity<HealthProfile>()
            .HasOne(profile => profile.User)
            .WithOne(user => user.HealthProfile)
            .HasForeignKey<HealthProfile>(profile => profile.UserId);

        builder.Entity<HealthProfile>()
            .Property(profile => profile.BiologicalSex)
            .HasConversion(v => v.ToString(), v => (BiologicalSex)Enum.Parse(typeof(BiologicalSex), v));

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
                value => string.IsNullOrWhiteSpace(value) ? null : (BiologicalSex)Enum.Parse(typeof(BiologicalSex), value));

        builder.Entity<BloodTest>()
            .Property(test => test.ProcessingStatus)
            .HasConversion(v => v.ToString(), v => (BloodTestProcessingStatus)Enum.Parse(typeof(BloodTestProcessingStatus), v));

        builder.Entity<BloodTest>()
            .HasIndex(test => test.SourceBlobName);

        builder.Entity<BloodTest>()
            .HasMany(test => test.MarkerResults)
            .WithOne(result => result.BloodTest)
            .HasForeignKey(result => result.BloodTestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<BloodTestMarkerResult>()
            .Property(result => result.Status)
            .HasConversion(v => v.ToString(), v => (MarkerStatus)Enum.Parse(typeof(MarkerStatus), v));

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
            .HasConversion(v => v.ToString(), v => (GalpinAdaptationType)Enum.Parse(typeof(GalpinAdaptationType), v));

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
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<List<ExerciseTag>>(v, (JsonSerializerOptions)null) ?? new List<ExerciseTag>()
            )
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<ExerciseTag>>(
                (c1, c2) => c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()
            ));

        base.OnModelCreating(builder);
    }
}