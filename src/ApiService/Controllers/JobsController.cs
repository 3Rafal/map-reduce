using ApiService.Models;
using ApiService.Services;
using Microsoft.AspNetCore.Mvc;
using Minio;
using Shared.Models;
using System.Net.Mime;

namespace ApiService.Controllers;

[ApiController]
[Route("jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly JobCoordinator _jobCoordinator;
    private readonly IMinioClient _minioClient;
    private readonly ILogger<JobsController> _logger;

    public JobsController(JobCoordinator jobCoordinator, IMinioClient minioClient, ILogger<JobsController> logger)
    {
        _jobCoordinator = jobCoordinator;
        _minioClient = minioClient;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(JobSummaryDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitJobAsync([FromBody] CreateJobRequest request, CancellationToken cancellationToken)
    {
        if (request?.InputFile is null)
        {
            return BadRequest(new ErrorResponse("Input file reference is required."));
        }

        try
        {
            var job = await _jobCoordinator.CreateJobAsync(request.InputFile, cancellationToken);
            return AcceptedAtRoute("GetJob", new { id = job.Id }, ToDto(job));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit job for input {Bucket}/{Object}", request.InputFile.BucketName, request.InputFile.ObjectKey);
            return StatusCode(StatusCodes.Status502BadGateway, new ErrorResponse("Failed to dispatch job to processing services."));
        }
    }

    [HttpGet("{id:guid}", Name = "GetJob")]
    [ProducesResponseType(typeof(JobSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetJobAsync(Guid id)
    {
        if (!_jobCoordinator.TryGetJob(id, out var job) || job is null)
        {
            return NotFound(new ErrorResponse($"Job {id} was not found."));
        }

        return Ok(ToDto(job));
    }

    [HttpGet("{id:guid}/result")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetResultAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!_jobCoordinator.TryGetJob(id, out var job) || job is null)
        {
            return NotFound(new ErrorResponse($"Job {id} was not found."));
        }

        if (job.Status != JobStatus.Completed || string.IsNullOrWhiteSpace(job.ResultObjectKey))
        {
            return Conflict(new ErrorResponse($"Job {id} has not completed yet."));
        }

        var buffer = new MemoryStream();
        var getArgs = new GetObjectArgs()
            .WithBucket(job.BucketName)
            .WithObject(job.ResultObjectKey)
            .WithCallbackStream(stream => stream.CopyTo(buffer));

        await _minioClient.GetObjectAsync(getArgs, cancellationToken);
        buffer.Position = 0;
        var payload = buffer.ToArray();

        var fileName = $"job-{id:N}-result.json";
        return File(payload, MediaTypeNames.Application.Json, fileName);
    }

    [HttpPost("{id:guid}/mapdone")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MapCompletedAsync(Guid id, [FromBody] MapCompletionNotification notification, CancellationToken cancellationToken)
    {
        if (notification is null || notification.JobId != id)
        {
            return BadRequest(new ErrorResponse("Job identifier mismatch in map completion payload."));
        }

        var handled = await _jobCoordinator.HandleMapCompletedAsync(notification, cancellationToken);
        return handled ? Accepted() : NotFound(new ErrorResponse($"Job {id} was not found."));
    }

    [HttpPost("{id:guid}/reducedone")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult ReduceCompleted(Guid id, [FromBody] ReduceCompletionNotification notification)
    {
        if (notification is null || notification.JobId != id)
        {
            return BadRequest(new ErrorResponse("Job identifier mismatch in reduce completion payload."));
        }

        var handled = _jobCoordinator.HandleReduceCompleted(notification);
        return handled ? Accepted() : NotFound(new ErrorResponse($"Job {id} was not found."));
    }

    private static JobSummaryDto ToDto(Job job) => new()
    {
        JobId = job.Id,
        Status = job.Status,
        BucketName = job.BucketName,
        InputObjectKey = job.InputObjectKey,
        IntermediateObjectKeys = job.IntermediateObjectKeys.ToArray(),
        ResultObjectKey = job.ResultObjectKey,
        FailureReason = job.FailureReason,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt
    };
}
