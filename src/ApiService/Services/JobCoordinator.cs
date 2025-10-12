using System.Collections.Concurrent;
using ApiService.Models;
using Minio;
using Shared.Models;

namespace ApiService.Services;

public sealed class JobCoordinator
{
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly ILogger<JobCoordinator> _logger;
    private readonly IQueuePublisher _queuePublisher;
    private readonly IMinioClient _minioClient;
    private const int ChunkSize = 256 * 1024; // 256 KiB

    public JobCoordinator(
        ILogger<JobCoordinator> logger,
        IQueuePublisher queuePublisher,
        IMinioClient minioClient)
    {
        _logger = logger;
        _queuePublisher = queuePublisher;
        _minioClient = minioClient;
    }

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

        public async Task<bool> HandleMapCompletionAsync(
            Guid jobId,
            string intermediateObjectKey,
            CancellationToken cancellationToken = default)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                _logger.LogWarning("Received map completion for unknown job {JobId}", jobId);
                return false;
            }

            // Add the intermediate result and increment completed count
            job.IntermediateObjectKeys.Add(intermediateObjectKey);
                        var completedCount = job.IncrementMapTasksCompleted();
                         job.UpdatedAt = DateTimeOffset.UtcNow;
             
                         _logger.LogInformation("Job {JobId}: Map chunk completed ({Completed}/{Total}), intermediate: {Key}",
                             jobId, completedCount, job.MapTasksTotal, intermediateObjectKey);
             
                         // Check if all map chunks are completed
                         if (completedCount == job.MapTasksTotal)            {
                _logger.LogInformation("Job {JobId}: All map chunks completed, starting reduce phase", jobId);
                job.Status = JobStatus.Reducing;
                job.UpdatedAt = DateTimeOffset.UtcNow;

                // Start the reduce phase
                await StartReducingAsync(job, cancellationToken);
            }

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

    public bool HandleReduceCompletion(
        Guid jobId,
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

    public void HandleReduceFailure(Guid jobId, string errorMessage)
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

        // Get file size to determine number of chunks
        var statArgs = new StatObjectArgs()
            .WithBucket(job.BucketName)
            .WithObject(job.InputObjectKey);

        var objectStat = await _minioClient.StatObjectAsync(statArgs, cancellationToken);
        var fileSize = objectStat.Size;

        // Calculate number of chunks
        var chunkCount = (int)Math.Ceiling((double)fileSize / ChunkSize);
        job.MapTasksTotal = chunkCount;

        _logger.LogInformation("Job {JobId}: File size {FileSize} bytes will be split into {ChunkCount} chunks",
            job.Id, fileSize, chunkCount);

        // Publish map job for each chunk
        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var offset = (long)chunkIndex * ChunkSize;
            var count = (int)Math.Min(ChunkSize, fileSize - offset);

            var mapJobMessage = new MapJobMessage
            {
                JobId = job.Id,
                InputBucketName = job.BucketName,
                InputObjectKey = job.InputObjectKey,
                Offset = offset,
                Count = count
            };

            await _queuePublisher.PublishMapJobAsync(mapJobMessage, cancellationToken);
        }

        _logger.LogInformation("Published {ChunkCount} map jobs for job {JobId}", chunkCount, job.Id);
    }

    private async Task StartReducingAsync(Job job, CancellationToken cancellationToken)
    {
        var reduceJobMessage = new ReduceJobMessage
        {
            JobId = job.Id,
            IntermediateBucketName = job.BucketName,
            IntermediateObjectKeys = job.IntermediateObjectKeys.ToList()
        };

        await _queuePublisher.PublishReduceJobAsync(reduceJobMessage, cancellationToken);
        _logger.LogInformation("Published reduce job for job {JobId} with {Count} intermediate results",
            job.Id, job.IntermediateObjectKeys.Count);
    }
}
