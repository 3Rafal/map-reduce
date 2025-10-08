using System.ComponentModel.DataAnnotations;
using Shared.Models;

namespace ReducerService.Models;

public sealed record ReduceRequest
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

    public ReduceJobRequest ToShared() => new()
    {
        JobId = JobId,
        BucketName = BucketName,
        IntermediateObjectKeys = IntermediateObjectKeys,
        OutputObjectKey = OutputObjectKey,
        CallbackUrl = CallbackUrl
    };
}
