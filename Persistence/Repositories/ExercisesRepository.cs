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
        var existingExercise = GetExercise(exercise.Id);
        var updatedExercise = existingExercise.Adapt<Exercise>();

        _context.Exercises.Update(updatedExercise);
        _context.SaveChanges();
    }
}