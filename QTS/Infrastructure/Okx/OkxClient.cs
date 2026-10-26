using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QTS.Contracts;
using QTS.Domain;
using QTS.Services;

namespace QTS.Infrastructure.Okx;

/// <summary>
/// 欧易（OKX）V5 REST API 客户端（行情 + 现货市价单 + 账户查询）。
/// 约定：网络异常、HTTP 非 2xx、欧易业务 code != 0、JSON 解析失败等一律转换为带中文说明的
/// <see cref="OkxResult{T}"/>.Fail，不向调用方抛异常；调用方只依据 Success/Error/Data 决策。
/// </summary>
public sealed class OkxClient : IOkxClient
{
    /// <summary>Program.cs 中应注册的命名 HttpClient（建议 Timeout 设为 12s）。</summary>
    public const string HttpClientName = "okx";

    /// <summary>未配置 BaseUrl 时使用的欧易主站地址。</summary>
    public const string DefaultBaseUrl = "https://www.okx.com";

    /// <summary>可配置：单个 OKX 请求的超时时间（默认 12 秒，与 HttpClient.Timeout 取先到者）。</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(25);

    private const int CandlePageLimit = 300;
    private const int CandleMaxPages = 30;
    private const string KlineSource = "okx";

    private const string PathCandles = "/api/v5/market/candles";
    private const string PathTicker = "/api/v5/market/ticker";
    private const string PathAccountBalance = "/api/v5/account/balance";
    private const string PathAccountConfig = "/api/v5/account/config";
    private const string PathSetLeverage = "/api/v5/account/set-leverage";
    private const string PathInstruments = "/api/v5/public/instruments";
    private const string PathTradeOrder = "/api/v5/trade/order";
    private const string PathPublicTime = "/api/v5/public/time";

