using MediatR;

namespace healthspan_api.Events;

public class BloodTestProcessedEvent : INotification
{
    public BloodTestProcessedEvent(Guid bloodTestId, string userId)
    {
        BloodTestId = bloodTestId;
        UserId = userId;
    }

    public Guid BloodTestId { get; }
    public string UserId { get; }
}