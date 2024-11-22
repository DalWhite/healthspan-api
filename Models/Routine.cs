public class Routine
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string UserId { get; set; }
    public User User { get; set; }
    public List<WorkoutTemplate> WorkoutTemplates { get; set; }
    public List<Workout> Workouts { get; set; }
    public string Status { get; set; } // e.g., "Planned", "Active", "Completed"
    public DateTime? CompletionDate { get; set; }
}