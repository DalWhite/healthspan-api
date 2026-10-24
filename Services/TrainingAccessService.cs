using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class TrainingAccessService(MainDbContext context)
{
    /// <summary>
    /// Trainer→trainee lookup helper: resolves the target user id for a request
    /// from <paramref name="trainerUserId"/>. An empty/null trainee resolves to
    /// the trainer themselves. Returns null when the relationship does not exist
    /// (caller should answer 403 without exposing data).
    /// </summary>
    public string? ResolveTraineeUserId(string trainerUserId, string? traineeUserId)
    {
        if (string.IsNullOrWhiteSpace(trainerUserId))
        {
            return null;
        }

        var targetUserId = string.IsNullOrWhiteSpace(traineeUserId)
            ? trainerUserId
            : traineeUserId;

        return CanAccessUser(trainerUserId, targetUserId) ? targetUserId : null;
    }

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
