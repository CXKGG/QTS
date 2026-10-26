using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;
using QTS.Services;

namespace QTS.Controllers;

[ApiController]
[Route("api/ai")]
public sealed class AiController : ControllerBase
{
    private readonly IAiAnalysisService _ai;

    public AiController(IAiAnalysisService ai)
    {
        _ai = ai;
    }

    [HttpGet("status")]
    public async Task<ApiResponse<AiStatusDto>> Status(CancellationToken ct) =>
        ApiResponse<AiStatusDto>.Ok(await _ai.GetStatusAsync(ct));

    [HttpPost("analyze")]
    public async Task<ApiResponse<AiAnalysisResultDto>> Analyze(
        [FromBody] AiAnalysisRequest request, CancellationToken ct)
    {
        var result = await _ai.AnalyzeAsync(request, ct);
        return ApiResponse<AiAnalysisResultDto>.Ok(result);
    }
}
