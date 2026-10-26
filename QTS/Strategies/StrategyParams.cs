using System.Globalization;
using System.Text.Json;

namespace QTS.Strategies;

/// <summary>
/// 策略参数读取与归一化工具。参数来源可能是 Dictionary&lt;string, JsonElement&gt;（HTTP 请求）、
/// JObject 反序列化得到的 object、或已经归一化的 CLR 值，因此这里统一做宽容转换。
/// </summary>
public static class StrategyParams
{
    /// <summary>按 code 读取原始参数（先精确匹配、再忽略大小写匹配）。</summary>
    public static object? Find(IReadOnlyDictionary<string, object?>? parameters, string code)
    {
        if (parameters is null || parameters.Count == 0 || string.IsNullOrEmpty(code))
        {
            return null;
        }

        if (parameters.TryGetValue(code, out var exact))
        {
            return exact;
        }

        foreach (var pair in parameters)
        {
            if (string.Equals(pair.Key, code, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>读取 int 参数并夹紧到 [min, max]；缺失/非法时返回 fallback。</summary>
    public static int GetInt(IReadOnlyDictionary<string, object?>? parameters, string code, int fallback, int min, int max)
    {
        var value = ToDecimal(Find(parameters, code));
        if (value is null)
        {
            return Math.Clamp(fallback, min, max);
        }

        var rounded = (int)Math.Round(Math.Clamp(value.Value, min, max), MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, min, max);
    }

    /// <summary>读取 decimal 参数并夹紧到 [min, max]；缺失/非法时返回 fallback。</summary>
    public static decimal GetDecimal(IReadOnlyDictionary<string, object?>? parameters, string code, decimal fallback, decimal min, decimal max)
    {
        var value = ToDecimal(Find(parameters, code));
        var result = value ?? fallback;
        return Math.Clamp(result, min, max);
    }

    /// <summary>读取 bool 参数；缺失/非法时返回 fallback。</summary>
    public static bool GetBool(IReadOnlyDictionary<string, object?>? parameters, string code, bool fallback)
    {
        return ToBool(Find(parameters, code)) ?? fallback;
    }

    /// <summary>读取 select/string 参数（仅在候选值内匹配，忽略大小写）；非法时返回 fallback。</summary>
    public static string GetString(IReadOnlyDictionary<string, object?>? parameters, string code, string fallback, params string[] allowed)
    {
        var raw = ToText(Find(parameters, code));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        raw = raw.Trim();
        if (allowed.Length == 0)
        {
            return raw;
        }

        foreach (var candidate in allowed)
        {
            if (string.Equals(candidate, raw, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return fallback;
    }

    /// <summary>把任意后端参数值转换为 decimal；无法转换时返回 null。</summary>
    public static decimal? ToDecimal(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case decimal d:
                return d;
            case int i:
                return i;
            case long l:
                return l;
            case short s:
                return s;
            case byte b:
                return b;
            case double dbl when !double.IsNaN(dbl) && !double.IsInfinity(dbl):
                return (decimal)dbl;
            case float f when !float.IsNaN(f) && !float.IsInfinity(f):
                return (decimal)f;
            case bool boolean:
                return boolean ? 1m : 0m;
            case string text when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                return parsed;
            case JsonElement element:
                return ToDecimal(element);
            default:
                return null;
        }
    }

    private static decimal? ToDecimal(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
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
                return decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            case JsonValueKind.True:
                return 1m;
            case JsonValueKind.False:
                return 0m;
            default:
                return null;
        }
    }

    /// <summary>把任意后端参数值转换为 bool；无法转换时返回 null。</summary>
    public static bool? ToBool(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case bool boolean:
                return boolean;
            case JsonElement element:
                switch (element.ValueKind)
                {
                    case JsonValueKind.True:
                        return true;
                    case JsonValueKind.False:
                        return false;
                    case JsonValueKind.Number:
                        return element.TryGetDecimal(out var dec) ? dec != 0m : null;
                    case JsonValueKind.String:
                        return ToBool(element.GetString());
                    default:
                        return null;
                }
            case string text:
                text = text.Trim();
                if (text.Length == 0)
                {
                    return null;
                }

                if (bool.TryParse(text, out var parsedBoolean))
                {
                    return parsedBoolean;
                }

                if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumber))
                {
                    return parsedNumber != 0m;
                }

                if (text is "是" or "开" or "启用" or "yes" or "on" or "y")
                {
                    return true;
                }

                if (text is "否" or "关" or "禁用" or "no" or "off" or "n")
                {
                    return false;
                }

                return null;
            default:
                var asNumber = ToDecimal(value);
                return asNumber is null ? null : asNumber.Value != 0m;
        }
    }

    /// <summary>把任意后端参数值转换为文本；无法转换时返回 null。</summary>
    public static string? ToText(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return text;
            case JsonElement element:
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };
            case bool boolean:
                return boolean ? "true" : "false";
            case decimal d:
                return d.ToString(CultureInfo.InvariantCulture);
            case double dbl:
                return dbl.ToString("R", CultureInfo.InvariantCulture);
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString();
        }
    }
}
