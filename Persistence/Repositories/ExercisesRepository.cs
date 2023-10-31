using Mapster;
using Microsoft.EntityFrameworkCore;

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
    public List<Exercise> GetExercises()
    {
        return _context.Exercises
            .Include(e => e.Type)
            .Include(e => e.Workout)
            .ToList();
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
        if (exercise.Type is not null)
        {
            existingEntity.Type = exercise.Type;
        }
        _context.SaveChanges();
    }
}