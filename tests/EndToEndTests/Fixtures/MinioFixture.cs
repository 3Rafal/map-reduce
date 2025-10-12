using System.Text.RegularExpressions;

namespace EndToEndTests.Fixtures;

public sealed class MinioFixture : IAsyncLifetime
{
    private const string Image = "minio/minio:latest";
    private const int MinioPort = 9000;
    private const int ConsolePort = 9001;

    private readonly HttpClient _httpClient = new();
    private string? _containerId;

    public static string Endpoint => "localhost";

    public int Port { get; private set; }

    public static string AccessKey => "minioadmin";

    public static string SecretKey => "minioadmin";

    public static string BucketName => "mapreduce";

    public async Task InitializeAsync()
    {
        await DockerHelper.RunCommandAsync($"pull {Image}");

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

        _containerId = await DockerHelper.RunCommandAsync(runArgs);
        Port = await GetPublishedPortAsync(_containerId!, MinioPort);
        await WaitForReadyAsync();
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_containerId))
        {
            await DockerHelper.RunCommandAsync($"stop {_containerId}");
        }

        _httpClient.Dispose();
    }

    private static async Task<int> GetPublishedPortAsync(string containerId, int containerPort)
    {
        var output = await DockerHelper.RunCommandAsync($"port {containerId} {containerPort}/tcp");
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
            }

            attempts++;
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException("MinIO did not become ready in time.");
    }
}
