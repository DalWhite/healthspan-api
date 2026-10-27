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
    /// Paged flat projection for list screens. It is not a narrowed "include":
    /// the query selects only the columns the library and the exercise pickers
    /// read (no nested Variations copy), resolves favourites with an EXISTS, and
    /// pages in SQL. The whole catalog is 1.4MB / 549KB slim, and parsing that much
    /// JSON in WASM blocked the main thread for seconds before the library could
    /// paint (t_81a400b3), so the client asks for one page at a time and hydrates
    /// the rest in the background.
    /// <paramref name="includeDescription"/> adds the editorial Description column
    /// for that background hydration: it is what search-by-description reads, and
    /// it is the copy the offline mirror has to keep.
    /// </summary>
    public async Task<(List<ExerciseType> Items, int Total)> GetExerciseListPageAsync(
        string userId, bool? favourite, int skip, int take, bool includeDescription)
    {
        var source = _context.ExerciseTypes.AsNoTracking();

        if (favourite == true)
        {
            source = source.Where(et => et.FavoritedUsers.Any(user => user.Id == userId));
        }
        else if (favourite == false)
        {
            source = source.Where(et => !et.FavoritedUsers.Any(user => user.Id == userId));
        }

        var total = await source.CountAsync();

        var items = await source
            .OrderBy(et => et.Name)
            .ThenBy(et => et.Id)
            .Skip(skip)
            .Take(take)
            .Select(et => new ExerciseType
            {
                Id = et.Id,
                Name = et.Name,
                Description = includeDescription ? et.Description : null,
                ImageRoute = et.ImageRoute,
                WeightType = et.WeightType,
                ParentExerciseTypeId = et.ParentExerciseTypeId,
                Tags = et.Tags,
                FavoritedUsers = et.FavoritedUsers
                    .Where(user => user.Id == userId)
                    .Select(user => new User { Id = user.Id })
                    .ToList(),
                MuscleAssignments = et.MuscleAssignments
                    .OrderBy(assignment => assignment.Role)
                    .ThenBy(assignment => assignment.SortOrder)
                    .ThenBy(assignment => assignment.MuscleId)
                    .Select(assignment => new ExerciseTypeMuscleAssignment
                    {
                        MuscleId = assignment.MuscleId,
                        Role = assignment.Role,
                        SortOrder = assignment.SortOrder
                    })
                    .ToList()
            })
            .ToListAsync();

        return (items, total);
    }

    /// <summary>
    /// Ids of exercise types (including variations) that the given user has
    /// logged in at least one workout.
    /// </summary>
    public HashSet<int> GetTrainedExerciseTypeIds(string userId)
    {
        return _context.Exercises
            .IgnoreQueryFilters()
            .Where(exercise => exercise.Workout.UserId == userId && exercise.TypeId != null)
            .Select(exercise => exercise.TypeId!.Value)
            .Distinct()
            .ToHashSet();
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
    
    public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
    {
        var normalizedName = name.Trim().ToLower();
        return await _context.ExerciseTypes
            .AnyAsync(et => et.Name.ToLower() == normalizedName
                && (excludeId == null || et.Id != excludeId.Value));
    }

    public async Task<ExerciseType> GetExerciseTypeAsync(int id)
    {
        return await QueryWithRelations()
            .FirstOrDefaultAsync(et => et.Id == id);
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

        if (_context.WorkoutTemplateExercises.Any(templateExercise => templateExercise.ExerciseTypeId == id))
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
