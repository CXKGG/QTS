using QTS.Domain;

namespace QTS.Services;

/// <summary>回测引擎输入：已对齐的 K 线、逐根目标仓位信号与交易成本配置。</summary>
public sealed class BacktestEngineInput
{
    public required IReadOnlyList<CandlePoint> Candles { get; init; }

    public required IReadOnlyList<StrategySignal> Signals { get; init; }

    public decimal InitialCapital { get; init; } = 100m;

    /// <summary>单边手续费率，百分比单位（0.1 表示 0.1%）。</summary>
    public decimal FeeRatePct { get; init; } = 0.1m;

    /// <summary>单边滑点，百分比单位（0.05 表示 0.05%）。</summary>
    public decimal SlippagePct { get; init; } = 0.05m;

    /// <summary>每次开仓投入的可用资金比例，百分比单位（95 表示 95%）。</summary>
    public decimal PositionSizePct { get; init; } = 95m;

    /// <summary>逐仓杠杆倍数，1 表示无杠杆。保证金 = 投入资金，名义价值 = 保证金 × 杠杆。</summary>
    public int Leverage { get; init; } = 1;

    /// <summary>止损百分比，0 表示关闭。</summary>
    public decimal StopLossPct { get; init; }

    /// <summary>止盈百分比，0 表示关闭。</summary>
    public decimal TakeProfitPct { get; init; }
}

/// <summary>逐 bar 权益点。</summary>
public sealed class BacktestEquityPoint
{
    public DateTime Time { get; init; }

    public decimal Equity { get; init; }

    public decimal DrawdownPct { get; init; }
}

/// <summary>一次完整的开平仓回合。</summary>
public sealed class BacktestRoundTrip
{
    public string Side { get; init; } = "long";

    public DateTime EntryTime { get; init; }

    public DateTime ExitTime { get; init; }

    public decimal EntryPrice { get; init; }

    public decimal ExitPrice { get; init; }

    public decimal Quantity { get; init; }

    /// <summary>本笔交易占用的保证金（含杠杆后的自有资金）。</summary>
    public decimal Margin { get; init; }

    public decimal Fee { get; init; }

    public decimal Pnl { get; init; }

    public decimal PnlPct { get; init; }

    public string ExitReason { get; init; } = string.Empty;

    public int HoldingBars { get; init; }
}

/// <summary>月度收益。</summary>
public sealed class BacktestMonthlyReturn
{
    public string Month { get; init; } = string.Empty;

    public decimal ReturnPct { get; init; }
}

/// <summary>回测输出指标。</summary>
public sealed class BacktestEngineResult
{
    public required IReadOnlyList<BacktestEquityPoint> EquityCurve { get; init; }

    public required IReadOnlyList<BacktestRoundTrip> Trades { get; init; }

    public required IReadOnlyList<BacktestMonthlyReturn> MonthlyReturns { get; init; }

    public decimal InitialCapital { get; init; }

    public decimal FinalEquity { get; init; }

    public decimal TotalReturnPct { get; init; }

    public decimal MaxDrawdownPct { get; init; }

    public decimal Sharpe { get; init; }

    public decimal WinRatePct { get; init; }

    public decimal ProfitFactor { get; init; }

    public decimal Fees { get; init; }

    public decimal BuyAndHoldReturnPct { get; init; }

    public int TradeCount { get; init; }

    public int Leverage { get; init; } = 1;
}

/// <summary>
/// 事件驱动回测引擎（支持多/空 + 逐仓杠杆）：
/// 1) 信号在 K 线收盘后产生，目标仓位（1 多 / 0 空仓 / -1 空）在“下一根 K 线开盘价”成交（无未来函数）；
/// 2) 成交价叠加方向感知滑点：开多买贵、平多卖便宜、开空卖便宜、平空买贵；
/// 3) 逐仓保证金：开仓冻结 margin，名义价值 = margin × 杠杆，手续费按名义价值收取，权益 = 现金 + 保证金 + 浮动盈亏；
/// 4) 持仓期间按 K 线最高/最低价检查止损/止盈（0 关闭），同时触发时按最坏的止损处理；
/// 5) 单笔最大亏损不超过该仓保证金，模拟逐仓强平；每根 K 线收盘记录权益与回撤。
/// </summary>
public sealed class BacktestEngine
{
    private const decimal Hundred = 100m;

