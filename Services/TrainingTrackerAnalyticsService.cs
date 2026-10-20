using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// Coverage and readiness over the 9 Galpin adaptations
/// (docs/training-tracker-universal-design.md §5).
///
/// Adaptations trained by a logged exercise come from a per-session override
/// when the user declared an intention, otherwise from the canonical
/// exercise-type mapping. A session also counts towards its declared goal.
/// </summary>
public sealed class TrainingTrackerAnalyticsService(MainDbContext db)
{
    /// <summary>Adaptations this far apart in the canonical chain interfere.</summary>
    private const int InterferenceDistance = 4;

    /// <summary>High perceived fatigue blocks high neural-cost adaptations.</summary>
    private const double HighFatigueThreshold = 4.0;

    private static readonly GalpinAdaptationType[] HighNeuralCostAdaptations =
    [
        GalpinAdaptationType.Speed,
        GalpinAdaptationType.Power,
        GalpinAdaptationType.Strength
    ];

    public async Task<List<AdaptationResource>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var adaptations = await db.Adaptations.AsNoTracking().ToListAsync(cancellationToken);
        var counts = await db.ExerciseTypeAdaptations
            .AsNoTracking()
            .GroupBy(mapping => mapping.AdaptationId)
            .Select(group => new { AdaptationId = group.Key, Count = group.Select(mapping => mapping.ExerciseTypeId).Distinct().Count() })
            .ToListAsync(cancellationToken);
        var countsByAdaptation = counts.ToDictionary(item => item.AdaptationId, item => item.Count);

