public class AddRoutineResource
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string UserId { get; set; }
    public List<WorkoutTemplateResource> WorkoutTemplates { get; set; }
    
}
public class WorkoutTemplateResource 
{
    public string Type { get; set; }
    public int Frequency { get; set; }
    public List<int> ExerciseTypesIds { get; set; }
}