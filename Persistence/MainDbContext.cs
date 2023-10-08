using Microsoft.EntityFrameworkCore;

public class MainDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Workout> Workouts { get; set; }
    public DbSet<Exercise> Exercises { get; set; }
    public DbSet<ExerciseType> ExerciseTypes { get; set; }

    public MainDbContext(DbContextOptions<MainDbContext> options)
        : base(options)
    {
    }
}