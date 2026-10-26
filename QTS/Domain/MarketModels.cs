namespace QTS.Domain;

/// <summary>行情 K 线（统一使用 UTC 时间、Unix 毫秒用于图表）。</summary>
public sealed class CandlePoint
{
    public long Time { get; set; }
    public DateTime OpenTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public string Source { get; set; } = "okx";
}

public sealed record StrategySignal(int Index, DateTime Time, int Position, string Reason);

public sealed record StrategyParamOption(string Label, string Value);

public sealed record StrategyParamDescriptor(
    string Code,
    string Label,
    string Type,
    object? Default,
    decimal? Min = null,
    decimal? Max = null,
    decimal? Step = null,
    IReadOnlyList<StrategyParamOption>? Options = null);

public sealed class MarketInstrument
{
    public string InstId { get; init; } = "";
    public string Base { get; init; } = "";
    public string Quote { get; init; } = "USDT";
    public string Name => $"{Base}/{Quote}";
}
