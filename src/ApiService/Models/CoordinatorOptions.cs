using System.ComponentModel.DataAnnotations;

namespace ApiService.Models;

public sealed class CoordinatorOptions
{
    [Required]
    [Url]
    public string MapperBaseUrl { get; init; } = "http://localhost:5072";

    [Required]
    [Url]
    public string ReducerBaseUrl { get; init; } = "http://localhost:5082";

    [Required]
    [Url]
    public string CallbackBaseUrl { get; init; } = "http://localhost:5000";
}
