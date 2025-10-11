using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Shared.Models;
using Shared.Utils;
using System.Text.Json;

namespace MapperService.Services;

public class MapConsumer : IConsumer<MapJobMessage>
{
    private readonly IMinioClient _minioClient;
    private readonly ILogger<MapConsumer> _logger;
    private readonly IOptions<MinioOptions> _minioOptions;
    private readonly IPublishEndpoint _publishEndpoint;

    public MapConsumer(
        IMinioClient minioClient,
        ILogger<MapConsumer> logger,
        IOptions<MinioOptions> minioOptions,
        IPublishEndpoint publishEndpoint)
    {
        _minioClient = minioClient;
        _logger = logger;
        _minioOptions = minioOptions;
        _publishEndpoint = publishEndpoint;
    }

    public async Task Consume(ConsumeContext<MapJobMessage> context)
    {
        var message = context.Message;
        _logger.LogInformation("Processing map job for JobId: {JobId}", message.JobId);

        try
        {
            var intermediateObjectKey = $"intermediate/{message.JobId:N}.json";

            // Read input file from MinIO
            var buffer = new MemoryStream();
            var getArgs = new GetObjectArgs()
                .WithBucket(message.InputBucketName)
                .WithObject(message.InputObjectKey)
                .WithCallbackStream(stream => stream.CopyTo(buffer));

            await _minioClient.GetObjectAsync(getArgs);
            buffer.Position = 0;
            var content = await new StreamReader(buffer).ReadToEndAsync();

            // Perform word counting
            var wordCounts = WordCountMapper.ProcessContent(content);

            // Write intermediate result to MinIO
            var intermediateJson = JsonSerializer.Serialize(wordCounts);
            var intermediateBytes = System.Text.Encoding.UTF8.GetBytes(intermediateJson);
            var intermediateStream = new MemoryStream(intermediateBytes);

            var putArgs = new PutObjectArgs()
                .WithBucket(_minioOptions.Value.BucketName)
                .WithObject(intermediateObjectKey)
                .WithContentType("application/json")
                .WithObjectSize(intermediateStream.Length)
                .WithStreamData(intermediateStream);

            await _minioClient.PutObjectAsync(putArgs);

            // Publish map completion message
            var resultMessage = new MapResultMessage
            {
                JobId = message.JobId,
                IntermediateBucketName = _minioOptions.Value.BucketName,
                IntermediateObjectKey = intermediateObjectKey,
                Success = true
            };

            await _publishEndpoint.Publish(resultMessage);
            _logger.LogInformation("Map job completed successfully for JobId: {JobId}", message.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Map job failed for JobId: {JobId}", message.JobId);

            // Publish failure message
            var resultMessage = new MapResultMessage
            {
                JobId = message.JobId,
                Success = false,
                ErrorMessage = ex.Message
            };

            await _publishEndpoint.Publish(resultMessage);
        }
    }
}