    public BacktestEngineResult Run(BacktestEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var candles = input.Candles;
        if (candles.Count == 0)
        {
            return new BacktestEngineResult
            {
                EquityCurve = [],
                Trades = [],
                MonthlyReturns = [],
                InitialCapital = input.InitialCapital,
                Leverage = Math.Clamp(input.Leverage, 1, 20),
            };
        }

        var targets = BuildPositionTargets(input.Signals, candles.Count);
        var capital = input.InitialCapital > 0m ? input.InitialCapital : 100m;
        var feeRate = Positive(input.FeeRatePct) / Hundred;
        var slippage = Positive(input.SlippagePct) / Hundred;
        var positionRatio = Math.Clamp(input.PositionSizePct, 1m, 100m) / Hundred;
        var leverage = Math.Clamp(input.Leverage, 1, 20);
        var stopLossPct = Positive(input.StopLossPct);
        var takeProfitPct = Positive(input.TakeProfitPct);

        var equityCurve = new List<BacktestEquityPoint>(candles.Count);
        var trades = new List<BacktestRoundTrip>();

        decimal cash = capital;
        decimal quantity = 0m;
        decimal entryPrice = 0m;
        decimal entryFee = 0m;
        decimal margin = 0m;
        var direction = 0;
        DateTime entryTime = default;
        var entryIndex = -1;
        var waitForFlat = false;
        var feeTotal = 0m;
        var peakEquity = capital;

        for (var i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            var openTime = ResolveTime(candle);
            var desired = i > 0 ? targets[i - 1] : 0;
            var openRaw = Positive(candle.Open);

            // —— 1) 执行上一根 K 线收盘后的目标仓位（下一根开盘成交）——
            if (quantity > 0m && desired != direction)
            {
                var exitPrice = ApplyExitSlippage(openRaw, direction, slippage);
                if (exitPrice > 0m)
                {
                    var trade = ClosePosition(
                        direction, quantity, exitPrice, entryPrice, entryFee, margin,
                        entryTime, openTime, entryIndex, i,
                        desired == 0 ? "策略离场" : "反手平仓", feeRate);
                    cash += margin + trade.Pnl;
                    feeTotal += trade.Fee;
                    trades.Add(trade);
                    quantity = 0m;
                    entryPrice = 0m;
                    entryFee = 0m;
                    margin = 0m;
                    direction = 0;
                    entryIndex = -1;
                }
            }

            if (desired == 0)
                waitForFlat = false;

            if (quantity <= 0m && desired != 0 && !waitForFlat && openRaw > 0m)
            {
                var entry = ApplyEntrySlippage(openRaw, desired, slippage);
                var newMargin = cash * positionRatio;
                var notional = newMargin * leverage;
                var newQty = entry > 0m ? notional / entry : 0m;
                var fee = notional * feeRate;
                if (newMargin > 0m && newQty > 0m && newMargin + fee <= cash + 1e-12m)
                {
                    cash -= newMargin + fee;
                    quantity = newQty;
                    entryPrice = entry;
                    margin = newMargin;
                    entryFee = fee;
                    direction = desired;
                    entryTime = openTime;
                    entryIndex = i;
                    feeTotal += fee;
                }
            }

            // —— 2) 持仓期间检查止损/止盈（同一根内同时触发按止损处理）——
            if (quantity > 0m && (stopLossPct > 0m || takeProfitPct > 0m))
            {
                var high = Math.Max(candle.High, Math.Max(candle.Open, candle.Close));
                var low = Math.Min(candle.Low > 0m ? candle.Low : candle.Open, Math.Min(candle.Open, candle.Close));
                var stopRaw = stopLossPct > 0m
                    ? entryPrice * (direction > 0 ? 1m - stopLossPct / Hundred : 1m + stopLossPct / Hundred)
                    : 0m;
                var takeRaw = takeProfitPct > 0m
                    ? entryPrice * (direction > 0 ? 1m + takeProfitPct / Hundred : 1m - takeProfitPct / Hundred)
                    : 0m;
                var stopHit = stopRaw > 0m && (direction > 0 ? low <= stopRaw : high >= stopRaw);
                var takeHit = takeRaw > 0m && (direction > 0 ? high >= takeRaw : low <= takeRaw);
                if (stopHit || takeHit)
                {
                    // 保守假设：同一根 K 线内先触发止损，用更差的价格成交。
                    var isStop = stopHit;
                    var rawExit = isStop ? stopRaw : takeRaw;
                    var exitPrice = ApplyExitSlippage(rawExit, direction, slippage);
                    var reason = isStop ? "止损" : "止盈";
                    var trade = ClosePosition(
                        direction, quantity, exitPrice, entryPrice, entryFee, margin,
                        entryTime, openTime, entryIndex, i, reason, feeRate);
                    cash += margin + trade.Pnl;
                    feeTotal += trade.Fee;
                    trades.Add(trade);
                    quantity = 0m;
                    entryPrice = 0m;
                    entryFee = 0m;
                    margin = 0m;
                    direction = 0;
                    entryIndex = -1;
                    waitForFlat = isStop; // 止损离场后等待策略信号先转空仓，避免立即追回
                }
            }

            // —— 3) 收盘按市值记录权益与回撤 ——
            var floating = quantity > 0m ? (candle.Close - entryPrice) * quantity * direction : 0m;
            var equity = Round2(cash + margin + floating);
            if (equity > peakEquity)
            {
                peakEquity = equity;
            }

            var drawdown = peakEquity > 0m ? Round2((peakEquity - equity) / peakEquity * Hundred) : 0m;
            equityCurve.Add(new BacktestEquityPoint
            {
                Time = openTime,
                Equity = equity,
                DrawdownPct = drawdown < 0m ? 0m : drawdown,
            });
        }

        // —— 4) 回测结束强制平仓（按最后一根收盘价，未平仓仓位计入一次完整回合）——
        if (quantity > 0m)
        {
            var last = candles[^1];
            var lastTime = ResolveTime(last);
            var exitPrice = ApplyExitSlippage(Positive(last.Close), direction, slippage);
            if (exitPrice > 0m)
            {
                var trade = ClosePosition(
                    direction, quantity, exitPrice, entryPrice, entryFee, margin,
                    entryTime, lastTime, entryIndex, candles.Count - 1, "回测结束平仓", feeRate);
                cash += margin + trade.Pnl;
                feeTotal += trade.Fee;
                trades.Add(trade);
                quantity = 0m;
                margin = 0m;
                direction = 0;
            }
        }

        var finalEquity = Round2(cash);
        var totalReturnPct = capital > 0m ? Round2((finalEquity - capital) / capital * Hundred) : 0m;
        var maxDrawdownPct = equityCurve.Count > 0 ? equityCurve.Max(static point => point.DrawdownPct) : 0m;
        var sharpe = ComputeSharpe(equityCurve);
        var (winRatePct, profitFactor) = ComputeTradeStats(trades);
        var buyAndHoldPct = ComputeBuyAndHold(candles);

        return new BacktestEngineResult
        {
            EquityCurve = equityCurve,
            Trades = trades,
            MonthlyReturns = ComputeMonthlyReturns(equityCurve, capital),
            InitialCapital = capital,
            FinalEquity = finalEquity,
            TotalReturnPct = totalReturnPct,
            MaxDrawdownPct = Round2(maxDrawdownPct),
            Sharpe = Round2(sharpe),
            WinRatePct = winRatePct,
            ProfitFactor = profitFactor,
            Fees = Round2(feeTotal),
            BuyAndHoldReturnPct = buyAndHoldPct,
            TradeCount = trades.Count,
            Leverage = leverage,
        };
    }

