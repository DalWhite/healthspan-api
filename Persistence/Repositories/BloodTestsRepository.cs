#nullable enable

using Microsoft.EntityFrameworkCore;

namespace healthspan_api.Persistence.Repositories;

public class BloodTestsRepository(MainDbContext context)
{
    public Guid AddBloodTest(BloodTest bloodTest)
    {
        context.BloodTests.Add(bloodTest);
        context.SaveChanges();
        return bloodTest.Id;
    }

    public List<BloodTestSummary> GetBloodTestSummaries(
        string userId)
    {
        return context.BloodTests
            .IgnoreQueryFilters()
            .Where(test => test.UserId == userId)
            .OrderByDescending(test => test.TestDate)
            .Select(test => new BloodTestSummary
            {
                Id = test.Id,
                TestDate = test.TestDate,
                LabName = test.LabName,
                SourceFileName = test.SourceFileName,
                ProcessingStatus = test.ProcessingStatus,
                ProcessingNotes = test.ProcessingNotes,
                MarkerCount = test.MarkerResults.Count,
                UpdatedAtUtc = test.UpdatedAtUtc
            })
            .ToList();
    }

    public BloodTest? GetBloodTest(Guid id, string userId)
    {
        return context.BloodTests
            .IgnoreQueryFilters()
            .Where(test => test.UserId == userId)
            .Include(test => test.MarkerResults)
            .FirstOrDefault(test => test.Id == id);
    }

    public List<BloodTestSummary>
        GetPendingReviewBloodTestSummaries(string userId)
    {
        return context.BloodTests
            .IgnoreQueryFilters()
            .Where(test => test.UserId == userId && test.ProcessingStatus == BloodTestProcessingStatus.PendingReview)
            .OrderByDescending(test => test.TestDate)
            .Select(test => new BloodTestSummary
            {
                Id = test.Id,
                TestDate = test.TestDate,
                LabName = test.LabName,
                SourceFileName = test.SourceFileName,
                ProcessingStatus = test.ProcessingStatus,
                ProcessingNotes = test.ProcessingNotes,
                MarkerCount = test.MarkerResults.Count,
                UpdatedAtUtc = test.UpdatedAtUtc
            })
            .ToList();
    }
}
