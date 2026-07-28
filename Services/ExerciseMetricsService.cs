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
            .OrderBy(e => e.Workout.Date);
        var result = new GetExerciseTypeTrends() { Points = new List<DataPoint>() };

        // Group exercises by session (e.g., by date) and calculate max RM per session
        var maxRMPerSession = typeExercises
            .Where(exercise =>
                exercise.Workout?.Date.HasValue == true
                && exercise.MaxRepetition.HasValue)
            .GroupBy(e => e.Workout.Date)
            .Select(g => new DataPoint
            {
                Data = (float)g.Max(e => e.MaxRepetition!.Value),
                Time = g.Key!.Value
            })
            .ToList();

        switch (type)
        {
            case TrendKpiType.RM:
                result.Points.AddRange(maxRMPerSession);
                break;

            case TrendKpiType.Volume:
                result.Points.AddRange(typeExercises
                    .Where(exercise =>
                        exercise.Workout?.Date.HasValue == true
                        && exercise.Volume.HasValue)
                    .Select(exercise => new DataPoint
                    {
                        Data = exercise.Volume!.Value,
                        Time = exercise.Workout.Date!.Value
                    }));
                break;

            case TrendKpiType.Intensity:
                result.Points.AddRange(typeExercises
                    .Where(exercise =>
                        exercise.Workout?.Date.HasValue == true
                        && exercise.Intensity.HasValue)
                    .Select(exercise => new DataPoint
                    {
                        Data = exercise.Intensity!.Value,
                        Time = exercise.Workout.Date!.Value
                    }));
                break;
        }
        return result;
    }
}