    private static decimal ApplyEntrySlippage(decimal price, int direction, decimal slippage) =>
        direction > 0 ? price * (1m + slippage) : price * (1m - slippage);

    private static decimal ApplyExitSlippage(decimal price, int direction, decimal slippage) =>
        direction > 0 ? price * (1m - slippage) : price * (1m + slippage);

    private static BacktestRoundTrip ClosePosition(
        int direction,
        decimal quantity,
        decimal exitPrice,
        decimal entryPrice,
        decimal entryFee,
        decimal margin,
        DateTime entryTime,
        DateTime exitTime,
        int entryIndex,
        int exitIndex,
        string exitReason,
        decimal feeRate)
    {
        var exitNotional = quantity * exitPrice;
        var exitFee = exitNotional * feeRate;
        var rawPnl = (exitPrice - entryPrice) * quantity * direction - entryFee - exitFee;
        // 逐仓最大亏损不超过保证金，模拟强平。
        var pnl = Math.Max(rawPnl, -margin);
        var pnlPct = margin > 0m ? pnl / margin * Hundred : 0m;
        return new BacktestRoundTrip
        {
            Side = direction > 0 ? "long" : "short",
            EntryTime = entryTime,
            ExitTime = exitTime,
            EntryPrice = Round8(entryPrice),
            ExitPrice = Round8(exitPrice),
            Quantity = Round8(quantity),
            Margin = Round2(margin),
            Fee = Round2(entryFee + exitFee),
            Pnl = Round2(pnl),
            PnlPct = Round2(pnlPct),
            ExitReason = exitReason,
            HoldingBars = Math.Max(0, exitIndex - entryIndex),
        };
    }

    /// <summary>把逐根信号转换为 -1/0/1 目标仓位序列（缺失下标沿用上一根，避免异常数据导致仓位跳变）。</summary>
    private static int[] BuildPositionTargets(IReadOnlyList<StrategySignal> signals, int candleCount)
    {
        var targets = new int[candleCount];
        if (signals.Count == 0)
        {
            return targets;
        }

        var byIndex = new Dictionary<int, int>(signals.Count);
        foreach (var signal in signals)
        {
            if (signal.Index < 0 || signal.Index >= candleCount)
            {
                continue;
            }

            byIndex[signal.Index] = Math.Clamp(signal.Position, -1, 1);
        }

        var previous = 0;
        for (var i = 0; i < candleCount; i++)
        {
            if (byIndex.TryGetValue(i, out var position))
            {
                previous = position;
            }

            targets[i] = previous;
        }

        return targets;
    }

