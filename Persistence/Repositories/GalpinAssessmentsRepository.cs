#nullable enable

using Microsoft.EntityFrameworkCore;

namespace healthspan_api.Persistence.Repositories;

public class GalpinAssessmentsRepository(MainDbContext context)
{
    public Guid AddAssessment(GalpinAssessment assessment)
    {
        context.GalpinAssessments.Add(assessment);
        context.SaveChanges();
        return assessment.Id;
    }

    public bool UpdateAssessment(
        string userId,
        Guid assessmentId,
        DateTime assessedOnUtc,
        string? notes,
        List<GalpinAssessmentScoreInput> scores)
    {
        var assessment = context.GalpinAssessments
            .IgnoreQueryFilters()
            .Include(item => item.Scores)
            .FirstOrDefault(item =>
                item.Id == assessmentId
                && item.UserId == userId);

        if (assessment is null)
        {
            return false;
        }

        assessment.AssessedOnUtc = assessedOnUtc;
        assessment.Notes = notes;
        context.GalpinAssessmentScores.RemoveRange(
            assessment.Scores);
        assessment.Scores = scores.Select(score =>
            new GalpinAssessmentScore
            {
                Adaptation = score.Adaptation,
                Score = score.Score,
                EvidenceNotes = score.EvidenceNotes
            }).ToList();
        context.SaveChanges();
        return true;
    }

    public bool DeleteAssessment(
        string userId,
        Guid assessmentId)
    {
        var assessment = context.GalpinAssessments
            .IgnoreQueryFilters()
            .FirstOrDefault(item =>
                item.Id == assessmentId
                && item.UserId == userId);

        if (assessment is null)
        {
            return false;
        }

        context.GalpinAssessments.Remove(assessment);
        context.SaveChanges();
        return true;
    }

    public List<GalpinAssessment> GetAssessments(string userId)
    {
        return context.GalpinAssessments
            .IgnoreQueryFilters()
            .Where(assessment => assessment.UserId == userId)
            .Include(assessment => assessment.Scores)
            .OrderByDescending(assessment => assessment.AssessedOnUtc)
            .ToList();
    }

    public GalpinAssessment? GetLatestAssessment(string userId)
    {
        return context.GalpinAssessments
            .IgnoreQueryFilters()
            .Where(assessment => assessment.UserId == userId)
            .Include(assessment => assessment.Scores)
            .OrderByDescending(assessment => assessment.AssessedOnUtc)
            .FirstOrDefault();
    }
}
