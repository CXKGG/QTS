using System.Globalization;
using System.Text.Json.Serialization;

namespace QTS.Infrastructure.Okx;

// 欧易 V5 响应信封：{ "code": "0", "msg": "", "data": [ ... ] }，code/msg/数值字段均为字符串。
internal sealed class OkxApiResponse<T>
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("data")]
    public List<T>? Data { get; set; }
}

/// <summary>GET /api/v5/market/ticker 的 data[0]。</summary>
internal sealed class OkxTickerPayload
{
    [JsonPropertyName("instId")]
    public string? InstId { get; set; }

    [JsonPropertyName("last")]
    public string? Last { get; set; }

    [JsonPropertyName("open24h")]
    public string? Open24h { get; set; }

    [JsonPropertyName("high24h")]
    public string? High24h { get; set; }

    [JsonPropertyName("low24h")]
    public string? Low24h { get; set; }

    [JsonPropertyName("vol24h")]
    public string? Vol24h { get; set; }

    /// <summary>24h 成交量（计价货币，USDT 本位下更直观），优先于 vol24h。</summary>
    [JsonPropertyName("volCcy24h")]
    public string? VolCcy24h { get; set; }

    [JsonPropertyName("ts")]
    public string? Ts { get; set; }
}

/// <summary>GET /api/v5/account/balance 的 data[0]。</summary>
internal sealed class OkxBalancePayload
{
    [JsonPropertyName("totalEq")]
    public string? TotalEq { get; set; }

    [JsonPropertyName("details")]
    public List<OkxBalanceDetail>? Details { get; set; }
}

internal sealed class OkxBalanceDetail
{
    [JsonPropertyName("ccy")]
    public string? Ccy { get; set; }

    [JsonPropertyName("eq")]
    public string? Eq { get; set; }

    [JsonPropertyName("availBal")]
    public string? AvailBal { get; set; }

    [JsonPropertyName("availEq")]
    public string? AvailEq { get; set; }

    [JsonPropertyName("upl")]
    public string? Upl { get; set; }
}

/// <summary>POST /api/v5/trade/order 的 data[0]。</summary>
internal sealed class OkxOrderPayload
{
    [JsonPropertyName("ordId")]
    public string? OrdId { get; set; }

    [JsonPropertyName("clOrdId")]
    public string? ClOrdId { get; set; }

    [JsonPropertyName("sCode")]
    public string? SCode { get; set; }

    [JsonPropertyName("sMsg")]
    public string? SMsg { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("fillSz")]
    public string? FillSz { get; set; }

    [JsonPropertyName("avgPx")]
    public string? AvgPx { get; set; }

    [JsonPropertyName("fee")]
    public string? Fee { get; set; }
}

/// <summary>GET /api/v5/public/time 的 data[0]。</summary>
internal sealed class OkxTimePayload
{
    [JsonPropertyName("ts")]
    public string? Ts { get; set; }
}

/// <summary>GET /api/v5/account/config 的 data[0]。</summary>
internal sealed class OkxAccountConfigPayload
{
    [JsonPropertyName("posMode")]
    public string? PosMode { get; set; }

    [JsonPropertyName("acctLv")]
    public string? AcctLv { get; set; }
}

/// <summary>GET /api/v5/public/instruments?instType=SWAP 的 data[0]。</summary>
internal sealed class OkxSwapInstrumentPayload
{
    [JsonPropertyName("instId")]
    public string? InstId { get; set; }

    [JsonPropertyName("ctVal")]
    public string? CtVal { get; set; }

    [JsonPropertyName("lotSz")]
    public string? LotSz { get; set; }

    [JsonPropertyName("minSz")]
    public string? MinSz { get; set; }

    [JsonPropertyName("tickSz")]
    public string? TickSz { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }
}

/// <summary>欧易字符串数值的宽松解析（"1.23"、"0"、""、缺失都不抛异常）。</summary>
internal static class OkxJson
{
    public static decimal ToDecimal(string? raw, decimal fallback = 0m) => ToDecimalOrNull(raw) ?? fallback;

    public static decimal? ToDecimalOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return decimal.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public static long ToLong(string? raw, long fallback = 0L)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        var text = raw.Trim();
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return (long)number;
        return fallback;
    }
}
