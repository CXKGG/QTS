using System.Globalization;
using QTS.Domain;

namespace QTS.Strategies;

/// <summary>
/// RSI 超卖反转策略（可双向）：超卖做多、超买做空，回到中轴离场；allowShort=false 时超买只平多。
/// 逐根 K 线因果计算，无未来函数。
/// </summary>
public sealed class RsiReversionStrategy : StrategyBase
{
    public override string Code => "rsi_reversion";

    public override string Name => "RSI 超卖反转";

    public override string Category => "反转";

    public override string Description =>
        "RSI 跌入超卖区做多、升入超买区做空（可关闭做空）；持仓回到中轴离场，反向极值可直接反手。适合震荡行情，单边趋势需严格止损。";

    public override IReadOnlyList<StrategyParamDescriptor> ParamsSchema { get; } =
    [
        new("period", "RSI 周期", "int", 14, 2m, 100m, 1m),
        new("oversold", "超卖阈值", "decimal", 30m, 5m, 45m, 1m),
        new("overbought", "超买阈值", "decimal", 70m, 55m, 95m, 1m),
        new("exitLevel", "离场中轴", "decimal", 50m, 20m, 80m, 1m),
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
        var period = StrategyParams.GetInt(parameters, "period", 14, 2, 100);
        var oversold = StrategyParams.GetDecimal(parameters, "oversold", 30m, 5m, 45m);
        var overbought = StrategyParams.GetDecimal(parameters, "overbought", 70m, 55m, 95m);
        if (overbought <= oversold)
        {
            overbought = Math.Min(95m, oversold + 10m);
        }

        var exitLevel = StrategyParams.GetDecimal(parameters, "exitLevel", 50m, 20m, 80m);
        var allowShort = StrategyParams.GetBool(parameters, "allowShort", true);
        var closes = CloseSeries(candles);
        var rsi = TechnicalIndicators.Rsi(closes, period);

        var signals = new StrategySignal[candles.Count];
        var position = 0;
        for (var i = 0; i < candles.Count; i++)
        {
            if (i < period)
            {
                signals[i] = MakeSignal(candles[i], i, 0, $"RSI 预热中（需 {period + 1} 根收盘价），保持空仓");
                continue;
            }

            var value = rsi[i];
            if (position == 0)
            {
                if (value <= oversold)
                {
                    position = 1;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"RSI={Format(value)} 进入超卖区（≤{Format(oversold)}），超跌反弹开多");
                }
                else if (allowShort && value >= overbought)
                {
                    position = -1;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"RSI={Format(value)} 进入超买区（≥{Format(overbought)}），超买回落开空");
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"RSI={Format(value)} 处于（{Format(oversold)}，{Format(overbought)}）区间，保持空仓");
                }
            }
            else if (position == 1)
            {
                if (value >= overbought)
                {
                    position = allowShort ? -1 : 0;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        allowShort
                            ? $"RSI={Format(value)} 进入超买区（≥{Format(overbought)}），平多反手做空"
                            : $"RSI={Format(value)} 进入超买区（≥{Format(overbought)}），平仓止盈");
                }
                else if (value >= exitLevel)
                {
                    position = 0;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"RSI={Format(value)} 回升至中轴（≥{Format(exitLevel)}），离场观望");
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"RSI={Format(value)} 仍在超卖修复区间，继续持多");
                }
            }
            else
            {
                if (value <= oversold)
                {
                    position = 1;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"RSI={Format(value)} 进入超卖区（≤{Format(oversold)}），平空反手做多");
                }
                else if (value <= exitLevel)
                {
                    position = 0;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"RSI={Format(value)} 回落至中轴（≤{Format(exitLevel)}），空头离场观望");
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"RSI={Format(value)} 仍在超买回落区间，继续持空");
                }
            }
        }

        return signals;
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
