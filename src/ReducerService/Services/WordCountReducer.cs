using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Minio;
using ReducerService.Models;
using Shared.Models;

namespace ReducerService.Services;

public sealed class WordCountReducer
{
    private readonly IMinioClient _minioClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WordCountReducer> _logger;

    public WordCountReducer(IMinioClient minioClient, IHttpClientFactory httpClientFactory, ILogger<WordCountReducer> logger)
    {
        _minioClient = minioClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task ProcessAsync(ReduceRequest request, CancellationToken cancellationToken)
    {
        var sharedRequest = request.ToShared();
        var aggregate = await AggregateAsync(sharedRequest, cancellationToken);
        await WriteResultAsync(sharedRequest, aggregate, cancellationToken);
        await NotifyCoordinatorAsync(sharedRequest, cancellationToken);
    }

    private async Task<Dictionary<string, long>> AggregateAsync(ReduceJobRequest request, CancellationToken cancellationToken)
    {
        var aggregate = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var objectKey in request.IntermediateObjectKeys)
        {
            var buffer = new MemoryStream();
            var getArgs = new GetObjectArgs()
                .WithBucket(request.BucketName)
                .WithObject(objectKey)
                .WithCallbackStream(stream => stream.CopyTo(buffer));

            await _minioClient.GetObjectAsync(getArgs, cancellationToken);
            buffer.Position = 0;

            var counts = await JsonSerializer.DeserializeAsync<Dictionary<string, long>>(buffer, cancellationToken: cancellationToken)
                         ?? new Dictionary<string, long>();

            foreach (var (token, count) in counts)
            {
                aggregate[token] = aggregate.TryGetValue(token, out var current) ? current + count : count;
            }
        }

        _logger.LogInformation("Reducer aggregated {Count} tokens for job {JobId}", aggregate.Count, request.JobId);
        return aggregate;
    }

    private async Task WriteResultAsync(ReduceJobRequest request, IReadOnlyDictionary<string, long> aggregate, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(aggregate, new JsonSerializerOptions { WriteIndented = false });
        var bytes = Encoding.UTF8.GetBytes(json);
        await using var stream = new MemoryStream(bytes);

        var putArgs = new PutObjectArgs()
            .WithBucket(request.BucketName)
            .WithObject(request.OutputObjectKey)
            .WithContentType("application/json")
            .WithObjectSize(bytes.LongLength)
            .WithStreamData(stream);

        await _minioClient.PutObjectAsync(putArgs, cancellationToken);
        _logger.LogInformation("Reducer wrote result object {ObjectKey} for job {JobId}", request.OutputObjectKey, request.JobId);
    }

    private async Task NotifyCoordinatorAsync(ReduceJobRequest request, CancellationToken cancellationToken)
    {
        var notification = new ReduceCompletionNotification
        {
            JobId = request.JobId,
            ResultObjectKey = request.OutputObjectKey
        };

        var client = _httpClientFactory.CreateClient("Callback");
        var response = await client.PostAsJsonAsync(request.CallbackUrl, notification, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to notify coordinator for job {JobId}. Status: {Status}. Body: {Body}", request.JobId, response.StatusCode, error);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Reducer notified coordinator for job {JobId}", request.JobId);
    }
}
