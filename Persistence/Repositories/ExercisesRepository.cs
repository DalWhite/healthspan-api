using Mapster;

public class ExercisesRepository
{
    private readonly MainDbContext _context;
    public ExercisesRepository(MainDbContext context)
    {
        _context = context;
    }
    public void AddExercise(Exercise exercise)
    {
        _context.Exercises.Add(exercise);
        _context.SaveChanges();   
    }
    public Exercise GetExercise(Guid Id)
    {
        return _context.Exercises.Find(Id);
    }
    public void UpdateExercise(Exercise exercise)
    {
        var existingEntity = GetExercise(exercise.Id);
        if (exercise.Series is not null)
        {
            existingEntity.Series = exercise.Series;
        }
        if (exercise.Repetitions is not null)
        {
            existingEntity.Repetitions = exercise.Repetitions;
        }
        if (exercise.Weight is not null)
        {
            existingEntity.Weight = exercise.Weight;
        }
        if (exercise.TypeId is not null)
        {
            existingEntity.TypeId = exercise.TypeId;
        }
        _context.SaveChanges();
    }
}