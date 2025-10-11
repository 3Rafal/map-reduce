
using System.ComponentModel.DataAnnotations;

namespace Shared.Models;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string HostName { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    [Required]
    public string UserName { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; }
}
