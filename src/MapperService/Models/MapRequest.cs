using System.ComponentModel.DataAnnotations;
using Shared.Models;

namespace MapperService.Models;

public sealed record MapRequest
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

    public MapJobRequest ToShared() => new()
    {
        JobId = JobId,
        BucketName = BucketName,
        InputObjectKey = InputObjectKey,
        IntermediateObjectKey = IntermediateObjectKey,
        CallbackUrl = CallbackUrl
    };
}
