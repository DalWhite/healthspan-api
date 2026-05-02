using Microsoft.EntityFrameworkCore;

public static class MuscleCatalogSeeder
{
    public static readonly IReadOnlyList<CanonicalMuscleDefinition> CanonicalMuscles =
    [
        new("chest", "Chest", MuscleSide.front, MuscleGroup.Chest, 10),
        new("front-delts", "Front delts", MuscleSide.front, MuscleGroup.Shoulders, 20),
        new("side-delts", "Side delts", MuscleSide.both, MuscleGroup.Shoulders, 30),
        new("rear-delts", "Rear delts", MuscleSide.back, MuscleGroup.Shoulders, 40),
        new("lats", "Lats", MuscleSide.back, MuscleGroup.Back, 50),
        new("upper-back", "Upper back", MuscleSide.back, MuscleGroup.Back, 60),
        new("traps", "Traps", MuscleSide.back, MuscleGroup.Back, 70),
        new("biceps", "Biceps", MuscleSide.front, MuscleGroup.Arms, 80),
        new("triceps", "Triceps", MuscleSide.back, MuscleGroup.Arms, 90),
        new("forearms", "Forearms", MuscleSide.both, MuscleGroup.Arms, 100),
        new("abs", "Abs", MuscleSide.front, MuscleGroup.Core, 110),
        new("obliques", "Obliques", MuscleSide.front, MuscleGroup.Core, 120),
        new("serratus", "Serratus", MuscleSide.front, MuscleGroup.Core, 130),
        new("hip-flexors", "Hip flexors", MuscleSide.front, MuscleGroup.Legs, 140),
        new("glutes", "Glutes", MuscleSide.back, MuscleGroup.Glutes, 150),
        new("quads", "Quads", MuscleSide.front, MuscleGroup.Legs, 160),
        new("hamstrings", "Hamstrings", MuscleSide.back, MuscleGroup.Legs, 170),
        new("adductors", "Adductors", MuscleSide.front, MuscleGroup.Legs, 180),
        new("abductors", "Abductors", MuscleSide.back, MuscleGroup.Legs, 190),
        new("calves", "Calves", MuscleSide.back, MuscleGroup.Legs, 200)
    ];

