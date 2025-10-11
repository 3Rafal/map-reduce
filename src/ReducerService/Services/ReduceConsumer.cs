using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Shared.Models;
using Shared.Utils;
using System.Text.Json;

namespace ReducerService.Services;

public class ReduceConsumer : IConsumer<ReduceJobMessage>
{
    private readonly IMinioClient _minioClient;
    private readonly ILogger<ReduceConsumer> _logger;
    private readonly IOptions<MinioOptions> _minioOptions;
    private readonly IPublishEndpoint _publishEndpoint;

    public ReduceConsumer(
        IMinioClient minioClient,
        ILogger<ReduceConsumer> logger,
        IOptions<MinioOptions> minioOptions,
        IPublishEndpoint publishEndpoint)
    {
        _minioClient = minioClient;
        _logger = logger;
        _minioOptions = minioOptions;
        _publishEndpoint = publishEndpoint;
    }

    public async Task Consume(ConsumeContext<ReduceJobMessage> context)
    {
        var message = context.Message;
        _logger.LogInformation("Processing reduce job for JobId: {JobId}", message.JobId);

        try
        {
            var resultObjectKey = $"results/{message.JobId:N}.json";

            // Read all intermediate files from MinIO
            var aggregatedWordCounts = new Dictionary<string, int>();

            foreach (var intermediateKey in message.IntermediateObjectKeys)
            {
                var buffer = new MemoryStream();
                var getArgs = new GetObjectArgs()
                    .WithBucket(message.IntermediateBucketName)
                    .WithObject(intermediateKey)
                    .WithCallbackStream(stream => stream.CopyTo(buffer));

                await _minioClient.GetObjectAsync(getArgs);
                buffer.Position = 0;
                var intermediateJson = await new StreamReader(buffer).ReadToEndAsync();
                var intermediateCounts = JsonSerializer.Deserialize<Dictionary<string, int>>(intermediateJson);

                if (intermediateCounts != null)
                {
                    foreach (var kvp in intermediateCounts)
                    {
                        if (aggregatedWordCounts.ContainsKey(kvp.Key))
                        {
                            aggregatedWordCounts[kvp.Key] += kvp.Value;
                        }
                        else
                        {
                            aggregatedWordCounts[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }

            // Write final result to MinIO
            var resultJson = JsonSerializer.Serialize(aggregatedWordCounts);
            var resultBytes = System.Text.Encoding.UTF8.GetBytes(resultJson);
            var resultStream = new MemoryStream(resultBytes);

            var putArgs = new PutObjectArgs()
                .WithBucket(_minioOptions.Value.BucketName)
                .WithObject(resultObjectKey)
                .WithContentType("application/json")
                .WithObjectSize(resultStream.Length)
                .WithStreamData(resultStream);

            await _minioClient.PutObjectAsync(putArgs);

            // Publish reduce completion message
            var resultMessage = new ReduceResultMessage
            {
                JobId = message.JobId,
                ResultBucketName = _minioOptions.Value.BucketName,
                ResultObjectKey = resultObjectKey,
                Success = true
            };

            await _publishEndpoint.Publish(resultMessage);
            _logger.LogInformation("Reduce job completed successfully for JobId: {JobId}", message.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reduce job failed for JobId: {JobId}", message.JobId);

            // Publish failure message
            var resultMessage = new ReduceResultMessage
            {
                JobId = message.JobId,
                Success = false,
                ErrorMessage = ex.Message
            };

            await _publishEndpoint.Publish(resultMessage);
        }
    }
}