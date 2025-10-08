using ApiService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Minio;
using Shared.Models;

namespace ApiService.Controllers;

[ApiController]
[Route("files")]
public sealed class FilesController : ControllerBase
{
    private readonly IMinioClient _minioClient;
    private readonly MinioOptions _options;
    private readonly ILogger<FilesController> _logger;

    public FilesController(IMinioClient minioClient, IOptions<MinioOptions> options, ILogger<FilesController> logger)
    {
        _minioClient = minioClient;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(FileReference), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAsync([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse("File payload is missing or empty."));
        }

        await EnsureBucketExistsAsync(cancellationToken);

        var objectKey = $"inputs/{Guid.NewGuid():N}-{file.FileName}";
        await using var stream = file.OpenReadStream();

        var putArgs = new PutObjectArgs()
            .WithBucket(_options.BucketName)
            .WithObject(objectKey)
            .WithContentType(file.ContentType ?? "application/octet-stream")
            .WithObjectSize(file.Length)
            .WithStreamData(stream);

        await _minioClient.PutObjectAsync(putArgs, cancellationToken);

        var reference = new FileReference(_options.BucketName, objectKey);
        _logger.LogInformation("Uploaded file for job input: {ObjectKey}", objectKey);

        return Created("/files", reference);
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        var exists = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(_options.BucketName), cancellationToken);
        if (!exists)
        {
            await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_options.BucketName), cancellationToken);
            _logger.LogInformation("Created MinIO bucket {Bucket}", _options.BucketName);
        }
    }
}
