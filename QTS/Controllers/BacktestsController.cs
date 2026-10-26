using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;
using QTS.Services;

namespace QTS.Controllers;

/// <summary>回测接口：发起回测、分页查询记录、查看详情与删除。统一 ApiResponse 信封。</summary>
[ApiController]
[Route("api/backtests")]
public sealed class BacktestsController : ControllerBase
{
    private readonly IBacktestService _backtests;

    public BacktestsController(IBacktestService backtests)
    {
        _backtests = backtests;
    }

    /// <summary>POST /api/backtests：按策略与区间发起一次回测，返回 summary 指标。</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<BacktestRunResult>>> Create(
        [FromBody] BacktestCreateRequest request,
        CancellationToken ct)
    {
        var result = await _backtests.RunAsync(request, CurrentUserId(), ct);
        return Ok(ApiResponse<BacktestRunResult>.Ok(result, result.Message));
    }

    /// <summary>GET /api/backtests：分页查询回测记录，可按策略/交易对过滤。</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<BacktestSummaryDto>>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? strategyCode = null,
        [FromQuery] string? instId = null,
        CancellationToken ct = default)
    {
        var result = await _backtests.ListAsync(page, pageSize, strategyCode, instId, ct);
        return Ok(ApiResponse<PagedResult<BacktestSummaryDto>>.Ok(result));
    }

    /// <summary>GET /api/backtests/{id}：回测详情（summary + 权益曲线 + 交易明细 + 月度收益 + 基准）。</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<BacktestDetailDto>>> Get(long id, CancellationToken ct)
    {
        var detail = await _backtests.GetAsync(id, ct)
            ?? throw new BusinessException($"回测记录不存在：{id}", statusCode: 404);
        return Ok(ApiResponse<BacktestDetailDto>.Ok(detail));
    }

    /// <summary>DELETE /api/backtests/{id}：删除回测记录及其明细。</summary>
    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(long id, CancellationToken ct)
    {
        var deleted = await _backtests.DeleteAsync(id, ct);
        if (!deleted)
        {
            throw new BusinessException($"回测记录不存在：{id}", statusCode: 404);
        }

        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>从 Bearer 认证写入的 Claims 中取用户 ID；缺失时回退为 1（与鉴权方案约定一致）。</summary>
    private long CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(raw, out var userId) && userId > 0 ? userId : 1;
    }
}
