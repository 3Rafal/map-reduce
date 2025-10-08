using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace EndToEndTests.Fixtures;

public sealed class MinioFixture : IAsyncLifetime
{
    private const string Image = "minio/minio:latest";
    private const int MinioPort = 9000;
    private const int ConsolePort = 9001;

    private readonly HttpClient _httpClient = new();
    private string? _containerId;

    public string Endpoint => "localhost";

    public int Port { get; private set; }

    public string AccessKey => "minioadmin";

    public string SecretKey => "minioadmin";

    public string BucketName => "mapreduce";

    public async Task InitializeAsync()
    {
        await RunDockerCommandAsync($"pull {Image}");

        var containerName = $"mapreduce-minio-{Guid.NewGuid():N}";
        var runArgs = string.Join(' ', new[]
        {
            "run -d --rm",
            $"--name {containerName}",
            $"-p 0:{MinioPort}",
            $"-p 0:{ConsolePort}",
            $"-e MINIO_ROOT_USER={AccessKey}",
            $"-e MINIO_ROOT_PASSWORD={SecretKey}",
            Image,
            "server /data",
            $"--console-address :{ConsolePort}"
        });

        _containerId = await RunDockerCommandAsync(runArgs);
        Port = await GetPublishedPortAsync(_containerId!, MinioPort);
        await WaitForReadyAsync();
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_containerId))
        {
            await RunDockerCommandAsync($"stop {_containerId}");
        }

        _httpClient.Dispose();
    }

    private static async Task<string> RunDockerCommandAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start docker process.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Docker command failed: docker {arguments}\nSTDOUT: {stdout}\nSTDERR: {stderr}");
        }

        return stdout.Trim();
    }

    private static async Task<int> GetPublishedPortAsync(string containerId, int containerPort)
    {
        var output = await RunDockerCommandAsync($"port {containerId} {containerPort}/tcp");
        var match = Regex.Match(output, @":(?<port>\d+)$");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Unable to determine published port from: {output}");
        }

        return int.Parse(match.Groups["port"].Value);
    }

    private async Task WaitForReadyAsync()
    {
        var attempts = 0;
        var uri = new Uri($"http://{Endpoint}:{Port}/minio/health/ready");

        while (attempts < 40)
        {
            try
            {
                using var response = await _httpClient.GetAsync(uri);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // ignore and retry
            }

            attempts++;
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException("MinIO did not become ready in time.");
    }
}
