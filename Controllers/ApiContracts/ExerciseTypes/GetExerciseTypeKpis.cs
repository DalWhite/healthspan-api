public class GetExerciseTypeKpis
{
    public float MaxRM { get; set; }
    public DateTime MaxRMDate {get;set;}
    public float Current1RM { get; set; }
    /// <summary>null = not enough data in the window (0 is a real plateau).</summary>
    public int? RMDiferentialInLast3Months { get; set; }
    public int WeeklyFrequency { get; set; }
    public int PerceibedEffortLastSession { get; set; }
}