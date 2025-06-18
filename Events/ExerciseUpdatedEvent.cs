using MediatR;
namespace healthspan_api.Events;
public class ExerciseUpdatedEvent : INotification
{
    public Exercise Exercise { get; }
    public string UserId { get; }
    public ExerciseUpdatedEvent(Exercise exercise, string userId)
    {
      Exercise = exercise;
      UserId = userId;
    }
}