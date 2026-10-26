using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;
using QTS.Services;

namespace QTS.Controllers;

[ApiController]
[Route("api/trade")]
public sealed class TradeController : ControllerBase
{
    private readonly IManualTradeService _trade;

    public TradeController(IManualTradeService trade)
    {
        _trade = trade;
    }

    /// <summary>交易台概览：模拟盘账户、持仓、策略任务、行情。</summary>
    [HttpGet("overview")]
    public async Task<ApiResponse<TradeOverviewDto>> Overview(
        [FromQuery] string? instId = null,
        CancellationToken ct = default)
    {
        var overview = await _trade.GetOverviewAsync(instId, ct);
        return ApiResponse<TradeOverviewDto>.Ok(overview);
    }

    /// <summary>手动开仓（仅模拟盘）。</summary>
    [HttpPost("open")]
    public async Task<ApiResponse<ManualTradeResult>> Open(
        [FromBody] ManualTradeRequest request,
        CancellationToken ct)
    {
        var result = await _trade.OpenAsync(request, ct);
        return ApiResponse<ManualTradeResult>.Ok(result, result.Message);
    }

    /// <summary>手动平仓（仅手动交易持仓）。</summary>
    [HttpPost("close")]
    public async Task<ApiResponse<ManualTradeResult>> Close(
        [FromBody] ManualCloseRequest request,
        CancellationToken ct)
    {
        var result = await _trade.CloseAsync(request, ct);
        return ApiResponse<ManualTradeResult>.Ok(result, result.Message);
    }
}
