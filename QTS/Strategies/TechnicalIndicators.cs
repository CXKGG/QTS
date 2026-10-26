namespace QTS.Strategies;

/// <summary>
/// 因果技术指标计算工具：任意下标 i 的结果只依赖 [0..i] 的数据，绝不使用未来 K 线。
/// 预热期（不足周期）的位置统一返回 0，调用方需自行用根数门控。
/// </summary>
internal static class TechnicalIndicators
{
    /// <summary>简单移动平均（SMA）。第 i 个有效值 = 最近 period 根收盘价的算术平均。</summary>
    public static decimal[] Sma(IReadOnlyList<decimal> values, int period)
    {
        var result = new decimal[values.Count];
        if (period <= 0 || values.Count == 0)
        {
            return result;
        }

        decimal sum = 0m;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period)
            {
                sum -= values[i - period];
            }

            if (i >= period - 1)
            {
                result[i] = sum / period;
            }
        }

        return result;
    }

    /// <summary>指数移动平均（EMA），以首个 period 根的 SMA 作为种子。</summary>
    public static decimal[] Ema(IReadOnlyList<decimal> values, int period)
    {
        var result = new decimal[values.Count];
        if (period <= 0 || values.Count < period)
        {
            return result;
        }

        return EmaCore(result, values, 0, period);
    }

    /// <summary>从 startIndex 开始计算 EMA（用于对已有指标序列再做平滑，如 MACD 的 DEA）。</summary>
    public static decimal[] EmaFrom(IReadOnlyList<decimal> values, int startIndex, int period)
    {
        var result = new decimal[values.Count];
        if (period <= 0 || startIndex < 0 || startIndex >= values.Count)
        {
            return result;
        }

        var seedEnd = startIndex + period - 1;
        if (seedEnd >= values.Count)
        {
            return result;
        }

        return EmaCore(result, values, startIndex, period);
    }

    private static decimal[] EmaCore(decimal[] result, IReadOnlyList<decimal> values, int startIndex, int period)
    {
        var seedEnd = startIndex + period - 1;
        decimal sum = 0m;
        for (var i = startIndex; i <= seedEnd; i++)
        {
            sum += values[i];
        }

        var alpha = 2m / (period + 1m);
        result[seedEnd] = sum / period;
        for (var i = seedEnd + 1; i < values.Count; i++)
        {
            result[i] = alpha * values[i] + (1m - alpha) * result[i - 1];
        }

        return result;
    }

    /// <summary>
    /// RSI（Wilder 平滑）。首个有效值出现在下标 = period 处（需要 period 个涨跌幅）。
    /// </summary>
    public static decimal[] Rsi(IReadOnlyList<decimal> closes, int period)
    {
        var result = new decimal[closes.Count];
        if (period <= 0 || closes.Count <= period)
        {
            return result;
        }

        decimal gainSum = 0m;
        decimal lossSum = 0m;
        for (var i = 1; i <= period; i++)
        {
            var diff = closes[i] - closes[i - 1];
            if (diff > 0m)
            {
                gainSum += diff;
            }
            else
            {
                lossSum -= diff;
            }
        }

        var avgGain = gainSum / period;
        var avgLoss = lossSum / period;
        result[period] = ToRsi(avgGain, avgLoss);

        for (var i = period + 1; i < closes.Count; i++)
        {
            var diff = closes[i] - closes[i - 1];
            var gain = diff > 0m ? diff : 0m;
            var loss = diff < 0m ? -diff : 0m;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            result[i] = ToRsi(avgGain, avgLoss);
        }

        return result;
    }

    private static decimal ToRsi(decimal avgGain, decimal avgLoss)
    {
        if (avgLoss <= 0m)
        {
            return avgGain <= 0m ? 50m : 100m;
        }

        var rs = avgGain / avgLoss;
        return 100m - 100m / (1m + rs);
    }

    /// <summary>滚动标准差（总体标准差，除以 period），用于布林带。</summary>
    public static decimal[] StdDev(IReadOnlyList<decimal> values, int period)
    {
        var result = new decimal[values.Count];
        if (period <= 0 || values.Count == 0)
        {
            return result;
        }

        decimal sum = 0m;
        decimal sumSquares = 0m;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            sumSquares += values[i] * values[i];
            if (i >= period)
            {
                sum -= values[i - period];
                sumSquares -= values[i - period] * values[i - period];
            }

            if (i >= period - 1)
            {
                var mean = sum / period;
                var variance = sumSquares / period - mean * mean;
                if (variance < 0m)
                {
                    variance = 0m;
                }

                result[i] = (decimal)Math.Sqrt((double)variance);
            }
        }

        return result;
    }

    /// <summary>MACD：DIF(快线-慢线)、DEA(DIF 的 signal 平滑)、柱状图 2*(DIF-DEA)。</summary>
    public static (decimal[] Macd, decimal[] Signal, decimal[] Histogram) Macd(
        IReadOnlyList<decimal> closes,
        int fastPeriod,
        int slowPeriod,
        int signalPeriod)
    {
        var fast = Ema(closes, fastPeriod);
        var slow = Ema(closes, slowPeriod);
        var macd = new decimal[closes.Count];
        var signal = new decimal[closes.Count];
        var histogram = new decimal[closes.Count];

        var macdStart = Math.Max(fastPeriod, slowPeriod) - 1;
        if (macdStart < 0 || macdStart >= closes.Count)
        {
            return (macd, signal, histogram);
        }

        for (var i = macdStart; i < closes.Count; i++)
        {
            macd[i] = fast[i] - slow[i];
        }

        signal = EmaFrom(macd, macdStart, signalPeriod);
        var signalStart = macdStart + signalPeriod - 1;
        for (var i = Math.Max(0, signalStart); i < closes.Count; i++)
        {
            histogram[i] = 2m * (macd[i] - signal[i]);
        }

        return (macd, signal, histogram);
    }
}
