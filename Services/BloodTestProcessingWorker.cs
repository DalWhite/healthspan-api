#nullable enable

public class BloodTestProcessingWorker : BackgroundService
{
    private readonly BloodTestProcessingChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BloodTestProcessingWorker> _logger;

    public BloodTestProcessingWorker(BloodTestProcessingChannel channel, IServiceScopeFactory scopeFactory, ILogger<BloodTestProcessingWorker> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BloodTestProcessingWorker started");

        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("Processing blood test {BloodTestId} for user {UserId}", job.BloodTestId, job.UserId);
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<BloodAnalyticsService>();
                await service.ProcessBloodTestAsync(job.BloodTestId, job.UserId, job.FileBytes, job.FileName, job.ContentType, stoppingToken);
                _logger.LogInformation("Completed processing blood test {BloodTestId}", job.BloodTestId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Processing cancelled for blood test {BloodTestId} during shutdown", job.BloodTestId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process blood test {BloodTestId}", job.BloodTestId);

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<BloodAnalyticsService>();
                    service.MarkBloodTestFailed(
                        job.BloodTestId,
                        job.UserId,
                        "The report could not be read. Enter results manually or upload it again.");
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Failed to mark blood test {BloodTestId} as failed", job.BloodTestId);
                }
            }
        }

        _logger.LogInformation("BloodTestProcessingWorker stopped");
    }
}
