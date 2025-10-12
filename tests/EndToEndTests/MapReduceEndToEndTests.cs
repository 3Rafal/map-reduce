using EndToEndTests.Fixtures;

namespace EndToEndTests;

public sealed class MapReduceEndToEndTests : BaseMapReduceTest
{
    public MapReduceEndToEndTests(MinioFixture minio) : base(minio)
    {
    }

    [SkippableFact]
    public async Task WordCountPipeline_CompletesAndAggregates()
    {
        var seedText = "Hello world hello map reduce map";

        var counts = await RunMapReduceWorkflowAsync(seedText, "sample.txt");

        Assert.Equal(2, counts["hello"]);
        Assert.Equal(1, counts["world"]);
        Assert.Equal(2, counts["map"]);
        Assert.Equal(1, counts["reduce"]);
    }

    [SkippableFact]
    public async Task CanProcessLargeFileWithChunking_EndToEnd()
    {
        var sherlockTextPath = Path.Combine(Directory.GetCurrentDirectory(), "sherlock_holmes.txt");
        Assert.True(File.Exists(sherlockTextPath), $"Test file not found: {sherlockTextPath}");

        var sherlockText = await File.ReadAllTextAsync(sherlockTextPath);
        Assert.False(string.IsNullOrEmpty(sherlockText), "Test file is empty");

        var uploadedReference = await UploadFileAsync(sherlockText, "sherlock_holmes.txt");

        var jobSummary = await SubmitJobAsync(uploadedReference);

        var finalStatus = await WaitForJobCompletionAsync(
            jobSummary.JobId,
            maxAttempts: 300,
            delay: TimeSpan.FromSeconds(1));

        Assert.True(finalStatus.MapTasksTotal > 1, $"Expected multiple map tasks, but got {finalStatus.MapTasksTotal}");
        Assert.Equal(finalStatus.MapTasksTotal, finalStatus.MapTasksCompleted);

        var counts = await GetJobResultAsync(finalStatus.JobId);

        var totalWords = counts.Values.Sum();
        Console.WriteLine($"Total words counted: {totalWords}");

        Assert.Equal(108087, totalWords);

        Assert.True(counts.ContainsKey("the"));
        Assert.True(counts.ContainsKey("holmes"));
        Assert.True(counts.ContainsKey("sherlock"));
        Assert.True(counts.ContainsKey("watson"));

        Console.WriteLine($"Chunking test passed: {finalStatus.MapTasksTotal} chunks processed, {totalWords} total words counted");
    }
}
