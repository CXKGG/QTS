using System.Diagnostics;
using System.Text.Json;
using QTS.Contracts;
using QTS.Domain;
using QTS.Strategies;
using SqlSugar;

namespace QTS.Services;

/// <summary>
/// 回测服务：校验请求 → 获取 K 线（okx 优先，失败由行情服务回退 synthetic）→ 执行策略与回测引擎
/// → 持久化 qts_backtest / qts_backtest_equity / qts_backtest_trade → 返回统一结果。
/// 支持多/空与逐仓杠杆；所有收益/回撤/胜率等指标均为百分比单位。
/// </summary>
public sealed class BacktestService : IBacktestService
{
    private const decimal Hundred = 100m;
    private const int MinCandleCount = 100;
    private const int EquityBatchSize = 500;
    private const int TradeBatchSize = 200;

    private static readonly string[] SupportedBars = ["1m", "5m", "15m", "1H", "4H", "1D"];

    private readonly ISqlSugarClient _db;
    private readonly IStrategyRegistry _strategies;
    private readonly IMarketDataService _marketData;
    private readonly BacktestEngine _engine;

    public BacktestService(
        ISqlSugarClient db,
        IStrategyRegistry strategies,
        IMarketDataService marketData,
        BacktestEngine engine)
    {
        _db = db;
        _strategies = strategies;
        _marketData = marketData;
        _engine = engine;
    }

