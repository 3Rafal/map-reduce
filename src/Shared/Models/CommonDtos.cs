using System.ComponentModel.DataAnnotations;

namespace Shared.Models;

public sealed record MapJobRequest
{
    [Required]
    public Guid JobId { get; init; }

    [Required]
    public string BucketName { get; init; } = string.Empty;

    [Required]
    public string InputObjectKey { get; init; } = string.Empty;

    [Required]
    public string IntermediateObjectKey { get; init; } = string.Empty;

    [Required]
    [Url]
    public string CallbackUrl { get; init; } = string.Empty;
}

public sealed record MapCompletionNotification
{
    [Required]
    public Guid JobId { get; init; }

    [Required]
    public IReadOnlyList<string> IntermediateObjectKeys { get; init; } = Array.Empty<string>();
}

public sealed record ReduceJobRequest
{
    [Required]
    public Guid JobId { get; init; }

    [Required]
    public string BucketName { get; init; } = string.Empty;

    [Required]
    public IReadOnlyList<string> IntermediateObjectKeys { get; init; } = Array.Empty<string>();

    [Required]
    public string OutputObjectKey { get; init; } = string.Empty;

    [Required]
    [Url]
    public string CallbackUrl { get; init; } = string.Empty;
}

public sealed record ReduceCompletionNotification
{
    [Required]
    public Guid JobId { get; init; }

    [Required]
    public string ResultObjectKey { get; init; } = string.Empty;
}

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

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record ErrorResponse(string Message);

public sealed class MinioOptions
{
    public string Endpoint { get; init; } = "localhost";
    public int Port { get; init; } = 9000;
    public bool UseSsl { get; init; }
    public string AccessKey { get; init; } = "minioadmin";
    public string SecretKey { get; init; } = "minioadmin";
    public string BucketName { get; init; } = "mapreduce";
}
