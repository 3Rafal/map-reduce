using System.ComponentModel.DataAnnotations;

namespace Shared.Models;

public record MapJobMessage
{
    public Guid JobId { get; init; } = Guid.NewGuid();
    [Required]
    public string InputBucketName { get; init; } = string.Empty;
    [Required]
    public string InputObjectKey { get; init; } = string.Empty;
    public Dictionary<string, string> Options { get; init; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public record MapResultMessage
{
    public Guid JobId { get; init; }
    [Required]
    public string IntermediateBucketName { get; init; } = string.Empty;
    [Required]
    public string IntermediateObjectKey { get; init; } = string.Empty;
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
    public bool Success { get; init; } = true;
    public string? ErrorMessage { get; init; }
}

public record ReduceJobMessage
{
    public Guid JobId { get; init; }
    [Required]
    public List<string> IntermediateObjectKeys { get; init; } = new();
    [Required]
    public string IntermediateBucketName { get; init; } = string.Empty;
    public Dictionary<string, string> Options { get; init; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public record ReduceResultMessage
{
    public Guid JobId { get; init; }
    [Required]
    public string ResultBucketName { get; init; } = string.Empty;
    [Required]
    public string ResultObjectKey { get; init; } = string.Empty;
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
    public bool Success { get; init; } = true;
    public string? ErrorMessage { get; init; }
}

public static class QueueNames
{
    public const string MapJobs = "map-jobs";
    public const string MapResults = "map-results";
    public const string ReduceJobs = "reduce-jobs";
    public const string ReduceResults = "reduce-results";
}