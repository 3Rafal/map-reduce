extern alias ApiServiceAlias;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EndToEndTests.Factories;
using EndToEndTests.Fixtures;
using Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using ApiFileReference = ApiServiceAlias::ApiService.Models.FileReference;
using ApiCreateJobRequest = ApiServiceAlias::ApiService.Models.CreateJobRequest;

namespace EndToEndTests;

/// <summary>
/// Base class for MapReduce end-to-end tests with common setup and helper methods
/// </summary>
[Collection("EndToEnd")]
public abstract class BaseMapReduceTest : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    protected readonly MinioFixture _minio;
    protected RabbitMqFixture? _rabbitMq;
    protected MapperServiceFactory? _mapperFactory1;
    protected MapperServiceFactory? _mapperFactory2;
    protected ReducerServiceFactory? _reducerFactory;
    protected ApiServiceFactory? _apiFactory;
    protected HttpClient? _apiClient;
    protected bool _skip;
    protected string? _skipReason;

    protected BaseMapReduceTest(MinioFixture minio)
    {
        _minio = minio;
    }

    public async Task InitializeAsync()
    {
        var rabbitMq = new RabbitMqFixture();
        await rabbitMq.InitializeAsync();

        if (!rabbitMq.IsReady)
        {
            _skip = true;
            _skipReason = rabbitMq.SkipReason ?? "RabbitMQ fixture failed to start.";
            await rabbitMq.DisposeAsync();
            return;
        }

        _rabbitMq = rabbitMq;

        _mapperFactory1 = new MapperServiceFactory(_minio, _rabbitMq);
        _mapperFactory2 = new MapperServiceFactory(_minio, _rabbitMq);
        _reducerFactory = new ReducerServiceFactory(_minio, _rabbitMq);
        _apiFactory = new ApiServiceFactory(_minio, _rabbitMq, _mapperFactory1, _reducerFactory);

        var mapperClient1 = _mapperFactory1.CreateClient();
        var mapperClient2 = _mapperFactory2.CreateClient();
        var reducerClient = _reducerFactory.CreateClient();
        _apiClient = _apiFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/")
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await WaitForServiceToBeReadyAsync(_apiClient, "ApiService", cts.Token);
        await WaitForServiceToBeReadyAsync(mapperClient1, "MapperService-1", cts.Token);
        await WaitForServiceToBeReadyAsync(mapperClient2, "MapperService-2", cts.Token);
        await WaitForServiceToBeReadyAsync(reducerClient, "ReducerService", cts.Token);

        // Ensure default headers mimic JSON clients
        _apiClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task DisposeAsync()
    {
        _apiClient?.Dispose();
        _apiFactory?.Dispose();
        _mapperFactory1?.Dispose();
        _mapperFactory2?.Dispose();
        _reducerFactory?.Dispose();
        if (_rabbitMq is not null)
        {
            await _rabbitMq.DisposeAsync();
        }
    }

    private async Task WaitForServiceToBeReadyAsync(HttpClient client, string serviceName, CancellationToken cancellationToken)
    {
        var attempts = 0;
        var maxAttempts = 20;
        var delay = TimeSpan.FromMilliseconds(500);

        while (attempts < maxAttempts)
        {
            try
            {
                var response = await client.GetAsync("/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"{serviceName} is ready.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Attempt {attempts + 1} to connect to {serviceName} failed: {ex.Message}");
            }

            attempts++;
            await Task.Delay(delay, cancellationToken);
        }

        throw new Exception($"{serviceName} did not become ready in the allotted time.");
    }

    /// <summary>
    /// Uploads a file with the given content and filename
    /// </summary>
    protected async Task<ApiFileReference> UploadFileAsync(string content, string filename)
    {
        var multipart = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(content)) { Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } }, "file", filename }
        };

        var uploadResponse = await _apiClient!.PostAsync("/files", multipart);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        Assert.True(uploadResponse.IsSuccessStatusCode, $"Upload failed: {uploadResponse.StatusCode} {uploadBody}");

        var uploadedReference = await uploadResponse.Content.ReadFromJsonAsync<ApiFileReference>(SerializerOptions);
        Assert.NotNull(uploadedReference);
        return uploadedReference!;
    }

    /// <summary>
    /// Creates and submits a MapReduce job for the given file
    /// </summary>
    protected async Task<JobSummaryDto> SubmitJobAsync(ApiFileReference fileReference)
    {
        var createRequest = new ApiCreateJobRequest
        {
            InputFile = fileReference
        };

        var submitResponse = await _apiClient!.PostAsJsonAsync("/jobs", createRequest, SerializerOptions);
        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        Assert.True(submitResponse.IsSuccessStatusCode, $"Job submission failed: {submitResponse.StatusCode} {submitBody}");

        var jobSummary = await submitResponse.Content.ReadFromJsonAsync<JobSummaryDto>(SerializerOptions);
        Assert.NotNull(jobSummary);
        Assert.NotEqual(JobStatus.Failed, jobSummary!.Status);

        return jobSummary!;
    }

    /// <summary>
    /// Waits for a job to complete and returns the final status
    /// </summary>
    protected async Task<JobSummaryDto> WaitForJobCompletionAsync(Guid jobId, int maxAttempts = 120, TimeSpan? delay = null)
    {
        var actualDelay = delay ?? TimeSpan.FromMilliseconds(500);
        JobSummaryDto? current;

        var attempts = 0;
        do
        {
            await Task.Delay(actualDelay);
            var statusResponse = await _apiClient!.GetAsync($"/jobs/{jobId}");
            statusResponse.EnsureSuccessStatusCode();
            current = await statusResponse.Content.ReadFromJsonAsync<JobSummaryDto>(SerializerOptions);

            if (attempts % 10 == 0)
            {
                Console.WriteLine($"Attempt {attempts}: Job status = {current?.Status}, MapTasksCompleted = {current?.MapTasksCompleted}, MapTasksTotal = {current?.MapTasksTotal}");
            }

            attempts++;
        }
        while (current is not { Status: JobStatus.Completed or JobStatus.Failed } && attempts < maxAttempts);

        Assert.NotNull(current);

        if (current!.Status == JobStatus.Failed)
        {
            Assert.Fail($"Job failed: {current.FailureReason}");
        }

        return current;
    }

    /// <summary>
    /// Downloads and deserializes the job result
    /// </summary>
    protected async Task<Dictionary<string, int>> GetJobResultAsync(Guid jobId)
    {
        var resultResponse = await _apiClient!.GetAsync($"/jobs/{jobId}/result");
        var resultBody = await resultResponse.Content.ReadAsStringAsync();
        Assert.True(resultResponse.IsSuccessStatusCode, $"Result retrieval failed: {resultResponse.StatusCode} {resultBody}");

        var resultJson = await resultResponse.Content.ReadAsStringAsync();
        var counts = JsonSerializer.Deserialize<Dictionary<string, int>>(resultJson, SerializerOptions);
        Assert.NotNull(counts);

        return counts!;
    }

    /// <summary>
    /// Helper method to run the complete MapReduce workflow for a given content
    /// </summary>
    protected async Task<Dictionary<string, int>> RunMapReduceWorkflowAsync(string content, string filename, int maxAttempts = 120)
    {
        Skip.If(_skip, _skipReason ?? "RabbitMQ fixture not ready.");

        var uploadedReference = await UploadFileAsync(content, filename);

        var jobSummary = await SubmitJobAsync(uploadedReference);

        var finalStatus = await WaitForJobCompletionAsync(jobSummary.JobId, maxAttempts);

        Assert.Equal(JobStatus.Completed, finalStatus.Status);
        Assert.False(string.IsNullOrWhiteSpace(finalStatus.ResultObjectKey));

        return await GetJobResultAsync(finalStatus.JobId);
    }
}