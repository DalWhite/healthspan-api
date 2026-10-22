using Microsoft.EntityFrameworkCore;
using Services;
namespace healthspan_api.Persistence.Repositories;

public class ExercisesRepository(MainDbContext context)
{
  public Guid AddExercise(Exercise exercise, string userId)
  {
    exercise.Tags = ExerciseTagNormalizer.Normalize(exercise.Tags);
    CalculateComputedValues(exercise, userId);

    context.Exercises.Add(exercise);
    context.SaveChanges();

    return exercise.Id;
  }
  public bool DeleteExercise(Guid id, string userId)
  {
    var exerciseToDelete = context.Exercises
      .IgnoreQueryFilters()
      .FirstOrDefault(exercise =>
        exercise.Id == id
        && exercise.Workout.UserId == userId);
    if (exerciseToDelete is null)
    {
      return false;
    }

    context.Remove(exerciseToDelete);
    context.SaveChanges();
    return true;
  }

  public Exercise GetExercise(Guid id, string userId)
  {
    return context.Exercises
      .IgnoreQueryFilters()
      .Where(exercise => exercise.Workout.UserId == userId)
      .Include(e => e.Type)
        .ThenInclude(type => type.MuscleAssignments)
          .ThenInclude(assignment => assignment.Muscle)
      .Include(e => e.Workout)
      .FirstOrDefault(e => e.Id == id);
  }
  public List<Exercise> GetExercises(string userId, string tag = "")
  {
    var query = context.Exercises
      .Include(e => e.Type)
        .ThenInclude(type => type.MuscleAssignments)
          .ThenInclude(assignment => assignment.Muscle)
      .Include(e => e.Workout)
      .AsQueryable();

    if(!string.IsNullOrEmpty(userId))
    {
      query = query
        .IgnoreQueryFilters()
        .Where(e => e.Workout.User.Id == userId);
    }

    return FilterByTag(query.ToList(), tag);
  }
  public List<Exercise> GetExercisesByType(
    int exerciseTypeId,
    string userId,
    string tag = "")
  {
    var exercises = context.Exercises
      .IgnoreQueryFilters()
      .Where(e => e.Workout.UserId == userId)
      .Where(e => e.TypeId == exerciseTypeId)
      .Include(e => e.Type)
        .ThenInclude(type => type.MuscleAssignments)
          .ThenInclude(assignment => assignment.Muscle)
      .Include(e => e.Workout)
      .ToList();

    return FilterByTag(exercises, tag);
  }
  public ExerciseUpdateResult UpdateExercise(
    ExerciseUpdateCommand command,
    string userId)
  {
    var existingEntity = GetExercise(command.Id, userId);
    if (existingEntity is null)
    {
      return new ExerciseUpdateResult(ExerciseUpdateStatus.NotFound);
    }

    if (command.WorkoutId.HasValue &&
        command.WorkoutId.Value != existingEntity.WorkoutId)
    {
      var targetWorkout = context.Workouts
        .IgnoreQueryFilters()
        .FirstOrDefault(workout =>
          workout.Id == command.WorkoutId.Value
          && workout.UserId == userId);
      if (targetWorkout is null)
      {
        return new ExerciseUpdateResult(ExerciseUpdateStatus.WorkoutNotFound);
      }

      existingEntity.WorkoutId = targetWorkout.Id;
      existingEntity.Workout = targetWorkout;
    }

    if (command.TypeId.HasValue &&
        command.TypeId.Value != existingEntity.TypeId)
    {
      var targetType = context.ExerciseTypes
        .Include(type => type.MuscleAssignments)
          .ThenInclude(assignment => assignment.Muscle)
        .FirstOrDefault(type => type.Id == command.TypeId.Value);
      if (targetType is null)
      {
        return new ExerciseUpdateResult(ExerciseUpdateStatus.ExerciseTypeNotFound);
      }

      existingEntity.TypeId = targetType.Id;
      existingEntity.Type = targetType;
    }

    if (command.Series.HasValue)
    {
      existingEntity.Series = command.Series;
    }
    if (command.Repetitions.HasValue)
    {
      existingEntity.Repetitions = command.Repetitions;
    }
    if (command.Weight.HasValue)
    {
      existingEntity.Weight = command.Weight;
    }
    if (command.BodyWeight.HasValue || command.AddedWeight.HasValue)
    {
      existingEntity.BodyWeight = command.BodyWeight;
      existingEntity.AddedWeight = command.AddedWeight;
    }
    if (command.Comments is not null)
    {
      existingEntity.Comments = command.Comments;
    }
    if (command.Tags is not null)
    {
      existingEntity.Tags = ExerciseTagNormalizer.Normalize(command.Tags);
    }

    CalculateComputedValues(existingEntity, userId);
    context.SaveChanges();
    return new ExerciseUpdateResult(
      ExerciseUpdateStatus.Updated,
      existingEntity);
  }

