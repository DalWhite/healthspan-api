using Microsoft.AspNetCore.SignalR;
using Services;

public class WorkoutsRepository
{
    private readonly MainDbContext _context;
    private readonly UserProvider _userProvider;
    public WorkoutsRepository(MainDbContext context, UserProvider userProvider)
    {
        _context = context;
        _userProvider = userProvider;
    }
    public Guid AddWorkout(Workout workout)
    {
        workout.UserId = _userProvider.GetUserId();

        _context.Workouts.Add(workout);
        _context.SaveChanges();
        return workout.Id;
    }
    public Workout GetWorkout(Guid Id)
    {
        return _context.Workouts.Find(Id);
    }
    public List<Workout> GetWorkouts()
    {
        return _context.Workouts.ToList();
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