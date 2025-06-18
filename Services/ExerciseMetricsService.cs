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
    public void CalculateComputedValues(Exercise exercise, string userId)
    {
        var raw1RMCalculation = exercise.Weight * (1 + 0.0333 * exercise.Repetitions);
        exercise.MaxRepetition = Math.Round((double)raw1RMCalculation,1);
        exercise.Volume = (int?)(exercise.Repetitions * exercise.Series * exercise.Weight);

        double? historicMaxRepetition = exercise.MaxRepetition;
        
        var exercisesOfType = _exercisesRepository.GetExercisesByType((int)exercise.TypeId, userId);
        
        if (exercisesOfType.Any())
        {
            historicMaxRepetition = exercisesOfType
                .OrderByDescending(e => e.MaxRepetition)
                .Select(e => e.MaxRepetition)
                .First();
        }
        if(exercise.Weight != 0)
        {
            exercise.Intensity = (int?)(exercise.Weight / historicMaxRepetition * 100);
        }
        else
        {
            exercise.Intensity = 0;
        }
    }
}
