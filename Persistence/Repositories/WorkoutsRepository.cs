using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Services;

public class WorkoutsRepository
{
    private readonly MainDbContext _context;
    private readonly CurrentUserProvider _userProvider;
    public WorkoutsRepository(MainDbContext context, CurrentUserProvider userProvider)
    {
        _context = context;
        _userProvider = userProvider;
    }
    public Guid AddWorkout(Workout workout)
    {
        _context.Workouts.Add(workout);
        _context.SaveChanges();
        return workout.Id;
    }
    public Workout GetWorkout(Guid Id)
    {
        return _context.Workouts.Find(Id);
    }
    public List<Workout> GetWorkouts(string userId)
    {
        return _context.Workouts
        .IgnoreQueryFilters()
        .Where(w => w.UserId == userId)
        .ToList();
    }
    public void UpdateWorkout(Workout workout)
    {
        var existingEntity = GetWorkout(workout.Id);
        if (workout.Date is not null)
        {
            existingEntity.Date = workout.Date;
        }
        _context.SaveChanges();
    }
}