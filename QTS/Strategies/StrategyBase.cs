using QTS.Domain;
using QTS.Services;

namespace QTS.Strategies;

/// <summary>
/// 内置策略基类：统一默认参数（来自参数 schema 的 Default）、信号时间兜底逻辑。
/// 子类只需描述元信息、参数 schema 并实现逐 K 线的目标仓位计算。
/// </summary>
public abstract class StrategyBase : IStrategy
{
    private Dictionary<string, object?>? _defaultParams;

    public abstract string Code { get; }

    public abstract string Name { get; }

    public abstract string Category { get; }

    public abstract string Description { get; }

    public abstract IReadOnlyList<StrategyParamDescriptor> ParamsSchema { get; }

    /// <summary>默认参数由 schema 中每个参数的 Default 自动派生，保证唯一来源。</summary>
    public IReadOnlyDictionary<string, object?> DefaultParams
    {
        get
        {
            if (_defaultParams is not null)
            {
                return _defaultParams;
            }

            var defaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var descriptor in ParamsSchema)
            {
                if (descriptor.Default is not null)
                {
                    defaults[descriptor.Code] = StrategyParamNormalizer.UnwrapValue(descriptor.Default);
                }
            }

            _defaultParams = defaults;
            return defaults;
        }
    }

    public abstract IReadOnlyList<StrategySignal> Evaluate(
        IReadOnlyList<CandlePoint> candles,
        IReadOnlyDictionary<string, object?> parameters);

    /// <summary>构造逐 K 线信号：目标仓位 1=持多、0=空仓、-1=持空（越界值夹紧，禁止未来函数）。</summary>
    protected static StrategySignal MakeSignal(CandlePoint candle, int index, int position, string reason)
    {
        return new StrategySignal(index, ResolveTime(candle), Math.Clamp(position, -1, 1), reason);
    }

    protected static DateTime ResolveTime(CandlePoint candle)
    {
        if (candle.OpenTime != default)
        {
            return candle.OpenTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(candle.OpenTime, DateTimeKind.Utc)
                : candle.OpenTime.ToUniversalTime();
        }

        return candle.Time > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(candle.Time).UtcDateTime
            : default;
    }

    protected static decimal[] CloseSeries(IReadOnlyList<CandlePoint> candles)
    {
        var closes = new decimal[candles.Count];
        for (var i = 0; i < candles.Count; i++)
        {
            closes[i] = candles[i].Close;
        }

        return closes;
    }
}