        return adaptations
            .OrderBy(adaptation => adaptation.Order)
            .Select(adaptation => new AdaptationResource
            {
                Id = adaptation.Id,
                Order = adaptation.Order,
                Type = adaptation.Type.ToString(),
                Name = adaptation.Name,
                Description = adaptation.Description,
                Color = adaptation.Color,
                TargetSessionsPerWeek = adaptation.TargetSessionsPerWeek,
                ExerciseTypeCount = countsByAdaptation.GetValueOrDefault(adaptation.Id)
            })
            .ToList();
    }

    public async Task<AdaptationMappingResource?> GetTypeMappingAsync(
        int exerciseTypeId,
        CancellationToken cancellationToken)
    {
        var exerciseType = await db.ExerciseTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(type => type.Id == exerciseTypeId, cancellationToken);
        if (exerciseType is null)
        {
            return null;
        }

        var mappings = await db.ExerciseTypeAdaptations
            .AsNoTracking()
            .Where(mapping => mapping.ExerciseTypeId == exerciseTypeId)
            .Include(mapping => mapping.Adaptation)
            .ToListAsync(cancellationToken);

        return BuildMapping(exerciseTypeId, mappings);
    }

    /// <summary>
    /// Replaces the canonical mapping of an exercise type. The mapping is global
    /// (a catalog edit), not per user.
    /// </summary>
    public async Task<AdaptationMappingResource?> SetTypeMappingAsync(
        int exerciseTypeId,
        AdaptationMappingInputResource input,
        CancellationToken cancellationToken)
    {
        var exerciseType = await db.ExerciseTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(type => type.Id == exerciseTypeId, cancellationToken);
        if (exerciseType is null)
        {
            return null;
        }

        var adaptations = await db.Adaptations.AsNoTracking().ToListAsync(cancellationToken);
        var byType = adaptations.ToDictionary(adaptation => adaptation.Type, adaptation => adaptation);

        var existing = await db.ExerciseTypeAdaptations
            .Where(mapping => mapping.ExerciseTypeId == exerciseTypeId)
            .ToListAsync(cancellationToken);
        db.ExerciseTypeAdaptations.RemoveRange(existing);

        foreach (var (role, names) in new[]
        {
            (AdaptationRole.Primary, input.Primary ?? []),
            (AdaptationRole.Secondary, input.Secondary ?? [])
        })
        {
            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse(name, ignoreCase: true, out GalpinAdaptationType parsed)
                    || !byType.TryGetValue(parsed, out var adaptation))
                {
                    continue;
                }

                db.ExerciseTypeAdaptations.Add(new ExerciseTypeAdaptation
                {
                    ExerciseTypeId = exerciseTypeId,
                    AdaptationId = adaptation.Id,
                    Role = role
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var saved = await db.ExerciseTypeAdaptations
            .AsNoTracking()
            .Where(mapping => mapping.ExerciseTypeId == exerciseTypeId)
            .Include(mapping => mapping.Adaptation)
            .ToListAsync(cancellationToken);

        return BuildMapping(exerciseTypeId, saved);
    }

    public async Task<AdaptationCoverageResource> GetCoverageAsync(
        string userId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var from = fromUtc.Date;
        var to = toUtc.Date;
        if (to <= from)
        {
            to = from.AddDays(1);
        }

        var span = to - from;
        var previousFrom = from - span;

        var sessions = await LoadSessionsAsync(userId, previousFrom, to, cancellationToken);
        var current = sessions.Where(session => session.Workout.Date >= from).ToList();
        var previous = sessions.Where(session => session.Workout.Date < from).ToList();

        var adaptations = await db.Adaptations.AsNoTracking().ToListAsync(cancellationToken);
        var currentAggregates = Aggregate(current);
        var previousAggregates = Aggregate(previous);

        var items = adaptations
            .OrderBy(adaptation => adaptation.Order)
            .Select(adaptation =>
            {
                var aggregate = currentAggregates.GetValueOrDefault(adaptation.Type, new AdaptationAggregate());
                var previousVolume = previousAggregates.GetValueOrDefault(adaptation.Type, new AdaptationAggregate()).Volume;
                var target = adaptation.TargetSessionsPerWeek;

                return new AdaptationCoverageItemResource
                {
                    AdaptationId = adaptation.Id,
                    Order = adaptation.Order,
                    Type = adaptation.Type.ToString(),
                    Name = adaptation.Name,
                    Color = adaptation.Color,
                    Volume = TrainingVolume.Round(aggregate.Volume),
                    Sessions = aggregate.Sessions,
                    TargetSessionsPerWeek = target,
                    CoverageRatio = target > 0
                        ? Math.Round((double)aggregate.Sessions / target, 2, MidpointRounding.AwayFromZero)
                        : aggregate.Sessions > 0 ? 1 : 0,
                    LastSessionDate = aggregate.LastSessionDate,
                    DaysSinceLastSession = aggregate.LastSessionDate is null
                        ? null
                        : (int?)(to.Date - aggregate.LastSessionDate.Value.Date).TotalDays,
                    Trend = TrendOf(aggregate.Volume, previousVolume)
                };
            })
            .ToList();

        return new AdaptationCoverageResource
        {
            From = from,
            To = to.AddDays(-1),
            Sessions = current.Count,
            Items = items
        };
    }

    public async Task<ReadinessResource> GetReadinessAsync(
        string userId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var asOf = asOfUtc.Date;
        var from = asOf.AddDays(-6);
        var previousFrom = asOf.AddDays(-13);

        var loaded = await LoadSessionsAsync(userId, previousFrom, asOf.AddDays(1), cancellationToken);
        var last7Days = loaded.Where(session => session.Workout.Date >= from).ToList();
        var previous7Days = loaded.Where(session => session.Workout.Date < from).ToList();

        var adaptations = await db.Adaptations.AsNoTracking().ToListAsync(cancellationToken);
        var currentAggregates = Aggregate(last7Days);
        var previousAggregates = Aggregate(previous7Days);

        var fatigueValues = last7Days
            .Select(session => session.Workout.PerceivedFatigue)
            .Where(fatigue => fatigue.HasValue)
            .Select(fatigue => (double)fatigue!.Value)
            .ToList();
        var fatigueLoad = fatigueValues.Count > 0
            ? Math.Round(fatigueValues.Average(), 2, MidpointRounding.AwayFromZero)
            : 0;

        var totalVolume = TrainingVolume.Round(TotalVolumeOf(last7Days));

        var perAdaptation = adaptations
            .OrderBy(adaptation => adaptation.Order)
            .Select(adaptation =>
            {
                var aggregate = currentAggregates.GetValueOrDefault(adaptation.Type, new AdaptationAggregate());
                var previousVolume = previousAggregates.GetValueOrDefault(adaptation.Type, new AdaptationAggregate()).Volume;

                return new ReadinessAdaptationResource
                {
                    AdaptationId = adaptation.Id,
                    Order = adaptation.Order,
                    Type = adaptation.Type.ToString(),
                    Name = adaptation.Name,
                    Color = adaptation.Color,
                    Volume7d = TrainingVolume.Round(aggregate.Volume),
                    Sessions7d = aggregate.Sessions,
                    LastSessionDate = aggregate.LastSessionDate,
                    DaysSinceLastSession = aggregate.LastSessionDate is null
                        ? null
                        : (int?)(asOf - aggregate.LastSessionDate.Value.Date).TotalDays,
                    Trend = TrendOf(aggregate.Volume, previousVolume)
                };
            })
            .ToList();

        var score = ComputeScore(fatigueLoad, last7Days.Count, totalVolume);

        return new ReadinessResource
        {
            Date = asOf,
            Score = score,
            FatigueLoad7d = fatigueLoad,
            Sessions7d = last7Days.Count,
            TotalVolume7d = totalVolume,
            PerAdaptation = perAdaptation,
            Suggestions = BuildSuggestions(adaptations, currentAggregates, last7Days, fatigueLoad, asOf)
        };
    }

    /// <summary>
    /// Deterministic v1 score: start at 10 and subtract perceived fatigue,
    /// detraining (no sessions), missing recovery days and volume spikes.
    /// </summary>
    private static double ComputeScore(double fatigueLoad, int sessions, double totalVolume)
    {
        var score = 10d;

        score += fatigueLoad switch
        {
            >= 4.5 => -4,
            >= 3.5 => -2.5,
            >= 2.5 => -1,
            _ => 0
        };

        if (sessions == 0)
        {
            score -= 1.5;
        }
        else if (sessions >= 7)
        {
            score -= 1;
        }

        score += totalVolume switch
        {
            >= 30000 => -1.5,
            >= 20000 => -0.5,
            _ => 0
        };

        return Math.Round(Math.Clamp(score, 0, 10), 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Suggest the least covered adaptation that is compatible with what was
    /// trained in the last two days (Chart of Interference) and with the current
    /// fatigue level.
    /// </summary>
    private static List<ReadinessSuggestionResource> BuildSuggestions(
        List<Adaptation> adaptations,
        Dictionary<GalpinAdaptationType, AdaptationAggregate> aggregates,
        List<TrainingSession> last7Days,
        double fatigueLoad,
        DateTime asOf)
    {
        var recentOrders = last7Days
            .Where(session => session.Workout.Date >= asOf.AddDays(-2))
            .SelectMany(session => session.Adaptations)
            .Select(adaptation => (int)adaptation + 1)
            .ToHashSet();

        var suggestions = new List<ReadinessSuggestionResource>();

        foreach (var adaptation in adaptations
                     .OrderBy(candidate => CoverageRatioOf(aggregates.GetValueOrDefault(candidate.Type, new AdaptationAggregate()), candidate.TargetSessionsPerWeek))
                     .ThenBy(candidate => candidate.Order))
        {
            var aggregate = aggregates.GetValueOrDefault(adaptation.Type, new AdaptationAggregate());

            if (recentOrders.Contains(adaptation.Order))
            {
                continue;
            }

            if (recentOrders.Any(order => Math.Abs(order - adaptation.Order) >= InterferenceDistance))
            {
                continue;
            }

            if (fatigueLoad >= HighFatigueThreshold && HighNeuralCostAdaptations.Contains(adaptation.Type))
            {
                continue;
            }

            var target = adaptation.TargetSessionsPerWeek;
            var reason = aggregate.Sessions == 0
                ? $"Sin sesiones en los últimos 7 días (objetivo: {target}/semana)."
                : $"Cobertura baja: {aggregate.Sessions}/{target} sesiones en los últimos 7 días.";

            if (fatigueLoad >= HighFatigueThreshold)
            {
                reason += " Fatiga alta: prioriza baja carga neural.";
            }

            suggestions.Add(new ReadinessSuggestionResource
            {
                AdaptationId = adaptation.Id,
                Order = adaptation.Order,
                Type = adaptation.Type.ToString(),
                Name = adaptation.Name,
                Color = adaptation.Color,
                Reason = reason
            });

            if (suggestions.Count >= 2)
            {
                break;
            }
        }

        return suggestions;
    }

    private static double CoverageRatioOf(AdaptationAggregate aggregate, int target)
        => target > 0
            ? (double)aggregate.Sessions / target
            : aggregate.Sessions > 0 ? 1 : 0;

    private static string TrendOf(double currentVolume, double previousVolume)
    {
        if (currentVolume == 0 && previousVolume == 0)
        {
            return "none";
        }

        if (currentVolume > previousVolume * 1.1)
        {
            return "up";
        }

        if (currentVolume < previousVolume * 0.9)
        {
            return "down";
        }

        return "flat";
    }

    private static double TotalVolumeOf(List<TrainingSession> sessions)
        => sessions
            .SelectMany(session => session.Exercises)
            .Sum(exercise => TrainingVolume.ForAdaptation(
                exercise.Exercise,
                exercise.Adaptations.Count > 0
                    ? exercise.Adaptations[0]
                    : GalpinAdaptationType.Hypertrophy));

    /// <summary>
    /// Volume and session count per adaptation. An exercise mapped to a primary
    /// and a secondary adaptation contributes to both.
    /// </summary>
    private static Dictionary<GalpinAdaptationType, AdaptationAggregate> Aggregate(List<TrainingSession> sessions)
    {
        var aggregates = new Dictionary<GalpinAdaptationType, AdaptationAggregate>();

        foreach (var session in sessions)
        {
            var sessionAdaptations = new HashSet<GalpinAdaptationType>();

            foreach (var exercise in session.Exercises)
            {
                foreach (var adaptation in exercise.Adaptations)
                {
                    var aggregate = aggregates.GetValueOrDefault(adaptation, new AdaptationAggregate());
                    aggregate.Volume += TrainingVolume.ForAdaptation(exercise.Exercise, adaptation);
                    aggregate.LastSessionDate = MaxDate(aggregate.LastSessionDate, session.Workout.Date);
                    aggregates[adaptation] = aggregate;
                    sessionAdaptations.Add(adaptation);
                }
            }

            if (session.Workout.Goal.HasValue)
            {
                sessionAdaptations.Add(session.Workout.Goal.Value);
            }

            foreach (var secondaryGoal in session.Workout.SecondaryGoals ?? [])
            {
                sessionAdaptations.Add(secondaryGoal);
            }

            foreach (var adaptation in sessionAdaptations)
            {
                var aggregate = aggregates.GetValueOrDefault(adaptation, new AdaptationAggregate());
                aggregate.Sessions += 1;
                aggregate.LastSessionDate = MaxDate(aggregate.LastSessionDate, session.Workout.Date);
                aggregates[adaptation] = aggregate;
            }
        }

        return aggregates;
    }

    private static DateTime? MaxDate(DateTime? left, DateTime? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        return left.Value >= right.Value ? left : right;
    }

    private static AdaptationMappingResource BuildMapping(
        int exerciseTypeId,
        List<ExerciseTypeAdaptation> mappings)
        => new()
        {
            ExerciseTypeId = exerciseTypeId,
            Primary = mappings
                .Where(mapping => mapping.Role == AdaptationRole.Primary && mapping.Adaptation is not null)
                .Select(mapping => mapping.Adaptation.Type.ToString())
                .Distinct()
                .ToList(),
            Secondary = mappings
                .Where(mapping => mapping.Role == AdaptationRole.Secondary && mapping.Adaptation is not null)
                .Select(mapping => mapping.Adaptation.Type.ToString())
                .Distinct()
                .ToList()
        };

    /// <summary>
    /// Flat queries with explicit user scoping instead of navigation includes:
    /// the tracker aggregates a trainee's sessions for their trainer, and the
    /// per-entity query filters are user-scoped.
    /// </summary>
    private async Task<List<TrainingSession>> LoadSessionsAsync(
        string userId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var workouts = await db.Workouts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(workout => workout.UserId == userId && workout.Date >= fromUtc && workout.Date < toUtc)
            .ToListAsync(cancellationToken);

        if (workouts.Count == 0)
        {
            return [];
        }

        var workoutIds = workouts.Select(workout => workout.Id).ToList();

        var exercises = await db.Exercises
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(exercise => exercise.WorkoutId.HasValue && workoutIds.Contains(exercise.WorkoutId.Value))
            .ToListAsync(cancellationToken);

        var exerciseIds = exercises.Select(exercise => exercise.Id).ToList();

        var typeIds = exercises
            .Where(exercise => exercise.TypeId.HasValue)
            .Select(exercise => exercise.TypeId!.Value)
            .Distinct()
            .ToList();

        var typeMappings = typeIds.Count == 0
            ? []
            : await db.ExerciseTypeAdaptations
                .AsNoTracking()
                .Where(mapping => typeIds.Contains(mapping.ExerciseTypeId))
                .Include(mapping => mapping.Adaptation)
                .ToListAsync(cancellationToken);

        var overrides = exerciseIds.Count == 0
            ? []
            : await db.ExerciseAdaptations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(adaptation => exerciseIds.Contains(adaptation.ExerciseId))
                .Include(adaptation => adaptation.Adaptation)
                .ToListAsync(cancellationToken);

        var adaptationsById = (await db.Adaptations.AsNoTracking().ToListAsync(cancellationToken))
            .ToDictionary(adaptation => adaptation.Id, adaptation => adaptation.Type);

        var mappingsByType = typeMappings
            .GroupBy(mapping => mapping.ExerciseTypeId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(mapping => mapping.Role == AdaptationRole.Primary ? 0 : 1)
                    .ThenBy(mapping => mapping.Adaptation?.Order ?? int.MaxValue)
                    .Select(mapping => mapping.Adaptation)
                    .Where(adaptation => adaptation is not null)
                    .Select(adaptation => adaptation.Type)
                    .Distinct()
                    .ToList());

        var overridesByExercise = overrides
            .GroupBy(adaptation => adaptation.ExerciseId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(adaptation => adaptation.Adaptation)
                    .Where(adaptation => adaptation is not null)
                    .OrderBy(adaptation => adaptation.Order)
                    .Select(adaptation => adaptation.Type)
                    .Distinct()
                    .ToList());

        return workouts
            .Select(workout => new TrainingSession(
                workout,
                exercises
                    .Where(exercise => exercise.WorkoutId == workout.Id)
                    .OrderBy(exercise => exercise.Order ?? int.MaxValue)
                    .ThenBy(exercise => exercise.Id)
                    .Select(exercise => (
                        exercise,
                        overridesByExercise.GetValueOrDefault(exercise.Id, [])
                            .Concat(mappingsByType.GetValueOrDefault(exercise.TypeId ?? 0, []))
                            .Distinct()
                            .ToList()))
                    .ToList()))
            .OrderBy(session => session.Workout.Date)
            .ToList();
    }

    private sealed record TrainingSession(
        Workout Workout,
        List<(Exercise Exercise, List<GalpinAdaptationType> Adaptations)> Exercises)
    {
        public IEnumerable<GalpinAdaptationType> Adaptations => Exercises.SelectMany(exercise => exercise.Adaptations);
    }

    private sealed class AdaptationAggregate
    {
        public double Volume { get; set; }
        public int Sessions { get; set; }
        public DateTime? LastSessionDate { get; set; }
    }
}
