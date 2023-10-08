using Mapster;

public class WorkoutsRepository
{
    private readonly MainDbContext _context;
    public WorkoutsRepository(MainDbContext context)
    {
        _context = context;
    }
    public void AddWorkout(Workout workout)
    {
        _context.Workouts.Add(workout);
        _context.SaveChanges();   
    }
    public Workout GetWorkout(Guid Id)
    {
        return _context.Workouts.Find(Id);
    }
    public void UpdateWorkout(Workout workout)
    {
        var existingWorkout = GetWorkout(workout.Id);
        var updatedWorkout = existingWorkout.Adapt<Workout>();

        _context.Workouts.Update(existingWorkout);
        _context.SaveChanges();
    }
}