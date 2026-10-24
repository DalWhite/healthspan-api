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
    private readonly CsvImportJobService _importJobs;
    private readonly UserDataExportService _exportService;

    public DataController(
        CsvService csvService,
        CurrentUserProvider userProvider,
        CsvImportJobService importJobs,
        UserDataExportService exportService)
    {
        _csvService = csvService;
        _userProvider = userProvider;
        _importJobs = importJobs;
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

    /// <summary>
    /// Accepts a CSV and starts a background import job (t_c1f8e6ac contract
    /// from t_37acc65b §4): 202 + jobId instead of a synchronous import that
    /// dies on the FE 300 s timeout past ~5k rows.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> ImportExercisesCsvData(IFormFile file)
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

        if (file.Length > CsvService.MaxImportBytes)
        {
            return BadRequest($"File too large (max {CsvService.MaxImportBytes / 1024 / 1024} MB).");
        }

        CsvImportParseResult parsed;
        try
        {
            using var stream = file.OpenReadStream();
            parsed = _csvService.ParseCsvRows(stream);
        }
        catch (Exception)
        {
            return BadRequest("Could not read the CSV file.");
        }

        if (parsed.StructuralErrors.Count > 0)
        {
            return BadRequest(new { errors = parsed.StructuralErrors });
        }

        var userId = _userProvider.GetCurrentUserId();
        var job = await _importJobs.StartAsync(userId, parsed.Rows, parsed.Errors);

        return Accepted(new { jobId = job.Id, total = job.Total });
    }

    [HttpGet("import/{jobId}")]
    public async Task<IActionResult> GetImportJobStatus(string jobId)
    {
        var userId = _userProvider.GetCurrentUserId();
        // Live job if this process owns it; otherwise hydrated from the durable
        // snapshot so the FE can re-attach after an API restart (t_e26d8b7d).
        var job = await _importJobs.FindAsync(jobId, userId);
        if (job is null)
        {
            return NotFound();
        }

        return Ok(job.ToStatusDto(CsvImportJobService.MaxErrorsExposed));
    }
}
