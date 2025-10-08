using System.ComponentModel.DataAnnotations;

namespace ApiService.Models;

public sealed class CreateJobRequest
{
    [Required]
    public FileReference? InputFile { get; init; }
}
