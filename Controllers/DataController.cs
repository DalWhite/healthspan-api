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
    public DataController(CsvService csvService, CurrentUserProvider userProvider, ExercisesRepository exercisesRepository, WorkoutsRepository workoutsRepository)
    {
        _csvService = csvService;
        _userProvider = userProvider;
        _exercisesRepository = exercisesRepository;
        _workoutsRepository = workoutsRepository;
    }

    [HttpGet("import/template")]
    public IActionResult GetImportTemplate()
    {
        var content = _csvService.CreateCsvImportTemplate();
        return File(content, "text/csv", "exercisesImportTemplate.csv");
    }
    [HttpPost("import")]
    public IActionResult ImportExercisesCsvData(IFormFile file)
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
        AddExercisesToDatabase(exerciseGroups);

        return Ok("Data imported successfully.");
    }

    private void AddExercisesToDatabase(Dictionary<DateTime, List<Exercise>> exerciseGroups)
    {
        foreach (var group in exerciseGroups)
        {
            var workout = new Workout
            {
                Date = group.Key,
                UserId = _userProvider.GetCurrentUserId()
            };
            var workoutId = _workoutsRepository.AddWorkout(workout);
            foreach (var ex in group.Value)
            {
                ex.WorkoutId = workoutId;
                _exercisesRepository.AddExercise(ex, _userProvider.GetCurrentUserId());
            }
        }
    }
}
