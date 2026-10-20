/// <summary>
/// Volume proxy for the universal training tracker (design §5).
/// There is no duration model yet, so the proxy is:
/// - tonnage (sets x reps x load) for strength/hypertrophy/endurance work,
///   bodyweight counting as load,
/// - attempt count for skill/speed/power work, where the number of quality
///   attempts is the training signal (a muscle-up round logged 8/0 has volume 8
///   attempts and 0 completed attempts).
/// </summary>
public static class TrainingVolume
{
    /// <summary>Adaptations whose work signal is attempts instead of tonnage.</summary>
    private const int AttemptBasedOrderCeiling = 3;

    public static double ForAdaptation(Exercise exercise, GalpinAdaptationType adaptation)
        => ForAdaptation(exercise, AdaptationCatalogSeeder.OrderOf(adaptation));

    public static double ForAdaptation(Exercise exercise, int adaptationOrder)
    {
        if (exercise is null)
        {
            return 0;
        }

        var attempts = exercise.Attempts ?? 0;
        var sets = exercise.Series ?? (attempts > 0 ? 1 : 0);
        var reps = exercise.Repetitions ?? 0;
        var load = exercise.Weight ?? exercise.AddedWeight ?? exercise.BodyWeight ?? 0;

        if (adaptationOrder <= AttemptBasedOrderCeiling)
        {
            return attempts > 0 ? attempts : sets * reps;
        }

        if (sets > 0 && reps > 0)
        {
            return sets * reps * (load > 0 ? load : 1);
        }

        if (attempts > 0)
        {
            return attempts;
        }

        return sets + reps;
    }

    public static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
