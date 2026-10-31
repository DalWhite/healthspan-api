#nullable enable

using Controllers.ApiContracts.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

/// <summary>
/// Monthly coach↔trainee health-result KPIs (spec t_358e09e9 §3).
/// Routes are owner-or-direct-trainer only: [TraineeAccess] runs before the
/// action, so a foreign trainee gets 403 without loading any data (privacy §5).
/// Responses carry monthly aggregates only — never raw readings, never values
/// in logs.
/// </summary>
[Authorize]
[ApiController]
[Route("[controller]")]
public sealed class ResultsController(
    ResultsService resultsService,
    TimeProvider timeProvider,
    ILogger<ResultsController> logger) : ControllerBase
{
    public const int DefaultSeriesMonths = 6;
    public const int MaxSeriesMonths = 12;
    public const int MinSeriesMonths = 1;

    [HttpGet("{traineeId}/month")]
    [TraineeAccess]
    public async Task<ActionResult<MonthlyResultsResource>> GetMonth(
        string traineeId,
        string? month,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month, "yyyy-MM", out var parsedMonth))
        {
            return ApiProblemResult.Validation(
                "month_invalid",
                "month must be an exact calendar month in YYYY-MM format.");
        }

        var currentMonth = DateOnly.FromDateTime(
            timeProvider.GetUtcNow().UtcDateTime).ToString("yyyy-MM");
        if (parsedMonth.ToString("yyyy-MM").CompareTo(currentMonth) > 0)
        {
            // A future month would answer with a misleading score; reject instead.
            return ApiProblemResult.Validation(
                "month_in_future",
                "month cannot be later than the current calendar month.");
        }

        try
        {
            var result = await resultsService.GetMonthAsync(
                traineeId,
                parsedMonth,
                cancellationToken);

            // Privacy §5: identifiers + states only, never KPI values.
            logger.LogInformation(
                "results month trainee={TraineeId} month={Month} states={Consistency}/{Weight}/{Strength}",
                traineeId,
                parsedMonth.ToString("yyyy-MM"),
                result.Consistency.State,
                result.Weight.State,
                result.Strength.State);

            return Ok(result);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to load results KPIs for trainee {TraineeId}",
                traineeId);
            return ApiProblemResult.Unexpected("results_load_failed");
        }
    }

    [HttpGet("{traineeId}/series")]
    [TraineeAccess]
    public async Task<ActionResult<ResultsSeriesResource>> GetSeries(
        string traineeId,
        int months = DefaultSeriesMonths,
        CancellationToken cancellationToken = default)
    {
        var window = Math.Clamp(months, MinSeriesMonths, MaxSeriesMonths);

        try
        {
            var result = await resultsService.GetSeriesAsync(
                traineeId,
                window,
                cancellationToken);

            logger.LogInformation(
                "results series trainee={TraineeId} months={Months}",
                traineeId,
                window);

            return Ok(result);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to load results series for trainee {TraineeId}",
                traineeId);
            return ApiProblemResult.Unexpected("results_load_failed");
        }
    }
}
