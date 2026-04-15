using Microsoft.EntityFrameworkCore;

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

    public async Task<int> AddExerciseTypeAsync(ExerciseType exerciseType)
    {
        _context.ExerciseTypes.Add(exerciseType);
        await _context.SaveChangesAsync();
        return exerciseType.Id;
    }
    public ExerciseType GetExerciseType(int Id)
    {
        return _context.ExerciseTypes
            .Include(et => et.FavoritedUsers)
            .Include(et => et.Variations)
            .FirstOrDefault(et => et.Id == Id);
    }
    public List<ExerciseType> GetExerciseTypes()
    {
        return _context.ExerciseTypes
            .Include(et => et.FavoritedUsers)
            .Include(et => et.Variations)
            .ToList();
    }
    
    /// <summary>
    /// Gets exercise types filtered by equipment tag
    /// </summary>
    public List<ExerciseType> GetExerciseTypesByEquipment(string equipment)
    {
        return _context.ExerciseTypes
            .Where(et => et.Tags.Any(t => 
                t.Category == "equipment" && 
                t.Name.ToLower().Contains(equipment.ToLower())))
            .Include(et => et.FavoritedUsers)
            .ToList();
    }
    
    /// <summary>
    /// Gets exercise types filtered by difficulty level tag
    /// </summary>
    public List<ExerciseType> GetExerciseTypesByLevel(string level)
    {
        return _context.ExerciseTypes
            .Where(et => et.Tags.Any(t => 
                t.Category == "level" && 
                t.Name.ToLower() == level.ToLower()))
            .Include(et => et.FavoritedUsers)
            .ToList();
    }
    
    /// <summary>
    /// Gets exercise types filtered by mechanic tag
    /// </summary>
    public List<ExerciseType> GetExerciseTypesByMechanic(string mechanic)
    {
        return _context.ExerciseTypes
            .Where(et => et.Tags.Any(t => 
                t.Category == "mechanic" && 
                t.Name.ToLower() == mechanic.ToLower()))
            .Include(et => et.FavoritedUsers)
            .ToList();
    }
    
    /// <summary>
    /// Gets exercise types by multiple filters
    /// </summary>
    public List<ExerciseType> GetExerciseTypesFiltered(string searchTerm = null, string equipment = null, string level = null)
    {
        var query = _context.ExerciseTypes.Include(et => et.FavoritedUsers).AsQueryable();
        
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var lowerSearch = searchTerm.ToLower();
            query = query.Where(et => 
                et.Name.ToLower().Contains(lowerSearch) || 
                et.Description.ToLower().Contains(lowerSearch));
        }
        
        if (!string.IsNullOrWhiteSpace(equipment))
        {
            query = query.Where(et => et.Tags.Any(t => 
                t.Category == "equipment" && 
                t.Name.ToLower().Contains(equipment.ToLower())));
        }
        
        if (!string.IsNullOrWhiteSpace(level))
        {
            query = query.Where(et => et.Tags.Any(t => 
                t.Category == "level" && 
                t.Name.ToLower() == level.ToLower()));
        }
        
        return query.ToList();
    }
    
    public List<ExerciseType> GetExerciseTypesByIds(List<int> exerciseTypeIds)
    {
        return _context.ExerciseTypes
            .Where(et => exerciseTypeIds.Contains(et.Id))
            .ToList();
    }
    
    public async Task<ExerciseType> GetByNameAsync(string name)
    {
        return await _context.ExerciseTypes
            .FirstOrDefaultAsync(et => et.Name.ToLower() == name.ToLower());
    }
    
    /// <summary>
    /// Gets only main exercises (exercises without a parent)
    /// </summary>
    public List<ExerciseType> GetMainExercises()
    {
        return _context.ExerciseTypes
            .Where(et => et.ParentExerciseTypeId == null)
            .Include(et => et.FavoritedUsers)
            .Include(et => et.Variations)
            .ToList();
    }
    
    /// <summary>
    /// Gets variations for a specific main exercise
    /// </summary>
    public List<ExerciseType> GetVariationsByParentId(int parentExerciseTypeId)
    {
        return _context.ExerciseTypes
            .Where(et => et.ParentExerciseTypeId == parentExerciseTypeId)
            .Include(et => et.FavoritedUsers)
            .ToList();
    }
    
    /// <summary>
    /// Gets a main exercise with all its variations
    /// </summary>
    public async Task<ExerciseType> GetMainExerciseWithVariationsAsync(int mainExerciseId)
    {
        return await _context.ExerciseTypes
            .Where(et => et.Id == mainExerciseId && et.ParentExerciseTypeId == null)
            .Include(et => et.Variations)
            .Include(et => et.FavoritedUsers)
            .FirstOrDefaultAsync();
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
        if (exerciseType.ParentExerciseTypeId != existingEntity.ParentExerciseTypeId)
        {
            existingEntity.ParentExerciseTypeId = exerciseType.ParentExerciseTypeId;
        }
        if (exerciseType.WeightType is not null)
        {
            existingEntity.WeightType = exerciseType.WeightType;
        }
        if (exerciseType.Tags is not null)
        {
            existingEntity.Tags = exerciseType.Tags;
        }
        existingEntity.FavoritedUsers = exerciseType.FavoritedUsers;

        _context.SaveChanges();
    }

    public async Task<bool> UpdateExerciseTypeAsync(ExerciseType exerciseType)
    {
        var existingEntity = await GetByNameAsync(exerciseType.Name);
        if (existingEntity == null)
        {
            return false;
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
        if (exerciseType.ParentExerciseTypeId != existingEntity.ParentExerciseTypeId)
        {
            existingEntity.ParentExerciseTypeId = exerciseType.ParentExerciseTypeId;
        }
        if (exerciseType.Tags is not null)
        {
            existingEntity.Tags = exerciseType.Tags;
        }

        await _context.SaveChangesAsync();
        return true;
    }
}
