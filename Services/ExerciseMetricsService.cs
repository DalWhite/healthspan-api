using static ExercisesController;
using System.Linq;
using healthspan_api.Persistence.Repositories;

public class ExerciseMetricsService
{
    private readonly ExercisesRepository _exercisesRepository;

    public ExerciseMetricsService(ExercisesRepository exercisesRepository)
    {
        _exercisesRepository = exercisesRepository;
    }

    public GetExerciseTypeKpis GetExerciseTypeKpisOfUser(
        int exerciseTypeId,
        string userId,
        string tag = "")
    {
        var typeExercises = _exercisesRepository.GetExercisesByType(
            exerciseTypeId,
            userId,
            tag);
        if (typeExercises.Count is 0)
        {
            return new GetExerciseTypeKpis();
        }

        var weeklyFrequency = CalculateWeeklyFrequency(
            typeExercises,
            exerciseTypeId);
        var exercisesWithLoadMetrics = typeExercises
            .Where(exercise =>
                exercise.Workout?.Date.HasValue == true
                && exercise.MaxRepetition.HasValue)
            .ToList();
        if (exercisesWithLoadMetrics.Count == 0)
        {
            return new GetExerciseTypeKpis
            {
                WeeklyFrequency = weeklyFrequency
            };
        }

        // Group exercises by session (e.g., by date) and calculate max RM per session
        var sessions = exercisesWithLoadMetrics
            .GroupBy(e => e.Workout.Date)
            .Select(g => new
            {
                Date = g.Key,
                MaxRM = g.Max(e => e.MaxRepetition!.Value)
            })
            .OrderBy(s => s.Date)
            .ToList();
        var maximum = sessions.Max(session => session.MaxRM);

        var result = new GetExerciseTypeKpis
        {
            MaxRM = (float)maximum,
            MaxRMDate = exercisesWithLoadMetrics
                .First(e => e.MaxRepetition == maximum)
                .Workout.Date!.Value,
            Current1RM = (float)sessions.Last().MaxRM,
            RMDiferentialInLast3Months =
                CalculateRMDiferentialInLast3Months(exercisesWithLoadMetrics),
            WeeklyFrequency = weeklyFrequency,
            PerceibedEffortLastSession = 5
        };

        return result;
    }

    private int CalculateRMDiferentialInLast3Months(IEnumerable<Exercise> typeExercises)
    {
        var exercisesInlast3Months = typeExercises
            .Where(e =>
                e.Workout?.Date >= DateTime.Now.AddMonths(-3)
                && e.MaxRepetition.HasValue)
            .ToList();

        var oldestExercise = exercisesInlast3Months.MinBy(e => e.Workout.Date);
        var newestExercise = exercisesInlast3Months.MaxBy(e => e.Workout.Date);

        if (oldestExercise == null || newestExercise == null)
        {
            return 0;
        }

        var currentMax = newestExercise.MaxRepetition!.Value;
        var previousMax = oldestExercise.MaxRepetition!.Value;

        return (int)Math.Round(currentMax - previousMax);
    }

    private int CalculateWeeklyFrequency(IEnumerable<Exercise> typeExercises, int exerciseTypeId)
    {
        var lastWeek = typeExercises.Where(e => e.Workout.Date >= DateTime.Now.AddDays(-7));
        var typeSessions = lastWeek.Count(e => e.TypeId == exerciseTypeId);
        return typeSessions;
    }

    public GetExerciseTypeTrends GetExerciseTypeTrendsOfUser(
        int exerciseTypeId,
        TrendKpiType type,
        string userId,
        string tag = "")
    {
        var typeExercises = _exercisesRepository
            .GetExercisesByType(exerciseTypeId, userId, tag)
            .Where(exercise => exercise.Workout?.Date.HasValue == true)
            .OrderBy(exercise => exercise.Workout.Date)
            .ToList();
        var result = new GetExerciseTypeTrends() { Points = new List<DataPoint>() };

        // A trend point represents a workout session, not an individual exercise row.
        var maxRMPerSession = typeExercises
            .Where(exercise => exercise.MaxRepetition.HasValue)
            .GroupBy(exercise => new
            {
                exercise.WorkoutId,
                Date = exercise.Workout.Date!.Value
            })
            .Select(session => new DataPoint
            {
                Data = (float)session.Max(
                    exercise => exercise.MaxRepetition!.Value),
                Time = session.Key.Date
            })
            .OrderBy(point => point.Time)
            .ToList();

        switch (type)
        {
            case TrendKpiType.RM:
                result.Points.AddRange(maxRMPerSession);
                break;

            case TrendKpiType.Volume:
                result.Points.AddRange(typeExercises
                    .Where(exercise => exercise.Volume.HasValue)
                    .GroupBy(exercise => new
                    {
                        exercise.WorkoutId,
                        Date = exercise.Workout.Date!.Value
                    })
                    .Select(session => new DataPoint
                    {
                        Data = session.Sum(
                            exercise => exercise.Volume!.Value),
                        Time = session.Key.Date
                    })
                    .OrderBy(point => point.Time));
                break;

            case TrendKpiType.Intensity:
                result.Points.AddRange(typeExercises
                    .Where(exercise => exercise.Intensity.HasValue)
                    .GroupBy(exercise => new
                    {
                        exercise.WorkoutId,
                        Date = exercise.Workout.Date!.Value
                    })
                    .Select(session => new DataPoint
                    {
                        Data = (float)session.Average(
                            exercise => exercise.Intensity!.Value),
                        Time = session.Key.Date
                    })
                    .OrderBy(point => point.Time));
                break;
        }
        return result;
    }
}
