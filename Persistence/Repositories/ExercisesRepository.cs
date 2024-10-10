using Mapster;
using Microsoft.EntityFrameworkCore;
using Services;

public class ExercisesRepository
{
    private readonly MainDbContext _context;
    public ExercisesRepository(MainDbContext context)
    {
        _context = context;
    }
    public Guid AddExercise(Exercise exercise, string userId)
    {
        CalculateComputedValues(exercise, userId);

        _context.Exercises.Add(exercise);
        _context.SaveChanges();

        return exercise.Id;
    }
    public void DeleteExercise(Guid id)
    {
        var exerciseToDelete = _context.Exercises.FirstOrDefault(e => e.Id == id);
        _context.Remove(exerciseToDelete);
        _context.SaveChanges();
    }

    public Exercise GetExercise(Guid Id)
    {
        return _context.Exercises
            .Include(e => e.Type)
            .Include(e => e.Workout)
            .IgnoreQueryFilters()
            .FirstOrDefault(e => e.Id == Id);
    }
    public List<Exercise> GetExercises(string userId)
    {
        var query = _context.Exercises
            .Include(e => e.Type)
            .Include(e => e.Workout)
            .AsQueryable();

        if(!string.IsNullOrEmpty(userId))
        {
            query = query
                .IgnoreQueryFilters()
                .Where(e => e.Workout.User.Id == userId);
        }

        return query.ToList();
    }
    public List<Exercise> GetExercisesByType(int exerciseTypeId, string userId)
    {
        return _context.Exercises
            .IgnoreQueryFilters()
            .Where(e => e.Workout.UserId == userId)
            .Where(e => e.TypeId == exerciseTypeId)
            .Include(e => e.Type)
            .Include(e => e.Workout)
            .ToList();
    }
    public void UpdateExercise(Exercise exercise, string userId)
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
        //FIX: this is wrong. Should try get a workout on that date. If there is one, swap workout. If not, create one.
        existingEntity.Workout.Date = exercise.Workout.Date;
        CalculateComputedValues(exercise,userId);
        _context.SaveChanges();
    }
    public void CalculateComputedValues(Exercise exercise, string userId)
    {
        var raw1RMCalculation = exercise.Weight * (1 + 0.0333 * exercise.Repetitions);
        exercise.MaxRepetition = Math.Round((double)raw1RMCalculation,1);
        exercise.Volume = (int?)(exercise.Repetitions * exercise.Series * exercise.Weight);

        double? historicMaxRepetition = exercise.MaxRepetition;
        
        var exercisesOfType = GetExercisesByType((int)exercise.TypeId, userId);
        
        if (exercisesOfType.Any())
        {
            historicMaxRepetition = exercisesOfType
                .OrderByDescending(e => e.MaxRepetition)
                .Select(e => e.MaxRepetition)
                .First();
        }
        if(exercise.Weight != 0)
        {
            exercise.Intensity = (int?)(exercise.Weight / historicMaxRepetition * 100);
        }
        else
        {
            exercise.Intensity = 0;
        }
    }
}