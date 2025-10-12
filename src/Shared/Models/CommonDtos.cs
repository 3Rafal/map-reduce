using System.ComponentModel.DataAnnotations;

namespace Shared.Models;

public sealed record JobSummaryDto
{
    [Required]
    public Guid JobId { get; init; }

    public JobStatus Status { get; init; }

    [Required]
    public string BucketName { get; init; } = string.Empty;

    [Required]
    public string InputObjectKey { get; init; } = string.Empty;

    public IReadOnlyList<string> IntermediateObjectKeys { get; init; } = Array.Empty<string>();

    public string? ResultObjectKey { get; init; }

    public string? FailureReason { get; init; }

    public int MapTasksTotal { get; init; }

    public int MapTasksCompleted { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record ErrorResponse(string Message);

