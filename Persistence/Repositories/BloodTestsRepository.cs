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

    public List<BloodTest> GetBloodTests(string userId)
    {
        return context.BloodTests
            .IgnoreQueryFilters()
            .Where(test => test.UserId == userId)
            .Include(test => test.MarkerResults)
            .OrderByDescending(test => test.TestDate)
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

    public List<BloodTest> GetPendingReviewBloodTests(string userId)
    {
        return context.BloodTests
            .IgnoreQueryFilters()
            .Where(test => test.UserId == userId && test.ProcessingStatus == BloodTestProcessingStatus.PendingReview)
            .Include(test => test.MarkerResults)
            .OrderByDescending(test => test.TestDate)
            .ToList();
    }
}