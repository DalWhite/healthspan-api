public sealed class WeeklyTrainingSummaryResource
{
    public List<WeeklyTrainingWeekResource> Weeks { get; set; } = [];
}

public sealed class WeeklyTrainingWeekResource
{
    public DateTime WeekStart { get; set; }
    public double TotalLoad { get; set; }
    public int WorkoutCount { get; set; }
    public int ExerciseCount { get; set; }
    public List<WeeklyMuscleLoadRegionResource> Regions { get; set; } = [];
    public List<WeeklyTrainingSessionResource> Sessions { get; set; } = [];
}

public sealed class WeeklyMuscleLoadRegionResource
{
    public string Key { get; set; }
    public string Label { get; set; }
    public string Side { get; set; }
    public string Group { get; set; }
    public double Load { get; set; }
    public double RelativeLoad { get; set; }
}

public sealed class WeeklyTrainingSessionResource
{
    public string Key { get; set; }
    public DateTime Date { get; set; }
    public string Label { get; set; }
    public double TotalLoad { get; set; }
    public int ExerciseCount { get; set; }
    public List<string> FocusGroups { get; set; } = [];
    public List<WeeklyTrainingExerciseResource> Exercises { get; set; } = [];
}

public sealed class WeeklyTrainingExerciseResource
{
    public string Name { get; set; }
    public string Prescription { get; set; }
    public string SessionLabel { get; set; }
    public DateTime SessionDate { get; set; }
    public List<string> PrimaryMuscles { get; set; } = [];
    public List<string> SecondaryMuscles { get; set; } = [];
    public double Load { get; set; }
    public double? MaxRepetition { get; set; }
    public List<string> Tags { get; set; } = [];
}
