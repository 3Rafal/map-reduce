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
    private MapperServiceFactory? _mapperFactory;
    private ReducerServiceFactory? _reducerFactory;
    private ApiServiceFactory? _apiFactory;
    private HttpClient? _apiClient;

    public MapReduceEndToEndTests(MinioFixture minio)
    {
        _minio = minio;
    }

    public Task InitializeAsync()
    {
        _mapperFactory = new MapperServiceFactory(_minio);
        _reducerFactory = new ReducerServiceFactory(_minio);
        _apiFactory = new ApiServiceFactory(_minio, _mapperFactory, _reducerFactory);

        _mapperFactory.UseCallbackHandler(() => _apiFactory.Server.CreateHandler());
        _reducerFactory.UseCallbackHandler(() => _apiFactory.Server.CreateHandler());

        // Initialize mapper and reducer servers
        _mapperFactory.CreateClient();
        _reducerFactory.CreateClient();

        _apiClient = _apiFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/")
        });

        // Ensure default headers mimic JSON clients
        _apiClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _apiClient?.Dispose();
        _apiFactory?.Dispose();
        _mapperFactory?.Dispose();
        _reducerFactory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WordCountPipeline_CompletesAndAggregates()
    {
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
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            var statusResponse = await _apiClient.GetAsync($"/jobs/{jobSummary.JobId}");
            statusResponse.EnsureSuccessStatusCode();
            current = await statusResponse.Content.ReadFromJsonAsync<JobSummaryDto>(SerializerOptions);
            attempts++;
        }
        while (current is not { Status: JobStatus.Completed } && attempts < 40);

        Assert.NotNull(current);
        Assert.Equal(JobStatus.Completed, current!.Status);
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
