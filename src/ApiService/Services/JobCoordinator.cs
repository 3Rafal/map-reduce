using System.Collections.Concurrent;
using System.Net.Http.Json;
using ApiService.Models;
using Microsoft.Extensions.Options;
using Shared.Models;

namespace ApiService.Services;

public sealed class JobCoordinator
{
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly ILogger<JobCoordinator> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CoordinatorOptions _coordinatorOptions;

    public JobCoordinator(
        ILogger<JobCoordinator> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<CoordinatorOptions> coordinatorOptions)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _coordinatorOptions = coordinatorOptions.Value;
    }

    public IEnumerable<Job> GetJobs() => _jobs.Values;

    public bool TryGetJob(Guid jobId, out Job? job) => _jobs.TryGetValue(jobId, out job);

    public async Task<Job> CreateJobAsync(FileReference fileReference, CancellationToken cancellationToken)
    {
        var job = new Job
        {
            BucketName = fileReference.BucketName,
            InputObjectKey = fileReference.ObjectKey,
            Status = JobStatus.Pending,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (!_jobs.TryAdd(job.Id, job))
        {
            throw new InvalidOperationException($"Job with id {job.Id} already exists.");
        }

        try
        {
            await StartMappingAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.FailureReason = ex.Message;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            _logger.LogError(ex, "Failed to start mapping for job {JobId}", job.Id);
            throw;
        }

        return job;
    }

    public async Task<bool> HandleMapCompletedAsync(MapCompletionNotification notification, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(notification.JobId, out var job))
        {
            _logger.LogWarning("Received map completion for unknown job {JobId}", notification.JobId);
            return false;
        }

        job.IntermediateObjectKeys.Clear();
        job.IntermediateObjectKeys.AddRange(notification.IntermediateObjectKeys);
        job.Status = JobStatus.Reducing;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await StartReducingAsync(job, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.FailureReason = ex.Message;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            _logger.LogError(ex, "Failed to start reducing for job {JobId}", job.Id);
            return false;
        }
    }

    public bool HandleReduceCompleted(ReduceCompletionNotification notification)
    {
        if (!_jobs.TryGetValue(notification.JobId, out var job))
        {
            _logger.LogWarning("Received reduce completion for unknown job {JobId}", notification.JobId);
            return false;
        }

        job.ResultObjectKey = notification.ResultObjectKey;
        job.Status = JobStatus.Completed;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    private async Task StartMappingAsync(Job job, CancellationToken cancellationToken)
    {
        job.Status = JobStatus.Mapping;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        var intermediateObjectKey = $"intermediate/{job.Id:N}.json";
        var callbackUrl = BuildCallbackUri(_coordinatorOptions.CallbackBaseUrl, $"jobs/{job.Id}/mapdone");

        var request = new MapJobRequest
        {
            JobId = job.Id,
            BucketName = job.BucketName,
            InputObjectKey = job.InputObjectKey,
            IntermediateObjectKey = intermediateObjectKey,
            CallbackUrl = callbackUrl.ToString()
        };

        var client = _httpClientFactory.CreateClient("Mapper");
        client.BaseAddress = new Uri(_coordinatorOptions.MapperBaseUrl, UriKind.Absolute);

        var response = await client.PostAsJsonAsync("map", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Mapper responded with {(int)response.StatusCode}: {error}");
        }

        _logger.LogInformation("Mapper started for job {JobId}", job.Id);
    }

    private async Task StartReducingAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.IntermediateObjectKeys.Count == 0)
        {
            throw new InvalidOperationException("Cannot start reducer without intermediate artifacts");
        }

        var outputObjectKey = $"results/{job.Id:N}.json";
        var callbackUrl = BuildCallbackUri(_coordinatorOptions.CallbackBaseUrl, $"jobs/{job.Id}/reducedone");

        var request = new ReduceJobRequest
        {
            JobId = job.Id,
            BucketName = job.BucketName,
            IntermediateObjectKeys = job.IntermediateObjectKeys.ToArray(),
            OutputObjectKey = outputObjectKey,
            CallbackUrl = callbackUrl.ToString()
        };

        var client = _httpClientFactory.CreateClient("Reducer");
        client.BaseAddress = new Uri(_coordinatorOptions.ReducerBaseUrl, UriKind.Absolute);

        var response = await client.PostAsJsonAsync("reduce", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Reducer responded with {(int)response.StatusCode}: {error}");
        }

        _logger.LogInformation("Reducer started for job {JobId}", job.Id);
    }

    private static Uri BuildCallbackUri(string baseUrl, string relativePath)
    {
        var normalized = EnsureTrailingSlash(baseUrl);
        return new Uri(new Uri(normalized, UriKind.Absolute), relativePath);
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith("/") ? value : value + "/";
}
