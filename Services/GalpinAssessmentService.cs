#nullable enable

using healthspan_api.Persistence.Repositories;

public class GalpinAssessmentService
{
    private readonly GalpinAssessmentsRepository _repository;

    public GalpinAssessmentService(GalpinAssessmentsRepository repository)
    {
        _repository = repository;
    }

    public Guid CreateAssessment(string userId, DateTime assessedOnUtc, string? notes, List<GalpinAssessmentScoreInput> scores)
    {
        var assessment = new GalpinAssessment
        {
            UserId = userId,
            AssessedOnUtc = assessedOnUtc,
            Notes = notes,
            Scores = scores.Select(score => new GalpinAssessmentScore
            {
                Adaptation = score.Adaptation,
                Score = score.Score,
                EvidenceNotes = score.EvidenceNotes
            }).ToList()
        };

        return _repository.AddAssessment(assessment);
    }

    public List<GalpinAssessment> GetAssessments(string userId)
    {
        return _repository.GetAssessments(userId);
    }

    public GalpinAssessment? GetLatestAssessment(string userId)
    {
        return _repository.GetLatestAssessment(userId);
    }

    public List<GalpinAdaptationTrendPoint> GetAdaptationTrend(string userId, GalpinAdaptationType adaptation)
    {
        return _repository.GetAssessments(userId)
            .SelectMany(assessment => assessment.Scores.Select(score => new { assessment.AssessedOnUtc, Score = score }))
            .Where(item => item.Score.Adaptation == adaptation && item.Score.Score.HasValue)
            .OrderBy(item => item.AssessedOnUtc)
            .Select(item => new GalpinAdaptationTrendPoint
            {
                Time = item.AssessedOnUtc,
                Score = item.Score.Score ?? 0
            })
            .ToList();
    }
}

public class GalpinAssessmentScoreInput
{
    public GalpinAdaptationType Adaptation { get; set; }
    public decimal? Score { get; set; }
    public string? EvidenceNotes { get; set; }
}

public class GalpinAdaptationTrendPoint
{
    public DateTime Time { get; set; }
    public decimal Score { get; set; }
}