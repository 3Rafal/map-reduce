using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MapperService.Models;
using Minio;
using Shared.Models;

namespace MapperService.Services;

public sealed class WordCountMapper
{
    private readonly IMinioClient _minioClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WordCountMapper> _logger;

    public WordCountMapper(IMinioClient minioClient, IHttpClientFactory httpClientFactory, ILogger<WordCountMapper> logger)
    {
        _minioClient = minioClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task ProcessAsync(MapRequest request, CancellationToken cancellationToken)
    {
        var sharedRequest = request.ToShared();

        var counts = await ReadAndCountAsync(sharedRequest, cancellationToken);
        await WriteIntermediateAsync(sharedRequest, counts, cancellationToken);
        await NotifyCoordinatorAsync(sharedRequest, cancellationToken);
    }

    private async Task<Dictionary<string, int>> ReadAndCountAsync(MapJobRequest request, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var getArgs = new GetObjectArgs()
            .WithBucket(request.BucketName)
            .WithObject(request.InputObjectKey)
            .WithCallbackStream(stream => stream.CopyTo(buffer));

        await _minioClient.GetObjectAsync(getArgs, cancellationToken);
        buffer.Position = 0;

        using var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: false);
        var text = await reader.ReadToEndAsync();

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokenize(text))
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

        _logger.LogInformation("Mapper produced {Count} unique tokens for job {JobId}", counts.Count, request.JobId);
        return counts;
    }

    private async Task WriteIntermediateAsync(MapJobRequest request, IReadOnlyDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(counts, new JsonSerializerOptions { WriteIndented = false });
        var bytes = Encoding.UTF8.GetBytes(json);
        await using var stream = new MemoryStream(bytes);

        var putArgs = new PutObjectArgs()
            .WithBucket(request.BucketName)
            .WithObject(request.IntermediateObjectKey)
            .WithContentType("application/json")
            .WithObjectSize(bytes.LongLength)
            .WithStreamData(stream);

        await _minioClient.PutObjectAsync(putArgs, cancellationToken);
        _logger.LogInformation("Mapper wrote intermediate object {ObjectKey} for job {JobId}", request.IntermediateObjectKey, request.JobId);
    }

    private async Task NotifyCoordinatorAsync(MapJobRequest request, CancellationToken cancellationToken)
    {
        var notification = new MapCompletionNotification
        {
            JobId = request.JobId,
            IntermediateObjectKeys = new[] { request.IntermediateObjectKey }
        };

        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync(request.CallbackUrl, notification, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to notify coordinator for job {JobId}. Status: {Status}. Body: {Body}", request.JobId, response.StatusCode, error);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Mapper notified coordinator for job {JobId}", request.JobId);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
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
