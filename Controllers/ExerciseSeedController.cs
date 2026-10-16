using System.Text.Json;
using Controllers.ApiContracts.ExerciseTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
//[Authorize(Roles = "Admin")]
public class ExerciseSeedController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ExerciseTypesRepository _exerciseTypeRepository;
    private readonly MuscleRepository _muscleRepository;
    private readonly MainDbContext _context;
    private readonly ILogger<ExerciseSeedController> _logger;

    public ExerciseSeedController(
        IHttpClientFactory httpClientFactory,
        ExerciseTypesRepository exerciseTypeRepository,
        MuscleRepository muscleRepository,
        MainDbContext context,
        ILogger<ExerciseSeedController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _exerciseTypeRepository = exerciseTypeRepository;
        _muscleRepository = muscleRepository;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Seeds the database with exercises from the free-exercise-db repository
    /// </summary>
    /// <returns>Result with count of successfully added exercises</returns>
    [HttpPost("seed-from-db")]
    public async Task<IActionResult> SeedFromFreeExerciseDb()
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(
                "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/dist/exercises.json");

            if (!response.IsSuccessStatusCode)
            {
                return BadRequest(new { error = "Failed to fetch exercise data from remote source" });
            }

            var json = await response.Content.ReadAsStringAsync();
            var exercises = JsonSerializer.Deserialize<List<ExternalExerciseDto>>(json, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            int addedCount = 0;
            int updatedCount = 0;
            var errors = new List<string>();

            foreach (var exercise in exercises ?? new())
            {
                try
                {
                    var tags = BuildTagsFromExercise(exercise);
                    var assignmentResult = await ResolveAssignmentsAsync(
                        exercise.PrimaryMuscles,
                        exercise.SecondaryMuscles);
                    var imageRoute = BuildImageUrl(exercise);

                    // Check if exercise already exists
                    var existing = await _exerciseTypeRepository.GetByNameAsync(exercise.Name);
                    
                    var exerciseData = new ExerciseType
                    {
                        Name = exercise.Name,
                        Description = exercise.Instructions != null ? string.Join("; ", exercise.Instructions) : "",
                        ImageRoute = imageRoute,
                        Tags = tags,
                        DifficultyLevel = 1,
                        MuscleAssignments = assignmentResult.Assignments
                    };

                    if (existing != null)
                    {
                        // Update existing exercise
                        bool updated = await _exerciseTypeRepository.UpdateExerciseTypeAsync(exerciseData);
                        if (updated)
                        {
                            updatedCount++;
                        }
                    }
                    else
                    {
                        // Add new exercise
                        await _exerciseTypeRepository.AddExerciseTypeAsync(exerciseData);
                        addedCount++;
                    }

                    foreach (var unmappedMuscle in assignmentResult.UnmappedMuscles)
                    {
                        errors.Add($"{exercise.Name}: unmapped external muscle '{unmappedMuscle}'");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to process exercise {exercise.Name}: {ex.Message}");
                    errors.Add($"{exercise.Name}: {ex.Message}");
                }
            }

            return Ok(new 
            { 
                message = "Seed completed successfully",
                addedCount,
                updatedCount,
                totalProcessed = exercises?.Count ?? 0,
                errors = errors.Count > 0 ? errors : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Seeding failed: {ex.Message}");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Applies curated variant families (SeedData/exercise-variant-families.json) by setting
    /// ParentExerciseTypeId on each variant, and backfills WeightType from tags
    /// (body only -> BodyWeight, stretching -> None, otherwise Standard).
    /// Idempotent: safe to run multiple times.
    /// </summary>
    [HttpPost("seed-variants")]
    public async Task<IActionResult> SeedVariants()
    {
        var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "SeedData", "exercise-variant-families.json");
        if (!System.IO.File.Exists(jsonPath))
        {
            return BadRequest(new { error = $"Variant families file not found: {jsonPath}" });
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var families = JsonSerializer.Deserialize<VariantFamiliesFile>(
            await System.IO.File.ReadAllTextAsync(jsonPath), options)?.Families
            ?? new List<VariantFamily>();

        var byName = await _context.ExerciseTypes
            .Select(et => new { et.Id, et.Name })
            .ToListAsync();
        var lookup = byName
            .GroupBy(x => x.Name.ToLowerInvariant().Trim())
            .ToDictionary(g => g.Key, g => g.First().Id);

        int linked = 0, missing = 0;
        var missingNames = new List<string>();

        foreach (var family in families)
        {
            if (!lookup.TryGetValue(family.Parent.ToLowerInvariant().Trim(), out var parentId))
            {
                missing++;
                missingNames.Add(family.Parent);
                continue;
            }

            foreach (var variantName in family.Variants)
            {
                var key = variantName.ToLowerInvariant().Trim();
                if (!lookup.TryGetValue(key, out var variantId) || variantId == parentId)
                {
                    missing++;
                    missingNames.Add(variantName);
                    continue;
                }

                var variant = await _context.ExerciseTypes.FindAsync(variantId);
                if (variant != null && variant.ParentExerciseTypeId != parentId)
                {
                    variant.ParentExerciseTypeId = parentId;
                    linked++;
                }
            }
        }

        // Backfill WeightType from tags: body only -> BodyWeight, stretching -> None, else Standard
        var weightUpdated = 0;
        var all = await _context.ExerciseTypes
            .Select(et => new { et.Id, et.Tags })
            .ToListAsync();
        foreach (var row in all)
        {
            var equipment = row.Tags?.FirstOrDefault(t => t.Category == "equipment")?.Name ?? "";
            var type = row.Tags?.FirstOrDefault(t => t.Category == "type")?.Name ?? "";
            var weightType = equipment.Equals("body only", StringComparison.OrdinalIgnoreCase)
                ? "BodyWeight"
                : type.Equals("stretching", StringComparison.OrdinalIgnoreCase)
                    ? "None"
                    : "Standard";

            var entity = await _context.ExerciseTypes.FindAsync(row.Id);
            if (entity != null && entity.WeightType != weightType)
            {
                entity.WeightType = weightType;
                weightUpdated++;
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Variant seed completed",
            families = families.Count,
            linked,
            weightTypeUpdated = weightUpdated,
            missing = missing > 0 ? missingNames : null
        });
    }

    private sealed record VariantFamiliesFile(List<VariantFamily> Families);

    private sealed class VariantFamily
    {
        public string Parent { get; set; } = string.Empty;
        public List<string> Variants { get; set; } = new();
    }

    /// <summary>
    /// Builds exercise tags from external exercise data
    /// </summary>
    private List<ExerciseTag> BuildTagsFromExercise(ExternalExerciseDto exercise)
    {
        var tags = new List<ExerciseTag>();

        if (!string.IsNullOrEmpty(exercise.Equipment))
        {
            tags.Add(new ExerciseTag 
            { 
                Name = exercise.Equipment, 
                Category = "equipment" 
            });
        }

        if (!string.IsNullOrEmpty(exercise.Mechanic))
        {
            tags.Add(new ExerciseTag 
            { 
                Name = exercise.Mechanic, 
                Category = "mechanic" 
            });
        }

        if (!string.IsNullOrEmpty(exercise.Level))
        {
            tags.Add(new ExerciseTag 
            { 
                Name = exercise.Level, 
                Category = "level" 
            });
        }

        if (!string.IsNullOrEmpty(exercise.Force))
        {
            tags.Add(new ExerciseTag 
            { 
                Name = exercise.Force, 
                Category = "force" 
            });
        }

        if (!string.IsNullOrEmpty(exercise.Category))
        {
            tags.Add(new ExerciseTag 
            { 
                Name = exercise.Category, 
                Category = "type" 
            });
        }

        return tags;
    }

    /// <summary>
    /// Resolves external muscle lists to canonical primary/secondary assignments.
    /// </summary>
    private async Task<AssignmentResolutionResult> ResolveAssignmentsAsync(
        IEnumerable<string>? primaryMuscles,
        IEnumerable<string>? secondaryMuscles)
    {
        var assignments = new List<ExerciseTypeMuscleAssignment>();
        var unmappedMuscles = new List<string>();
        var seenMuscleIds = new HashSet<int>();

        await AddAssignmentsAsync(primaryMuscles, ExerciseMuscleRole.Primary, assignments, seenMuscleIds, unmappedMuscles);
        await AddAssignmentsAsync(secondaryMuscles, ExerciseMuscleRole.Secondary, assignments, seenMuscleIds, unmappedMuscles);

        return new AssignmentResolutionResult(assignments, unmappedMuscles);
    }

    private async Task AddAssignmentsAsync(
        IEnumerable<string>? muscleNames,
        ExerciseMuscleRole role,
        ICollection<ExerciseTypeMuscleAssignment> assignments,
        ISet<int> seenMuscleIds,
        ICollection<string> unmappedMuscles)
    {
        var sortOrder = assignments.Count(assignment => assignment.Role == role) + 1;

        foreach (var muscleName in muscleNames ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(muscleName))
            {
                continue;
            }

            var resolvedSlug = MuscleCatalogSeeder.TryResolveSlug(muscleName);
            if (string.IsNullOrWhiteSpace(resolvedSlug))
            {
                unmappedMuscles.Add(muscleName);
                continue;
            }

            var muscle = await _muscleRepository.GetBySlugAsync(resolvedSlug);
            if (muscle?.Id is not int muscleId || !seenMuscleIds.Add(muscleId))
            {
                if (muscle is null)
                {
                    unmappedMuscles.Add(muscleName);
                }

                continue;
            }

            assignments.Add(new ExerciseTypeMuscleAssignment
            {
                MuscleId = muscleId,
                Role = role,
                SortOrder = sortOrder++
            });
        }
    }

    /// <summary>
    /// Builds the full image URL from the exercise images array
    /// Uses the first image if available
    /// </summary>
    private string BuildImageUrl(ExternalExerciseDto exercise)
    {
        if (exercise.Images == null || exercise.Images.Count == 0)
        {
            return null;
        }

        var imagePath = exercise.Images.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        // Construct full URL to the image in the free-exercise-db repository
        return $"https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/{imagePath}";
    }

    private sealed record AssignmentResolutionResult(
        List<ExerciseTypeMuscleAssignment> Assignments,
        List<string> UnmappedMuscles);
}
