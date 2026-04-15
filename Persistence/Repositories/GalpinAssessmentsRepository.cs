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