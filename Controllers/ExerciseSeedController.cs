using System.Text.Json;
using Controllers.ApiContracts.ExerciseTypes;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
//[Authorize(Roles = "Admin")]
public class ExerciseSeedController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ExerciseTypesRepository _exerciseTypeRepository;
    private readonly MuscleRepository _muscleRepository;
    private readonly ILogger<ExerciseSeedController> _logger;

    public ExerciseSeedController(
        IHttpClientFactory httpClientFactory,
        ExerciseTypesRepository exerciseTypeRepository,
        MuscleRepository muscleRepository,
        ILogger<ExerciseSeedController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _exerciseTypeRepository = exerciseTypeRepository;
        _muscleRepository = muscleRepository;
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
                    var muscleId = await ResolveMuscleNameAsync(exercise.PrimaryMuscles?.FirstOrDefault());
                    var imageRoute = BuildImageUrl(exercise);

                    // Check if exercise already exists
                    var existing = await _exerciseTypeRepository.GetByNameAsync(exercise.Name);
                    
                    var exerciseData = new ExerciseType
                    {
                        Name = exercise.Name,
                        Description = exercise.Instructions != null ? string.Join("; ", exercise.Instructions) : "",
                        ImageRoute = imageRoute,
                        PrimaryMuscleId = muscleId,
                        Tags = tags,
                        DifficultyLevel = 1,
                        MuscleGroup = MuscleGroup.Core
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
    /// Resolves a muscle name to the muscle ID in the database
    /// Returns null if muscle is not found or name is empty
    /// </summary>
    private async Task<int?> ResolveMuscleNameAsync(string muscleName)
    {
        if (string.IsNullOrWhiteSpace(muscleName))
        {
            return null;
        }

        var muscle = await _muscleRepository.GetByNameAsync(muscleName.ToLower());
        return muscle?.Id;
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
}
