using MassTransit;
using Microsoft.Extensions.Options;
using Minio;
using Shared.Models;
using System.Text;
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
        _logger.LogInformation("Processing map job for JobId: {JobId}, Offset: {Offset}, Count: {Count}",
            message.JobId, message.Offset, message.Count);

        try
        {
            var intermediateObjectKey = $"intermediate/{message.JobId:N}-{message.Offset}.json";

            // Read specific byte range from MinIO
            var content = await ReadChunkWithBoundariesAsync(message, context.CancellationToken);

            // Perform word counting on chunk content
            var wordCounts = ProcessContent(content);

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
            _logger.LogInformation("Map job completed successfully for JobId: {JobId}, Offset: {Offset}",
                message.JobId, message.Offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Map job failed for JobId: {JobId}, Offset: {Offset}",
                message.JobId, message.Offset);

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

    private const int BoundaryOverlap = 200; // Must be larger than the longest expected word

    private async Task<string> ReadChunkWithBoundariesAsync(MapJobMessage message, CancellationToken cancellationToken)
    {
        // Determine the real start and end of our read, including overlap
        var startReadOffset = Math.Max(0, message.Offset - BoundaryOverlap);
        var readLength = message.Offset - startReadOffset + message.Count + BoundaryOverlap;

        // Read the oversized chunk from MinIO
        var buffer = new MemoryStream();
        var getArgs = new GetObjectArgs()
            .WithBucket(message.InputBucketName)
            .WithObject(message.InputObjectKey)
            .WithOffsetAndLength(startReadOffset, readLength)
            .WithCallbackStream(stream => stream.CopyToAsync(buffer, cancellationToken));

        await _minioClient.GetObjectAsync(getArgs, cancellationToken);
        buffer.Position = 0;
        var content = await new StreamReader(buffer).ReadToEndAsync(cancellationToken);

        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        // Find the true start index for processing. This is the first character of the first word
        // that starts at or after our assigned chunk offset.
        var relativeStart = (int)(message.Offset - startReadOffset);
        var startIndex = 0;
        if (message.Offset > 0)
        {
            for (var i = relativeStart; i < content.Length; i++)
            {
                // A word starts if the current char is not whitespace AND (it's the first char OR the previous char is whitespace)
                if (!char.IsWhiteSpace(content[i]) && (i == 0 || char.IsWhiteSpace(content[i - 1])))
                {
                    startIndex = i;
                    break;
                }
            }
        }

        // If we didn't find a word start, the chunk is probably all whitespace, so return empty
        if (startIndex == 0 && message.Offset > 0) return string.Empty;

        // Find the true end index. This is the start of the first word that begins at or after the end of our assigned chunk.
        var relativeEnd = relativeStart + message.Count;
        var endIndex = content.Length;
        if (relativeEnd < content.Length)
        {
            for (var i = relativeEnd; i < content.Length; i++)
            {
                if (!char.IsWhiteSpace(content[i]) && (i == 0 || char.IsWhiteSpace(content[i - 1])))
                {
                    endIndex = i;
                    break;
                }
            }
        }

        if (startIndex >= endIndex)
        {
            return string.Empty;
        }

        // Return the substring that this chunk is responsible for
        return content[startIndex..endIndex];
    }

    public static Dictionary<string, int> ProcessContent(string content)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokenize(content))
        {
            if (counts.TryGetValue(token, out var current))
            {
                counts[token] = current + 1;
            }
            else
            {
                counts[token] = 1;
            }
        }
        return counts;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }
}