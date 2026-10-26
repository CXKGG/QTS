using QTS.Contracts;
using QTS.Domain;
using QTS.Services;
using SqlSugar;

namespace QTS.Services;

/// <summary>
/// 行情服务：优先请求欧易；不可用时回退本地缓存；本地无数据时生成确定性示例行情。
/// 回退时始终通过 Warning/dataSource 明确告知前端，绝不冒充实盘数据。
/// </summary>
public sealed class MarketDataService : IMarketDataService
{
    private const string OkxSource = "okx";
    private const string SyntheticSource = "synthetic";
    private static readonly DateTime SyntheticAnchor = new(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly (string InstId, string Base, decimal Price, decimal Volume)[] InstrumentSeeds =
    [
        ("BTC-USDT", "BTC", 65_000m, 900m),
        ("ETH-USDT", "ETH", 3_200m, 12_000m),
        ("SOL-USDT", "SOL", 150m, 180_000m),
        ("XRP-USDT", "XRP", 0.55m, 6_000_000m),
        ("DOGE-USDT", "DOGE", 0.12m, 30_000_000m),
        ("BNB-USDT", "BNB", 580m, 20_000m)
    ];

    private static readonly MarketInstrument[] Instruments = InstrumentSeeds
        .Select(x => new MarketInstrument { InstId = x.InstId, Base = x.Base, Quote = "USDT" })
        .ToArray();

    private readonly ISqlSugarClient _db;
    private readonly IOkxClient _okx;
    private readonly ILogger<MarketDataService> _logger;

    public MarketDataService(ISqlSugarClient db, IOkxClient okx, ILogger<MarketDataService> logger)
    {
        _db = db;
        _okx = okx;
        _logger = logger;
    }

    public IReadOnlyList<MarketInstrument> GetInstruments() => Instruments;

    public async Task<MarketCandleSet> EnsureCandlesAsync(
        string instId, string bar, DateTime startUtc, DateTime endUtc, bool preferLocal, CancellationToken ct = default)
    {
        var normalizedInst = NormalizeInstId(instId);
        bar = MarketBars.Normalize(bar);
        var (start, end) = NormalizeRange(startUtc, endUtc);

        var local = await LoadLocalAsync(normalizedInst, bar, start, end);
        var expected = ExpectedBarCount(bar, start, end);
        var enoughLocal = local.Count >= 20 && local.Count >= expected * 0.8;
        if (preferLocal && enoughLocal)
            return new MarketCandleSet { Candles = local, Source = OkxSource };

        // 明确要求优先本地数据且本地不足时，不访问外网，直接生成示例行情。
        if (preferLocal)
        {
            return new MarketCandleSet
            {
                Candles = BuildSynthetic(normalizedInst, bar, start, end),
                Source = SyntheticSource,
                Warning = "已按“优先本地数据”设置使用示例行情数据（仅供演示，不代表真实行情）"
            };
        }

        try
        {
            var response = await _okx.GetCandlesAsync(normalizedInst, bar, start, end, ct);
            if (response.Success && response.Data is { Count: > 0 })
            {
                await SaveAsync(normalizedInst, bar, response.Data);
                return new MarketCandleSet { Candles = response.Data, Source = OkxSource };
            }
            _logger.LogWarning("OKX 行情不可用：{Error}", response.Error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取 OKX 行情异常，尝试本地缓存");
        }

        if (enoughLocal)
        {
            return new MarketCandleSet
            {
                Candles = local,
                Source = OkxSource,
                Warning = "OKX 当前不可达，已使用本地缓存行情数据（可能不是最新）"
            };
        }

        return new MarketCandleSet
        {
            Candles = BuildSynthetic(normalizedInst, bar, start, end),
            Source = SyntheticSource,
            Warning = "OKX 当前不可达，已自动切换为示例行情数据（仅供演示，不代表真实行情）"
        };
    }

    public async Task<MarketSyncResult> SyncAsync(string instId, string bar, int days, CancellationToken ct = default)
    {
        var normalizedInst = NormalizeInstId(instId);
        bar = MarketBars.Normalize(bar);
        days = Math.Clamp(days, 1, 365);
        var end = DateTime.UtcNow;
        var start = end.AddDays(-days);

        try
        {
            var response = await _okx.GetCandlesAsync(normalizedInst, bar, start, end, ct);
            if (response.Success && response.Data is { Count: > 0 })
            {
                await SaveAsync(normalizedInst, bar, response.Data);
                return new MarketSyncResult
                {
                    InstId = normalizedInst,
                    Bar = bar,
                    Count = response.Data.Count,
                    Source = OkxSource,
                    Message = $"已同步 {response.Data.Count} 根欧易 {bar} K 线"
                };
            }

            var synthetic = BuildSynthetic(normalizedInst, bar, start, end);
            return new MarketSyncResult
            {
                InstId = normalizedInst,
                Bar = bar,
                Count = synthetic.Count,
                Source = SyntheticSource,
                Message = $"OKX 当前不可达（{response.Error}），已生成 {synthetic.Count} 根示例行情用于本地演示（未写入数据库）"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "同步行情异常");
            var synthetic = BuildSynthetic(normalizedInst, bar, start, end);
            return new MarketSyncResult
            {
                InstId = normalizedInst,
                Bar = bar,
                Count = synthetic.Count,
                Source = SyntheticSource,
                Message = $"OKX 当前不可达，已生成 {synthetic.Count} 根示例行情用于本地演示（未写入数据库）"
            };
        }
    }

    public async Task<TickerDto> GetTickerAsync(string instId, CancellationToken ct = default)
    {
        var normalizedInst = NormalizeInstId(instId);
        try
        {
            var response = await _okx.GetTickerAsync(normalizedInst, ct);
            if (response.Success && response.Data is not null)
                return response.Data;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "获取 OKX ticker 失败，使用本地/示例行情");
        }

        var now = DateTime.UtcNow;
        var local = await LoadLocalAsync(normalizedInst, "1H", now.AddDays(-3), now);
        var candles = local.Count >= 24
            ? local
            : BuildSynthetic(normalizedInst, "1H", now.AddDays(-3), now);

        var last = candles[^1];
        var reference = candles[Math.Max(0, candles.Count - 25)];
        return new TickerDto
        {
            InstId = normalizedInst,
            Last = last.Close,
            Change24hPct = reference.Close <= 0m ? 0m : Math.Round((last.Close - reference.Close) / reference.Close * 100m, 4),
            High24h = candles.Skip(Math.Max(0, candles.Count - 24)).Max(x => x.High),
            Low24h = candles.Skip(Math.Max(0, candles.Count - 24)).Min(x => x.Low),
            Volume24h = candles.Skip(Math.Max(0, candles.Count - 24)).Sum(x => x.Volume),
            Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public async Task<decimal> GetLatestPriceAsync(string instId, CancellationToken ct = default)
    {
        var ticker = await GetTickerAsync(instId, ct);
        return ticker.Last;
    }

    public async Task<List<CandleDto>> GetRecentCandlesAsync(string instId, string bar, int limit, CancellationToken ct = default)
    {
        var normalizedInst = NormalizeInstId(instId);
        bar = MarketBars.Normalize(bar);
        limit = Math.Clamp(limit, 10, 1000);
        var now = DateTime.UtcNow;
        var span = MarketBars.GetSpan(bar);
        var start = now - TimeSpan.FromTicks(span.Ticks * (limit * 2L));
        var local = await LoadLocalAsync(normalizedInst, bar, start, now);

        List<CandlePoint> candles;
        // 图表需要足够长的窗口；本地缓存只有零星几十根时，用完整示例行情兜底，
        // 避免图上一片空白。真实 OKX 缓存达到 80% 时才直接展示缓存。
        var minimumLocal = Math.Max(20, (int)Math.Ceiling(limit * 0.8));
        if (local.Count >= minimumLocal)
        {
            candles = local;
        }
        else
        {
            candles = BuildSynthetic(normalizedInst, bar, now - TimeSpan.FromTicks(span.Ticks * (limit * 2L)), now);
        }

        return candles.TakeLast(limit).Select(ToCandleDto).ToList();
    }

    private async Task<List<CandlePoint>> LoadLocalAsync(string instId, string bar, DateTime start, DateTime end)
    {
        var rows = await _db.Queryable<KlineEntity>()
            .Where(x => x.InstId == instId && x.Bar == bar && x.OpenTime >= start && x.OpenTime <= end)
            .OrderBy(x => x.OpenTime)
            .ToListAsync();
        return rows.Select(ToCandlePoint).ToList();
    }

    private async Task SaveAsync(string instId, string bar, IReadOnlyList<CandlePoint> candles)
    {
        if (candles.Count == 0) return;
        var start = candles.Min(x => x.OpenTime);
        var end = candles.Max(x => x.OpenTime);
        await _db.Deleteable<KlineEntity>()
            .Where(x => x.InstId == instId && x.Bar == bar && x.OpenTime >= start && x.OpenTime <= end)
            .ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        var entities = candles.Select(c => new KlineEntity
        {
            InstId = instId,
            Bar = bar,
            OpenTime = ToUtc(c.OpenTime),
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume,
            Source = OkxSource,
            CreatedAt = now
        }).ToList();
        await _db.Insertable(entities).ExecuteCommandAsync();
    }

    private static CandlePoint ToCandlePoint(KlineEntity e) => new()
    {
        Time = new DateTimeOffset(ToUtc(e.OpenTime)).ToUnixTimeMilliseconds(),
        OpenTime = ToUtc(e.OpenTime),
        Open = e.Open,
        High = e.High,
        Low = e.Low,
        Close = e.Close,
        Volume = e.Volume,
        Source = e.Source
    };

    private static CandleDto ToCandleDto(CandlePoint c) => new()
    {
        Time = c.Time,
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume
    };

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static int ExpectedBarCount(string bar, DateTime start, DateTime end)
    {
        var barsPerDay = bar switch
        {
            "1m" => 1440,
            "5m" => 288,
            "15m" => 96,
            "30m" => 48,
            "1H" => 24,
            "4H" => 6,
            _ => 1
        };
        return Math.Max(1, (int)Math.Ceiling((end - start).TotalDays * barsPerDay));
    }

    private static (DateTime Start, DateTime End) NormalizeRange(DateTime start, DateTime end)
    {
        start = ToUtc(start);
        end = ToUtc(end);
        var now = DateTime.UtcNow;
        if (end > now) end = now;
        if (start > end) (start, end) = (end, start);
        if (end - start < TimeSpan.FromHours(1)) start = end.AddHours(-1);
        return (start, end);
    }

    private static string NormalizeInstId(string instId)
    {
        var normalized = (instId ?? "").Trim().ToUpperInvariant();
        if (!Instruments.Any(x => x.InstId == normalized))
            throw new BusinessException($"不支持的交易对：{instId}");
        return normalized;
    }

    /// <summary>确定性示例行情：同一交易对/周期/时间永远生成同一根 K 线，便于回测复现。</summary>
    private static List<CandlePoint> BuildSynthetic(string instId, string bar, DateTime startUtc, DateTime endUtc)
    {
        var seedInfo = InstrumentSeeds.FirstOrDefault(x => x.InstId == instId);
        var basePrice = seedInfo.Price == 0m ? 100m : seedInfo.Price;
        var baseVolume = seedInfo.Volume == 0m ? 1000m : seedInfo.Volume;
        var span = MarketBars.GetSpan(bar);
        var anchor = SyntheticAnchor;
        var start = ToUtc(startUtc);
        var end = ToUtc(endUtc);

        var startIndex = FloorIndex(anchor, start, span);
        var endIndex = FloorIndex(anchor, end, span);
        if (endIndex < startIndex) endIndex = startIndex;
        var maxCount = 20_000;
        if (endIndex - startIndex + 1 > maxCount) startIndex = endIndex - maxCount + 1;

        // 统一使用“自锚点起的小时数”而不是 K 线序号，保证 1m/15m/1H/1D 等周期
        // 在同一时刻看到同一个价格尺度（否则不同周期会生成完全不同的价格）。
        var seed = StableSeed(instId);
        var barDays = span.TotalHours / 24d;
        var list = new List<CandlePoint>((int)Math.Min(maxCount, endIndex - startIndex + 1));
        for (var i = startIndex; i <= endIndex; i++)
        {
            var timeDays = i * barDays;
            var close = SyntheticClose(seed, timeDays, basePrice);
            var open = SyntheticClose(seed, timeDays - barDays, basePrice);
            var wick = 0.0025 + Random01(seed, i, 7) * 0.006;
            var high = Math.Max(open, close) * (1m + (decimal)wick);
            var low = Math.Min(open, close) * (1m - (decimal)(wick * (0.6 + Random01(seed, i, 11) * 0.8)));
            var volume = baseVolume * (0.45m + (decimal)Random01(seed, i, 13) * 1.1m);
            var openTime = anchor.AddTicks(span.Ticks * i);
            list.Add(new CandlePoint
            {
                Time = new DateTimeOffset(openTime).ToUnixTimeMilliseconds(),
                OpenTime = openTime,
                Open = Math.Round(open, 8),
                High = Math.Round(high, 8),
                Low = Math.Round(low, 8),
                Close = Math.Round(close, 8),
                Volume = Math.Round(volume, 4),
                Source = SyntheticSource
            });
        }
        return list;
    }

    private static decimal SyntheticClose(ulong seed, double timeDays, decimal basePrice)
    {
        var drift = 0.45 * (timeDays / 30_000.0);
        var longWave = 0.10 * Math.Sin(timeDays / 5_000.0);
        var mediumWave = 0.07 * Math.Sin(timeDays / 173.0 + 0.7);
        var shortWave = 0.012 * Math.Sin(timeDays / 47.0 + 1.9);
        // 噪声按绝对分钟取桶，使各周期在同一时刻共享同一价格路径。
        var minuteBucket = (long)Math.Floor(timeDays * 24d * 60d);
        var noise = (Random01(seed, minuteBucket, 3) - 0.5) * 0.010;
        var factor = 1.0 + drift + longWave + mediumWave + shortWave + noise;
        if (factor < 0.15) factor = 0.15;
        return basePrice * (decimal)factor;
    }

    private static long FloorIndex(DateTime anchor, DateTime time, TimeSpan span) =>
        (long)Math.Floor((time - anchor).Ticks / (double)span.Ticks);

    private static ulong StableSeed(string text)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (var ch in text)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }

    private static double Random01(ulong seed, long index, int salt)
    {
        unchecked
        {
            var x = seed + (ulong)index * 0x9E3779B97F4A7C15UL + (ulong)salt * 0xBF58476D1CE4E5B9UL;
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (x >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}
