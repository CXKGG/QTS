using System.Globalization;
using QTS.Domain;

namespace QTS.Strategies;

/// <summary>
/// 布林带突破策略（可双向）：向上突破上轨做多、向下跌破下轨做空；
/// 多头跌破离场参考线（中轨/下轨）离场，空头涨破对称参考线（中轨/上轨）离场。逐根 K 线计算，无未来函数。
/// </summary>
public sealed class BollBreakoutStrategy : StrategyBase
{
    public override string Code => "boll_breakout";

    public override string Name => "布林带突破";

    public override string Category => "突破";

    public override string Description =>
        "收盘价向上突破上轨做多、向下跌破下轨做空（可关闭做空）；多头跌破中轨/下轨离场，空头涨破中轨/上轨离场。适合放量突破行情。";

    public override IReadOnlyList<StrategyParamDescriptor> ParamsSchema { get; } =
    [
        new("period", "布林周期", "int", 20, 5m, 200m, 1m),
        new("stdDevMultiplier", "标准差倍数", "decimal", 2m, 0.5m, 5m, 0.1m),
        new("tolerancePct", "突破容差(%)", "decimal", 0m, 0m, 5m, 0.1m),
        new(
            "exitMode",
            "离场参考线",
            "select",
            "mid",
            Options:
            [
                new StrategyParamOption("中轨", "mid"),
                new StrategyParamOption("下轨", "lower"),
            ]),
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
        var period = StrategyParams.GetInt(parameters, "period", 20, 5, 200);
        var multiplier = StrategyParams.GetDecimal(parameters, "stdDevMultiplier", 2m, 0.5m, 5m);
        var tolerancePct = StrategyParams.GetDecimal(parameters, "tolerancePct", 0m, 0m, 5m);
        var exitMode = StrategyParams.GetString(parameters, "exitMode", "mid", "mid", "lower");
        var allowShort = StrategyParams.GetBool(parameters, "allowShort", true);

        var closes = CloseSeries(candles);
        var middle = TechnicalIndicators.Sma(closes, period);
        var deviation = TechnicalIndicators.StdDev(closes, period);
        var tolerance = tolerancePct / 100m;
        var warmup = period - 1;

        var signals = new StrategySignal[candles.Count];
        var position = 0;
        for (var i = 0; i < candles.Count; i++)
        {
            if (i < warmup)
            {
                signals[i] = MakeSignal(candles[i], i, 0, $"布林带预热中（需 {period} 根收盘价），保持空仓");
                continue;
            }

            var mid = middle[i];
            var upper = mid + multiplier * deviation[i];
            var lower = mid - multiplier * deviation[i];
            var close = candles[i].Close;

            if (position == 0)
            {
                if (upper > 0m && close > upper * (1m + tolerance))
                {
                    position = 1;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"收盘价 {Format(close)} 突破布林上轨 {Format(upper)}（{Format(multiplier)} 倍标准差），开仓做多");
                }
                else if (allowShort && lower > 0m && close < lower * (1m - tolerance))
                {
                    position = -1;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"收盘价 {Format(close)} 跌破布林下轨 {Format(lower)}（{Format(multiplier)} 倍标准差），开仓做空");
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"收盘价 {Format(close)} 未有效突破布林上轨 {Format(upper)} 或下轨 {Format(lower)}，保持空仓");
                }
            }
            else if (position == 1)
            {
                // 多头离场参考线：默认中轨（趋势跟踪），可选下轨（更宽的跟踪止损）。
                var exitReference = exitMode == "lower" ? lower : mid;
                if (exitReference > 0m && close < exitReference * (1m - tolerance))
                {
                    position = 0;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"收盘价 {Format(close)} 跌破布林{(exitMode == "lower" ? "下轨" : "中轨")} {Format(exitReference)}，平多离场");
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"收盘价 {Format(close)} 仍在布林{(exitMode == "lower" ? "下轨" : "中轨")} {Format(exitReference)} 之上，继续持多");
                }
            }
            else
            {
                // 空头离场参考线与多头镜像：默认中轨，exitMode=lower 时对应上轨（对称的宽跟踪）。
                var exitReference = exitMode == "lower" ? upper : mid;
                if (exitReference > 0m && close > exitReference * (1m + tolerance))
                {
                    position = 0;
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"收盘价 {Format(close)} 涨破布林{(exitMode == "lower" ? "上轨" : "中轨")} {Format(exitReference)}，平空离场");
                }
                else
                {
                    signals[i] = MakeSignal(
                        candles[i], i, position,
                        $"收盘价 {Format(close)} 仍在布林{(exitMode == "lower" ? "上轨" : "中轨")} {Format(exitReference)} 之下，继续持空");
                }
            }
        }

        return signals;
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
