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
    public List<WorkoutBlock> Blocks { get; set; } = [];

    /// <summary>
    /// Session goal chosen from the 9 Galpin adaptations (universal training
    /// tracker). Kept as the enum so the goal is queryable, not a free string.
    /// </summary>
    public GalpinAdaptationType? Goal { get; set; }
    /// <summary>Secondary adaptations the session also trained.</summary>
    public List<GalpinAdaptationType> SecondaryGoals { get; set; } = [];
    /// <summary>How the session was performed (modalidad).</summary>
    public WorkoutModality? Modality { get; set; }
    /// <summary>User perceived fatigue 1..5, the input of the readiness score.</summary>
    public int? PerceivedFatigue { get; set; }
}
