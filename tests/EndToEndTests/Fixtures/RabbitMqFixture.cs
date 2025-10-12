using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace EndToEndTests.Fixtures;

[CollectionDefinition("RabbitMq")]
public class RabbitMqCollection : ICollectionFixture<MinioFixture>
{
}

public sealed class RabbitMqFixture : IAsyncLifetime
{
    private const string Image = "rabbitmq:3.13-management";
    private const int BrokerPort = 5672;
    private const int ManagementPort = 15672;

    private readonly HttpClient _httpClient = new();
    private string? _containerId;

    public bool IsReady { get; private set; }
    public string? SkipReason { get; private set; }

    public string HostName => "localhost";
    public int Port { get; private set; }
    public string UserName => "guest";
    public string Password => "guest";
    public string VirtualHost => "/";

    public async Task InitializeAsync()
    {
        try
        {
            await DockerHelper.RunCommandAsync($"pull {Image}");

            var containerName = $"rabbitmq-e2e-{Guid.NewGuid():N}";
            var runArgs = string.Join(' ', new[]
            {
                "run -d --rm",
                $"--name {containerName}",
                $"-p 0:{BrokerPort}",
                $"-p 0:{ManagementPort}",
                $"-e RABBITMQ_DEFAULT_USER={UserName}",
                $"-e RABBITMQ_DEFAULT_PASS={Password}",
                $"-e RABBITMQ_DEFAULT_VHOST={VirtualHost}",
                Image
            });

            _containerId = await DockerHelper.RunCommandAsync(runArgs);
            Port = await GetPublishedPortAsync(_containerId!, BrokerPort);
            var managementHttpPort = await GetPublishedPortAsync(_containerId!, ManagementPort);

            var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{UserName}:{Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

            await WaitForReadyAsync(managementHttpPort);

            IsReady = true;
        }
        catch (Exception ex)
        {
            SkipReason = $"RabbitMQ container failed to start: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_containerId))
            {
                await DockerHelper.RunCommandAsync($"stop {_containerId}", throwOnError: false);
            }
        }
        catch
        {
            // ignore cleanup errors
        }
        finally
        {
            _httpClient.Dispose();
        }
    }

    private async Task WaitForReadyAsync(int managementHttpPort)
    {
        var attempts = 0;
        var uri = new Uri($"http://localhost:{managementHttpPort}/api/overview");

        while (attempts < 120)
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
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException("RabbitMQ did not become ready in time.");
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
}
