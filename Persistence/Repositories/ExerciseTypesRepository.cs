using Microsoft.EntityFrameworkCore;

public class ExerciseTypesRepository
{
    private readonly MainDbContext _context;
    private IQueryable<ExerciseType> QueryWithRelations()
        => _context.ExerciseTypes
            .AsSplitQuery()
            .Include(et => et.FavoritedUsers)
            .Include(et => et.MuscleAssignments)
                .ThenInclude(assignment => assignment.Muscle)
            .Include(et => et.Variations)
                .ThenInclude(variation => variation.MuscleAssignments)
                    .ThenInclude(assignment => assignment.Muscle)
            .Include(et => et.Variations)
                .ThenInclude(variation => variation.FavoritedUsers);

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
        return QueryWithRelations()
            .FirstOrDefault(et => et.Id == Id);
    }
    public List<ExerciseType> GetExerciseTypes()
    {
        return QueryWithRelations()
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
            .Include(et => et.MuscleAssignments)
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
            .Include(et => et.MuscleAssignments)
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
            .Include(et => et.MuscleAssignments)
            .ToList();
    }
    
    /// <summary>
    /// Gets exercise types by multiple filters
    /// </summary>
    public List<ExerciseType> GetExerciseTypesFiltered(string searchTerm = null, string equipment = null, string level = null)
    {
        var query = _context.ExerciseTypes
            .Include(et => et.FavoritedUsers)
            .Include(et => et.MuscleAssignments)
            .AsQueryable();
        
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
            .Include(et => et.MuscleAssignments)
            .ToList();
    }
    
    public async Task<ExerciseType> GetByNameAsync(string name)
    {
        return await QueryWithRelations()
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
            .Include(et => et.MuscleAssignments)
            .Include(et => et.Variations)
                .ThenInclude(variation => variation.MuscleAssignments)
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
            .Include(et => et.MuscleAssignments)
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
                .ThenInclude(variation => variation.MuscleAssignments)
            .Include(et => et.MuscleAssignments)
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
        if (exerciseType.MuscleAssignments is not null)
        {
            existingEntity.MuscleAssignments.Clear();

            foreach (var assignment in exerciseType.MuscleAssignments
                         .OrderBy(assignment => assignment.Role)
                         .ThenBy(assignment => assignment.SortOrder)
                         .ThenBy(assignment => assignment.MuscleId))
            {
                existingEntity.MuscleAssignments.Add(new ExerciseTypeMuscleAssignment
                {
                    ExerciseTypeId = existingEntity.Id,
                    MuscleId = assignment.MuscleId,
                    Role = assignment.Role,
                    SortOrder = assignment.SortOrder
                });
            }
        }
        existingEntity.FavoritedUsers = exerciseType.FavoritedUsers;

        _context.SaveChanges();
    }

    public bool SetFavourite(int exerciseTypeId, User user, bool isFavourite)
    {
        var exerciseType = _context.ExerciseTypes
            .Include(item => item.FavoritedUsers)
            .FirstOrDefault(item => item.Id == exerciseTypeId);

        if (exerciseType is null)
        {
            return false;
        }

        var existingFavourite = exerciseType.FavoritedUsers
            .FirstOrDefault(favoritedUser => favoritedUser.Id == user.Id);

        if (isFavourite && existingFavourite is null)
        {
            exerciseType.FavoritedUsers.Add(user);
        }
        else if (!isFavourite && existingFavourite is not null)
        {
            exerciseType.FavoritedUsers.Remove(existingFavourite);
        }

        _context.SaveChanges();
        return true;
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
        if (exerciseType.ParentExerciseTypeId != existingEntity.ParentExerciseTypeId)
        {
            existingEntity.ParentExerciseTypeId = exerciseType.ParentExerciseTypeId;
        }
        if (exerciseType.Tags is not null)
        {
            existingEntity.Tags = exerciseType.Tags;
        }
        if (exerciseType.MuscleAssignments is not null)
        {
            existingEntity.MuscleAssignments.Clear();

            foreach (var assignment in exerciseType.MuscleAssignments
                         .OrderBy(assignment => assignment.Role)
                         .ThenBy(assignment => assignment.SortOrder)
                         .ThenBy(assignment => assignment.MuscleId))
            {
                existingEntity.MuscleAssignments.Add(new ExerciseTypeMuscleAssignment
                {
                    ExerciseTypeId = existingEntity.Id,
                    MuscleId = assignment.MuscleId,
                    Role = assignment.Role,
                    SortOrder = assignment.SortOrder
                });
            }
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public DeleteExerciseTypeResult DeleteExerciseType(int id)
    {
        var existingEntity = QueryWithRelations()
            .FirstOrDefault(et => et.Id == id);

        if (existingEntity is null)
        {
            return DeleteExerciseTypeResult.NotFound;
        }

        if (existingEntity.Variations.Any())
        {
            return DeleteExerciseTypeResult.HasVariations;
        }

        if (_context.Exercises.Any(exercise => exercise.TypeId == id))
        {
            return DeleteExerciseTypeResult.InUseByExercises;
        }

        if (_context.WorkoutTemplate.Any(template => template.ExerciseTypes.Any(exerciseType => exerciseType.Id == id)))
        {
            return DeleteExerciseTypeResult.InUseByWorkoutTemplates;
        }

        _context.ExerciseTypes.Remove(existingEntity);
        _context.SaveChanges();
        return DeleteExerciseTypeResult.Deleted;
    }
}

public enum DeleteExerciseTypeResult
{
    Deleted,
    NotFound,
    HasVariations,
    InUseByExercises,
    InUseByWorkoutTemplates
}
