using Microsoft.AspNetCore.Mvc;
using ReducerService.Models;
using ReducerService.Services;
using Shared.Models;

namespace ReducerService.Controllers;

[ApiController]
[Route("reduce")]
public sealed class ReduceController : ControllerBase
{
    private readonly WordCountReducer _reducer;
    private readonly ILogger<ReduceController> _logger;

    public ReduceController(WordCountReducer reducer, ILogger<ReduceController> logger)
    {
        _reducer = reducer;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReduceAsync([FromBody] ReduceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _reducer.ProcessAsync(request, cancellationToken);
            return Accepted();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process reduce request for job {JobId}", request.JobId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse("Reduce processing failed."));
        }
    }
}
