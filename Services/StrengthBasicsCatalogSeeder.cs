#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// The "basics" strength set (t_358e09e9 K3): a SEEDED, EDITABLE tag
/// (`basic` / category `strength`) applied to the 5 canonical foundational
/// types — squat, deadlift, chest press, pull-up, muscle-up. Community-added
/// types keep working because KPIs are computed over tagged types with data,
/// so nothing here is hardcoded beyond the initial seed.
/// </summary>
public static class StrengthBasicsCatalogSeeder
{
    public const string BasicTagName = "basic";
    public const string BasicTagCategory = "strength";

    private sealed record Canonical(string DisplayName, string WeightType, string[] Aliases);

    private static readonly Canonical[] Fundamentals =
    [
        new("Squat", "Standard", ["squat", "back squat", "air squat"]),
        new("Deadlift", "Standard", ["deadlift"]),
        new("Chest Press", "Standard", ["chest press", "bench press"]),
        new("Pull-Up", "Bodyweight", ["pull-up", "pull up", "pullup"]),
        new("Muscle-Up", "Bodyweight", ["muscle-up", "muscleup"])
    ];

    /// <summary>
    /// Idempotent upsert: tag canonical types that already exist (by lowercase
    /// name/alias); create the missing ones with the tag. Existing user tags are
    /// never removed.
    /// </summary>
    public static async Task UpsertAsync(
        MainDbContext context,
        CancellationToken cancellationToken = default)
    {
        var types = await context.ExerciseTypes
            .ToListAsync(cancellationToken);

        foreach (var canonical in Fundamentals)
        {
            var matches = types
                .Where(type => canonical.Aliases.Contains(
                    (type.Name ?? string.Empty).Trim().ToLowerInvariant()))
                .ToList();

            if (matches.Count > 0)
            {
                foreach (var type in matches)
                {
                    EnsureBasicTag(context, type);
                }

                continue;
            }

            context.ExerciseTypes.Add(new ExerciseType
            {
                Name = canonical.DisplayName,
                Description = $"Foundational strength basic ({canonical.DisplayName}).",
                DifficultyLevel = 1,
                WeightType = canonical.WeightType,
                Tags = [new ExerciseTag { Name = BasicTagName, Category = BasicTagCategory }]
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureBasicTag(MainDbContext context, ExerciseType type)
    {
        type.Tags ??= [];

        if (type.Tags.Any(tag =>
                string.Equals(tag.Name, BasicTagName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        type.Tags.Add(new ExerciseTag
        {
            Name = BasicTagName,
            Category = BasicTagCategory
        });

        // List<ExerciseTag> is a longtext JSON column: EF change detection does
        // not see item additions, so the entity must be marked modified.
        context.Entry(type).State = EntityState.Modified;
    }
}