    public async Task<BacktestRunResult> RunAsync(
        BacktestCreateRequest request,
        long userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var strategyCode = NormalizeStrategyCode(request.StrategyCode);
        var strategy = _strategies.Get(strategyCode)
            ?? throw new BusinessException($"策略不存在：{strategyCode}", statusCode: 400);
        var bar = NormalizeBar(request.Bar);
        var instId = NormalizeInstId(request.InstId);
        var (startUtc, endUtc) = NormalizeTimeRange(request.StartTime, request.EndTime);
        var initialCapital = NormalizeInitialCapital(request.InitialCapital);
        var feeRatePct = ClampDecimal(request.FeeRatePct, 0m, 5m);
        var slippagePct = ClampDecimal(request.SlippagePct, 0m, 5m);
        var positionSizePct = ClampDecimal(request.PositionSizePct, 1m, 100m);
        var stopLossPct = ClampDecimal(request.StopLossPct, 0m, 100m);
        var takeProfitPct = ClampDecimal(request.TakeProfitPct, 0m, 2000m);
        var leverage = Math.Clamp(request.Leverage, 1, 20);
        var parameters = StrategyParamNormalizer.Merge(strategy, request.Params);

        var stopwatch = Stopwatch.StartNew();
        MarketCandleSet candleSet;
        try
        {
            candleSet = await _marketData.EnsureCandlesAsync(instId, bar, startUtc, endUtc, request.PreferLocalData, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BusinessException($"获取行情数据失败：{ex.Message}", statusCode: 502);
        }

        var candles = NormalizeCandles(candleSet?.Candles);
        if (candles.Count < MinCandleCount)
        {
            throw new BusinessException(
                $"回测区间内可用的 K 线数据不足（至少 {MinCandleCount} 根，当前 {candles.Count} 根），请扩大时间范围或更换周期");
        }

        var dataSource = NormalizeDataSource(candleSet!.Source);
        var warning = dataSource == "synthetic"
            ? FirstNonEmpty(candleSet.Warning, "OKX 当前不可达，已自动切换为示例行情数据")
            : FirstNonEmpty(candleSet.Warning, null);

        IReadOnlyList<StrategySignal> signals;
        BacktestEngineResult engineResult;
        try
        {
            signals = strategy.Evaluate(candles, parameters);
            engineResult = _engine.Run(new BacktestEngineInput
            {
                Candles = candles,
                Signals = signals,
                InitialCapital = initialCapital,
                FeeRatePct = feeRatePct,
                SlippagePct = slippagePct,
                PositionSizePct = positionSizePct,
                Leverage = leverage,
                StopLossPct = stopLossPct,
                TakeProfitPct = takeProfitPct,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 引擎层异常：落库一条 failed 记录，便于前端在列表里看到失败原因。
            var failed = new BacktestRunResult
            {
                Id = PersistFailedRecord(
                    request, strategy, instId, bar, startUtc, endUtc, initialCapital,
                    feeRatePct, slippagePct, positionSizePct, stopLossPct, takeProfitPct,
                    parameters, dataSource, userId, stopwatch.ElapsedMilliseconds,
                    leverage,
                    $"回测执行失败：{ex.Message}"),
                Status = "failed",
                Message = $"回测执行失败：{ex.Message}",
                DataSource = dataSource,
                Summary = null,
            };
            return failed;
        }

        var name = string.IsNullOrWhiteSpace(request.Name)
            ? $"{strategy.Name} 回测 {DateTime.UtcNow.ToLocalTime():MM-dd HH:mm}"
            : request.Name.Trim();

        var entity = new BacktestEntity
        {
            Name = name,
            StrategyCode = strategy.Code,
            InstId = instId,
            Bar = bar,
            StartTime = startUtc,
            EndTime = endUtc,
            InitialCapital = initialCapital,
            FeeRatePct = feeRatePct,
            SlippagePct = slippagePct,
            PositionSizePct = positionSizePct,
            Leverage = leverage,
            StopLossPct = stopLossPct,
            TakeProfitPct = takeProfitPct,
            ParamsJson = StrategyParamNormalizer.SerializeParams(parameters),
            Status = "completed",
            DataSource = dataSource,
            FinalEquity = engineResult.FinalEquity,
            TotalReturnPct = engineResult.TotalReturnPct,
            MaxDrawdownPct = engineResult.MaxDrawdownPct,
            Sharpe = engineResult.Sharpe,
            WinRatePct = engineResult.WinRatePct,
            ProfitFactor = engineResult.ProfitFactor,
            BuyAndHoldReturnPct = engineResult.BuyAndHoldReturnPct,
            TradeCount = engineResult.TradeCount,
            Fees = engineResult.Fees,
            DurationMs = stopwatch.ElapsedMilliseconds,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        long backtestId;
        try
        {
            backtestId = _db.Insertable(entity).ExecuteReturnBigIdentity();
        }
        catch (Exception ex)
        {
            throw new BusinessException($"保存回测结果失败：{ex.Message}", statusCode: 500);
        }

        try
        {
            PersistEquityCurve(backtestId, engineResult.EquityCurve);
            PersistTrades(backtestId, engineResult.Trades);
        }
        catch (Exception ex)
        {
            throw new BusinessException($"保存回测明细失败：{ex.Message}", statusCode: 500);
        }

        entity.Id = backtestId;
        var summary = MapSummary(entity);
        return new BacktestRunResult
        {
            Id = backtestId,
            Status = "completed",
            Message = warning ?? "回测完成",
            DataSource = dataSource,
            Summary = summary,
        };
    }

    public Task<PagedResult<BacktestSummaryDto>> ListAsync(
        int page,
        int pageSize,
        string? strategyCode,
        string? instId,
        CancellationToken ct = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 20;
        }
        else if (pageSize > 200)
        {
            pageSize = 200;
        }

        var code = string.IsNullOrWhiteSpace(strategyCode) ? null : strategyCode.Trim();
        var instrument = string.IsNullOrWhiteSpace(instId) ? null : instId.Trim();

        ISugarQueryable<BacktestEntity> BuildQuery()
        {
            var query = _db.Queryable<BacktestEntity>();
            if (code is not null)
            {
                query = query.Where(item => item.StrategyCode == code);
            }

            if (instrument is not null)
            {
                query = query.Where(item => item.InstId == instrument);
            }

            return query;
        }

        var total = BuildQuery().Count();
        var rows = BuildQuery()
            .OrderBy(item => item.CreatedAt, OrderByType.Desc)
            .OrderBy(item => item.Id, OrderByType.Desc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new PagedResult<BacktestSummaryDto>
        {
            Items = rows.Select(MapSummary).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    public Task<BacktestDetailDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var entity = _db.Queryable<BacktestEntity>().First(item => item.Id == id);
        if (entity is null)
        {
            return Task.FromResult<BacktestDetailDto?>(null);
        }

        var equityRows = _db.Queryable<BacktestEquityEntity>()
            .Where(item => item.BacktestId == id)
            .OrderBy(item => item.Time, OrderByType.Asc)
            .ToList();
        var tradeRows = _db.Queryable<BacktestTradeEntity>()
            .Where(item => item.BacktestId == id)
            .OrderBy(item => item.EntryTime, OrderByType.Asc)
            .OrderBy(item => item.Id, OrderByType.Asc)
            .ToList();

        var equityCurve = equityRows
            .Select(row => new EquityPointDto
            {
                Time = ToUnixMilliseconds(row.Time),
                Equity = row.Equity,
                DrawdownPct = row.DrawdownPct,
            })
            .ToList();

        var detail = new BacktestDetailDto
        {
            Summary = MapSummary(entity),
            EquityCurve = equityCurve,
            Trades = tradeRows
                .Select(row => new BacktestTradeDto
                {
                    Id = row.Id,
                    EntryTime = row.EntryTime,
                    ExitTime = row.ExitTime,
                    EntryPrice = row.EntryPrice,
                    ExitPrice = row.ExitPrice,
                    Quantity = row.Quantity,
                    Side = string.IsNullOrWhiteSpace(row.Side) ? "long" : row.Side,
                    Margin = row.Margin,
                    Fee = row.Fee,
                    Pnl = row.Pnl,
                    PnlPct = row.PnlPct,
                    ExitReason = row.ExitReason,
                    HoldingBars = row.HoldingBars,
                })
                .ToList(),
            MonthlyReturns = ComputeMonthlyReturns(equityRows, entity.InitialCapital),
            Benchmark = new BenchmarkDto { BuyAndHoldReturnPct = entity.BuyAndHoldReturnPct },
        };

        return Task.FromResult<BacktestDetailDto?>(detail);
    }

    public Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var exists = _db.Queryable<BacktestEntity>().Any(item => item.Id == id);
        if (!exists)
        {
            return Task.FromResult(false);
        }

        _db.Deleteable<BacktestEquityEntity>().Where(item => item.BacktestId == id).ExecuteCommand();
        _db.Deleteable<BacktestTradeEntity>().Where(item => item.BacktestId == id).ExecuteCommand();
        var affected = _db.Deleteable<BacktestEntity>().Where(item => item.Id == id).ExecuteCommand();
        return Task.FromResult(affected > 0);
    }

    private BacktestSummaryDto MapSummary(BacktestEntity entity)
    {
        return new BacktestSummaryDto
        {
            Id = entity.Id,
            Name = entity.Name,
            StrategyCode = entity.StrategyCode,
            StrategyName = ResolveStrategyName(entity.StrategyCode),
            InstId = entity.InstId,
            Bar = entity.Bar,
            StartTime = AsUtc(entity.StartTime),
            EndTime = AsUtc(entity.EndTime),
            InitialCapital = entity.InitialCapital,
            FinalEquity = entity.FinalEquity,
            TotalReturnPct = entity.TotalReturnPct,
            MaxDrawdownPct = entity.MaxDrawdownPct,
            Sharpe = entity.Sharpe,
            WinRatePct = entity.WinRatePct,
            TradeCount = entity.TradeCount,
            Status = NormalizeStatus(entity.Status),
            DataSource = string.IsNullOrWhiteSpace(entity.DataSource) ? "okx" : entity.DataSource,
            FeeRatePct = entity.FeeRatePct,
            SlippagePct = entity.SlippagePct,
            PositionSizePct = entity.PositionSizePct,
            Leverage = entity.Leverage,
            StopLossPct = entity.StopLossPct,
            TakeProfitPct = entity.TakeProfitPct,
            ProfitFactor = entity.ProfitFactor,
            Fees = entity.Fees,
            BuyAndHoldReturnPct = entity.BuyAndHoldReturnPct,
            DurationMs = entity.DurationMs,
            Error = entity.Error,
            Params = ParseParams(entity.ParamsJson),
            CreatedAt = AsUtc(entity.CreatedAt),
        };
    }

    /// <summary>策略名统一从 IStrategyRegistry 解析，避免历史记录里策略改名后展示不一致。</summary>
    private string ResolveStrategyName(string strategyCode)
    {
        var strategy = _strategies.Get(strategyCode);
        return strategy?.Name ?? strategyCode;
    }

    private long PersistFailedRecord(
        BacktestCreateRequest request,
        IStrategy strategy,
        string instId,
        string bar,
        DateTime startUtc,
        DateTime endUtc,
        decimal initialCapital,
        decimal feeRatePct,
        decimal slippagePct,
        decimal positionSizePct,
        decimal stopLossPct,
        decimal takeProfitPct,
        IReadOnlyDictionary<string, object?> parameters,
        string dataSource,
        long userId,
        long durationMs,
        int leverage,
        string error)
    {
        var entity = new BacktestEntity
        {
            Name = string.IsNullOrWhiteSpace(request.Name)
                ? $"{strategy.Name} 回测 {DateTime.UtcNow.ToLocalTime():MM-dd HH:mm}"
                : request.Name.Trim(),
            StrategyCode = strategy.Code,
            InstId = instId,
            Bar = bar,
            StartTime = startUtc,
            EndTime = endUtc,
            InitialCapital = initialCapital,
            FeeRatePct = feeRatePct,
            SlippagePct = slippagePct,
            PositionSizePct = positionSizePct,
            Leverage = leverage,
            StopLossPct = stopLossPct,
            TakeProfitPct = takeProfitPct,
            ParamsJson = StrategyParamNormalizer.SerializeParams(parameters),
            Status = "failed",
            DataSource = dataSource,
            DurationMs = durationMs,
            Error = Truncate(error, 2000),
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        try
        {
            return _db.Insertable(entity).ExecuteReturnBigIdentity();
        }
        catch (Exception ex)
        {
            throw new BusinessException($"保存回测失败记录时出错：{ex.Message}", statusCode: 500);
        }
    }

    private void PersistEquityCurve(long backtestId, IReadOnlyList<BacktestEquityPoint> curve)
    {
        for (var offset = 0; offset < curve.Count; offset += EquityBatchSize)
        {
            var batch = new List<BacktestEquityEntity>(EquityBatchSize);
            var end = Math.Min(offset + EquityBatchSize, curve.Count);
            for (var i = offset; i < end; i++)
            {
                var point = curve[i];
                batch.Add(new BacktestEquityEntity
                {
                    BacktestId = backtestId,
                    Time = AsUtc(point.Time),
                    Equity = point.Equity,
                    DrawdownPct = point.DrawdownPct,
                });
            }

            _db.Insertable(batch).ExecuteCommand();
        }
    }

    private void PersistTrades(long backtestId, IReadOnlyList<BacktestRoundTrip> trades)
    {
        for (var offset = 0; offset < trades.Count; offset += TradeBatchSize)
        {
            var batch = new List<BacktestTradeEntity>(TradeBatchSize);
            var end = Math.Min(offset + TradeBatchSize, trades.Count);
            for (var i = offset; i < end; i++)
            {
                var trade = trades[i];
                batch.Add(new BacktestTradeEntity
                {
                    BacktestId = backtestId,
                    EntryTime = AsUtc(trade.EntryTime),
                    ExitTime = AsUtc(trade.ExitTime),
                    EntryPrice = trade.EntryPrice,
                    ExitPrice = trade.ExitPrice,
                    Quantity = trade.Quantity,
                    Side = trade.Side,
                    Margin = trade.Margin,
                    Fee = trade.Fee,
                    Pnl = trade.Pnl,
                    PnlPct = trade.PnlPct,
                    ExitReason = Truncate(trade.ExitReason, 64),
                    HoldingBars = trade.HoldingBars,
                });
            }

            _db.Insertable(batch).ExecuteCommand();
        }
    }

    /// <summary>详情接口的月度收益：按持久化权益曲线的最后一条权益计算自然月收益。</summary>
    private static List<MonthlyReturnDto> ComputeMonthlyReturns(IReadOnlyList<BacktestEquityEntity> equityRows, decimal initialCapital)
    {
        var result = new List<MonthlyReturnDto>();
        if (equityRows.Count == 0)
        {
            return result;
        }

        var previousEnd = initialCapital;
        var index = 0;
        while (index < equityRows.Count)
        {
            var key = AsUtc(equityRows[index].Time).ToString("yyyy-MM");
            var startEquity = previousEnd;
            var endEquity = equityRows[index].Equity;
            while (index < equityRows.Count && AsUtc(equityRows[index].Time).ToString("yyyy-MM") == key)
            {
                endEquity = equityRows[index].Equity;
                index++;
            }

            result.Add(new MonthlyReturnDto
            {
                Month = key,
                ReturnPct = startEquity > 0m
                    ? Math.Round((endEquity - startEquity) / startEquity * Hundred, 2, MidpointRounding.AwayFromZero)
                    : 0m,
            });
            previousEnd = endEquity;
        }

        return result;
    }

    /// <summary>把合成/本地/真实行情的 K 线清洗为有序、无重复、价格合法的序列。</summary>
    private static List<CandlePoint> NormalizeCandles(IReadOnlyList<CandlePoint>? candles)
    {
        var result = new List<CandlePoint>();
        if (candles is null || candles.Count == 0)
        {
            return result;
        }

        var seen = new HashSet<long>();
        foreach (var candle in candles)
        {
            if (candle is null)
            {
                continue;
            }

            var time = ResolveTime(candle);
            if (time == default)
            {
                continue;
            }

            var open = candle.Open > 0m ? candle.Open : candle.Close;
            var close = candle.Close > 0m ? candle.Close : open;
            if (open <= 0m || close <= 0m)
            {
                continue;
            }

            var high = Math.Max(candle.High, Math.Max(open, close));
            var low = candle.Low > 0m ? Math.Min(candle.Low, Math.Min(open, close)) : Math.Min(open, close);
            var millis = new DateTimeOffset(time).ToUnixTimeMilliseconds();
            if (!seen.Add(millis))
            {
                continue;
            }

            result.Add(new CandlePoint
            {
                Time = millis,
                OpenTime = time,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = candle.Volume > 0m ? candle.Volume : 0m,
                Source = string.IsNullOrWhiteSpace(candle.Source) ? "okx" : candle.Source,
            });
        }

        result.Sort(static (left, right) => left.OpenTime.CompareTo(right.OpenTime));
        return result;
    }

    private static string NormalizeBar(string? bar)
    {
        var value = (bar ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "1H";
        }

        foreach (var supported in SupportedBars)
        {
            if (string.Equals(supported, value, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        throw new BusinessException($"不支持的 K 线周期：{value}，可选 {string.Join("/", SupportedBars)}", statusCode: 400);
    }

    private static string NormalizeInstId(string? instId)
    {
        var value = (instId ?? string.Empty).Trim();
        return value.Length == 0 ? "BTC-USDT" : value;
    }

    private static string NormalizeStrategyCode(string? strategyCode)
    {
        var value = (strategyCode ?? string.Empty).Trim();
        return value.Length == 0 ? throw new BusinessException("策略代码不能为空", statusCode: 400) : value;
    }

    private static decimal NormalizeInitialCapital(decimal initialCapital)
    {
        if (initialCapital <= 0m)
        {
            throw new BusinessException("初始资金必须大于 0", statusCode: 400);
        }

        return Math.Round(initialCapital, 2, MidpointRounding.AwayFromZero);
    }

    private static (DateTime StartUtc, DateTime EndUtc) NormalizeTimeRange(DateTime start, DateTime end)
    {
        if (start == default || end == default)
        {
            throw new BusinessException("回测时间范围无效：请提供开始时间和结束时间", statusCode: 400);
        }

        var startUtc = AsUtc(start);
        var endUtc = AsUtc(end);
        if (startUtc >= endUtc)
        {
            throw new BusinessException("回测时间范围无效：开始时间必须早于结束时间", statusCode: 400);
        }

        return (startUtc, endUtc);
    }

    private static string NormalizeDataSource(string? source)
    {
        var value = (source ?? string.Empty).Trim().ToLowerInvariant();
        return value.Length == 0 ? "okx" : value;
    }

    private static string NormalizeStatus(string? status)
    {
        var value = (status ?? string.Empty).Trim().ToLowerInvariant();
        return value.Length == 0 ? "completed" : value;
    }

    private static object? ParseParams(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(paramsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = StrategyParamNormalizer.NormalizeJson(property.Value);
            }

            return result;
        }
        catch (JsonException)
        {
            return paramsJson;
        }
    }

    private static DateTime ResolveTime(CandlePoint candle)
    {
        if (candle.OpenTime != default)
        {
            return AsUtc(candle.OpenTime);
        }

        return candle.Time > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(candle.Time).UtcDateTime
            : default;
    }

    private static DateTime AsUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    private static long ToUnixMilliseconds(DateTime value)
    {
        return new DateTimeOffset(AsUtc(value)).ToUnixTimeMilliseconds();
    }

    private static decimal ClampDecimal(decimal value, decimal min, decimal max)
    {
        return Math.Round(Math.Clamp(value, min, max), 4, MidpointRounding.AwayFromZero);
    }

    private static string? FirstNonEmpty(string? first, string? fallback)
    {
        return string.IsNullOrWhiteSpace(first) ? fallback : first.Trim();
    }

    private static string Truncate(string? value, int maxLength)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
