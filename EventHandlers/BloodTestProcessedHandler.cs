using healthspan_api.Events;
using MediatR;

namespace healthspan_api.EventHandlers;

public class BloodTestProcessedHandler : INotificationHandler<BloodTestProcessedEvent>
{
    private readonly IServiceProvider _serviceProvider;

    public BloodTestProcessedHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task Handle(BloodTestProcessedEvent notification, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MainDbContext>();
        var bloodTest = dbContext.BloodTests.FirstOrDefault(test => test.Id == notification.BloodTestId && test.UserId == notification.UserId);
        if (bloodTest is null)
        {
            return Task.CompletedTask;
        }

        bloodTest.UpdatedAtUtc = DateTime.UtcNow;
        dbContext.SaveChanges();
        return Task.CompletedTask;
    }
}