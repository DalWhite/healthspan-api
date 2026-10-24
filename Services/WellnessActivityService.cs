#nullable enable

using healthspan_api.Persistence.Repositories;

/// <summary>
/// CRUD for wellness activities. Type is an extensible slug; per-type extras
/// live in the JSON metadata bag and are passed through untouched.
/// </summary>
public class WellnessActivityService(WellnessActivitiesRepository repository)
{
    public WellnessActivity CreateActivity(string userId, WellnessActivityUpdateInput input)
    {
        return repository.AddActivity(new WellnessActivity
        {
            UserId = userId,
            Type = WellnessActivitiesRepository.NormalizeType(input.Type),
            Dimension = input.Dimension,
            OccurredOnUtc = input.OccurredOnUtc,
            DurationMinutes = input.DurationMinutes,
            FrequencyPerWeek = input.FrequencyPerWeek,
            Intensity = input.Intensity,
            TemperatureCelsius = input.TemperatureCelsius,
            Cycles = input.Cycles,
            Notes = input.Notes,
            Metadata = input.Metadata
        });
    }

    public (List<WellnessActivity> Items, int Total) GetActivities(
        string userId,
        string? type,
        WellnessDimension? dimension,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize)
        => repository.GetActivitiesPage(userId, type, dimension, fromUtc, toUtc, page, pageSize);

    public WellnessActivity? GetActivity(string userId, Guid activityId)
        => repository.GetActivity(userId, activityId);

    public bool UpdateActivity(string userId, Guid activityId, WellnessActivityUpdateInput input)
        => repository.UpdateActivity(userId, activityId, input);

    public bool DeleteActivity(string userId, Guid activityId)
        => repository.DeleteActivity(userId, activityId);
}
