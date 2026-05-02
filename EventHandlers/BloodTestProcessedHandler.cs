using healthspan_api.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace healthspan_api.EventHandlers;

public class BloodTestProcessedHandler : INotificationHandler<BloodTestProcessedEvent>
{
    private readonly ILogger<BloodTestProcessedHandler> _logger;

    public BloodTestProcessedHandler(ILogger<BloodTestProcessedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(BloodTestProcessedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Blood test {BloodTestId} processed for user {UserId}", notification.BloodTestId, notification.UserId);
        return Task.CompletedTask;
    }
}