#nullable enable

using Microsoft.EntityFrameworkCore;

namespace healthspan_api.Persistence.Repositories;

public class WellnessActivitiesRepository(MainDbContext context)
{
    public WellnessActivity AddActivity(WellnessActivity activity)
    {
        context.WellnessActivities.Add(activity);
        context.SaveChanges();
        return activity;
    }

    public List<WellnessActivity> GetActivities(
        string userId,
        string? type,
        WellnessDimension? dimension,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var query = context.WellnessActivities
            .IgnoreQueryFilters()
            .Where(activity => activity.UserId == userId);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = NormalizeType(type);
            query = query.Where(activity => activity.Type == normalizedType);
        }

        if (dimension.HasValue)
        {
            query = query.Where(activity => activity.Dimension == dimension.Value);
        }

        if (fromUtc.HasValue)
        {
            var from = fromUtc.Value.Date;
            query = query.Where(activity => activity.OccurredOnUtc >= from);
        }

        if (toUtc.HasValue)
        {
            var to = toUtc.Value.Date.AddDays(1);
            query = query.Where(activity => activity.OccurredOnUtc < to);
        }

        return query
            .OrderByDescending(activity => activity.OccurredOnUtc)
            .ThenByDescending(activity => activity.CreatedAtUtc)
            .ToList();
    }

    public WellnessActivity? GetActivity(string userId, Guid activityId)
    {
        return context.WellnessActivities
            .IgnoreQueryFilters()
            .FirstOrDefault(activity =>
                activity.Id == activityId
                && activity.UserId == userId);
    }

    public bool UpdateActivity(
        string userId,
        Guid activityId,
        WellnessActivityUpdateInput input)
    {
        var activity = GetActivity(userId, activityId);
        if (activity is null)
        {
            return false;
        }

        activity.Type = NormalizeType(input.Type);
        activity.Dimension = input.Dimension;
        activity.OccurredOnUtc = input.OccurredOnUtc;
        activity.DurationMinutes = input.DurationMinutes;
        activity.FrequencyPerWeek = input.FrequencyPerWeek;
        activity.Intensity = input.Intensity;
        activity.TemperatureCelsius = input.TemperatureCelsius;
        activity.Cycles = input.Cycles;
        activity.Notes = input.Notes;
        activity.Metadata = input.Metadata;
        context.SaveChanges();
        return true;
    }

    public bool DeleteActivity(string userId, Guid activityId)
    {
        var activity = GetActivity(userId, activityId);
        if (activity is null)
        {
            return false;
        }

        context.WellnessActivities.Remove(activity);
        context.SaveChanges();
        return true;
    }

    public static string NormalizeType(string type)
        => type.Trim().ToLowerInvariant();
}

public class WellnessActivityUpdateInput
{
    public string Type { get; set; } = string.Empty;
    public WellnessDimension Dimension { get; set; }
    public DateTime OccurredOnUtc { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? FrequencyPerWeek { get; set; }
    public decimal? Intensity { get; set; }
    public decimal? TemperatureCelsius { get; set; }
    public int? Cycles { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
}
