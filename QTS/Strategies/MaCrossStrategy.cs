using System.Globalization;
using QTS.Domain;

namespace QTS.Strategies;

/// <summary>
/// 双均线交叉策略（可双向）：快线上穿慢线做多、下穿做空；allowShort=false 时退化为只做多。
/// 逐根 K 线只使用已收盘数据，无未来函数。
/// </summary>
public sealed class MaCrossStrategy : StrategyBase
{
    public override string Code => "ma_cross";

    public override string Name => "双均线交叉";

    public override string Category => "趋势";

    public override string Description =>
        "快慢两条收盘价均线金叉做多、死叉做空（可关闭做空，改为死叉离场）；适合单边趋势行情，震荡市易被反复止损，建议配合止损/止盈使用。";

    public override IReadOnlyList<StrategyParamDescriptor> ParamsSchema { get; } =
    [
        new("fastPeriod", "快线周期", "int", 5, 2m, 200m, 1m),
        new("slowPeriod", "慢线周期", "int", 20, 3m, 500m, 1m),
        new(
            "allowShort",
            "允许做空",
            "bool",
            true,
            Options: [new StrategyParamOption("开启", "true"), new StrategyParamOption("关闭", "false")]),
        new(
            "ignoreFlat",
            "忽略均线粘合",
            "bool",
            false,
            Options: [new StrategyParamOption("开启", "true"), new StrategyParamOption("关闭", "false")]),
    ];

    public override IReadOnlyList<StrategySignal> Evaluate(
        IReadOnlyList<CandlePoint> candles,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var fastPeriod = StrategyParams.GetInt(parameters, "fastPeriod", 5, 2, 500);
        var slowPeriod = StrategyParams.GetInt(parameters, "slowPeriod", 20, 3, 500);
        if (fastPeriod >= slowPeriod)
        {
            // 快线周期不小于慢线时退化为安全默认值，避免金叉/死叉永远不出现。
            slowPeriod = Math.Min(500, fastPeriod + 1);
        }

        var allowShort = StrategyParams.GetBool(parameters, "allowShort", true);
        var ignoreFlat = StrategyParams.GetBool(parameters, "ignoreFlat", false);
        var closes = CloseSeries(candles);
        var fast = TechnicalIndicators.Sma(closes, fastPeriod);
        var slow = TechnicalIndicators.Sma(closes, slowPeriod);
        var warmup = slowPeriod - 1;

        var signals = new StrategySignal[candles.Count];
        var position = 0;
        for (var i = 0; i < candles.Count; i++)
        {
            if (i < warmup)
            {
                signals[i] = MakeSignal(candles[i], i, 0, $"均线预热中（需 {slowPeriod} 根收盘价），保持空仓");
                continue;
            }

            var golden = fast[i] > slow[i];
            var prevGolden = i > warmup && fast[i - 1] > slow[i - 1];

            if (ignoreFlat)
            {
                // 只响应“状态切换”，避免两条均线粘合时来回折腾。
                if (golden && !prevGolden)
                {
                    position = 1;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"均线金叉：MA{fastPeriod}({Format(fast[i])}) 上穿 MA{slowPeriod}({Format(slow[i])})，开仓做多");
                }
                else if (!golden && prevGolden)
                {
                    position = allowShort ? -1 : 0;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        allowShort
                            ? $"均线死叉：MA{fastPeriod}({Format(fast[i])}) 下穿 MA{slowPeriod}({Format(slow[i])})，平多反手做空"
                            : $"均线死叉：MA{fastPeriod}({Format(fast[i])}) 下穿 MA{slowPeriod}({Format(slow[i])})，平仓离场");
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        position switch
                        {
                            1 => $"均线多头排列，继续持多（MA{fastPeriod}={Format(fast[i])}，MA{slowPeriod}={Format(slow[i])}）",
                            -1 => $"均线空头排列，继续持空（MA{fastPeriod}={Format(fast[i])}，MA{slowPeriod}={Format(slow[i])}）",
                            _ => "均线粘合、未出现有效交叉，保持空仓",
                        });
                }
            }
            else
            {
                position = golden ? 1 : (allowShort ? -1 : 0);
                signals[i] = MakeSignal(
                    candles[i], i, position,
                    golden
                        ? $"收盘价 MA{fastPeriod}({Format(fast[i])}) 高于 MA{slowPeriod}({Format(slow[i])})，持多"
                        : allowShort
                            ? $"收盘价 MA{fastPeriod}({Format(fast[i])}) 低于 MA{slowPeriod}({Format(slow[i])})，持空（做空模式）"
                            : $"收盘价 MA{fastPeriod}({Format(fast[i])}) 低于 MA{slowPeriod}({Format(slow[i])})，空仓");
            }
        }

        return signals;
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
