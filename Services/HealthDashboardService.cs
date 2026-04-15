using Microsoft.EntityFrameworkCore;

public class HealthDashboardService
{
    private readonly MainDbContext _context;

    public HealthDashboardService(MainDbContext context)
    {
        _context = context;
    }

    public HealthDashboardSummary GetSummary(string userId)
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var latestBloodTest = _context.BloodTests
            .IgnoreQueryFilters()
            .Include(test => test.MarkerResults)
            .Where(test => test.UserId == userId && test.ProcessingStatus == BloodTestProcessingStatus.Processed)
            .OrderByDescending(test => test.TestDate)
            .FirstOrDefault();

        var latestAssessment = _context.GalpinAssessments
            .IgnoreQueryFilters()
            .Include(assessment => assessment.Scores)
            .Where(assessment => assessment.UserId == userId)
            .OrderByDescending(assessment => assessment.AssessedOnUtc)
            .FirstOrDefault();

        var recentWorkouts = _context.Workouts
            .IgnoreQueryFilters()
            .Count(workout => workout.UserId == userId && workout.Date >= thirtyDaysAgo);

        var outOfRangeMarkers = latestBloodTest?.MarkerResults.Count(result => result.Status == MarkerStatus.High || result.Status == MarkerStatus.Low) ?? 0;
        decimal? assessmentAverage = null;

        if (latestAssessment?.Scores.Any(score => score.Score.HasValue) == true)
        {
            assessmentAverage = latestAssessment.Scores.Where(score => score.Score.HasValue).Average(score => score.Score!.Value);
        }

        return new HealthDashboardSummary
        {
            RecentWorkoutCount = recentWorkouts,
            BloodTestCount = _context.BloodTests.IgnoreQueryFilters().Count(test => test.UserId == userId),
            LatestBloodTestDate = latestBloodTest?.TestDate,
            OutOfRangeMarkersInLatestBloodTest = outOfRangeMarkers,
            LatestAssessmentDate = latestAssessment?.AssessedOnUtc,
            LatestAssessmentAverageScore = assessmentAverage
        };
    }
}

public class HealthDashboardSummary
{
    public int RecentWorkoutCount { get; set; }
    public int BloodTestCount { get; set; }
    public DateTime? LatestBloodTestDate { get; set; }
    public int OutOfRangeMarkersInLatestBloodTest { get; set; }
    public DateTime? LatestAssessmentDate { get; set; }
    public decimal? LatestAssessmentAverageScore { get; set; }
}