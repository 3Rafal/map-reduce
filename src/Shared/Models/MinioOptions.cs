using System.ComponentModel.DataAnnotations;

namespace Shared.Models;


public sealed class MinioOptions
{
    public string Endpoint { get; init; } = "localhost";
    public int Port { get; init; } = 9000;
    public bool UseSsl { get; init; }
    public string AccessKey { get; init; } = "minioadmin";
    public string SecretKey { get; init; } = "minioadmin";
    public string BucketName { get; init; } = "mapreduce";
}