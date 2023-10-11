using Mapster;

public class ExerciseTypesRepository
{
    private readonly MainDbContext _context;
    public ExerciseTypesRepository(MainDbContext context)
    {
        _context = context;
    }
    public void AddExerciseType(ExerciseType exerciseType)
    {
        _context.ExerciseTypes.Add(exerciseType);
        _context.SaveChanges();   
    }
    public ExerciseType GetExerciseType(int Id)
    {
        return _context.ExerciseTypes.Find(Id);
    }
    public void UpdateExerciseType(ExerciseType exerciseType)
    {
        var existingEntity = GetExerciseType(exerciseType.Id);
        
        if (exerciseType.Name is not null)
        {
            existingEntity.Name = exerciseType.Name;
        }
        if (exerciseType.Description is not null)
        {
            existingEntity.Description = exerciseType.Description;
        }
        if (exerciseType.MuscleGroup is not null)
        {
            existingEntity.MuscleGroup = exerciseType.MuscleGroup;
        }

        _context.SaveChanges();
    }
}