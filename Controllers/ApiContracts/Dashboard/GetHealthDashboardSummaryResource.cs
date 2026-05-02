#nullable enable

public class GetHealthDashboardSummaryResource
{
    public int RecentWorkoutCount { get; set; }
    public int BloodTestCount { get; set; }
    public DateTime? LatestBloodTestDate { get; set; }
    public int OutOfRangeMarkersInLatestBloodTest { get; set; }
    public DateTime? LatestAssessmentDate { get; set; }
    public decimal? LatestAssessmentAverageScore { get; set; }
}