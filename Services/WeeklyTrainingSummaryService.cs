using healthspan_api.Persistence.Repositories;

namespace Services;

public sealed class WeeklyTrainingSummaryService(
    WorkoutsRepository workoutsRepository)
{
    private static readonly IReadOnlyList<BodyRegionDefinition> BodyRegions =
    [
        new("chest", "Chest", "front", "Chest"),
        new("front-delts", "Front delts", "front", "Shoulders"),
        new("side-delts", "Side delts", "both", "Shoulders"),
        new("rear-delts", "Rear delts", "back", "Shoulders"),
        new("lats", "Lats", "back", "Back"),
        new("upper-back", "Upper back", "back", "Back"),
        new("traps", "Traps", "back", "Back"),
        new("biceps", "Biceps", "front", "Arms"),
        new("triceps", "Triceps", "back", "Arms"),
        new("forearms", "Forearms", "both", "Arms"),
        new("abs", "Abs", "front", "Core"),
        new("obliques", "Obliques", "front", "Core"),
        new("serratus", "Serratus", "front", "Core"),
        new("hip-flexors", "Hip flexors", "front", "Legs"),
        new("glutes", "Glutes", "back", "Glutes"),
        new("quads", "Quads", "front", "Legs"),
        new("hamstrings", "Hamstrings", "back", "Legs"),
        new("adductors", "Adductors", "front", "Legs"),
        new("abductors", "Abductors", "back", "Legs"),
        new("calves", "Calves", "back", "Legs")
    ];

    public async Task<WeeklyTrainingSummaryResource> GetAsync(
        string userId,
        int weekLimit,
        CancellationToken cancellationToken)
    {
        var workouts = await workoutsRepository
            .GetWeeklyTrainingWorkoutsAsync(userId, cancellationToken);
        var weeks = workouts
            .Where(workout =>
                workout.Date.HasValue
                && workout.Exercises?.Count > 0)
            .GroupBy(workout => StartOfWeek(workout.Date!.Value.Date))
            .OrderByDescending(group => group.Key)
            .Take(weekLimit)
            .Select(group => BuildWeek(group.Key, group))
            .ToList();

        return new WeeklyTrainingSummaryResource { Weeks = weeks };
    }

    private static WeeklyTrainingWeekResource BuildWeek(
        DateTime weekStart,
        IEnumerable<Workout> workouts)
    {
        var regionLoads = BodyRegions.ToDictionary(
            region => region.Key,
            _ => 0d);
        var sessions = workouts
            .OrderBy(workout => workout.Date)
            .ThenBy(workout => workout.Id)
            .Select(workout => BuildSession(workout, regionLoads))
            .ToList();
        var maxRegionLoad = regionLoads.Values.DefaultIfEmpty().Max();

        return new WeeklyTrainingWeekResource
        {
            WeekStart = weekStart,
            TotalLoad = sessions.Sum(session => session.TotalLoad),
            WorkoutCount = sessions.Count,
            ExerciseCount = sessions.Sum(session => session.ExerciseCount),
            Sessions = sessions,
            Regions = BodyRegions
                .Select(region =>
                {
                    var load = regionLoads.GetValueOrDefault(region.Key);
                    return new WeeklyMuscleLoadRegionResource
                    {
                        Key = region.Key,
                        Label = region.Label,
                        Side = region.Side,
                        Group = region.Group,
                        Load = load,
                        RelativeLoad = maxRegionLoad <= 0
                            ? 0
                            : load / maxRegionLoad
                    };
                })
                .ToList()
        };
    }

    private static WeeklyTrainingSessionResource BuildSession(
        Workout workout,
        IDictionary<string, double> regionLoads)
    {
        var focusLoads = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        var exercises = (workout.Exercises ?? [])
            .OrderBy(exercise => exercise.Type?.Name)
            .ThenBy(exercise => exercise.Id)
            .Select(exercise => BuildExercise(
                exercise,
                workout,
                regionLoads,
                focusLoads))
            .OrderByDescending(exercise => exercise.Load)
            .ThenBy(exercise => exercise.Name)
            .ToList();
        var sessionLabel = string.IsNullOrWhiteSpace(workout.Type)
            ? $"{workout.Date:dddd} session"
            : workout.Type;

        foreach (var exercise in exercises)
        {
            exercise.SessionLabel = sessionLabel;
        }

        return new WeeklyTrainingSessionResource
        {
            Key = workout.Id.ToString(),
            Date = workout.Date?.Date ?? default,
            Label = sessionLabel,
            TotalLoad = exercises.Sum(exercise => exercise.Load),
            ExerciseCount = exercises.Count,
            FocusGroups = focusLoads
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key)
                .Select(item => item.Key)
                .Take(3)
                .ToList(),
            Exercises = exercises
        };
    }

    private static WeeklyTrainingExerciseResource BuildExercise(
        Exercise exercise,
        Workout workout,
        IDictionary<string, double> regionLoads,
        IDictionary<string, double> focusLoads)
    {
        var load = ComputeLoad(exercise);
        var assignments = exercise.Type?.MuscleAssignments ?? [];
        var primary = assignments
            .Where(assignment =>
                assignment.Role == ExerciseMuscleRole.Primary
                && assignment.Muscle is not null)
            .OrderBy(assignment => assignment.SortOrder)
            .ThenBy(assignment => assignment.Muscle.SortOrder)
            .ToList();
        var secondary = assignments
            .Where(assignment =>
                assignment.Role == ExerciseMuscleRole.Secondary
                && assignment.Muscle is not null)
            .OrderBy(assignment => assignment.SortOrder)
            .ThenBy(assignment => assignment.Muscle.SortOrder)
            .ToList();

        DistributeLoad(load, primary, secondary, regionLoads, focusLoads);

        var primaryNames = primary
            .Select(assignment => assignment.Muscle.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var secondaryNames = secondary
            .Select(assignment => assignment.Muscle.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (primaryNames.Count == 0 && secondaryNames.Count == 0)
        {
            var fallbackRegion = InferRegionFromExerciseName(
                exercise.Type?.Name);
            AddLoad(regionLoads, fallbackRegion, load);
            AddFocusLoad(
                focusLoads,
                ResolveRegion(fallbackRegion)?.Group,
                load);
            var fallbackLabel = ResolveRegion(fallbackRegion)?.Label;
            if (!string.IsNullOrWhiteSpace(fallbackLabel))
            {
                primaryNames.Add(fallbackLabel);
            }
        }

        var tags = (exercise.Tags ?? [])
            .Concat((exercise.Type?.Tags ?? [])
                .Select(tag => tag.Name))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        return new WeeklyTrainingExerciseResource
        {
            Name = exercise.Type?.Name ?? "Exercise",
            Prescription = FormatPrescription(exercise),
            SessionDate = workout.Date?.Date ?? default,
            PrimaryMuscles = primaryNames,
            SecondaryMuscles = secondaryNames,
            Load = load,
            MaxRepetition = exercise.MaxRepetition,
            Tags = tags
        };
    }

    private static void DistributeLoad(
        double load,
        IReadOnlyList<ExerciseTypeMuscleAssignment> primary,
        IReadOnlyList<ExerciseTypeMuscleAssignment> secondary,
        IDictionary<string, double> regionLoads,
        IDictionary<string, double> focusLoads)
    {
        if (load <= 0)
        {
            return;
        }

        if (primary.Count > 0 && secondary.Count > 0)
        {
            DistributeRoleLoad(
                load * 0.7d,
                primary,
                regionLoads,
                focusLoads);
            DistributeRoleLoad(
                load * 0.3d,
                secondary,
                regionLoads,
                focusLoads);
            return;
        }

        var assignments = primary.Count > 0 ? primary : secondary;
        DistributeRoleLoad(load, assignments, regionLoads, focusLoads);
    }

    private static void DistributeRoleLoad(
        double load,
        IReadOnlyList<ExerciseTypeMuscleAssignment> assignments,
        IDictionary<string, double> regionLoads,
        IDictionary<string, double> focusLoads)
    {
        if (assignments.Count == 0)
        {
            return;
        }

        var loadPerMuscle = load / assignments.Count;
        foreach (var assignment in assignments)
        {
            var regionKey = ResolveRegionKey(assignment.Muscle);
            AddLoad(regionLoads, regionKey, loadPerMuscle);
            AddFocusLoad(
                focusLoads,
                assignment.Muscle.MuscleGroup.ToString(),
                loadPerMuscle);
        }
    }

    private static double ComputeLoad(Exercise exercise)
    {
        if (exercise.Volume.HasValue && exercise.Volume.Value > 0)
        {
            return exercise.Volume.Value;
        }

        if (string.Equals(
                exercise.Type?.WeightType,
                "None",
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return Math.Max(0, exercise.Series ?? 0)
            * Math.Max(0, exercise.Repetitions ?? 0)
            * Math.Max(0, exercise.Weight ?? 0);
    }

    private static string FormatPrescription(Exercise exercise)
    {
        var structure =
            exercise.Series.GetValueOrDefault() > 0
            && exercise.Repetitions.GetValueOrDefault() > 0
                ? $"{exercise.Series} x {exercise.Repetitions}"
                : exercise.Series.GetValueOrDefault() > 0
                    ? $"{exercise.Series} sets"
                    : exercise.Repetitions.GetValueOrDefault() > 0
                        ? $"{exercise.Repetitions} reps"
                        : string.Empty;
        string load;

        if (string.Equals(
                exercise.Type?.WeightType,
                "None",
                StringComparison.OrdinalIgnoreCase))
        {
            load = "No external load";
        }
        else if (string.Equals(
                     exercise.Type?.WeightType,
                     "BodyWeight",
                     StringComparison.OrdinalIgnoreCase))
        {
            load = FormatBodyWeight(exercise);
        }
        else
        {
            load = exercise.Weight.GetValueOrDefault() > 0
                ? $"{exercise.Weight:0.#} kg"
                : string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(structure)
            && !string.IsNullOrWhiteSpace(load))
        {
            return $"{structure} @ {load}";
        }

        if (!string.IsNullOrWhiteSpace(structure))
        {
            return structure;
        }

        return !string.IsNullOrWhiteSpace(load)
            ? load
            : "Load not logged";
    }

    private static string FormatBodyWeight(Exercise exercise)
    {
        if (exercise.BodyWeight.GetValueOrDefault() > 0)
        {
            var baseWeight = $"{exercise.BodyWeight:0.#} kg BW";
            return exercise.AddedWeight.GetValueOrDefault() > 0
                ? $"{baseWeight} + {exercise.AddedWeight:0.#} kg"
                : baseWeight;
        }

        return exercise.Weight.GetValueOrDefault() > 0
            ? $"{exercise.Weight:0.#} kg BW"
            : "BW";
    }

    private static void AddLoad(
        IDictionary<string, double> regionLoads,
        string regionKey,
        double load)
    {
        if (string.IsNullOrWhiteSpace(regionKey)
            || !regionLoads.ContainsKey(regionKey)
            || load <= 0)
        {
            return;
        }

        regionLoads[regionKey] += load;
    }

    private static void AddFocusLoad(
        IDictionary<string, double> focusLoads,
        string group,
        double load)
    {
        if (string.IsNullOrWhiteSpace(group) || load <= 0)
        {
            return;
        }

        focusLoads.TryGetValue(group, out var current);
        focusLoads[group] = current + load;
    }

    private static string ResolveRegionKey(Muscle muscle)
    {
        if (!string.IsNullOrWhiteSpace(muscle.Slug)
            && ResolveRegion(muscle.Slug) is not null)
        {
            return muscle.Slug;
        }

        var normalizedName = Normalize(muscle.Name);
        var exactRegion = BodyRegions.FirstOrDefault(
            region => Normalize(region.Label) == normalizedName);
        if (exactRegion is not null)
        {
            return exactRegion.Key;
        }

        return muscle.MuscleGroup switch
        {
            MuscleGroup.Chest => "chest",
            MuscleGroup.Shoulders => muscle.Side switch
            {
                MuscleSide.back => "rear-delts",
                MuscleSide.both => "side-delts",
                _ => "front-delts"
            },
            MuscleGroup.Arms => muscle.Side switch
            {
                MuscleSide.back => "triceps",
                MuscleSide.both => "forearms",
                _ => "biceps"
            },
            MuscleGroup.Back => muscle.Side == MuscleSide.back
                ? "lats"
                : "upper-back",
            MuscleGroup.Core => "abs",
            MuscleGroup.Glutes => "glutes",
            MuscleGroup.Legs => muscle.Side switch
            {
                MuscleSide.back => "hamstrings",
                MuscleSide.both => "calves",
                _ => "quads"
            },
            _ => string.Empty
        };
    }

    private static string InferRegionFromExerciseName(string exerciseName)
    {
        var name = exerciseName?.ToLowerInvariant() ?? string.Empty;
        if (name.Contains("bench")
            || name.Contains("press banca")
            || name.Contains("pec")
            || name.Contains("chest"))
        {
            return "chest";
        }

        if (name.Contains("rear delt")
            || name.Contains("reverse fly")
            || name.Contains("face pull"))
        {
            return "rear-delts";
        }

        if (name.Contains("lateral raise") || name.Contains("side raise"))
        {
            return "side-delts";
        }

        if (name.Contains("shoulder press")
            || name.Contains("overhead press")
            || name.Contains("military press"))
        {
            return "front-delts";
        }

        if (name.Contains("curl") || name.Contains("bicep"))
        {
            return "biceps";
        }

        if (name.Contains("tricep")
            || name.Contains("dip")
            || name.Contains("fondo"))
        {
            return "triceps";
        }

        if (name.Contains("pull")
            || name.Contains("row")
            || name.Contains("lat")
            || name.Contains("dominada"))
        {
            return "lats";
        }

        if (name.Contains("shrug") || name.Contains("trap"))
        {
            return "traps";
        }

        if (name.Contains("adductor"))
        {
            return "adductors";
        }

        if (name.Contains("abductor"))
        {
            return "abductors";
        }

        if (name.Contains("squat")
            || name.Contains("sentadilla")
            || name.Contains("lunge")
            || name.Contains("leg extension"))
        {
            return "quads";
        }

        if (name.Contains("deadlift")
            || name.Contains("peso muerto")
            || name.Contains("hamstring")
            || name.Contains("leg curl")
            || name.Contains("rdl"))
        {
            return "hamstrings";
        }

        if (name.Contains("glute")
            || name.Contains("hip thrust")
            || name.Contains("puente"))
        {
            return "glutes";
        }

        if (name.Contains("calf"))
        {
            return "calves";
        }

        if (name.Contains("serratus"))
        {
            return "serratus";
        }

        if (name.Contains("oblique"))
        {
            return "obliques";
        }

        if (name.Contains("hip flexor") || name.Contains("knee raise"))
        {
            return "hip-flexors";
        }

        return name.Contains("ab")
               || name.Contains("core")
               || name.Contains("crunch")
            ? "abs"
            : string.Empty;
    }

    private static BodyRegionDefinition ResolveRegion(string key)
        => BodyRegions.FirstOrDefault(region =>
            string.Equals(
                region.Key,
                key,
                StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string value)
        => new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff =
            (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff);
    }

    private sealed record BodyRegionDefinition(
        string Key,
        string Label,
        string Side,
        string Group);
}
