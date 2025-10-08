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

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
