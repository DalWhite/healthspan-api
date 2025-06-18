using healthspan_api.Events;
using MediatR;

namespace healthspan_api.EventHandlers;

public class ExerciseUpdatedHandler : INotificationHandler<ExerciseUpdatedEvent>
{
  private readonly IServiceProvider _serviceProvider;

  public ExerciseUpdatedHandler(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task Handle(ExerciseUpdatedEvent notification, CancellationToken cancellationToken)
  {
    try
    {
      using var scope = _serviceProvider.CreateScope();
      var dbContext = scope.ServiceProvider.GetRequiredService<MainDbContext>();
      var exerciseMetricsService = scope.ServiceProvider.GetRequiredService<ExerciseMetricsService>();

      var exercise = notification.Exercise;
      var userId = notification.UserId
          ?? exercise.Workout?.User?.Id
          ?? exercise.Workout?.UserId;

      if (exercise == null || exercise.TypeId == null || userId == null)
      {
        return;
      }

      // Fetch the existing exercise from the database
      var exerciseId = notification.Exercise.Id;
      var dbExercise = dbContext.Exercises.FirstOrDefault(e => e.Id == exerciseId);
      if (dbExercise == null) return;

      // Update computed fields
      exerciseMetricsService.CalculateComputedValues(dbExercise, userId);

      dbContext.Entry(dbExercise).Property(e => e.MaxRepetition).IsModified = true;
      dbContext.Entry(dbExercise).Property(e => e.Volume).IsModified = true;
      dbContext.Entry(dbExercise).Property(e => e.Intensity).IsModified = true;
      await dbContext.SaveChangesAsync(cancellationToken);

      Console.WriteLine($"Exercise Updated: {exercise.Id}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Exception in ExerciseUpdatedHandler: {ex.Message}");
      if (ex.InnerException != null)
      {
        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
      }
      throw;
    }
  }
}