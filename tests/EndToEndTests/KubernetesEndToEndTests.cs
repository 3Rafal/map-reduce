extern alias ApiServiceAlias;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EndToEndTests.Fixtures;

namespace EndToEndTests.Kubernetes;

using ApiFileReference = ApiServiceAlias::ApiService.Models.FileReference;
using ApiCreateJobRequest = ApiServiceAlias::ApiService.Models.CreateJobRequest;

[Collection("Kubernetes")]
public sealed class KubernetesEndToEndTests
{
    private readonly KubernetesFixture _fixture;

    public KubernetesEndToEndTests(KubernetesFixture fixture)
    {
        _fixture = fixture;
        if (!fixture.IsReady)
        {
            Console.WriteLine($"[KubernetesTest] {fixture.SkipReason ?? "Not ready"}");
            Skip.If(true, fixture.SkipReason ?? "Kubernetes fixture is not ready.");
            return;
        }
    }

    [SkippableFact]
    public async Task WordCountPipeline_CompletesAndAggregates()
    {
        var seedText = "Hello world hello map reduce map";
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(seedText)) { Headers = { { "Content-Type", "text/plain" } } }, "file", "sample.txt" }
        };

        var client = _fixture.ApiClient;
        var uploadResponse = await client.PostAsync("files", content);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        Assert.True(uploadResponse.IsSuccessStatusCode, $"Upload failed: {uploadResponse.StatusCode} {uploadBody}");

        var reference = JsonSerializer.Deserialize<ApiFileReference>(uploadBody, _fixture.SerializerOptions);
        Assert.NotNull(reference);

        var createJob = new ApiCreateJobRequest
        {
            InputFile = reference
        };

        var submitResponse = await client.PostAsync("jobs", JsonContent.Create(createJob));
        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        Assert.True(submitResponse.IsSuccessStatusCode, $"Job submission failed: {submitResponse.StatusCode} {submitBody}");

        var job = JsonSerializer.Deserialize<Shared.Models.JobSummaryDto>(submitBody, _fixture.SerializerOptions);
        Assert.NotNull(job);

        Shared.Models.JobSummaryDto? current = job;
        var attempts = 0;
        while (current is { Status: not Shared.Models.JobStatus.Completed } && attempts < 120)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            var statusResponse = await client.GetAsync($"jobs/{job!.JobId}");
            statusResponse.EnsureSuccessStatusCode();
            var statusBody = await statusResponse.Content.ReadAsStringAsync();
            current = JsonSerializer.Deserialize<Shared.Models.JobSummaryDto>(statusBody, _fixture.SerializerOptions);
            attempts++;
        }

        Assert.NotNull(current);
        Assert.Equal(Shared.Models.JobStatus.Completed, current!.Status);
        Assert.False(string.IsNullOrWhiteSpace(current.ResultObjectKey));

        var resultResponse = await client.GetAsync($"jobs/{current.JobId}/result");
        var resultBody = await resultResponse.Content.ReadAsStringAsync();
        Assert.True(resultResponse.IsSuccessStatusCode, $"Result retrieval failed: {resultResponse.StatusCode} {resultBody}");

        var counts = JsonSerializer.Deserialize<Dictionary<string, int>>(resultBody, _fixture.SerializerOptions);
        Assert.NotNull(counts);
        Assert.Equal(2, counts!["hello"]);
        Assert.Equal(1, counts["world"]);
        Assert.Equal(2, counts["map"]);
        Assert.Equal(1, counts["reduce"]);
    }
}