  public void CalculateComputedValues(Exercise exercise, string userId)
  {
    var weightType = exercise.Type?.WeightType;
    if (weightType is null && exercise.TypeId.HasValue)
    {
      weightType = context.ExerciseTypes
        .AsNoTracking()
        .Where(type => type.Id == exercise.TypeId.Value)
        .Select(type => type.WeightType)
        .FirstOrDefault();
    }

    if (string.Equals(weightType, "None", StringComparison.OrdinalIgnoreCase))
    {
      exercise.Weight = 0;
      exercise.BodyWeight = null;
      exercise.AddedWeight = null;
      exercise.MaxRepetition = null;
      exercise.Volume = null;
      exercise.Intensity = null;
      return;
    }

    double weight;
    if (string.Equals(
          weightType,
          "BodyWeight",
          StringComparison.OrdinalIgnoreCase))
    {
      var hasBreakdown = exercise.BodyWeight.HasValue
        || exercise.AddedWeight.HasValue;
      exercise.BodyWeight = exercise.BodyWeight.HasValue
        ? Math.Max(0, exercise.BodyWeight.Value)
        : null;
      exercise.AddedWeight = exercise.AddedWeight.HasValue
        ? Math.Max(0, exercise.AddedWeight.Value)
        : null;
      weight = hasBreakdown
        ? (exercise.BodyWeight ?? 0) + (exercise.AddedWeight ?? 0)
        : Math.Max(0, exercise.Weight ?? 0);
    }
    else
    {
      exercise.BodyWeight = null;
      exercise.AddedWeight = null;
      weight = Math.Max(0, exercise.Weight ?? 0);
    }

    var repetitions = Math.Max(0, exercise.Repetitions ?? 0);
    var series = Math.Max(0, exercise.Series ?? 0);
    exercise.Weight = weight;

    // A row with no reps and no load is an empty placeholder (the "+ serie"
    // row before the athlete fills it in). Minting MaxRepetition = 0.0 for it
    // silently drags the 1RM trend down to 0 (t_0f19c744).
    if (repetitions <= 0 && weight <= 0)
    {
      exercise.MaxRepetition = null;
      exercise.Volume = null;
      exercise.Intensity = null;
      return;
    }

    exercise.MaxRepetition = Math.Round(
      weight * (1 + 0.0333 * repetitions),
      1);
    exercise.Volume = (int)(repetitions * series * weight);

    var historicMaxRepetition = exercise.TypeId.HasValue
      ? context.Exercises
        .IgnoreQueryFilters()
        .Where(candidate =>
          candidate.Id != exercise.Id
          && candidate.TypeId == exercise.TypeId
          && candidate.Workout.UserId == userId
          && candidate.MaxRepetition.HasValue)
        .Select(candidate => candidate.MaxRepetition)
        .ToList()
        .DefaultIfEmpty()
        .Max()
      : null;
    var referenceMax = Math.Max(
      exercise.MaxRepetition ?? 0,
      historicMaxRepetition ?? 0);

    if (weight > 0 && referenceMax > 0)
    {
      exercise.Intensity = (int)Math.Round(weight / referenceMax * 100);
      return;
    }

    exercise.Intensity = null;
  }

  private static List<Exercise> FilterByTag(
    List<Exercise> exercises,
    string tag)
    => string.IsNullOrWhiteSpace(tag)
      ? exercises
      : exercises
        .Where(exercise => ExerciseTagNormalizer.Matches(
          exercise.Tags,
          tag))
        .ToList();
}

public sealed record ExerciseUpdateCommand(
  Guid Id,
  int? Series,
  int? Repetitions,
  double? Weight,
  double? BodyWeight,
  double? AddedWeight,
  string Comments,
  IReadOnlyCollection<string> Tags,
  int? TypeId,
  Guid? WorkoutId);

public sealed record ExerciseUpdateResult(
  ExerciseUpdateStatus Status,
  Exercise Exercise = null);

public enum ExerciseUpdateStatus
{
  Updated,
  NotFound,
  WorkoutNotFound,
  ExerciseTypeNotFound
}