    private static readonly HashSet<string> SupportedBars = new(StringComparer.Ordinal)
    {
        "1m", "5m", "15m", "1H", "4H", "1D"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOkxConfigService _configService;
    private readonly ILogger<OkxClient> _logger;

    // 网络/DNS 故障熔断：第一次失败后 3 分钟内快速失败，避免每个页面都等待完整超时。
    private static readonly TimeSpan OfflineCooldown = TimeSpan.FromMinutes(3);
    private long _offlineUntilTicks;
    private string? _offlineReason;

    public OkxClient(
        IHttpClientFactory httpClientFactory,
        IOkxConfigService configService,
        ILogger<OkxClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ------------------------------------------------------------------
    // 行情：K 线
    // ------------------------------------------------------------------

    public async Task<OkxResult<List<CandlePoint>>> GetCandlesAsync(
        string instId,
        string bar,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instId))
            return OkxResult<List<CandlePoint>>.Fail("交易对不能为空");
        if (string.IsNullOrWhiteSpace(bar) || !SupportedBars.Contains(bar.Trim()))
            return OkxResult<List<CandlePoint>>.Fail($"不支持的 K 线周期：{bar}（可选 1m/5m/15m/1H/4H/1D）");

        var startMs = ToUnixMilliseconds(startUtc);
        var endMs = ToUnixMilliseconds(endUtc);
        if (startMs > endMs)
            return OkxResult<List<CandlePoint>>.Fail("K 线起始时间不能晚于结束时间");

        try
        {
            var config = await GetConfigAsync(ct).ConfigureAwait(false);
            var instrument = instId.Trim();
            var barValue = bar.Trim();

            // 欧易 /market/candles 返回“不晚于 ts”的最近数据（按时间倒序）；after 表示“早于该 ts”。
            // 第一次请求用 after = endMs+1 锚定结束时间，之后每次用本页最旧一根的时间继续向更早翻页，
            // 避免从“当前最新”一路回溯导致分页页数浪费。
            var collected = new Dictionary<long, CandlePoint>();
            long? after = endMs < long.MaxValue - 1 ? endMs + 1 : null;
            var previousOldest = long.MaxValue;
            var covered = false;

            for (var page = 0; page < CandleMaxPages; page++)
            {
                var query = $"instId={Uri.EscapeDataString(instrument)}&bar={Uri.EscapeDataString(barValue)}&limit={CandlePageLimit}"
                            + (after.HasValue ? $"&after={after.Value}" : string.Empty);

                var http = await SendAsync(HttpMethod.Get, PathCandles, query, null, signed: false, config, ct)
                    .ConfigureAwait(false);
                if (!http.Success)
                    return OkxResult<List<CandlePoint>>.Fail(http.Error ?? "请求欧易 K 线接口失败");

                var response = ParseEnvelope<JsonElement>(http.Body, "获取K线", out var parseError);
                if (response is null)
                    return OkxResult<List<CandlePoint>>.Fail(parseError ?? "解析欧易 K 线响应失败");

                var rows = response.Data ?? [];
                if (rows.Count == 0)
                {
                    covered = true;
                    break;
                }

                var pageOldest = long.MaxValue;
                var pageNewest = long.MinValue;
                foreach (var row in rows)
                {
                    if (!TryParseCandleRow(row, out var candle))
                        continue;

                    pageOldest = Math.Min(pageOldest, candle.Time);
                    pageNewest = Math.Max(pageNewest, candle.Time);
                    if (candle.Time < startMs || candle.Time > endMs)
                        continue;

                    collected[candle.Time] = new CandlePoint
                    {
                        Time = candle.Time,
                        OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(candle.Time).UtcDateTime,
                        Open = candle.Open,
                        High = candle.High,
                        Low = candle.Low,
                        Close = candle.Close,
                        Volume = candle.Volume,
                        Source = KlineSource
                    };
                }

                if (pageOldest == long.MaxValue)
                {
                    // 整页都无法解析，视为已到数据尽头，交给“无数据”分支报错。
                    covered = true;
                    break;
                }

                // 请求区间整体在未来（最新一根也早于 start），或已翻到区间起点，或已到上市最早数据。
                if (pageNewest < startMs || pageOldest <= startMs || rows.Count < CandlePageLimit)
                {
                    covered = true;
                    break;
                }

                // 防御：游标未向更早推进（避免异常响应造成死循环）。
                if (pageOldest >= previousOldest)
                {
                    covered = true;
                    break;
                }

                previousOldest = pageOldest;
                after = pageOldest;
            }

            if (!covered)
                return OkxResult<List<CandlePoint>>.Fail(
                    $"K 线分页达到上限（{CandleMaxPages} 页 × {CandlePageLimit} 根）仍未覆盖起始时间，请缩小时间范围或使用更大的 K 线周期");

            if (collected.Count == 0)
                return OkxResult<List<CandlePoint>>.Fail("欧易未返回指定时间区间的 K 线数据");

            var candles = collected
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value)
                .ToList();
            return OkxResult<List<CandlePoint>>.Ok(candles);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取欧易 K 线异常：instId={InstId}, bar={Bar}", instId, bar);
            return OkxResult<List<CandlePoint>>.Fail($"获取欧易 K 线失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // 行情：Ticker
    // ------------------------------------------------------------------

    public async Task<OkxResult<TickerDto>> GetTickerAsync(string instId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instId))
            return OkxResult<TickerDto>.Fail("交易对不能为空");

        try
        {
            var config = await GetConfigAsync(ct).ConfigureAwait(false);
            var instrument = instId.Trim();
            var query = $"instId={Uri.EscapeDataString(instrument)}";

            var http = await SendAsync(HttpMethod.Get, PathTicker, query, null, signed: false, config, ct)
                .ConfigureAwait(false);
            if (!http.Success)
                return OkxResult<TickerDto>.Fail(http.Error ?? "请求欧易行情接口失败");

            var response = ParseEnvelope<OkxTickerPayload>(http.Body, "获取行情", out var parseError);
            if (response is null)
                return OkxResult<TickerDto>.Fail(parseError ?? "解析欧易行情响应失败");

            var payload = response.Data?.FirstOrDefault();
            if (payload is null)
                return OkxResult<TickerDto>.Fail($"欧易行情接口未返回 {instrument} 的数据");

            var last = OkxJson.ToDecimal(payload.Last);
            var open24h = OkxJson.ToDecimal(payload.Open24h);
            var changePct = open24h > 0m
                ? Math.Round((last - open24h) / open24h * 100m, 4)
                : 0m;

            // 成交量优先使用 volCcy24h（计价货币量），缺失时退回 vol24h（基础货币量）。
            var volume = OkxJson.ToDecimalOrNull(payload.VolCcy24h)
                         ?? OkxJson.ToDecimalOrNull(payload.Vol24h)
                         ?? 0m;

            var time = OkxJson.ToLong(payload.Ts);
            if (time <= 0)
                time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return OkxResult<TickerDto>.Ok(new TickerDto
            {
                InstId = string.IsNullOrWhiteSpace(payload.InstId) ? instrument : payload.InstId!,
                Last = last,
                Change24hPct = changePct,
                High24h = OkxJson.ToDecimal(payload.High24h),
                Low24h = OkxJson.ToDecimal(payload.Low24h),
                Volume24h = volume,
                Time = time
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取欧易行情异常：instId={InstId}", instId);
            return OkxResult<TickerDto>.Fail($"获取欧易行情失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // 账户
    // ------------------------------------------------------------------

    public async Task<OkxResult<OkxAccountSummaryDto>> GetAccountAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await GetConfigAsync(ct).ConfigureAwait(false);
            if (!config.HasCredentials)
                return OkxResult<OkxAccountSummaryDto>.Fail(
                    "未配置欧易 API 凭据，请先在系统设置中填写 API Key、Secret Key 和 Passphrase");

            // 只请求 USDT 明细：totalEq 仍是账户总权益（美元估值）。
            var http = await SendAsync(HttpMethod.Get, PathAccountBalance, "ccy=USDT", null, signed: true, config, ct)
                .ConfigureAwait(false);
            if (!http.Success)
                return OkxResult<OkxAccountSummaryDto>.Fail(http.Error ?? "请求欧易账户接口失败");

            var response = ParseEnvelope<OkxBalancePayload>(http.Body, "获取账户余额", out var parseError);
            if (response is null)
                return OkxResult<OkxAccountSummaryDto>.Fail(parseError ?? "解析欧易账户响应失败");

            var payload = response.Data?.FirstOrDefault();
            if (payload is null)
                return OkxResult<OkxAccountSummaryDto>.Fail("欧易账户接口未返回余额数据");

            var usdt = payload.Details?.FirstOrDefault(
                detail => string.Equals(detail.Ccy, "USDT", StringComparison.OrdinalIgnoreCase));

            // 可用余额优先 availBal，缺失时退回 availEq / eq；均为字符串数值。
            var availableUsdt = OkxJson.ToDecimalOrNull(usdt?.AvailBal)
                                ?? OkxJson.ToDecimalOrNull(usdt?.AvailEq)
                                ?? OkxJson.ToDecimalOrNull(usdt?.Eq)
                                ?? 0m;

            return OkxResult<OkxAccountSummaryDto>.Ok(new OkxAccountSummaryDto
            {
                TotalEquity = OkxJson.ToDecimal(payload.TotalEq),
                AvailableUsdt = availableUsdt,
                UnrealizedPnl = OkxJson.ToDecimal(usdt?.Upl),
                Currency = "USDT"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取欧易账户信息异常");
            return OkxResult<OkxAccountSummaryDto>.Fail($"获取欧易账户信息失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // 账户配置与合约规格
    // ------------------------------------------------------------------

    public async Task<OkxResult<OkxAccountConfig>> GetAccountConfigAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await GetConfigAsync(ct).ConfigureAwait(false);
            if (!config.HasCredentials)
                return OkxResult<OkxAccountConfig>.Fail("未配置欧易 API 凭据，无法读取账户持仓模式");

            var http = await SendAsync(HttpMethod.Get, PathAccountConfig, null, null, signed: true, config, ct)
                .ConfigureAwait(false);
            if (!http.Success)
                return OkxResult<OkxAccountConfig>.Fail(http.Error ?? "请求欧易账户配置失败");

            var response = ParseEnvelope<OkxAccountConfigPayload>(http.Body, "获取账户配置", out var parseError);
            if (response is null)
                return OkxResult<OkxAccountConfig>.Fail(parseError ?? "解析欧易账户配置失败");

            var payload = response.Data?.FirstOrDefault();
            var posMode = string.IsNullOrWhiteSpace(payload?.PosMode) ? "net_mode" : payload!.PosMode!.Trim();
            return OkxResult<OkxAccountConfig>.Ok(new OkxAccountConfig { PositionMode = posMode });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取欧易账户配置异常");
            return OkxResult<OkxAccountConfig>.Fail($"获取欧易账户配置失败：{ex.Message}");
        }
    }

    public async Task<OkxResult<OkxSwapInstrument>> GetSwapInstrumentAsync(
        string instId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instId))
            return OkxResult<OkxSwapInstrument>.Fail("合约交易对不能为空");

        try
        {
            var config = await GetConfigAsync(ct).ConfigureAwait(false);
            var instrument = instId.Trim();
            var query = $"instType=SWAP&instId={Uri.EscapeDataString(instrument)}";
            var http = await SendAsync(HttpMethod.Get, PathInstruments, query, null, signed: false, config, ct)
                .ConfigureAwait(false);
            if (!http.Success)
                return OkxResult<OkxSwapInstrument>.Fail(http.Error ?? "请求欧易合约规格失败");

            var response = ParseEnvelope<OkxSwapInstrumentPayload>(http.Body, "获取合约规格", out var parseError);
            if (response is null)
                return OkxResult<OkxSwapInstrument>.Fail(parseError ?? "解析欧易合约规格失败");

            var payload = response.Data?.FirstOrDefault();
            if (payload is null)
                return OkxResult<OkxSwapInstrument>.Fail($"欧易未返回合约 {instrument} 的规格");

            var ctVal = OkxJson.ToDecimal(payload.CtVal);
            var lotSz = OkxJson.ToDecimal(payload.LotSz, 1m);
            var minSz = OkxJson.ToDecimal(payload.MinSz, 1m);
            if (ctVal <= 0m || lotSz <= 0m || minSz <= 0m)
                return OkxResult<OkxSwapInstrument>.Fail($"欧易返回的 {instrument} 合约规格无效");

            return OkxResult<OkxSwapInstrument>.Ok(new OkxSwapInstrument
            {
                InstId = string.IsNullOrWhiteSpace(payload.InstId) ? instrument : payload.InstId!,
                CtVal = ctVal,
                LotSz = lotSz,
                MinSz = minSz,
                TickSz = OkxJson.ToDecimal(payload.TickSz, 0.01m)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取欧易合约规格异常：instId={InstId}", instId);
            return OkxResult<OkxSwapInstrument>.Fail($"获取合约规格失败：{ex.Message}");
        }
    }

    public async Task<OkxResult<bool>> SetLeverageAsync(
        string instId, int leverage, string? posSide, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instId))
            return OkxResult<bool>.Fail("合约交易对不能为空");
        if (leverage < 1 || leverage > 125)
            return OkxResult<bool>.Fail("杠杆倍数必须在 1 到 125 之间");

        try
        {
            var config = await GetConfigAsync(ct).ConfigureAwait(false);
            if (!config.HasCredentials)
                return OkxResult<bool>.Fail("未配置欧易 API 凭据，无法设置杠杆");

            var payload = new Dictionary<string, string>
            {
                ["instId"] = instId.Trim(),
                ["lever"] = leverage.ToString(CultureInfo.InvariantCulture),
                ["mgnMode"] = "isolated"
            };
            if (!string.IsNullOrWhiteSpace(posSide))
                payload["posSide"] = posSide.Trim().ToLowerInvariant();

            var body = JsonSerializer.Serialize(payload);
            var http = await SendAsync(HttpMethod.Post, PathSetLeverage, null, body, signed: true, config, ct)
                .ConfigureAwait(false);
            if (!http.Success)
                return OkxResult<bool>.Fail(http.Error ?? "设置欧易杠杆失败");

            var response = ParseEnvelope<JsonElement>(http.Body, "设置杠杆", out var parseError);
            if (response is null)
                return OkxResult<bool>.Fail(parseError ?? "设置欧易杠杆失败");

            _logger.LogInformation("欧易杠杆已设置：instId={InstId}, leverage={Leverage}, posSide={PosSide}",
                instId, leverage, posSide ?? "net");
            return OkxResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置欧易杠杆异常：instId={InstId}", instId);
            return OkxResult<bool>.Fail($"设置杠杆失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // 交易：现货市价单
    // ------------------------------------------------------------------

    public async Task<OkxResult<OkxOrderFill>> PlaceMarketOrderAsync(
        string instId,
        OrderSide side,
        decimal baseQuantity,
        string clientOrderId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instId))
            return OkxResult<OkxOrderFill>.Fail("交易对不能为空");
        if (baseQuantity <= 0m)
            return OkxResult<OkxOrderFill>.Fail("下单数量必须大于 0");

        try
        {
            var config = await GetConfigAsync(ct).ConfigureAwait(false);
            if (!config.HasCredentials)
                return OkxResult<OkxOrderFill>.Fail("未配置欧易 API 凭据，无法提交实盘订单");

            var instrument = instId.Trim();
            // decimal.ToString 不会输出科学计数法（如 0.000001 保持原样），满足欧易 sz 参数要求。
            var size = baseQuantity.ToString(CultureInfo.InvariantCulture);
            var body = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["instId"] = instrument,
                ["tdMode"] = "cash",
                ["side"] = side == OrderSide.Buy ? "buy" : "sell",
                ["ordType"] = "market",
                ["sz"] = size,
                ["tgtCcy"] = "base_ccy",
                ["clOrdId"] = NormalizeClientOrderId(clientOrderId)
            });

            var http = await SendAsync(HttpMethod.Post, PathTradeOrder, null, body, signed: true, config, ct)
                .ConfigureAwait(false);
            if (!http.Success)
                return OkxResult<OkxOrderFill>.Fail(http.Error ?? "提交欧易订单失败");

            var response = ParseEnvelope<OkxOrderPayload>(http.Body, "提交订单", out var parseError);
            if (response is null)
                return OkxResult<OkxOrderFill>.Fail(parseError ?? "解析欧易下单响应失败");

            var order = response.Data?.FirstOrDefault();
            if (order is null)
                return OkxResult<OkxOrderFill>.Fail("欧易下单接口未返回订单信息");
            if (!string.IsNullOrWhiteSpace(order.SCode) && order.SCode != "0")
                return OkxResult<OkxOrderFill>.Fail($"欧易下单失败（{order.SCode}）：{ErrorText(order.SMsg)}");
            if (string.IsNullOrWhiteSpace(order.OrdId))
                return OkxResult<OkxOrderFill>.Fail($"欧易下单失败：未返回订单号（{ErrorText(order.SMsg)}）");

            // 下单响应通常不带 state/成交价；市价单被受理即视为成交，成交价由上层用市价补齐（AvgPrice=0）。
            var state = string.IsNullOrWhiteSpace(order.State) ? "filled" : order.State!;
            var filledQuantity = OkxJson.ToDecimal(order.FillSz);
            if (filledQuantity <= 0m)
                filledQuantity = baseQuantity;

            var fill = new OkxOrderFill
            {
                OrderId = order.OrdId!,
                FilledQuantity = filledQuantity,
                AvgPrice = OkxJson.ToDecimal(order.AvgPx),
                Fee = OkxJson.ToDecimal(order.Fee),
                State = state
            };

            _logger.LogInformation(
                "欧易市价单已提交：instId={InstId}, side={Side}, sz={Size}, ordId={OrderId}, state={State}",
                instrument, side, size, fill.OrderId, state);
            return OkxResult<OkxOrderFill>.Ok(fill);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提交欧易订单异常：instId={InstId}, side={Side}", instId, side);
            return OkxResult<OkxOrderFill>.Fail($"提交欧易订单失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // 交易：永续合约市价单（逐仓）
    // ------------------------------------------------------------------

    public async Task<OkxResult<OkxOrderFill>> PlaceSwapMarketOrderAsync(
        string instId,
        OrderSide side,
        string? posSide,
        decimal contracts,
        bool reduceOnly,
        string clientOrderId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instId))
            return OkxResult<OkxOrderFill>.Fail("合约交易对不能为空");
        if (contracts <= 0m)
            return OkxResult<OkxOrderFill>.Fail("合约下单张数必须大于 0");

        try
        {
            var config = await GetConfigAsync(ct).ConfigureAwait(false);
            if (!config.HasCredentials)
                return OkxResult<OkxOrderFill>.Fail("未配置欧易 API 凭据，无法提交合约订单");

            var instrument = instId.Trim();
            var payload = new Dictionary<string, string>
            {
                ["instId"] = instrument,
                ["tdMode"] = "isolated",
                ["side"] = side == OrderSide.Buy ? "buy" : "sell",
                ["ordType"] = "market",
                ["sz"] = contracts.ToString(CultureInfo.InvariantCulture),
                ["reduceOnly"] = reduceOnly ? "true" : "false",
                ["clOrdId"] = NormalizeClientOrderId(clientOrderId)
            };
            if (!string.IsNullOrWhiteSpace(posSide))
                payload["posSide"] = posSide.Trim().ToLowerInvariant();

            var body = JsonSerializer.Serialize(payload);
            var http = await SendAsync(HttpMethod.Post, PathTradeOrder, null, body, signed: true, config, ct)
                .ConfigureAwait(false);
            if (!http.Success)
                return OkxResult<OkxOrderFill>.Fail(http.Error ?? "提交欧易合约订单失败");

            var response = ParseEnvelope<OkxOrderPayload>(http.Body, "提交合约订单", out var parseError);
            if (response is null)
                return OkxResult<OkxOrderFill>.Fail(parseError ?? "解析欧易合约下单响应失败");

            var order = response.Data?.FirstOrDefault();
            if (order is null)
                return OkxResult<OkxOrderFill>.Fail("欧易合约下单接口未返回订单信息");
            if (!string.IsNullOrWhiteSpace(order.SCode) && order.SCode != "0")
                return OkxResult<OkxOrderFill>.Fail($"欧易合约下单失败（{order.SCode}）：{ErrorText(order.SMsg)}");
            if (string.IsNullOrWhiteSpace(order.OrdId))
                return OkxResult<OkxOrderFill>.Fail($"欧易合约下单失败：未返回订单号（{ErrorText(order.SMsg)}）");

            var state = string.IsNullOrWhiteSpace(order.State) ? "filled" : order.State!;
            var filledContracts = OkxJson.ToDecimal(order.FillSz);
            if (filledContracts <= 0m)
                filledContracts = contracts;

            var fill = new OkxOrderFill
            {
                OrderId = order.OrdId!,
                FilledQuantity = filledContracts,
                AvgPrice = OkxJson.ToDecimal(order.AvgPx),
                Fee = OkxJson.ToDecimal(order.Fee),
                State = state
            };

            _logger.LogInformation(
                "欧易合约市价单已提交：instId={InstId}, side={Side}, posSide={PosSide}, contracts={Contracts}, reduceOnly={ReduceOnly}, ordId={OrderId}",
                instrument, side, posSide ?? "net", contracts, reduceOnly, fill.OrderId);
            return OkxResult<OkxOrderFill>.Ok(fill);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提交欧易合约订单异常：instId={InstId}, side={Side}", instId, side);
            return OkxResult<OkxOrderFill>.Fail($"提交欧易合约订单失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // 连通性
    // ------------------------------------------------------------------

    public async Task<OkxResult<bool>> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await GetConfigAsync(ct).ConfigureAwait(false);

            // 有凭据时用账户余额接口验证“网络 + 密钥 + 签名”全链路；否则只测公共时间接口。
            if (config.HasCredentials)
            {
                var account = await GetAccountAsync(ct).ConfigureAwait(false);
                return account.Success
                    ? OkxResult<bool>.Ok(true)
                    : OkxResult<bool>.Fail(account.Error ?? "欧易账户接口不可达");
            }

            var http = await SendAsync(HttpMethod.Get, PathPublicTime, null, null, signed: false, config, ct)
                .ConfigureAwait(false);
            if (!http.Success)
                return OkxResult<bool>.Fail(http.Error ?? "欧易公共接口不可达");

            var response = ParseEnvelope<OkxTimePayload>(http.Body, "获取服务器时间", out var parseError);
            if (response is null)
                return OkxResult<bool>.Fail(parseError ?? "解析欧易时间响应失败");
            if (response.Data is null || response.Data.Count == 0)
                return OkxResult<bool>.Fail("欧易公共时间接口未返回数据");

            return OkxResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "欧易连通性测试异常");
            return OkxResult<bool>.Fail($"欧易连通性测试失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // 内部：HTTP 发送
    // ------------------------------------------------------------------

    private sealed record OkxHttpResult(bool Success, string Body, string? Error);

    private async Task<OkxHttpResult> SendAsync(
        HttpMethod method,
        string path,
        string? query,
        string? body,
        bool signed,
        OkxConfig config,
        CancellationToken ct)
    {
        var offlineUntil = Interlocked.Read(ref _offlineUntilTicks);
        if (offlineUntil > DateTime.UtcNow.Ticks)
            return new OkxHttpResult(false, string.Empty,
                _offlineReason ?? "欧易网络暂不可达，已暂时跳过请求，请稍后重试");

        var requestPath = string.IsNullOrEmpty(query) ? path : $"{path}?{query}";
        var url = NormalizeBaseUrl(config) + requestPath;

        try
        {
            if (signed && !config.HasCredentials)
                return new OkxHttpResult(false, string.Empty,
                    "未配置欧易 API 凭据，请先在系统设置中填写 API Key、Secret Key 和 Passphrase");

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            if (signed)
            {
                var timestamp = OkxSigner.CreateTimestamp();
                OkxSigner.Apply(request, config, timestamp, requestPath, body);
            }

            // 独立 12s 超时兜底：即使 Program.cs 注册的 HttpClient.Timeout 更长也保证不悬挂。
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(RequestTimeout);

            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            // 能拿到任意 HTTP 响应说明网络已恢复，解除熔断。
            Interlocked.Exchange(ref _offlineUntilTicks, 0);
            var text = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = $"欧易接口返回 HTTP {(int)response.StatusCode}：{Truncate(text, 200)}";
                _logger.LogWarning("欧易请求失败：{Method} {Path}，原因：{Error}", method.Method, requestPath, error);
                return new OkxHttpResult(false, text, error);
            }

            return new OkxHttpResult(true, text, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("欧易请求已取消：{Method} {Path}", method.Method, requestPath);
            return new OkxHttpResult(false, string.Empty, "请求已取消");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("欧易请求超时：{Method} {Path}", method.Method, requestPath);
            var reason = $"请求欧易接口超时（{RequestTimeout.TotalSeconds:0.#} 秒）";
            MarkOffline(reason);
            return new OkxHttpResult(false, string.Empty, reason);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("欧易网络请求失败：{Method} {Path}，原因：{Message}",
                method.Method, requestPath, ex.Message);
            var reason = $"网络请求失败：{ex.Message}";
            MarkOffline(reason);
            return new OkxHttpResult(false, string.Empty, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "欧易请求异常：{Method} {Path}", method.Method, requestPath);
            return new OkxHttpResult(false, string.Empty, $"请求欧易接口失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // 内部：解析与工具
    // ------------------------------------------------------------------

    private OkxApiResponse<T>? ParseEnvelope<T>(string body, string action, out string? error)
    {
        error = null;
        try
        {
            var response = JsonSerializer.Deserialize<OkxApiResponse<T>>(body, JsonOptions);
            if (response is null)
            {
                error = $"解析欧易接口响应失败（{action}）：响应为空";
                _logger.LogWarning("解析欧易响应失败（{Action}）：响应为空", action);
                return null;
            }

            var code = string.IsNullOrWhiteSpace(response.Code) ? "0" : response.Code.Trim();
            if (code != "0")
            {
                error = $"欧易接口返回错误（{code}）：{ErrorText(response.Msg)}";
                _logger.LogWarning("欧易接口返回错误（{Action}），code={Code}, msg={Msg}",
                    action, code, response.Msg);
                return null;
            }

            return response;
        }
        catch (JsonException ex)
        {
            error = $"解析欧易接口响应失败（{action}）：{ex.Message}";
            _logger.LogWarning("解析欧易响应失败（{Action}）：{Message}", action, ex.Message);
            return null;
        }
    }

    private readonly record struct CandleRow(long Time, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

    /// <summary>解析 K 线数组：[ts,o,h,l,c,vol,volCcy,volCcyQuote,confirm]（confirm 位不参与过滤）。</summary>
    private static bool TryParseCandleRow(JsonElement row, out CandleRow candle)
    {
        candle = default;
        if (row.ValueKind != JsonValueKind.Array)
            return false;

        var values = row.EnumerateArray().ToArray();
        if (values.Length < 6)
            return false;

        if (!TryReadLong(values[0], out var time))
            return false;
        if (!TryReadDecimal(values[1], out var open)
            || !TryReadDecimal(values[2], out var high)
            || !TryReadDecimal(values[3], out var low)
            || !TryReadDecimal(values[4], out var close)
            || !TryReadDecimal(values[5], out var volume))
            return false;

        if (time <= 0 || open <= 0m || high <= 0m || low <= 0m || close <= 0m)
            return false;

        candle = new CandleRow(time, open, high, low, close, volume);
        return true;
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        if (element.ValueKind == JsonValueKind.String)
            return decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDecimal(out value)
                   || decimal.TryParse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return false;
    }

    private static bool TryReadLong(JsonElement element, out long value)
    {
        value = 0L;
        if (element.ValueKind == JsonValueKind.String)
            return long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    private async Task<OkxConfig> GetConfigAsync(CancellationToken ct)
    {
        var config = await _configService.GetAsync(ct).ConfigureAwait(false);
        return config ?? new OkxConfig();
    }

    private static string NormalizeBaseUrl(OkxConfig config)
    {
        var raw = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultBaseUrl : config.BaseUrl.Trim();
        return raw.TrimEnd('/');
    }

    /// <summary>欧易 clOrdId 只允许字母/数字、最长 32 位；非法字符剔除，空值自动生成。</summary>
    private static string NormalizeClientOrderId(string? raw)
    {
        var builder = new StringBuilder(32);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (var ch in raw.Trim())
            {
                if (builder.Length >= 32)
                    break;
                if (char.IsAsciiLetterOrDigit(ch))
                    builder.Append(ch);
            }
        }

        if (builder.Length > 0)
            return builder.ToString();

        // 兜底：qts + UTC 时间 + 随机数，总长 21，满足字母数字且 ≤32 位。
        var generated = "qts"
                        + DateTime.UtcNow.ToString("yyMMddHHmmssfff", CultureInfo.InvariantCulture)
                        + Random.Shared.Next(100, 999).ToString(CultureInfo.InvariantCulture);
        return generated.Length > 32 ? generated[..32] : generated;
    }

    private void MarkOffline(string reason)
    {
        _offlineReason = $"欧易接口暂不可达（{reason}），已跳过后续外网请求 3 分钟，稍后自动重试";
        Interlocked.Exchange(ref _offlineUntilTicks,
            DateTime.UtcNow.Add(OfflineCooldown).Ticks);
    }

    private static long ToUnixMilliseconds(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    private static string ErrorText(string? message) =>
        string.IsNullOrWhiteSpace(message) ? "未知错误" : message.Trim();

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "无响应内容";
        var singleLine = text.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "…";
    }
}
