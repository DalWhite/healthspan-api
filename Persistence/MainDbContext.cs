using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Services;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

public class MainDbContext : IdentityDbContext<User, Role, string>
{
    public DbSet<Workout> Workouts { get; set; }
    public DbSet<Exercise> Exercises { get; set; }
    public DbSet<ExerciseType> ExerciseTypes { get; set; }
    public DbSet<Muscle> Muscles { get; set; }
    public DbSet<Routine> Routines { get; set; }
    public DbSet<WorkoutTemplate> WorkoutTemplate { get; set; }
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

        builder.Entity<Muscle>()
            .Property(e => e.Side).HasConversion(v => v.ToString(), v => (MuscleSide)Enum.Parse(typeof(MuscleSide), v));
        
        builder.Entity<Muscle>()
            .Property(e => e.MuscleGroup).HasConversion(v => v.ToString(), v => (MuscleGroup)Enum.Parse(typeof(MuscleGroup), v));

        base.OnModelCreating(builder);
    }
}