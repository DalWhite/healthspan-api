#nullable enable

using Controllers.ApiContracts.Results;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// Monthly coach↔trainee health-result KPIs (spec docs/results-health-kpis-t_358e09e9.md).
/// Pure + deterministic: same DB state + same TimeProvider ⇒ same response.
/// Everything UTC; TimeProvider is injected so tests pin "today".
///
/// Authorization is NOT this service's job: the controller's [TraineeAccess]
/// filter must have validated owner/direct-trainer BEFORE any call here, which
/// is what justifies the IgnoreQueryFilters reads below.
/// </summary>
public sealed class ResultsService(MainDbContext context, TimeProvider timeProvider)
{
    public async Task<MonthlyResultsResource> GetMonthAsync(
        string traineeId,
        DateOnly month,
        CancellationToken cancellationToken)
    {
        var data = await LoadDataAsync(traineeId, cancellationToken);
        var monthStart = new DateTime(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var today = timeProvider.GetUtcNow().UtcDateTime.Date;

        var (consistency, weight, strength) = ComputeMonth(
            monthStart, data, today);

        return new MonthlyResultsResource
        {
            TraineeId = traineeId,
            GeneratedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Month = month.ToString("yyyy-MM"),
            Consistency = consistency,
            Weight = weight,
            Strength = strength
        };
    }

    public async Task<ResultsSeriesResource> GetSeriesAsync(
        string traineeId,
        int months,
        CancellationToken cancellationToken)
    {
        var data = await LoadDataAsync(traineeId, cancellationToken);
        var today = timeProvider.GetUtcNow().UtcDateTime.Date;
        var currentMonthStart = new DateTime(
            today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var monthStarts = Enumerable.Range(0, months)
            .Select(offset => currentMonthStart.AddMonths(offset - (months - 1)))
            .ToList();

        var series = new ResultsSeriesResource
        {
            TraineeId = traineeId,
            GeneratedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        var strengthPointsByType = new Dictionary<int, List<StrengthTypePoint>>();

        foreach (var monthStart in monthStarts)
        {
            var label = monthStart.ToString("yyyy-MM");
            series.Months.Add(label);

            var (consistency, weight, strength) = ComputeMonth(
                monthStart, data, today);

            series.Consistency.Add(consistency);
            series.Weight.Add(weight);
            series.Strength.IndexPct.Add(new StrengthIndexPoint
            {
                Month = label,
                State = strength.State,
                Value = strength.IndexPct
            });

            foreach (var type in strength.ByType)
            {
                if (!strengthPointsByType.TryGetValue(type.TypeId, out var points))
                {
                    points = [];
                    strengthPointsByType[type.TypeId] = points;
                }

                points.Add(new StrengthTypePoint
                {
                    Month = label,
                    State = type.State,
                    Delta = type.Delta,
                    DeltaPct = type.DeltaPct
                });
            }
        }

        // Months with no observation for a type still carry an explicit
        // insufficient_data point so the FE never has to invent gaps.
        foreach (var (typeId, typeSeries) in strengthPointsByType.OrderBy(pair => pair.Key))
        {
            var byTypeId = data.Basics.First(
                basic => basic.Id == typeId);
            var filled = new List<StrengthTypePoint>();
            var seen = typeSeries.ToDictionary(point => point.Month);
            foreach (var label in series.Months)
            {
                filled.Add(seen.TryGetValue(label, out var point)
                    ? point
                    : new StrengthTypePoint
                    {
                        Month = label,
                        State = ResultsStates.InsufficientData
                    });
            }

            series.Strength.ByType.Add(new StrengthTypeSeriesResource
            {
                TypeId = typeId,
                Key = (byTypeId.Name ?? string.Empty).ToLowerInvariant(),
                Unit = byTypeId.WeightType ?? string.Empty,
                Points = filled
            });
        }

        return series;
    }

    // ── Computation (pure over the loaded snapshot) ──────────────────────────

    private (ConsistencyKpiResource, WeightKpiResource, StrengthKpiResource) ComputeMonth(
        DateTime monthStart,
        LoadedData data,
        DateTime todayUtc)
    {
        var monthEnd = monthStart.AddMonths(1);
        return (
            ComputeConsistency(monthStart, monthEnd, data.CompletedSessions, data.FirstCompletedUtc, todayUtc),
            ComputeWeight(monthStart, monthEnd, data.WeightReadings),
            ComputeStrength(monthStart, monthEnd, data.Basics, data.StrengthSessions));
    }

    /// <summary>
    /// K1: score = active_weeks / elapsed_weeks. Weeks are Monday-start, UTC.
    /// A week is elapsed when it already ended (its Sunday is before today) or
    /// it contains today; weeks still in the future are NOT in the denominator
    /// (no dilution for a month in progress, E7). not_started = the month
    /// precedes the user's first completed session ⇒ score null, never 0.
    /// </summary>
    private static ConsistencyKpiResource ComputeConsistency(
        DateTime monthStart,
        DateTime monthEnd,
        List<CompletedSession> sessions,
        DateTime? firstCompletedUtc,
        DateTime todayUtc)
    {
        var inMonth = sessions
            .Where(session => session.Date >= monthStart && session.Date < monthEnd)
            .ToList();

        var resource = new ConsistencyKpiResource
        {
            ActiveSessions = inMonth.Count
        };

        DateTime? firstMonth = firstCompletedUtc is null
            ? null
            : new DateTime(firstCompletedUtc.Value.Year, firstCompletedUtc.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        if (firstMonth is null || monthStart < firstMonth)
        {
            resource.State = ResultsStates.NotStarted;
            return resource;
        }

        var score = ComputeConsistencyScore(monthStart, sessions, todayUtc);
        resource.State = ResultsStates.Ok;
        resource.Score = score;

        var previousMonthStart = monthStart.AddMonths(-1);
        if (monthStart == firstMonth)
        {
            // First active month: the baseline is the implicit 0 of "no history yet",
            // so the delta is the score itself in percentage points (E3: positive).
            resource.DeltaPp = Round(score * 100);
        }
        else
        {
            var previousScore = ComputeConsistencyScore(
                previousMonthStart, sessions, todayUtc);
            resource.DeltaPp = Round((score - previousScore) * 100);
        }

        return resource;
    }

    private static double ComputeConsistencyScore(
        DateTime monthStart,
        List<CompletedSession> sessions,
        DateTime todayUtc)
    {
        var monthEnd = monthStart.AddMonths(1);
        var elapsedWeeks = 0;
        var activeWeeks = 0;

        for (var weekStart = MondayOf(monthStart); weekStart < monthEnd; weekStart = weekStart.AddDays(7))
        {
            var weekEnd = weekStart.AddDays(7);
            var isElapsed = weekEnd <= todayUtc || weekStart <= todayUtc;
            if (!isElapsed)
            {
                continue;
            }

            elapsedWeeks++;

            if (sessions.Any(session =>
                    session.Date >= monthStart
                    && session.Date < monthEnd
                    && session.Date >= weekStart
                    && session.Date < weekEnd))
            {
                activeWeeks++;
            }
        }

        return elapsedWeeks == 0
            ? 0
            : Round((double)activeWeeks / elapsedWeeks);
    }

    private static DateTime MondayOf(DateTime dateUtc)
        => dateUtc.AddDays(-(((int)dateUtc.DayOfWeek + 6) % 7)).Date;

    /// <summary>
    /// K2: monthly snapshot. Readings after the month are invisible (the KPI of
    /// a past month must not leak future data). delta is against the last
    /// previous calendar month that has a reading — gaps are fine (product
    /// truth: "since the last registration"). 0 is a legitimate plateau (E8).
    /// </summary>
    private static WeightKpiResource ComputeWeight(
        DateTime monthStart,
        DateTime monthEnd,
        List<BodyCompositionReading> readings)
    {
        var upToMonth = readings
            .Where(reading => reading.OccurredOnUtc < monthEnd)
            .ToList();

        if (upToMonth.Count == 0)
        {
            return new WeightKpiResource { State = ResultsStates.InsufficientData };
        }

        var lastInMonth = upToMonth
            .Where(reading => reading.OccurredOnUtc >= monthStart)
            .LastOrDefault();
        var lastPrevious = upToMonth
            .Where(reading => reading.OccurredOnUtc < monthStart)
            .LastOrDefault();

        if (lastInMonth is not null && lastPrevious is not null)
        {
            return new WeightKpiResource
            {
                State = ResultsStates.Ok,
                LastKg = Round(lastInMonth.Value),
                DeltaKg = Round(lastInMonth.Value - lastPrevious.Value)
            };
        }

        // Exactly one observation so far (or none in this month): show the
        // current registered value without any delta. Never a fake 0.
        var only = lastInMonth ?? upToMonth.Last();
        return new WeightKpiResource
        {
            State = ResultsStates.CurrentOnly,
            LastKg = Round(only.Value)
        };
    }

    /// <summary>
    /// K3: per basic type, best 1RM per session (rows with 1RM null/≤0 were
    /// already dropped on load, t_0f19c744). delta(M) = last session of M vs
    /// the last session before M (same "previous real observation" rule as K2).
    /// deltaPct uses the type's first observed 1RM as baseline. The aggregate
    /// index is the mean of the relative deltas of the qualifying types.
    /// </summary>
    private static StrengthKpiResource ComputeStrength(
        DateTime monthStart,
        DateTime monthEnd,
        List<ExerciseType> basics,
        Dictionary<int, List<SessionOneRm>> sessionsByType)
    {
        var resource = new StrengthKpiResource();

        var indexParts = new List<double>();

        foreach (var type in basics.OrderBy(type => type.Id))
        {
            if (!sessionsByType.TryGetValue(type.Id, out var sessions))
            {
                // Basic type with no load data for this user: not shown (the
                // aggregate state already reports insufficient_data).
                continue;
            }

            var inMonth = sessions
                .Where(session => session.Date >= monthStart && session.Date < monthEnd)
                .LastOrDefault();
            var previous = sessions
                .Where(session => session.Date < monthStart)
                .LastOrDefault();

            var typeKpi = new StrengthTypeKpiResource
            {
                TypeId = type.Id,
                Key = (type.Name ?? string.Empty).ToLowerInvariant(),
                Unit = type.WeightType ?? string.Empty
            };

            if (inMonth is not null && previous is not null)
            {
                var baseline = sessions[0].OneRm;
                var delta = inMonth.OneRm - previous.OneRm;

                typeKpi.State = ResultsStates.Ok;
                typeKpi.Delta = Round(delta);
                typeKpi.DeltaPct = baseline > 0 ? Round(delta / baseline * 100) : null;

                if (typeKpi.DeltaPct is not null)
                {
                    indexParts.Add(typeKpi.DeltaPct.Value);
                }
            }
            else
            {
                typeKpi.State = ResultsStates.InsufficientData;
            }

            resource.ByType.Add(typeKpi);
        }

        if (indexParts.Count > 0)
        {
            resource.State = ResultsStates.Ok;
            resource.IndexPct = Round(indexParts.Average());
        }
        else
        {
            resource.State = ResultsStates.InsufficientData;
        }

        return resource;
    }

    private static double Round(double value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static double Round(decimal value)
        => Math.Round((double)value, 2, MidpointRounding.AwayFromZero);

    // ── Data loading (one snapshot per request) ──────────────────────────────

    private sealed record CompletedSession(DateTime Date, string DedupKey);
    private sealed record SessionOneRm(DateTime Date, Guid WorkoutId, double OneRm);

    private sealed class LoadedData
    {
        public List<CompletedSession> CompletedSessions { get; init; } = [];
        public DateTime? FirstCompletedUtc { get; init; }
        public List<BodyCompositionReading> WeightReadings { get; init; } = [];
        public List<ExerciseType> Basics { get; init; } = [];
        public Dictionary<int, List<SessionOneRm>> StrengthSessions { get; set; } = [];
    }

    private async Task<LoadedData> LoadDataAsync(
        string traineeId,
        CancellationToken cancellationToken)
    {
        // K1 raw rows: every dated workout of the target user. Completed =
        // explicit CompletedAt, or legacy logs (no CompletedAt but with
        // exercises), the documented universal-tracker rule in Workout.cs.
        var workoutRows = await context.Workouts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(workout =>
                workout.UserId == traineeId
                && workout.Date != null)
            .Select(workout => new
            {
                workout.Date,
                workout.DailySessionKey,
                workout.CompletedAt,
                ExerciseCount = workout.Exercises.Count
            })
            .ToListAsync(cancellationToken);

        var completed = workoutRows
            .Where(row => row.CompletedAt is not null || row.ExerciseCount > 0)
            .Select(row => new
            {
                Date = row.Date!.Value.Date,
                DedupKey = string.IsNullOrWhiteSpace(row.DailySessionKey)
                    ? row.Date!.Value.Date.ToString("yyyy-MM-dd")
                    : row.DailySessionKey
            })
            // Day dedup: one key = one session (legacy rows fall back to date).
            .GroupBy(row => row.DedupKey)
            .Select(group => group
                .OrderBy(row => row.Date)
                .First())
            .OrderBy(row => row.Date)
            .ThenBy(row => row.DedupKey, StringComparer.Ordinal)
            .ToList();

        // K2: weight_kg readings up to "now", ordered deterministically
        // (OccurredOnUtc, then Id as tiebreaker). Readings after the requested
        // month are filtered out per month inside ComputeWeight.
        var weightReadings = await context.BodyCompositionReadings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(reading =>
                reading.UserId == traineeId
                && reading.Metric.Key == BodyCompositionCatalog.WeightKgKey)
            .OrderBy(reading => reading.OccurredOnUtc)
            .ThenBy(reading => reading.Id)
            .ToListAsync(cancellationToken);

        // K3 basics: seeded editable tag set, not a hardcoded list.
        var basics = (await context.ExerciseTypes
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .Where(type => (type.Tags ?? [])
                .Any(tag =>
                    string.Equals(tag.Name, StrengthBasicsCatalogSeeder.BasicTagName,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(tag.Category, StrengthBasicsCatalogSeeder.BasicTagCategory,
                        StringComparison.OrdinalIgnoreCase)))
            .OrderBy(type => type.Id)
            .ToList();

        var loaded = new LoadedData
        {
            CompletedSessions = completed
                .Select(row => new CompletedSession(row.Date, row.DedupKey))
                .ToList(),
            FirstCompletedUtc = completed.Count > 0 ? completed[0].Date : null,
            WeightReadings = weightReadings,
            Basics = basics
        };

        if (basics.Count > 0)
        {
            var basicIds = basics.Select(type => type.Id).ToList();

            // "A 0 1RM is not a measurement": null/≤0 rows never load (E5).
            var rows = await context.Exercises
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(exercise =>
                    exercise.Workout.UserId == traineeId
                    && exercise.Workout.Date != null
                    && basicIds.Contains(exercise.TypeId!.Value)
                    && exercise.MaxRepetition != null
                    && exercise.MaxRepetition > 0)
                .Select(exercise => new
                {
                    exercise.TypeId,
                    exercise.WorkoutId,
                    Date = exercise.Workout.Date!.Value.Date,
                    exercise.MaxRepetition
                })
                .ToListAsync(cancellationToken);

            loaded.StrengthSessions = rows
                .GroupBy(row => new { row.TypeId, row.WorkoutId, row.Date })
                .Select(group => new
                {
                    TypeId = group.Key.TypeId!.Value,
                    Session = new SessionOneRm(
                        group.Key.Date,
                        group.Key.WorkoutId ?? Guid.Empty,
                        group.Max(row => row.MaxRepetition!.Value))
                })
                .GroupBy(item => item.TypeId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(item => item.Session)
                        .OrderBy(session => session.Date)
                        .ThenBy(session => session.WorkoutId)
                        .ToList());
        }

        return loaded;
    }
}
