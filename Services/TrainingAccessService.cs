using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class TrainingAccessService(MainDbContext context)
{
    public bool CanAccessUser(
        string requesterUserId,
        string targetUserId)
    {
        if (string.IsNullOrWhiteSpace(requesterUserId)
            || string.IsNullOrWhiteSpace(targetUserId))
        {
            return false;
        }

        if (requesterUserId == targetUserId)
        {
            return true;
        }

        return context.Users
            .IgnoreQueryFilters()
            .Any(user =>
                user.Id == targetUserId
                && user.Trainer != null
                && user.Trainer.Id == requesterUserId);
    }
}
