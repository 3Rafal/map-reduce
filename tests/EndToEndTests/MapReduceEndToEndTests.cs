extern alias ApiServiceAlias;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EndToEndTests.Factories;
using EndToEndTests.Fixtures;
using Shared.Models;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using ApiFileReference = ApiServiceAlias::ApiService.Models.FileReference;
using ApiCreateJobRequest = ApiServiceAlias::ApiService.Models.CreateJobRequest;

namespace EndToEndTests;

[Collection("EndToEnd")]
public sealed class MapReduceEndToEndTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly MinioFixture _minio;
    private RabbitMqFixture? _rabbitMq;
    private MapperServiceFactory? _mapperFactory;
    private ReducerServiceFactory? _reducerFactory;
    private ApiServiceFactory? _apiFactory;
    private HttpClient? _apiClient;
    private bool _skip;
    private string? _skipReason;

    public MapReduceEndToEndTests(MinioFixture minio)
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

        _mapperFactory = new MapperServiceFactory(_minio, _rabbitMq);
        _reducerFactory = new ReducerServiceFactory(_minio, _rabbitMq);
        _apiFactory = new ApiServiceFactory(_minio, _rabbitMq, _mapperFactory, _reducerFactory);

        // Initialize mapper and reducer servers
        _mapperFactory.CreateClient();
        _reducerFactory.CreateClient();

        _apiClient = _apiFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/")
        });

        // Ensure default headers mimic JSON clients
        _apiClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Give a brief moment for MassTransit consumers to start up
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    public async Task DisposeAsync()
    {
        _apiClient?.Dispose();
        _apiFactory?.Dispose();
        _mapperFactory?.Dispose();
        _reducerFactory?.Dispose();
        if (_rabbitMq is not null)
        {
            await _rabbitMq.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task WordCountPipeline_CompletesAndAggregates()
    {
        Skip.If(_skip, _skipReason ?? "RabbitMQ fixture not ready.");

        var seedText = "Hello world hello map reduce map";
        var multipart = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(seedText)) { Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } }, "file", "sample.txt" }
        };

        var uploadResponse = await _apiClient!.PostAsync("/files", multipart);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        Assert.True(uploadResponse.IsSuccessStatusCode, $"Upload failed: {uploadResponse.StatusCode} {uploadBody}");
        var uploadedReference = await uploadResponse.Content.ReadFromJsonAsync<ApiFileReference>(SerializerOptions);
        Assert.NotNull(uploadedReference);

        var createRequest = new ApiCreateJobRequest
        {
            InputFile = uploadedReference
        };

        var submitResponse = await _apiClient.PostAsJsonAsync("/jobs", createRequest, SerializerOptions);
        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        Assert.True(submitResponse.IsSuccessStatusCode, $"Job submission failed: {submitResponse.StatusCode} {submitBody}");
        var jobSummary = await submitResponse.Content.ReadFromJsonAsync<JobSummaryDto>(SerializerOptions);
        Assert.NotNull(jobSummary);
        Assert.NotEqual(JobStatus.Failed, jobSummary!.Status);

        JobSummaryDto? current;
        var attempts = 0;
        var maxAttempts = 120; // Increase max attempts for queue processing
        var delay = TimeSpan.FromMilliseconds(500); // Slightly longer delay

        do
        {
            await Task.Delay(delay);
            var statusResponse = await _apiClient.GetAsync($"/jobs/{jobSummary.JobId}");
            statusResponse.EnsureSuccessStatusCode();
            current = await statusResponse.Content.ReadFromJsonAsync<JobSummaryDto>(SerializerOptions);

            // Log status for debugging
            if (attempts % 10 == 0)
            {
                Console.WriteLine($"Attempt {attempts}: Job status = {current?.Status}");
            }

            attempts++;
        }
        while (current is not { Status: JobStatus.Completed or JobStatus.Failed } && attempts < maxAttempts);

        Assert.NotNull(current);

        if (current!.Status == JobStatus.Failed)
        {
            Assert.Fail($"Job failed: {current.FailureReason}");
        }

        Assert.Equal(JobStatus.Completed, current.Status);
        Assert.False(string.IsNullOrWhiteSpace(current.ResultObjectKey));

        var resultResponse = await _apiClient.GetAsync($"/jobs/{current.JobId}/result");
        var resultBody = await resultResponse.Content.ReadAsStringAsync();
        Assert.True(resultResponse.IsSuccessStatusCode, $"Result retrieval failed: {resultResponse.StatusCode} {resultBody}");
        var resultJson = await resultResponse.Content.ReadAsStringAsync();
        var counts = JsonSerializer.Deserialize<Dictionary<string, int>>(resultJson, SerializerOptions);
        Assert.NotNull(counts);

        Assert.Equal(2, counts!["hello"]);
        Assert.Equal(1, counts["world"]);
        Assert.Equal(2, counts["map"]);
        Assert.Equal(1, counts["reduce"]);
    }
}