    /// <summary>夏普比率：按自然日聚合权益后计算日收益，年化 √365。</summary>
    private static decimal ComputeSharpe(IReadOnlyList<BacktestEquityPoint> equityCurve)
    {
        if (equityCurve.Count < 2)
        {
            return 0m;
        }

        var dailyEquity = new List<decimal>();
        var currentDay = DateOnly.MinValue;
        foreach (var point in equityCurve)
        {
            var day = DateOnly.FromDateTime(point.Time);
            if (day != currentDay)
            {
                dailyEquity.Add(point.Equity);
                currentDay = day;
            }
            else
            {
                dailyEquity[^1] = point.Equity;
            }
        }

        if (dailyEquity.Count < 3)
        {
            return 0m;
        }

        var returns = new List<double>(dailyEquity.Count - 1);
        for (var i = 1; i < dailyEquity.Count; i++)
        {
            if (dailyEquity[i - 1] <= 0m)
            {
                continue;
            }

            returns.Add((double)(dailyEquity[i] / dailyEquity[i - 1] - 1m));
        }

        if (returns.Count < 2)
        {
            return 0m;
        }

        var mean = returns.Average();
        var variance = returns.Sum(value => (value - mean) * (value - mean)) / returns.Count;
        if (variance <= 0d)
        {
            return 0m;
        }

        var sharpe = mean / Math.Sqrt(variance) * Math.Sqrt(365d);
        if (double.IsNaN(sharpe) || double.IsInfinity(sharpe))
        {
            return 0m;
        }

        return (decimal)sharpe;
    }

    private static (decimal WinRatePct, decimal ProfitFactor) ComputeTradeStats(IReadOnlyList<BacktestRoundTrip> trades)
    {
        if (trades.Count == 0)
        {
            return (0m, 0m);
        }

        decimal grossProfit = 0m;
        decimal grossLoss = 0m;
        var wins = 0;
        foreach (var trade in trades)
        {
            if (trade.Pnl > 0m)
            {
                wins++;
                grossProfit += trade.Pnl;
            }
            else if (trade.Pnl < 0m)
            {
                grossLoss += -trade.Pnl;
            }
        }

        var winRate = Round2(wins * Hundred / trades.Count);
        decimal profitFactor;
        if (grossLoss > 0m)
        {
            profitFactor = Round2(grossProfit / grossLoss);
        }
        else
        {
            profitFactor = grossProfit > 0m ? 99m : 0m;
        }

        return (winRate, profitFactor);
    }

    private static decimal ComputeBuyAndHold(IReadOnlyList<CandlePoint> candles)
    {
        var firstOpen = Positive(candles[0].Open);
        var lastClose = Positive(candles[^1].Close);
        if (firstOpen <= 0m || lastClose <= 0m)
        {
            return 0m;
        }

        return Round2((lastClose - firstOpen) / firstOpen * Hundred);
    }

    private static List<BacktestMonthlyReturn> ComputeMonthlyReturns(
        IReadOnlyList<BacktestEquityPoint> equityCurve,
        decimal initialCapital)
    {
        var result = new List<BacktestMonthlyReturn>();
        if (equityCurve.Count == 0)
        {
            return result;
        }

        decimal previousEnd = initialCapital;
        var index = 0;
        while (index < equityCurve.Count)
        {
            var key = equityCurve[index].Time.ToString("yyyy-MM");
            var startEquity = previousEnd;
            var endEquity = equityCurve[index].Equity;
            while (index < equityCurve.Count && equityCurve[index].Time.ToString("yyyy-MM") == key)
            {
                endEquity = equityCurve[index].Equity;
                index++;
            }

            var returnPct = startEquity > 0m ? Round2((endEquity - startEquity) / startEquity * Hundred) : 0m;
            result.Add(new BacktestMonthlyReturn { Month = key, ReturnPct = returnPct });
            previousEnd = endEquity;
        }

        return result;
    }

    private static DateTime ResolveTime(CandlePoint candle)
    {
        if (candle.OpenTime != default)
        {
            return candle.OpenTime.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(candle.OpenTime, DateTimeKind.Utc)
                : candle.OpenTime.ToUniversalTime();
        }

        return candle.Time > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(candle.Time).UtcDateTime
            : DateTime.UtcNow;
    }

    private static decimal Positive(decimal value) => value > 0m ? value : 0m;

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Round8(decimal value) => Math.Round(value, 8, MidpointRounding.AwayFromZero);
}
