using System.Text.Json;
using QTS.Domain;
using QTS.Services;

namespace QTS.Strategies;

/// <summary>
/// 请求参数归一化：把 HTTP 请求里的 Dictionary&lt;string, JsonElement&gt; 合并到策略默认参数，
/// 校验类型、select 候选值并夹紧 int/decimal 到 schema 的 min/max。
/// </summary>
public static class StrategyParamNormalizer
{
    /// <summary>
    /// 合并策略默认参数与请求参数，返回“有效参数”字典（键统一为 schema 中的 code，忽略未知参数）。
    /// </summary>
    public static Dictionary<string, object?> Merge(IStrategy strategy, IReadOnlyDictionary<string, JsonElement>? input)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in strategy.DefaultParams)
        {
            var value = UnwrapValue(pair.Value);
            if (value is not null)
            {
                result[pair.Key] = value;
            }
        }

        if (input is not null)
        {
            foreach (var pair in input)
            {
                var descriptor = FindDescriptor(strategy, pair.Key);
                if (descriptor is null || pair.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    continue;
                }

                var normalized = NormalizeValue(pair.Value, descriptor);
                if (normalized is not null)
                {
                    result[descriptor.Code] = normalized;
                }
            }
        }

        // 最终再按 schema 夹紧一次，避免默认值或外部注入值越界。
        foreach (var descriptor in strategy.ParamsSchema)
        {
            if (!result.TryGetValue(descriptor.Code, out var current) || current is null)
            {
                var fallback = UnwrapValue(descriptor.Default);
                if (fallback is not null)
                {
                    result[descriptor.Code] = fallback;
                }

                continue;
            }

            var clamped = ClampToDescriptor(current, descriptor);
            if (clamped is not null)
            {
                result[descriptor.Code] = clamped;
            }
        }

        return result;
    }

    /// <summary>把策略默认参数 + 请求参数归一化后序列化为 JSON（用于持久化 ParamsJson）。</summary>
    public static string SerializeParams(IReadOnlyDictionary<string, object?> parameters)
    {
        return JsonSerializer.Serialize(parameters);
    }

    /// <summary>把 JsonElement 或已归一化的值还原为 CLR 值（long/decimal/bool/string 等）。</summary>
    public static object? UnwrapValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement element => NormalizeJson(element),
            _ => value
        };
    }

    /// <summary>把 JSON 值转换为 CLR 值：整数 → long，小数 → decimal，字符串 → string，布尔 → bool。</summary>
    public static object? NormalizeJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    return integer;
                }

                if (element.TryGetDecimal(out var dec))
                {
                    return dec;
                }

                if (element.TryGetDouble(out var dbl) && !double.IsNaN(dbl) && !double.IsInfinity(dbl))
                {
                    return (decimal)dbl;
                }

                return null;
            case JsonValueKind.String:
                var text = element.GetString();
                return string.IsNullOrEmpty(text) ? null : text;
            case JsonValueKind.Object:
            case JsonValueKind.Array:
                return element.GetRawText();
            default:
                return null;
        }
    }

    private static StrategyParamDescriptor? FindDescriptor(IStrategy strategy, string code)
    {
        foreach (var descriptor in strategy.ParamsSchema)
        {
            if (string.Equals(descriptor.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return descriptor;
            }
        }

        return null;
    }

    private static object? NormalizeValue(JsonElement element, StrategyParamDescriptor descriptor)
    {
        var raw = NormalizeJson(element);
        return raw is null ? null : ClampToDescriptor(raw, descriptor);
    }

    private static object? ClampToDescriptor(object value, StrategyParamDescriptor descriptor)
    {
        var type = (descriptor.Type ?? "string").Trim().ToLowerInvariant();
        switch (type)
        {
            case "int":
            {
                var numeric = StrategyParams.ToDecimal(value);
                if (numeric is null)
                {
                    return UnwrapValue(descriptor.Default);
                }

                var clamped = Math.Clamp(numeric.Value, descriptor.Min ?? decimal.MinValue, descriptor.Max ?? decimal.MaxValue);
                return (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
            }

            case "decimal":
            case "number":
            {
                var numeric = StrategyParams.ToDecimal(value);
                if (numeric is null)
                {
                    return UnwrapValue(descriptor.Default);
                }

                return Math.Clamp(numeric.Value, descriptor.Min ?? decimal.MinValue, descriptor.Max ?? decimal.MaxValue);
            }

            case "bool":
            case "boolean":
                return StrategyParams.ToBool(value) ?? UnwrapValue(descriptor.Default);

            case "select":
            {
                var text = StrategyParams.ToText(value)?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    return UnwrapValue(descriptor.Default);
                }

                if (descriptor.Options is null || descriptor.Options.Count == 0)
                {
                    return text;
                }

                foreach (var option in descriptor.Options)
                {
                    if (string.Equals(option.Value, text, StringComparison.OrdinalIgnoreCase))
                    {
                        return option.Value;
                    }

                    if (string.Equals(option.Label, text, StringComparison.OrdinalIgnoreCase))
                    {
                        return option.Value;
                    }
                }

                return UnwrapValue(descriptor.Default);
            }

            default:
                return StrategyParams.ToText(value);
        }
    }
}
