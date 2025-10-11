using System.Collections.Concurrent;
using ApiService.Models;
using Shared.Models;

namespace ApiService.Services;

public sealed class JobCoordinator
{
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly ILogger<JobCoordinator> _logger;
    private readonly IQueuePublisher _queuePublisher;

    public JobCoordinator(
        ILogger<JobCoordinator> logger,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _queuePublisher = queuePublisher;
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

        public bool HandleMapCompletion(
            Guid jobId,
            string intermediateObjectKey)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                _logger.LogWarning("Received map completion for unknown job {JobId}", jobId);
                return false;
            }
    
            job.IntermediateObjectKeys.Clear();
            job.IntermediateObjectKeys.Add(intermediateObjectKey);
            job.Status = JobStatus.Reducing;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation("Job {JobId} status updated to Reducing", jobId);
            return true;
        }
    public void HandleMapFailureAsync(Guid jobId, string errorMessage)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            _logger.LogWarning("Received map failure for unknown job {JobId}", jobId);
            return;
        }

        job.Status = JobStatus.Failed;
        job.FailureReason = errorMessage;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        _logger.LogError("Map failed for job {JobId}: {ErrorMessage}", jobId, errorMessage);
    }

    public bool HandleReduceCompletionAsync(
        Guid jobId,
        string resultBucketName,
        string resultObjectKey)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            _logger.LogWarning("Received reduce completion for unknown job {JobId}", jobId);
            return false;
        }

        job.ResultObjectKey = resultObjectKey;
        job.Status = JobStatus.Completed;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public void HandleReduceFailureAsync(Guid jobId, string errorMessage)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            _logger.LogWarning("Received reduce failure for unknown job {JobId}", jobId);
            return;
        }

        job.Status = JobStatus.Failed;
        job.FailureReason = errorMessage;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        _logger.LogError("Reduce failed for job {JobId}: {ErrorMessage}", jobId, errorMessage);
    }

    private async Task StartMappingAsync(Job job, CancellationToken cancellationToken)
    {
        job.Status = JobStatus.Mapping;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        var mapJobMessage = new MapJobMessage
        {
            JobId = job.Id,
            InputBucketName = job.BucketName,
            InputObjectKey = job.InputObjectKey
        };

        await _queuePublisher.PublishMapJobAsync(mapJobMessage, cancellationToken);
        _logger.LogInformation("Published map job for job {JobId}", job.Id);
    }
}
