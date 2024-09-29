using static ExercisesController;
using System.Linq;

public class ExercisesService
{
    private readonly ExercisesRepository _exercisesRepository;

    public ExercisesService(ExercisesRepository exercisesRepository)
    {
        _exercisesRepository = exercisesRepository;
    }

    public GetExerciseTypeKpis GetExerciseTypeKpisOfUser(int exerciseTypeId, string userId)
    {
        var typeExercises = _exercisesRepository.GetExercisesByType(exerciseTypeId, userId);
        if (typeExercises.Count is 0)
        {
            return new GetExerciseTypeKpis();
        }
        var result = new GetExerciseTypeKpis
        {
            MaxRM = (float)typeExercises.Max(e => e.MaxRepetition),
            MaxRMDate = (DateTime)typeExercises.MaxBy(e => e.MaxRepetition).Workout.Date,
            Current1RM = (float)typeExercises.MaxBy(e => e.Workout.Date).MaxRepetition,
            RMDiferentialInLast3Months = CalculateRMDiferentialInLast3Months(typeExercises),
            WeeklyFrequency = CalculateWeeklyFrequency(typeExercises, exerciseTypeId),
            PerceibedEffortLastSession = 5
        };

        return result;
    }

    private int CalculateRMDiferentialInLast3Months(IEnumerable<Exercise> typeExercises)
    {
        var exercisesInlast3Months = typeExercises.Where(e => e.Workout.Date >= DateTime.Now.AddMonths(-3));

        var oldestExercise = exercisesInlast3Months.MinBy(e => e.Workout.Date);
        var newestExercise = exercisesInlast3Months.MaxBy(e => e.Workout.Date);

        if (oldestExercise == null)
        {
            return 0;
        }

        var currentMax = newestExercise.MaxRepetition;
        var previousMax = oldestExercise.MaxRepetition;

        return (int)(currentMax - previousMax);
    }

    private int CalculateWeeklyFrequency(IEnumerable<Exercise> typeExercises, int exerciseTypeId)
    {
        var lastWeek = typeExercises.Where(e => e.Workout.Date >= DateTime.Now.AddDays(-7));
        var typeSessions = lastWeek.Count(e => e.TypeId == exerciseTypeId);
        return typeSessions;
    }

    public GetExerciseTypeTrends GetExerciseTypeTrendsOfUser(int exerciseTypeId, TrendKpiType type, string userId)
    {
        var typeExercises = _exercisesRepository.GetExercisesByType(exerciseTypeId, userId).OrderBy(e => e.Workout.Date);
        var result = new GetExerciseTypeTrends() { Points = new List<DataPoint>() };

        switch (type)
        {
            case TrendKpiType.RM:
                result.Points.AddRange(typeExercises.Select(exercise => new DataPoint
                {
                    Data = (float)exercise.MaxRepetition,
                    Time = (DateTime)exercise.Workout.Date
                }));
            break;

            case TrendKpiType.Volume:
                result.Points.AddRange(typeExercises.Select(exercise => new DataPoint
                {
                    Data = (float)exercise.Volume,
                    Time = (DateTime)exercise.Workout.Date
                }));
            break;

            case TrendKpiType.Intensity:
                result.Points.AddRange(typeExercises.Select(exercise => new DataPoint
                {
                    Data = (float)exercise.Intensity,
                    Time = (DateTime)exercise.Workout.Date
                }));
            break;

        }
        return result;
    }
}
