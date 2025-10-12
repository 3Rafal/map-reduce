using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace EndToEndTests.Fixtures;

public sealed class KubernetesFixture : IAsyncLifetime
{
    private const string ApiImage = "mapreduce-api:latest";
    private const string MapperImage = "mapreduce-mapper:latest";
    private const string ReducerImage = "mapreduce-reducer:latest";

    private static readonly Lazy<string> RepoRoot = new(LocateRepoRoot);

    private string? _namespace;
    private Process? _portForwardProcess;
    private readonly CancellationTokenSource _cts = new();

    public bool IsReady { get; private set; }
    public string? SkipReason { get; private set; }
    public HttpClient ApiClient { get; private set; } = null!;
    public JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    public int ApiPort { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await RunProcessAsync("kubectl", ["version", "--client"], throwOnError: true);
            var (_, StandardOutput, _) = await RunProcessAsync("kubectl", ["config", "current-context"], throwOnError: true);
            var currentContext = StandardOutput.Trim();
            if (string.IsNullOrWhiteSpace(currentContext))
            {
                throw new InvalidOperationException("kubectl current-context is empty.");
            }

            await EnsureImagesAvailableAsync(currentContext);
        }
        catch (Exception ex)
        {
            SkipReason = $"kubectl/images not available: {ex.Message}";
            Console.WriteLine($"[KubernetesFixture] {SkipReason}");
            return;
        }

        _namespace = $"mapreduce-e2e-{Guid.NewGuid():N}";
        try
        {
            await RunProcessAsync("kubectl", ["create", "namespace", _namespace!], throwOnError: true);

            foreach (var manifest in GetManifestPaths())
            {
                await RunProcessAsync("kubectl", ["apply", "-n", _namespace!, "-f", manifest], throwOnError: true);
            }

            foreach (var deployment in new[] { "minio", "mapper-service", "reducer-service", "api-service" })
            {
                await RunProcessAsync("kubectl", ["wait", "--for=condition=available", "--timeout=180s", $"deploy/{deployment}", "-n", _namespace!], throwOnError: true);
            }

            ApiPort = GetFreeTcpPort();
            StartPortForward(ApiPort);
            ApiClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{ApiPort}/") };

            await WaitForHealthAsync();
            IsReady = true;
        }
        catch (Exception ex)
        {
            SkipReason = $"Failed to initialize Kubernetes fixtures: {ex.Message}";
            Console.WriteLine($"[KubernetesFixture] {SkipReason}");
            await DisposeAsync();
        }
    }

    public async Task DisposeAsync()
    {
        ApiClient?.Dispose();
        try
        {
            if (_portForwardProcess is not null && !_portForwardProcess.HasExited)
            {
                _cts.Cancel();
                _portForwardProcess.Kill(entireProcessTree: true);
                await _portForwardProcess.WaitForExitAsync();
            }
        }
        catch
        {
        }

        await RunProcessAsync("kubectl", ["delete", "namespace", _namespace!, "--ignore-not-found"]);
        Console.WriteLine($"[KubernetesFixture] Deleted namespace {_namespace}");
    }

    private static IEnumerable<string> GetManifestPaths()
    {
        var root = Path.Combine(RepoRoot.Value, "deploy", "k8s");
        return
        [
            Path.Combine(root, "minio.yaml"),
            Path.Combine(root, "mapper-service.yaml"),
            Path.Combine(root, "reducer-service.yaml"),
            Path.Combine(root, "api-service.yaml"),
            Path.Combine(root, "ingress.yaml")
        ];
    }

    private async Task WaitForHealthAsync()
    {
        var attempts = 0;
        while (attempts < 60)
        {
            try
            {
                var response = await ApiClient.GetAsync("health", _cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
            }

            attempts++;
            await Task.Delay(TimeSpan.FromSeconds(1), _cts.Token);
        }

        throw new TimeoutException("API service did not become healthy in time.");
    }

    private static async Task EnsureImagesAvailableAsync(string currentContext)
    {
        if (!currentContext.StartsWith("kind", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (ExitCode, _, StandardError) = await RunProcessAsync("kind", ["version"]);
        if (ExitCode != 0)
        {
            throw new InvalidOperationException($"kind CLI not available: {StandardError}");
        }

        foreach (var image in new[] { ApiImage, MapperImage, ReducerImage })
        {
            if (!await ImageExistsAsync(image))
            {
                await BuildImageAsync(image);
            }

            var loadResult = await RunProcessAsync("kind", ["load", "docker-image", image]);
            if (loadResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"kind load docker-image {image} failed:{Environment.NewLine}{loadResult.StandardError}");
            }
        }
    }

    private static async Task<bool> ImageExistsAsync(string tag)
    {
        var result = await RunProcessAsync("docker", ["inspect", "--type=image", tag]);
        return result.ExitCode == 0;
    }

    private static async Task BuildImageAsync(string tag)
    {
        static string DockerPath(string service) => Path.Combine("src", service, "Dockerfile");
        var dockerfile = tag switch
        {
            ApiImage => DockerPath("ApiService"),
            MapperImage => DockerPath("MapperService"),
            ReducerImage => DockerPath("ReducerService"),
            _ => throw new InvalidOperationException($"Unknown image tag {tag}")
        };

        await RunProcessAsync(
            "docker",
            ["build", "-t", tag, "-f", dockerfile, RepoRoot.Value],
            throwOnError: true);
    }

    private void StartPortForward(int localPort)
    {
        if (_namespace is null)
        {
            throw new InvalidOperationException("Namespace not created.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "kubectl",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot.Value
        };

        psi.ArgumentList.Add("port-forward");
        psi.ArgumentList.Add("svc/api-service");
        psi.ArgumentList.Add($"{localPort}:8080");
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add(_namespace);

        _portForwardProcess = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start kubectl port-forward.");

        _ = Task.Run(async () =>
        {
            var reader = _portForwardProcess!.StandardOutput;
            while (!_portForwardProcess.HasExited && !_cts.IsCancellationRequested)
            {
                await reader.ReadLineAsync();
            }
        }, _cts.Token);

        _ = Task.Run(async () =>
        {
            var reader = _portForwardProcess!.StandardError;
            while (!_portForwardProcess.HasExited && !_cts.IsCancellationRequested)
            {
                await reader.ReadLineAsync();
            }
        }, _cts.Token);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        string fileName,
        string[] arguments,
        bool throwOnError = false)
        => await RunProcessAsync(fileName, arguments, throwOnError, workingDirectory: RepoRoot.Value);

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        string fileName,
        string[] arguments,
        bool throwOnError,
        string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName} {string.Join(' ', arguments)}");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        stdout = stdout.Trim();
        stderr = stderr.Trim();

        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} failed.{Environment.NewLine}STDOUT: {stdout}{Environment.NewLine}STDERR: {stderr}");
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static string LocateRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "MapReduceSolution.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException("Unable to locate solution root (MapReduceSolution.sln).");
        }

        return current.FullName;
    }
}
