using MassTransit;
using Shared.Models;

namespace ApiService.Services;

public class MapResultConsumer : IConsumer<MapResultMessage>
{
    private readonly JobCoordinator _jobCoordinator;
    private readonly ILogger<MapResultConsumer> _logger;

    public MapResultConsumer(
        JobCoordinator jobCoordinator,
        ILogger<MapResultConsumer> logger)
    {
        _jobCoordinator = jobCoordinator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MapResultMessage> context)
    {
        var message = context.Message;
        _logger.LogInformation("Consuming map result for JobId: {JobId}, Success: {Success}, Key: {Key}",
            message.JobId, message.Success, message.IntermediateObjectKey);

        if (!message.Success)
        {
            _jobCoordinator.HandleMapFailureAsync(message.JobId, message.ErrorMessage ?? "Unknown map error");
            return;
        }

        await _jobCoordinator.HandleMapCompletionAsync(message.JobId, message.IntermediateObjectKey, context.CancellationToken);
    }
}

public class ReduceResultConsumer : IConsumer<ReduceResultMessage>
{
    private readonly JobCoordinator _jobCoordinator;
    private readonly ILogger<ReduceResultConsumer> _logger;

    public ReduceResultConsumer(JobCoordinator jobCoordinator, ILogger<ReduceResultConsumer> logger)
    {
        _jobCoordinator = jobCoordinator;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ReduceResultMessage> context)
    {
        var message = context.Message;
        _logger.LogInformation("Consuming reduce result for JobId: {JobId}, Success: {Success}",
            message.JobId, message.Success);

        if (!message.Success)
        {
            _jobCoordinator.HandleReduceFailure(message.JobId, message.ErrorMessage ?? "Unknown reduce error");
            return Task.CompletedTask;
        }

        _jobCoordinator.HandleReduceCompletion(
            message.JobId,
            message.ResultObjectKey);
        return Task.CompletedTask;
    }
}