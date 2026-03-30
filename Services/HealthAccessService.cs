using Microsoft.EntityFrameworkCore;

public class HealthAccessService
{
    private readonly MainDbContext _context;

    public HealthAccessService(MainDbContext context)
    {
        _context = context;
    }

    public bool CanAccessBloodAnalytics(string requesterUserId, string targetUserId)
    {
        return CanAccessTargetUser(requesterUserId, targetUserId, requireBloodAnalytics: true, requireAssessments: false);
    }

    public bool CanAccessAssessments(string requesterUserId, string targetUserId)
    {
        return CanAccessTargetUser(requesterUserId, targetUserId, requireBloodAnalytics: false, requireAssessments: true);
    }

    private bool CanAccessTargetUser(string requesterUserId, string targetUserId, bool requireBloodAnalytics, bool requireAssessments)
    {
        if (requesterUserId == targetUserId)
        {
            return true;
        }

        var trainee = _context.Users
            .IgnoreQueryFilters()
            .Include(user => user.Trainer)
            .FirstOrDefault(user => user.Id == targetUserId);

        if (trainee?.Trainer?.Id == requesterUserId)
        {
            return true;
        }

        return _context.AnalyticsPermissions
            .IgnoreQueryFilters()
            .Any(permission =>
                permission.OwnerUserId == targetUserId &&
                permission.ViewerUserId == requesterUserId &&
                (!requireBloodAnalytics || permission.CanViewBloodAnalytics) &&
                (!requireAssessments || permission.CanViewAssessments));
    }
}