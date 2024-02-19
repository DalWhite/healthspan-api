public class ExerciseTypesRepository
{
    private readonly MainDbContext _context;
    public ExerciseTypesRepository(MainDbContext context)
    {
        _context = context;
    }
    public int AddExerciseType(ExerciseType exerciseType)
    {
        _context.ExerciseTypes.Add(exerciseType);
        _context.SaveChanges();
        return exerciseType.Id;
    }
    public ExerciseType GetExerciseType(int Id)
    {
        return _context.ExerciseTypes.Find(Id);
    }
    public List<ExerciseType> GetExerciseTypes()
    {
        return _context.ExerciseTypes.ToList();
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
        if (exerciseType.ImageRoute is not null)
        {
            existingEntity.ImageRoute = exerciseType.ImageRoute;
        }
        if (exerciseType.PrimaryMuscleId is not null)
        {
            existingEntity.PrimaryMuscleId = exerciseType.PrimaryMuscleId;
        }
        if (exerciseType.SecondaryMuscleId is not null)
        {
            existingEntity.SecondaryMuscleId = exerciseType.SecondaryMuscleId;
        }

        _context.SaveChanges();
    }
}