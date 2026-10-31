#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// Body-composition metric catalog (t_358e09e9 K2). The only seeded definition
/// today is weight_kg; the shared definition table is the extension point for
/// future composition metrics (the readings table is metric-keyed for that).
/// </summary>
public static class BodyCompositionCatalog
{
    public const string WeightKgKey = "weight_kg";

    /// <summary>
    /// Finds the weight_kg definition or stages a new one on the given context
    /// (NOT saved: the caller saves it atomically with the first reading).
    /// </summary>
    public static async Task<HealthMetricDefinition> EnsureWeightKgDefinitionAsync(
        MainDbContext context,
        CancellationToken cancellationToken)
    {
        var existing = await context.HealthMetricDefinitions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                definition => definition.Key == WeightKgKey,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = new HealthMetricDefinition
        {
            Key = WeightKgKey,
            DisplayName = "Weight",
            DefaultUnit = "kg",
            Category = HealthMetricCategory.BodyComposition,
            Description = "Registered body weight (kg), time series via BodyCompositionReading."
        };

        context.HealthMetricDefinitions.Add(created);
        return created;
    }
}

/// <summary>Startup upsert so the definition exists before any reading is written.</summary>
public static class BodyCompositionCatalogSeeder
{
    public static async Task UpsertAsync(
        MainDbContext context,
        CancellationToken cancellationToken = default)
    {
        await BodyCompositionCatalog.EnsureWeightKgDefinitionAsync(
            context,
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}
