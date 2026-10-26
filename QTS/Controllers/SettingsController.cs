using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;
using QTS.Domain;
using QTS.Services;

namespace QTS.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;
    private readonly IPaperAccountService _paper;
    private readonly IAiConfigService _aiConfig;
    private readonly IOkxClient _okx;

    public SettingsController(
        ISettingsService settings, IPaperAccountService paper, IAiConfigService aiConfig, IOkxClient okx)
    {
        _settings = settings;
        _paper = paper;
        _aiConfig = aiConfig;
        _okx = okx;
    }

    [HttpGet("ai")]
    public async Task<ApiResponse<AiSettingsDto>> GetAi(CancellationToken ct)
    {
        var settings = await _aiConfig.GetAsync(ct);
        return ApiResponse<AiSettingsDto>.Ok(settings);
    }

    [HttpPut("ai")]
    public async Task<ApiResponse<AiSettingsDto>> SaveAi([FromBody] AiSettingsUpdateRequest request, CancellationToken ct)
    {
        var settings = await _aiConfig.SaveAsync(request, ct);
        return ApiResponse<AiSettingsDto>.Ok(settings, "AI 引擎配置已保存");
    }

    [HttpGet("okx")]
    public async Task<ApiResponse<OkxSettingsDto>> GetOkx(CancellationToken ct)
    {
        var settings = await _settings.GetOkxAsync(ct);
        return ApiResponse<OkxSettingsDto>.Ok(settings);
    }

    [HttpPut("okx")]
    public async Task<ApiResponse<OkxSettingsDto>> SaveOkx([FromBody] OkxSettingsUpdateRequest request, CancellationToken ct)
    {
        var settings = await _settings.SaveOkxAsync(request, ct);
        return ApiResponse<OkxSettingsDto>.Ok(settings, "欧易 API 配置已保存");
    }

    [HttpPost("okx/test")]
    public async Task<ApiResponse<OkxConnectionTestResult>> TestOkx(CancellationToken ct)
    {
        var result = await _settings.TestOkxAsync(ct);
        return ApiResponse<OkxConnectionTestResult>.Ok(result, result.Message);
    }

    /// <summary>诊断用：读取欧易账户持仓模式与指定永续合约的规格。</summary>
    [HttpGet("okx/contract")]
    public async Task<ApiResponse<object>> ContractInfo(
        [FromQuery] string instId = "BTC-USDT-SWAP", CancellationToken ct = default)
    {
        var config = await _okx.GetAccountConfigAsync(ct);
        if (!config.Success || config.Data is null)
            throw new BusinessException($"读取账户配置失败：{config.Error}");

        var spec = await _okx.GetSwapInstrumentAsync(instId, ct);
        if (!spec.Success || spec.Data is null)
            throw new BusinessException($"读取合约规格失败：{spec.Error}");

        return ApiResponse<object>.Ok(new
        {
            account = new
            {
                positionMode = config.Data.PositionMode,
                isLongShortMode = config.Data.IsLongShortMode
            },
            instrument = new
            {
                instId = spec.Data.InstId,
                ctVal = spec.Data.CtVal,
                lotSz = spec.Data.LotSz,
                minSz = spec.Data.MinSz,
                tickSz = spec.Data.TickSz
            }
        });
    }

    [HttpPost("paper-account/reset")]
    public async Task<ApiResponse<PaperSummaryDto>> ResetPaper([FromBody] PaperAccountResetRequest request, CancellationToken ct)
    {
        var balance = request?.Balance ?? 100m;
        await _settings.ResetPaperAccountAsync(balance, ct);
        var snapshot = await _paper.GetSnapshotAsync(ct);
        return ApiResponse<PaperSummaryDto>.Ok(new PaperSummaryDto
        {
            InitialBalance = snapshot.InitialBalance,
            Equity = snapshot.Equity,
            Available = snapshot.Available,
            PositionValue = snapshot.PositionValue,
            UnrealizedPnl = snapshot.UnrealizedPnl,
            RealizedPnl = snapshot.RealizedPnl,
            ReturnPct = snapshot.ReturnPct
        }, "模拟账户已重置");
    }

    [HttpGet("system")]
    public async Task<ApiResponse<SystemInfoDto>> System(CancellationToken ct)
    {
        var info = await _settings.GetSystemInfoAsync(ct);
        return ApiResponse<SystemInfoDto>.Ok(info);
    }
}
