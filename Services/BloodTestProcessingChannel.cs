#nullable enable

using System.Threading.Channels;

public class BloodTestProcessingChannel
{
    private readonly Channel<BloodTestProcessingJob> _channel = Channel.CreateUnbounded<BloodTestProcessingJob>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<BloodTestProcessingJob> Writer => _channel.Writer;
    public ChannelReader<BloodTestProcessingJob> Reader => _channel.Reader;
}

public class BloodTestProcessingJob
{
    public Guid BloodTestId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public byte[] FileBytes { get; init; } = Array.Empty<byte>();
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
}
