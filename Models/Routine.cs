public class Routine
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string UserId { get; set; }
    public User User { get; set; }
    // Trainer that created/owns this routine (null for self-created routines).
    public string? TrainerId { get; set; }
    public User? Trainer { get; set; }
    public List<WorkoutTemplate> WorkoutTemplates { get; set; }
    public List<Workout> Workouts { get; set; }
    public string Status { get; set; } // e.g., "Planned", "Active", "Completed"
    public DateTime? CompletionDate { get; set; }
    /// <summary>
    /// Bumped by +1 on every PUT /routines/{id}. The FE sends it back via
    /// If-Match; a mismatch returns 409 so edits never overwrite silently.
    /// </summary>
    public int Version { get; set; } = 1;
}