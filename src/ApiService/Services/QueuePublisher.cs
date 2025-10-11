using MassTransit;
using Shared.Models;

namespace ApiService.Services;

public interface IQueuePublisher
{
    Task PublishMapJobAsync(MapJobMessage message, CancellationToken cancellationToken = default);
    Task PublishReduceJobAsync(ReduceJobMessage message, CancellationToken cancellationToken = default);
}

public class QueuePublisher : IQueuePublisher
{
    private readonly IBus _bus;
    private readonly ILogger<QueuePublisher> _logger;

    public QueuePublisher(IBus bus, ILogger<QueuePublisher> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    public async Task PublishMapJobAsync(MapJobMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publishing map job for JobId: {JobId}", message.JobId);
        await _bus.Publish(message, cancellationToken);
    }

    public async Task PublishReduceJobAsync(ReduceJobMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publishing reduce job for JobId: {JobId}", message.JobId);
        await _bus.Publish(message, cancellationToken);
    }
}
