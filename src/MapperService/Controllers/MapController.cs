using MapperService.Models;
using MapperService.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace MapperService.Controllers;

[ApiController]
[Route("map")]
public sealed class MapController : ControllerBase
{
    private readonly WordCountMapper _wordCountMapper;
    private readonly ILogger<MapController> _logger;

    public MapController(WordCountMapper wordCountMapper, ILogger<MapController> logger)
    {
        _wordCountMapper = wordCountMapper;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MapAsync([FromBody] MapRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _wordCountMapper.ProcessAsync(request, cancellationToken);
            return Accepted();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process map request for job {JobId}", request.JobId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse("Map processing failed."));
        }
    }
}
