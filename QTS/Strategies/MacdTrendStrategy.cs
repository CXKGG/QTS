using System.Globalization;
using QTS.Domain;

namespace QTS.Strategies;

/// <summary>
/// MACD 趋势策略（可双向）：柱状图由负转正做多、由正转负做空；allowShort=false 时死叉只平多。
/// 可选最小持仓 K 线数对多空都生效，过滤震荡噪音。逐根 K 线计算，无未来函数。
/// </summary>
public sealed class MacdTrendStrategy : StrategyBase
{
    public override string Code => "macd_trend";

    public override string Name => "MACD 趋势";

    public override string Category => "趋势";

    public override string Description =>
        "MACD 金叉（柱状图由负转正）做多、死叉（由正转负）做空（可关闭做空）；可设置最少持仓 K 线数过滤震荡噪音。适合中级趋势行情。";

    public override IReadOnlyList<StrategyParamDescriptor> ParamsSchema { get; } =
    [
        new("fastPeriod", "快线周期", "int", 12, 2m, 100m, 1m),
        new("slowPeriod", "慢线周期", "int", 26, 3m, 200m, 1m),
        new("signalPeriod", "信号周期", "int", 9, 2m, 100m, 1m),
        new("minHoldBars", "最少持仓K线", "int", 0, 0m, 100m, 1m),
        new(
            "allowShort",
            "允许做空",
            "bool",
            true,
            Options: [new StrategyParamOption("开启", "true"), new StrategyParamOption("关闭", "false")]),
    ];

    public override IReadOnlyList<StrategySignal> Evaluate(
        IReadOnlyList<CandlePoint> candles,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var fastPeriod = StrategyParams.GetInt(parameters, "fastPeriod", 12, 2, 100);
        var slowPeriod = StrategyParams.GetInt(parameters, "slowPeriod", 26, 3, 200);
        if (fastPeriod >= slowPeriod)
        {
            slowPeriod = Math.Min(200, fastPeriod + 1);
        }

        var signalPeriod = StrategyParams.GetInt(parameters, "signalPeriod", 9, 2, 100);
        var minHoldBars = StrategyParams.GetInt(parameters, "minHoldBars", 0, 0, 100);
        var allowShort = StrategyParams.GetBool(parameters, "allowShort", true);

        var closes = CloseSeries(candles);
        var (macd, signal, histogram) = TechnicalIndicators.Macd(closes, fastPeriod, slowPeriod, signalPeriod);
        var macdStart = Math.Max(fastPeriod, slowPeriod) - 1;
        var warmup = macdStart + signalPeriod - 1;

        var signals = new StrategySignal[candles.Count];
        var position = 0;
        var entryIndex = -1;
        for (var i = 0; i < candles.Count; i++)
        {
            if (i < warmup)
            {
                signals[i] = MakeSignal(
                    candles[i], i, 0,
                    $"MACD 预热中（需 {warmup + 1} 根收盘价），保持空仓");
                continue;
            }

            var current = histogram[i];
            var previous = histogram[i - 1];
            if (position == 0)
            {
                if (previous <= 0m && current > 0m)
                {
                    position = 1;
                    entryIndex = i;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"MACD 金叉：DIF {Format(macd[i])} 上穿 DEA {Format(signal[i])}，开仓做多");
                }
                else if (allowShort && previous >= 0m && current < 0m)
                {
                    position = -1;
                    entryIndex = i;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"MACD 死叉：DIF {Format(macd[i])} 下穿 DEA {Format(signal[i])}，开仓做空");
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"MACD 柱状图 {Format(current)} 未出现有效交叉，保持空仓");
                }
            }
            else if (position == 1)
            {
                var heldBars = entryIndex >= 0 ? i - entryIndex : int.MaxValue;
                if (previous >= 0m && current < 0m)
                {
                    if (minHoldBars > 0 && heldBars < minHoldBars)
                    {
                        signals[i] = MakeSignal(
                            candles[i], i, position,
                            $"MACD 死叉但仅持仓 {heldBars} 根（最少 {minHoldBars} 根），暂缓离场");
                    }
                    else
                    {
                        position = allowShort ? -1 : 0;
                        entryIndex = allowShort ? i : -1;
                        signals[i] = MakeSignal(
                            candles[i], i, position,
                            allowShort
                                ? $"MACD 死叉：DIF {Format(macd[i])} 下穿 DEA {Format(signal[i])}，平多反手做空"
                                : $"MACD 死叉：DIF {Format(macd[i])} 下穿 DEA {Format(signal[i])}，平仓离场");
                    }
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"MACD 柱状图 {Format(current)} 仍为正，继续持多");
                }
            }
            else
            {
                var heldBars = entryIndex >= 0 ? i - entryIndex : int.MaxValue;
                if (previous <= 0m && current > 0m)
                {
                    if (minHoldBars > 0 && heldBars < minHoldBars)
                    {
                        signals[i] = MakeSignal(
                            candles[i], i, position,
                            $"MACD 金叉但仅持仓 {heldBars} 根（最少 {minHoldBars} 根），暂缓平空");
                    }
                    else
                    {
                        position = 1;
                        entryIndex = i;
                        signals[i] = MakeSignal(
                            candles[i], i, position,
                            $"MACD 金叉：DIF {Format(macd[i])} 上穿 DEA {Format(signal[i])}，平空反手做多");
                    }
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"MACD 柱状图 {Format(current)} 仍为负，继续持空");
                }
            }
        }

        return signals;
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
