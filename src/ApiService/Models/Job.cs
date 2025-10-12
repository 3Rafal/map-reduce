using Shared.Models;

namespace ApiService.Models;

public sealed class Job
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string BucketName { get; init; } = string.Empty;

    public string InputObjectKey { get; init; } = string.Empty;

    public List<string> IntermediateObjectKeys { get; } = new();

    public string? ResultObjectKey { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;

    public string? FailureReason { get; set; }

    public int MapTasksTotal { get; set; }

    private int _mapTasksCompleted;

    public int MapTasksCompleted => _mapTasksCompleted;

    public int IncrementMapTasksCompleted()
    {
        return Interlocked.Increment(ref _mapTasksCompleted);
    }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
