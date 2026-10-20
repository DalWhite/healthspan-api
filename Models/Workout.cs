public class Workout
{
    public Guid Id { get; set; }
    public Guid? ClientEntityId { get; set; }
    public DateTime? Date { get; set; }
    public string Type { get; set; }
    public string UserId { get; set; }
    public string DailySessionKey { get; set; }
    /// <summary>
    /// Explicit "I finished this workout" moment (POST /workouts/{id}/complete).
    /// Completed-workout queries use this instead of inferring from Exercises.Count.
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    public User User { get; set; }
    public List<Exercise> Exercises { get; set; } = [];
}
