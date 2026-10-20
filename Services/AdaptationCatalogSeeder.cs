using System.Text;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Seeds the 9 Galpin adaptations in canonical order and backfills the canonical
/// exercise-type mapping heuristically from exercise names/tags. The mapping is
/// editable afterwards (PUT /adaptations/exercise-types/{id}), and the backfill
/// only touches types that have no mapping yet, so edits are never overwritten.
/// </summary>
public static class AdaptationCatalogSeeder
{
    public record CanonicalAdaptation(
        GalpinAdaptationType Type,
        string Name,
        string Description,
        string Color,
        int TargetSessionsPerWeek);

    /// <summary>Canonical Galpin order. The position is functional, not cosmetic.</summary>
    public static readonly IReadOnlyList<CanonicalAdaptation> CanonicalAdaptations =
    [
        new(GalpinAdaptationType.Skill, "Habilidad motora", "Coordinación, precisión y patrones complejos.", "#8B5CF6", 2),
        new(GalpinAdaptationType.Speed, "Velocidad", "Desplazamientos máximos y cambios de dirección.", "#F59E0B", 1),
        new(GalpinAdaptationType.Power, "Potencia", "Producción máxima de fuerza en el menor tiempo.", "#EF4444", 1),
        new(GalpinAdaptationType.Strength, "Fuerza máxima", "Fuerza absoluta, cargas altas, bajas repeticiones.", "#B91C1C", 2),
        new(GalpinAdaptationType.Hypertrophy, "Hipertrofia", "Volumen muscular, tensión mecánica y estrés.", "#F97316", 2),
        new(GalpinAdaptationType.MuscularEndurance, "Resistencia muscular", "Repeticiones prolongadas contra resistencia.", "#22C55E", 2),
        new(GalpinAdaptationType.AnaerobicCapacity, "Capacidad anaeróbica", "Esfuerzos intensos de 30 s a 2 min sin oxígeno.", "#06B6D4", 1),
        new(GalpinAdaptationType.MaximalAerobicPower, "Potencia aeróbica máxima", "Intensidades altas sostenidas, VO2 máximo.", "#3B82F6", 2),
        new(GalpinAdaptationType.LongDurationEndurance, "Resistencia de larga duración", "Esfuerzos continuos de baja-media intensidad.", "#0EA5E9", 2)
    ];

    public static int IdOf(GalpinAdaptationType type) => (int)type + 1;
    public static int OrderOf(GalpinAdaptationType type) => (int)type + 1;

