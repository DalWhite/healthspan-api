using healthspan_api.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("[controller]")]
public class DataController : ControllerBase
{
    private readonly CsvService _csvService;
    private readonly CurrentUserProvider _userProvider;
    private readonly ExercisesRepository _exercisesRepository;
    private readonly WorkoutsRepository _workoutsRepository;
    private readonly UserDataExportService _exportService;

    public DataController(
        CsvService csvService,
        CurrentUserProvider userProvider,
        ExercisesRepository exercisesRepository,
        WorkoutsRepository workoutsRepository,
        UserDataExportService exportService)
    {
        _csvService = csvService;
        _userProvider = userProvider;
        _exercisesRepository = exercisesRepository;
        _workoutsRepository = workoutsRepository;
        _exportService = exportService;
    }

    [HttpGet("import/template")]
    public IActionResult GetImportTemplate()
    {
        var content = _csvService.CreateCsvImportTemplate();
        return File(content, "text/csv", "exercisesImportTemplate.csv");
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportUserData(
        CancellationToken cancellationToken)
    {
        var userId = _userProvider.GetCurrentUserId();
        var content = await _exportService.CreateExportAsync(
            userId,
            cancellationToken);
        return File(
            content,
            "application/json",
            $"healthspan-export-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportExercisesCsvData(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        if (!file.ContentType.Equals("text/csv", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("File must be a CSV.");
        }
        var exerciseGroups = _csvService.ReadCsvExercisesAndGroupByDate(file);
        await AddExercisesToDatabaseAsync(
            exerciseGroups,
            cancellationToken);

        return Ok("Data imported successfully.");
    }

    private async Task AddExercisesToDatabaseAsync(
        Dictionary<DateTime, List<Exercise>> exerciseGroups,
        CancellationToken cancellationToken)
    {
        var userId = _userProvider.GetCurrentUserId();
        foreach (var group in exerciseGroups)
        {
            var result = await _workoutsRepository
                .GetOrCreateWorkoutSessionAsync(
                    userId,
                    DateOnly.FromDateTime(group.Key),
                    "Training",
                    cancellationToken);
            foreach (var ex in group.Value)
            {
                ex.WorkoutId = result.Workout.Id;
                _exercisesRepository.AddExercise(ex, userId);
            }
        }
    }
}