    private static readonly IReadOnlyDictionary<string, string> MuscleSlugAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pectoralmajor"] = "chest",
            ["pectoralismajor"] = "chest",
            ["chest"] = "chest",
            ["shoulders"] = "front-delts",
            ["frontdelts"] = "front-delts",
            ["sidedelts"] = "side-delts",
            ["reardelts"] = "rear-delts",
            ["latissimusdorsi"] = "lats",
            ["lats"] = "lats",
            ["back"] = "upper-back",
            ["middleback"] = "upper-back",
            ["upperback"] = "upper-back",
            ["lowerback"] = "upper-back",
            ["trapezius"] = "traps",
            ["traps"] = "traps",
            ["biceps"] = "biceps",
            ["bicepsbrachii"] = "biceps",
            ["brachialis"] = "biceps",
            ["triceps"] = "triceps",
            ["tricepsbrachii"] = "triceps",
            ["forearms"] = "forearms",
            ["forearm"] = "forearms",
            ["abdominals"] = "abs",
            ["rectusabdominis"] = "abs",
            ["abs"] = "abs",
            ["obliques"] = "obliques",
            ["obliquusexternusabdominis"] = "obliques",
            ["serratus"] = "serratus",
            ["serratusanterior"] = "serratus",
            ["hipflexors"] = "hip-flexors",
            ["iliopsoas"] = "hip-flexors",
            ["glutes"] = "glutes",
            ["gluteusmaximus"] = "glutes",
            ["quadriceps"] = "quads",
            ["quadricepsfemoris"] = "quads",
            ["quads"] = "quads",
            ["hamstrings"] = "hamstrings",
            ["bicepsfemoris"] = "hamstrings",
            ["adductors"] = "adductors",
            ["abductors"] = "abductors",
            ["calves"] = "calves",
            ["gastrocnemius"] = "calves",
            ["soleus"] = "calves"
        };

    private static readonly IReadOnlyDictionary<string, ExerciseMuscleBlueprint> ExerciseBlueprints =
        new Dictionary<string, ExerciseMuscleBlueprint>(StringComparer.OrdinalIgnoreCase)
        {
            [Normalize("Press militar")] = new(["front-delts"], ["triceps"]),
            [Normalize("dominadas")] = new(["lats"], ["biceps"]),
            [Normalize("Curl Biceps")] = new(["biceps"], ["forearms"]),
            [Normalize("Remo")] = new(["upper-back"], ["lats", "biceps"]),
            [Normalize("Weight grab")] = new(["forearms"], ["upper-back"]),
            [Normalize("Press banca")] = new(["chest"], ["triceps", "front-delts"]),
            [Normalize("Fondos")] = new(["chest"], ["triceps", "front-delts"]),
            [Normalize("Sentadilla frontal")] = new(["quads"], ["glutes"]),
            [Normalize("Zancadas")] = new(["quads"], ["glutes", "hamstrings"]),
            [Normalize("Sentadilla")] = new(["quads"], ["glutes", "hamstrings"]),
            [Normalize("Peso Muerto")] = new(["glutes", "hamstrings"], ["upper-back"]),
            [Normalize("Jalon al pecho")] = new(["lats"], ["biceps"]),
            [Normalize("Rotary calf")] = new(["calves"], []),
            [Normalize("Upper back")] = new(["upper-back"], ["traps"]),
            [Normalize("Face pull")] = new(["rear-delts"], ["upper-back", "traps"]),
            [Normalize("Shoulder rotation")] = new(["rear-delts"], ["side-delts"]),
            [Normalize("Ketelbel banca")] = new(["chest"], ["triceps", "front-delts"]),
            [Normalize("Ketelbel militar")] = new(["front-delts"], ["triceps"]),
            [Normalize("Flexiones")] = new(["chest"], ["triceps", "front-delts"]),
            [Normalize("Press pecho mancuernas")] = new(["chest"], ["triceps", "front-delts"]),
            [Normalize("Press militar con mancuernas")] = new(["front-delts"], ["triceps"]),
            [Normalize("Peso muerto a una pierna")] = new(["glutes", "hamstrings"], ["abductors"]),
            [Normalize("Leg raise")] = new(["abs"], ["hip-flexors"]),
            [Normalize("Flexiones anillas")] = new(["chest"], ["triceps", "front-delts"]),
            [Normalize("Biceps colgado")] = new(["biceps"], ["forearms"]),
            [Normalize("Hip thrust")] = new(["glutes"], ["hamstrings"]),
            [Normalize("Press inclinado mancuernas")] = new(["chest"], ["front-delts", "triceps"]),
            [Normalize("Biceps mancuernas")] = new(["biceps"], ["forearms"]),
            [Normalize("Elevaciones hombro")] = new(["side-delts"], ["traps"]),
            [Normalize("Press banca inclinado")] = new(["chest"], ["front-delts", "triceps"]),
            [Normalize("Press banca mancuernas")] = new(["chest"], ["front-delts", "triceps"]),
            [Normalize("Triceps ketelbel")] = new(["triceps"], ["front-delts"]),
            [Normalize("Mariposas")] = new(["chest"], ["front-delts"]),
            [Normalize("Polea")] = new(["upper-back"], ["lats"]),
            [Normalize("Remo ketelbel")] = new(["upper-back"], ["lats", "biceps"]),
            [Normalize("Praise de pecho")] = new(["chest"], ["front-delts"]),
            [Normalize("Biceps polea")] = new(["biceps"], ["forearms"]),
            [Normalize("Triceps polea")] = new(["triceps"], ["front-delts"]),
            [Normalize("KB one arm swing")] = new(["glutes"], ["hamstrings"]),
            [Normalize("Clean n press one arm")] = new(["front-delts", "glutes"], ["triceps", "traps"]),
            [Normalize("Puente de gluteos")] = new(["glutes"], ["hamstrings"]),
            [Normalize("Kettlebell - Clean and Press")] = new(["front-delts", "glutes"], ["triceps", "traps"]),
            [Normalize("Kettlebell - Sentadilla")] = new(["quads"], ["glutes"]),
            [Normalize("Peso muerto rumano")] = new(["hamstrings"], ["glutes"]),
            [Normalize("Hack Squat")] = new(["quads"], ["glutes"])
        };

    public static async Task UpsertAsync(MainDbContext db, CancellationToken cancellationToken = default)
    {
        var existing = await db.Muscles.ToListAsync(cancellationToken);
        var matchedMuscles = new HashSet<Muscle>();

        foreach (var definition in CanonicalMuscles)
        {
            var muscle = existing.FirstOrDefault(item =>
                !matchedMuscles.Contains(item) &&
                (string.Equals(item.Slug, definition.Slug, StringComparison.OrdinalIgnoreCase) ||
                 TryResolveSlug(item.Name) == definition.Slug));

            if (muscle is null)
            {
                muscle = new Muscle();
                db.Muscles.Add(muscle);
                existing.Add(muscle);
            }

            muscle.Slug = definition.Slug;
            muscle.Name = definition.Name;
            muscle.Side = definition.Side;
            muscle.MuscleGroup = definition.MuscleGroup;
            muscle.SortOrder = definition.SortOrder;
            matchedMuscles.Add(muscle);
        }

        foreach (var legacyMuscle in existing.Where(item => !matchedMuscles.Contains(item)))
        {
            legacyMuscle.Slug = $"legacy-{legacyMuscle.Id}";
            legacyMuscle.SortOrder = 0;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task BackfillExerciseTypeAssignmentsAsync(MainDbContext db, CancellationToken cancellationToken = default)
    {
        var musclesBySlug = await db.Muscles
            .Where(muscle => muscle.SortOrder > 0 && !string.IsNullOrWhiteSpace(muscle.Slug))
            .ToDictionaryAsync(muscle => muscle.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var exerciseTypes = await db.ExerciseTypes
            .Include(exerciseType => exerciseType.MuscleAssignments)
            .ToListAsync(cancellationToken);

        foreach (var exerciseType in exerciseTypes)
        {
            if (exerciseType.MuscleAssignments.Count > 0)
            {
                continue;
            }

            var blueprint = ResolveBlueprint(exerciseType.Name);
            if (blueprint is null)
            {
                continue;
            }

            var primaryMuscles = blueprint.PrimarySlugs
                .Where(slug => musclesBySlug.ContainsKey(slug))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (primaryMuscles.Count == 0)
            {
                continue;
            }

            var secondaryMuscles = blueprint.SecondarySlugs
                .Where(slug => musclesBySlug.ContainsKey(slug))
                .Where(slug => !primaryMuscles.Contains(slug, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sortOrder = 1;
            foreach (var slug in primaryMuscles)
            {
                exerciseType.MuscleAssignments.Add(new ExerciseTypeMuscleAssignment
                {
                    ExerciseTypeId = exerciseType.Id,
                    MuscleId = musclesBySlug[slug].Id,
                    Role = ExerciseMuscleRole.Primary,
                    SortOrder = sortOrder++
                });
            }

            sortOrder = 1;
            foreach (var slug in secondaryMuscles)
            {
                exerciseType.MuscleAssignments.Add(new ExerciseTypeMuscleAssignment
                {
                    ExerciseTypeId = exerciseType.Id,
                    MuscleId = musclesBySlug[slug].Id,
                    Role = ExerciseMuscleRole.Secondary,
                    SortOrder = sortOrder++
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static string? TryResolveSlug(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var normalized = Normalize(source);
        return MuscleSlugAliases.TryGetValue(normalized, out var slug)
            ? slug
            : null;
    }

    private static ExerciseMuscleBlueprint? ResolveBlueprint(string? exerciseName)
    {
        if (string.IsNullOrWhiteSpace(exerciseName))
        {
            return null;
        }

        var normalized = Normalize(exerciseName);
        if (ExerciseBlueprints.TryGetValue(normalized, out var blueprint))
        {
            return blueprint;
        }

        if (normalized.Contains("bench") || normalized.Contains("pressbanca") || normalized.Contains("pecho"))
        {
            return new ExerciseMuscleBlueprint(["chest"], ["front-delts", "triceps"]);
        }

        if (normalized.Contains("militar") || normalized.Contains("shoulderpress"))
        {
            return new ExerciseMuscleBlueprint(["front-delts"], ["triceps"]);
        }

        if (normalized.Contains("curl") || normalized.Contains("biceps"))
        {
            return new ExerciseMuscleBlueprint(["biceps"], ["forearms"]);
        }

        if (normalized.Contains("triceps"))
        {
            return new ExerciseMuscleBlueprint(["triceps"], ["front-delts"]);
        }

        if (normalized.Contains("remo") || normalized.Contains("row"))
        {
            return new ExerciseMuscleBlueprint(["upper-back"], ["lats", "biceps"]);
        }

        if (normalized.Contains("dominada") || normalized.Contains("pullup") || normalized.Contains("jalon"))
        {
            return new ExerciseMuscleBlueprint(["lats"], ["biceps"]);
        }

        if (normalized.Contains("sentadilla") || normalized.Contains("squat") || normalized.Contains("lunge") || normalized.Contains("zancada"))
        {
            return new ExerciseMuscleBlueprint(["quads"], ["glutes", "hamstrings"]);
        }

        if (normalized.Contains("pesomuerto") || normalized.Contains("deadlift"))
        {
            return new ExerciseMuscleBlueprint(["glutes", "hamstrings"], ["upper-back"]);
        }

        if (normalized.Contains("glute") || normalized.Contains("hipthrust") || normalized.Contains("puente"))
        {
            return new ExerciseMuscleBlueprint(["glutes"], ["hamstrings"]);
        }

        if (normalized.Contains("legraise") || normalized.Contains("crunch") || normalized.Contains("situp"))
        {
            return new ExerciseMuscleBlueprint(["abs"], ["hip-flexors"]);
        }

        if (normalized.Contains("calf"))
        {
            return new ExerciseMuscleBlueprint(["calves"], []);
        }

        if (normalized.Contains("facepull"))
        {
            return new ExerciseMuscleBlueprint(["rear-delts"], ["upper-back", "traps"]);
        }

        return null;
    }

    private static string Normalize(string? value)
        => new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
}

public sealed record CanonicalMuscleDefinition(
    string Slug,
    string Name,
    MuscleSide Side,
    MuscleGroup MuscleGroup,
    int SortOrder);

public sealed record ExerciseMuscleBlueprint(
    IReadOnlyList<string> PrimarySlugs,
    IReadOnlyList<string> SecondarySlugs);
