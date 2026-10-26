using QTS.Domain;
using QTS.Services;

namespace QTS.Strategies;

/// <summary>
/// 内置策略注册表：由 DI 注入全部 IStrategy 实现（Autofac 按类型自动收集）。
/// 若容器未收集到任何实现（如手动 new 测试场景），回退为内置 4 个策略，保证接口始终可用。
/// </summary>
public sealed class StrategyRegistry : IStrategyRegistry
{
    /// <summary>内置策略的展示顺序。</summary>
    private static readonly string[] PreferredOrder =
    [
        "ma_cross", "rsi_reversion", "boll_breakout", "macd_trend",
    ];

    private readonly IReadOnlyList<IStrategy> _all;
    private readonly Dictionary<string, IStrategy> _byCode;

    public StrategyRegistry(IEnumerable<IStrategy> strategies)
    {
        var source = strategies?.Where(static strategy => strategy is not null).ToList() ?? [];
        if (source.Count == 0)
        {
            source =
            [
                new MaCrossStrategy(),
                new RsiReversionStrategy(),
                new BollBreakoutStrategy(),
                new MacdTrendStrategy(),
            ];
        }

        var ordered = new List<IStrategy>(source.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in PreferredOrder)
        {
            foreach (var strategy in source)
            {
                var strategyCode = NormalizeCode(strategy.Code);
                if (string.Equals(strategyCode, code, StringComparison.OrdinalIgnoreCase) && seen.Add(strategyCode))
                {
                    ordered.Add(strategy);
                }
            }
        }

        foreach (var strategy in source)
        {
            var strategyCode = NormalizeCode(strategy.Code);
            if (strategyCode.Length > 0 && seen.Add(strategyCode))
            {
                ordered.Add(strategy);
            }
        }

        _all = ordered;
        _byCode = ordered.ToDictionary(static strategy => NormalizeCode(strategy.Code), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IStrategy> All => _all;

    public IStrategy? Get(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var key = NormalizeCode(code);
        return _byCode.TryGetValue(key, out var strategy) ? strategy : null;
    }

    private static string NormalizeCode(string? code) => (code ?? string.Empty).Trim();
}
