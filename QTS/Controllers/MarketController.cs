using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;
using QTS.Domain;
using QTS.Services;

namespace QTS.Controllers;

[ApiController]
[Route("api/market")]
public sealed class MarketController : ControllerBase
{
    private readonly IMarketDataService _market;

    public MarketController(IMarketDataService market)
    {
        _market = market;
    }

    [HttpGet("instruments")]
    public ApiResponse<List<InstrumentDto>> Instruments()
    {
        var items = _market.GetInstruments()
            .Select(i => new InstrumentDto
            {
                InstId = i.InstId,
                Base = i.Base,
                Quote = i.Quote,
                Name = i.Name
            })
            .ToList();
        return ApiResponse<List<InstrumentDto>>.Ok(items);
    }

    [HttpGet("candles")]
    public async Task<ApiResponse<List<CandleDto>>> Candles(
        [FromQuery] string instId = "BTC-USDT",
        [FromQuery] string bar = "1H",
        [FromQuery] int limit = 300,
        CancellationToken ct = default)
    {
        var candles = await _market.GetRecentCandlesAsync(instId, bar, limit, ct);
        return ApiResponse<List<CandleDto>>.Ok(candles);
    }

    [HttpGet("ticker")]
    public async Task<ApiResponse<TickerDto>> Ticker([FromQuery] string instId = "BTC-USDT", CancellationToken ct = default)
    {
        var ticker = await _market.GetTickerAsync(instId, ct);
        return ApiResponse<TickerDto>.Ok(ticker);
    }

    [HttpPost("sync")]
    public async Task<ApiResponse<MarketSyncResult>> Sync([FromBody] MarketSyncRequest request, CancellationToken ct)
    {
        request ??= new MarketSyncRequest();
        var result = await _market.SyncAsync(
            string.IsNullOrWhiteSpace(request.InstId) ? "BTC-USDT" : request.InstId,
            string.IsNullOrWhiteSpace(request.Bar) ? "1H" : request.Bar,
            request.Days <= 0 ? 30 : request.Days,
            ct);
        return ApiResponse<MarketSyncResult>.Ok(result, result.Message);
    }

    /// <summary>一次同步全部交易对的历史 K 线，避免用户逐个品种点击同步。</summary>
    [HttpPost("sync-all")]
    public async Task<ApiResponse<MarketSyncAllResult>> SyncAll(
        [FromBody] MarketSyncAllRequest? request,
        CancellationToken ct)
    {
        request ??= new MarketSyncAllRequest();
        var bar = string.IsNullOrWhiteSpace(request.Bar) ? "1H" : request.Bar;
        var days = request.Days <= 0 ? 30 : request.Days;
        var items = new List<MarketSyncResult>();

        foreach (var instrument in _market.GetInstruments())
        {
            ct.ThrowIfCancellationRequested();
            items.Add(await _market.SyncAsync(instrument.InstId, bar, days, ct));
        }

        var total = items.Sum(i => i.Count);
        var allOkx = items.Count > 0 && items.All(i => i.Source == "okx");
        var source = allOkx ? "okx" : "synthetic";
        var message = allOkx
            ? $"已同步 {items.Count} 个交易对，共 {total} 根 {bar} K 线（欧易真实行情）"
            : $"OKX 当前不可达，已为 {items.Count} 个交易对生成示例行情，共 {total} 根 {bar} K 线（未写入数据库）";

        var result = new MarketSyncAllResult
        {
            Bar = bar,
            Days = days,
            InstrumentCount = items.Count,
            TotalCount = total,
            Source = source,
            Message = message,
            Items = items
        };
        return ApiResponse<MarketSyncAllResult>.Ok(result, message);
    }
}
