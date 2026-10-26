using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;
using QTS.Services;

namespace QTS.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [HttpGet("overview")]
    public async Task<ApiResponse<DashboardOverviewDto>> Overview(CancellationToken ct)
    {
        var overview = await _dashboard.GetOverviewAsync(ct);
        return ApiResponse<DashboardOverviewDto>.Ok(overview);
    }

    [HttpGet("equity-curve")]
    public async Task<ApiResponse<List<EquityPointDto>>> EquityCurve(
        [FromQuery] string range = "30d",
        CancellationToken ct = default)
    {
        var points = await _dashboard.GetEquityCurveAsync(string.IsNullOrWhiteSpace(range) ? "30d" : range, ct);
        return ApiResponse<List<EquityPointDto>>.Ok(points);
    }
}
