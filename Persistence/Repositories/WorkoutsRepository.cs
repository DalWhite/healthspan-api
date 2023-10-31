public class WorkoutsRepository
{
    private readonly MainDbContext _context;
    public WorkoutsRepository(MainDbContext context)
    {
        _context = context;
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