    public static async Task UpsertAsync(MainDbContext db, CancellationToken cancellationToken = default)
    {
        var existing = await db.Adaptations.ToListAsync(cancellationToken);

        foreach (var definition in CanonicalAdaptations)
        {
            var id = IdOf(definition.Type);
            var adaptation = existing.FirstOrDefault(item => item.Id == id);

            if (adaptation is null)
            {
                db.Adaptations.Add(new Adaptation
                {
                    Id = id,
                    Type = definition.Type,
                    Order = OrderOf(definition.Type),
                    Name = definition.Name,
                    Description = definition.Description,
                    Color = definition.Color,
                    TargetSessionsPerWeek = definition.TargetSessionsPerWeek
                });

                continue;
            }

            // Names/colors/targets follow the catalog; a renamed adaptation keeps
            // its identity because downstream rows reference the id.
            adaptation.Type = definition.Type;
            adaptation.Order = OrderOf(definition.Type);
            adaptation.Name = definition.Name;
            adaptation.Description = definition.Description;
            adaptation.Color = definition.Color;
            adaptation.TargetSessionsPerWeek = definition.TargetSessionsPerWeek;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task BackfillExerciseTypeAdaptationsAsync(MainDbContext db, CancellationToken cancellationToken = default)
    {
        var adaptations = await db.Adaptations.ToListAsync(cancellationToken);
        var adaptationsByType = adaptations.ToDictionary(adaptation => adaptation.Type, adaptation => adaptation.Id);

        var unmapped = await db.ExerciseTypes
            .Where(exerciseType => !db.ExerciseTypeAdaptations.Any(mapping => mapping.ExerciseTypeId == exerciseType.Id))
            .ToListAsync(cancellationToken);

        foreach (var exerciseType in unmapped)
        {
            foreach (var (adaptationType, role) in InferAdaptations(exerciseType))
            {
                if (!adaptationsByType.TryGetValue(adaptationType, out var adaptationId))
                {
                    continue;
                }

                db.ExerciseTypeAdaptations.Add(new ExerciseTypeAdaptation
                {
                    ExerciseTypeId = exerciseType.Id,
                    AdaptationId = adaptationId,
                    Role = role
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Heuristic v1: keywords decide the primary adaptation, the neighbour in the
    /// canonical chain becomes the secondary. Unknown weighted exercises fall back
    /// to hypertrophy, bodyweight work to muscular endurance.
    /// </summary>
    public static IReadOnlyList<(GalpinAdaptationType Adaptation, AdaptationRole Role)> InferAdaptations(ExerciseType exerciseType)
    {
        var haystack = Normalise(
            string.Join(' ', [
                exerciseType.Name ?? string.Empty,
                exerciseType.Description ?? string.Empty,
                string.Join(' ', (exerciseType.Tags ?? []).Select(tag => tag.Name ?? string.Empty))
            ]));

        var weightType = (exerciseType.WeightType ?? string.Empty).Trim().ToLowerInvariant();
        var isBodyweight = weightType.Contains("bodyweight") || weightType.Contains("body weight") || weightType == "peso corporal";

        GalpinAdaptationType? primary = null;

        if (Matches(haystack, SkillKeywords)) primary = GalpinAdaptationType.Skill;
        else if (Matches(haystack, SpeedKeywords)) primary = GalpinAdaptationType.Speed;
        else if (haystack.Contains("jump rope") || haystack.Contains("skipping")) primary = GalpinAdaptationType.MuscularEndurance;
        else if (Matches(haystack, PowerKeywords)) primary = GalpinAdaptationType.Power;
        else if (Matches(haystack, AnaerobicKeywords)) primary = GalpinAdaptationType.AnaerobicCapacity;
        else if (Matches(haystack, MaximalAerobicKeywords)) primary = GalpinAdaptationType.MaximalAerobicPower;
        else if (Matches(haystack, LongDurationKeywords)) primary = GalpinAdaptationType.LongDurationEndurance;
        else if (Matches(haystack, MuscularEnduranceKeywords)) primary = GalpinAdaptationType.MuscularEndurance;
        else if (Matches(haystack, StrengthKeywords) && !isBodyweight) primary = GalpinAdaptationType.Strength;
        else if (Matches(haystack, HypertrophyKeywords)) primary = GalpinAdaptationType.Hypertrophy;
        else if (isBodyweight) primary = GalpinAdaptationType.MuscularEndurance;
        else if (Matches(haystack, StrengthKeywords)) primary = GalpinAdaptationType.Strength;
        else if (!string.IsNullOrWhiteSpace(weightType)) primary = GalpinAdaptationType.Hypertrophy;

        if (primary is null)
        {
            return [];
        }

        var result = new List<(GalpinAdaptationType, AdaptationRole)> { (primary.Value, AdaptationRole.Primary) };

        var secondary = NeighbourOf(primary.Value);
        if (secondary is not null)
        {
            result.Add((secondary.Value, AdaptationRole.Secondary));
        }

        return result;
    }

    /// <summary>
    /// The adaptation most likely trained alongside the primary one: the next
    /// step in the canonical chain (strength feeds hypertrophy, hypertrophy
    /// feeds muscular endurance, ...).
    /// </summary>
    private static GalpinAdaptationType? NeighbourOf(GalpinAdaptationType adaptation)
        => adaptation switch
        {
            GalpinAdaptationType.Skill => GalpinAdaptationType.Speed,
            GalpinAdaptationType.Speed => GalpinAdaptationType.Power,
            GalpinAdaptationType.Power => GalpinAdaptationType.Strength,
            GalpinAdaptationType.Strength => GalpinAdaptationType.Hypertrophy,
            GalpinAdaptationType.Hypertrophy => GalpinAdaptationType.MuscularEndurance,
            GalpinAdaptationType.MuscularEndurance => GalpinAdaptationType.AnaerobicCapacity,
            GalpinAdaptationType.AnaerobicCapacity => GalpinAdaptationType.MaximalAerobicPower,
            GalpinAdaptationType.MaximalAerobicPower => GalpinAdaptationType.LongDurationEndurance,
            _ => null
        };

    private static readonly string[] SkillKeywords =
    [
        "muscle up", "muscleup", "handstand", "planche", "front lever", "back lever", "human flag",
        "rings", "anillas", "kipping", "giant swing", "technique", "skill", "drill", "jiu", "jitsu",
        "kata", "dribble", "juggling", "balance", "mobility", "movilidad", "stretch", "yoga", "pilates",
        "cartwheel", "walk on hands"
    ];

    private static readonly string[] SpeedKeywords =
    [
        "sprint", "velocidad", "agility", "agilidad", "change of direction", "cambio de direccion",
        "shuttle run", "acceleration", "acceleracion", "fast run", "dash"
    ];

    private static readonly string[] PowerKeywords =
    [
        "jump", "salto", "plyo", "plyometric", "pliometric", "box jump", "throw", "lanzamiento",
        "slam", "medicine ball", "clean and jerk", "clean", "snatch", "jerk", "push press",
        "kettlebell swing", "swing", "ballistic", "clubbell", "bat", "pogo"
    ];

    private static readonly string[] AnaerobicKeywords =
    [
        "hiit", "tabata", "all out", "allout", "interval", "intervalos", "battle rope", "rope slam",
        "assault bike", "air bike", "sled push", "sled drag", "prowler", "circuit training", "crossfit"
    ];

    private static readonly string[] MaximalAerobicKeywords =
    [
        "vo2", "tempo run", "threshold", "lactate threshold", "rowing", "row machine", "ergometer",
        "erg", "ski erg", "stair mill", "running intervals", "400m", "800m", "1500m"
    ];

    private static readonly string[] LongDurationKeywords =
    [
        "run", "running", "correr", "jog", "walk", "caminar", "hike", "hiking", "senderismo",
        "swim", "swimming", "nadar", "cycle", "cycling", "bicicleta", "bike", "spinning",
        "triathlon", "marathon", "maraton", "ultra", "trail", "rowing machine", "kayak", "paddle",
        "surf", "skate", "climbing machine", "stair climber", "elliptical", "eliptica"
    ];

    private static readonly string[] MuscularEnduranceKeywords =
    [
        "pushup", "push up", "pullup", "pull up", "chin up", "dip", "situp", "sit up", "crunch",
        "plank", "air squat", "bodyweight squat", "lunges", "mountain climber", "burpee", "jump rope",
        "skipping", "abs", "core", "high rep", "farmer carry", "suitcase carry", "hold", "isometric"
    ];

    private static readonly string[] StrengthKeywords =
    [
        "squat", "bench press", "bench", "deadlift", "overhead press", "shoulder press", "military press",
        "barbell row", "bent over row", "hip thrust", "glute bridge", "weight pull up", "weighted pull",
        "weighted dip", "front squat", "back squat", "sumo", "romanian", "good morning", "thruster",
        "clean and jerk", "snatch balance", "floor press", "zerc"
    ];

    private static readonly string[] HypertrophyKeywords =
    [
        "curl", "extension", "fly", "flye", "raise", "lateral raise", "rear delt raise", "pushdown",
        "cable", "machine", "leg press", "leg extension", "leg curl", "hip abduction", "adduction",
        "shrug", "calf raise", "triceps", "biceps", "lats", "pulldown", "lat pulldown", "fly machine",
        "iso", "single arm", "dumbbell"
    ];

    private static bool Matches(string haystack, string[] keywords)
        => keywords.Any(keyword => haystack.Contains(keyword, StringComparison.Ordinal));

    private static string Normalise(string value)
    {
        var lowered = value.ToLowerInvariant();
        var span = new char[lowered.Length];
        var length = 0;

        foreach (var character in lowered.Normalize(NormalizationForm.FormD))
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character);
            if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                span[length++] = character;
            }
        }

        return new string(span, 0, length).Normalize(NormalizationForm.FormC);
    }
}
