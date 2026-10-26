namespace QTS.Services;

/// <summary>K 线周期规范化与工具。</summary>
public static class MarketBars
{
    public static readonly string[] Supported = ["1m", "5m", "15m", "30m", "1H", "4H", "1D"];

    public static bool TryNormalize(string? raw, out string bar)
    {
        bar = "1H";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        foreach (var supported in Supported)
        {
            if (string.Equals(supported, raw.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                bar = supported;
                return true;
            }
        }
        return false;
    }

    public static string Normalize(string? raw) =>
        TryNormalize(raw, out var bar) ? bar : throw new Contracts.BusinessException($"不支持的 K 线周期：{raw}，可选 {string.Join("/", Supported)}");

    public static TimeSpan GetSpan(string bar) => bar switch
    {
        "1m" => TimeSpan.FromMinutes(1),
        "5m" => TimeSpan.FromMinutes(5),
        "15m" => TimeSpan.FromMinutes(15),
        "30m" => TimeSpan.FromMinutes(30),
        "1H" => TimeSpan.FromHours(1),
        "4H" => TimeSpan.FromHours(4),
        "1D" => TimeSpan.FromDays(1),
        _ => TimeSpan.FromHours(1)
    };
}
