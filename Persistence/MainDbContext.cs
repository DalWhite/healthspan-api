using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Services;

public class MainDbContext : IdentityDbContext<User>
{
    public DbSet<Workout> Workouts { get; set; }
    public DbSet<Exercise> Exercises { get; set; }
    public DbSet<ExerciseType> ExerciseTypes { get; set; }
    private UserProvider _userProvider;

    public MainDbContext(DbContextOptions<MainDbContext> options, UserProvider userProvider)
        : base(options)
    {
        _userProvider = userProvider;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Workout>().HasQueryFilter(w => w.User.Id == _userProvider.GetUserId()); 
        builder.Entity<Exercise>().HasQueryFilter(e => e.Workout.User.Id == _userProvider.GetUserId());
        
        base.OnModelCreating(builder);
    }
}