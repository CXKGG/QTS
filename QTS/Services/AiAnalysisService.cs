using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using QTS.Contracts;
using QTS.Domain;

namespace QTS.Services;

/// <summary>
/// AI 决策服务：默认使用完全离线可用的多因子量化引擎分析走势并生成策略方案；
/// 配置 OpenAI 兼容接口后，会把本地指标与行情摘要交给大模型做二次决策，失败时自动回退本地引擎。
/// 注意：生成的方案只用于研究和模拟盘验证，不构成投资建议。
/// </summary>
public sealed class AiAnalysisService : IAiAnalysisService
{
    private static readonly string[] SupportedBars = ["1H", "4H", "1D"];
    private static readonly string[] RiskProfiles = ["conservative", "balanced", "aggressive"];

    private readonly IMarketDataService _market;
    private readonly IStrategyRegistry _registry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAiConfigService _aiConfig;
    private readonly ILogger<AiAnalysisService> _logger;

    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AiAnalysisService(
        IMarketDataService market,
        IStrategyRegistry registry,
        IHttpClientFactory httpClientFactory,
        IAiConfigService aiConfig,
        ILogger<AiAnalysisService> logger)
    {
        _market = market;
        _registry = registry;
        _httpClientFactory = httpClientFactory;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public async Task<AiStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var options = await _aiConfig.GetOptionsAsync(ct);
        var configured = IsLlmConfigured(options);
        return new AiStatusDto
        {
            Mode = configured ? "llm" : "local",
            ProviderType = configured ? options.Provider : "local",
            Provider = configured ? $"{AiProviders.Label(options.Provider)} 大模型" : "本地量化引擎",
            Model = configured ? options.Model : "qts-local-v1",
            LlmConfigured = configured,
            Message = configured
                ? $"已配置 {AiProviders.Label(options.Provider)} 大模型 {options.Model}；调用失败会自动回退本地量化引擎。"
                : "未配置大模型 Key，当前使用离线多因子量化引擎（EMA/MACD/RSI/ATR/量能），无需外网即可生成策略。"
        };
    }

    public async Task<AiAnalysisResultDto> AnalyzeAsync(AiAnalysisRequest request, CancellationToken ct = default)
    {
        var options = await _aiConfig.GetOptionsAsync(ct);
        var instId = NormalizeInstrument(request.InstId);
        var bar = NormalizeBar(request.Bar);
        var riskProfile = NormalizeRiskProfile(request.RiskProfile);
        var lookbackDays = Math.Clamp(request.LookbackDays <= 0 ? 180 : request.LookbackDays, 30, 365);

        var endUtc = DateTime.UtcNow;
        var startUtc = endUtc.AddDays(-lookbackDays);
        var set = await _market.EnsureCandlesAsync(instId, bar, startUtc, endUtc, preferLocal: false, ct);
        var candles = set.Candles.Where(c => c.Close > 0 && c.High > 0 && c.Low > 0).ToList();
        if (candles.Count < 80)
        {
            throw new BusinessException($"可用 K 线不足（{candles.Count} 根），请增大回看天数或更换周期后再分析");
        }

        var analysisCandles = candles.Count > 500 ? candles.TakeLast(500).ToList() : candles;
        var result = BuildLocal(instId, bar, riskProfile, request.AllowShort, analysisCandles);
        result.DataSource = string.IsNullOrWhiteSpace(set.Source) ? "synthetic" : set.Source;
        result.Warning = BuildDataSourceWarning(result.DataSource, set.Warning);

        if (IsLlmConfigured(options))
        {
            await TryApplyLlmAsync(result, analysisCandles, riskProfile, request.AllowShort, options, ct);
        }

        return result;
    }

    private AiAnalysisResultDto BuildLocal(
        string instId, string bar, string riskProfile, bool allowShort, List<CandlePoint> candles)
    {
        var closes = candles.Select(c => (double)c.Close).ToArray();
        var highs = candles.Select(c => (double)c.High).ToArray();
        var lows = candles.Select(c => (double)c.Low).ToArray();
        var volumes = candles.Select(c => (double)c.Volume).ToArray();

        var ema20 = Ema(closes, 20);
        var ema60 = Ema(closes, 60);
        var (macdLine, signalLine, hist) = Macd(closes, 12, 26, 9);
        var rsi = Rsi(closes, 14);
        var atr = Atr(highs, lows, closes, 14);

        var last = candles[^1];
        var ema20Last = ema20[^1];
        var ema60Last = ema60[^1];
        var macdLast = macdLine[^1];
        var signalLast = signalLine[^1];
        var histLast = hist[^1];
        var prevHist = hist.Length > 1 ? hist[^2] : 0d;
        var rsiLast = rsi[^1];
        var atrLast = atr[^1];
        var atrPct = last.Close > 0 ? atrLast / (double)last.Close * 100d : 0d;

        var avgVolume = volumes.Length > 21
            ? volumes.Skip(Math.Max(0, volumes.Length - 21)).Take(20).Average()
            : volumes.Average();
        var volumeRatio = avgVolume > 0 ? volumes[^1] / avgVolume : 1d;

        var support = lows.Skip(Math.Max(0, lows.Length - 30)).Min();
        var resistance = highs.Skip(Math.Max(0, highs.Length - 30)).Max();
        var ema20Prev = ema20.Length > 10 ? ema20[^11] : ema20Last;
        var slopePct = ema20Prev > 0 ? (ema20Last - ema20Prev) / ema20Prev * 100d : 0d;

        double score = 0;
        score += ema20Last > ema60Last ? 25 : -25;
        score += (double)last.Close > ema20Last ? 15 : -15;
        score += (double)last.Close > ema60Last ? 10 : -10;
        score += macdLast > signalLast ? 15 : -15;
        score += histLast > prevHist ? 10 : -10;
        score += rsiLast >= 55 ? 10 : rsiLast <= 45 ? -10 : 0;
        score += slopePct > 0.3 ? 15 : slopePct < -0.3 ? -15 : 0;
        score = Math.Clamp(score, -100, 100);

        var direction = score >= 20 ? "up" : score <= -20 ? "down" : "sideways";
        var directionLabel = direction switch
        {
            "up" => "上升趋势",
            "down" => "下降趋势",
            _ => "震荡整理"
        };
        var strength = (int)Math.Clamp(Math.Abs(score) + (Math.Abs(score) >= 45 ? 15 : 0), 0, 100);

        var barsPerDay = bar switch { "1H" => 24, "4H" => 6, _ => 1 };
        var changePct = closes.Length > barsPerDay && closes[^(barsPerDay + 1)] > 0
            ? (closes[^1] / closes[^(barsPerDay + 1)] - 1d) * 100d
            : 0d;

        var strategyCode = ChooseStrategy(score, atrPct, volumeRatio);
        var strategy = _registry.Get(strategyCode) ?? _registry.Get("ma_cross")
            ?? throw new BusinessException("策略库为空，无法生成 AI 策略方案");
        strategyCode = strategy.Code;

        var (maxPositionPct, baseLeverage, atrMultiplier, rewardRisk) = riskProfile switch
        {
            "conservative" => (20m, 1, 1.6m, 2.0m),
            "aggressive" => (50m, 3, 2.5m, 2.2m),
            _ => (35m, 2, 2.0m, 2.0m)
        };

        var absScore = Math.Abs(score);
        var confidence = (int)Math.Clamp(46 + absScore * 0.45 + (absScore >= 50 ? 8 : 0) + (volumeRatio > 1.15 ? 5 : 0), 35, 92);
        if (direction == "sideways") confidence = Math.Min(confidence, 66);
        if (!allowShort && direction == "down") confidence = Math.Min(confidence, 40);

        var leverage = baseLeverage;
        if (confidence < 60) leverage = 1;
        if (riskProfile == "balanced" && confidence >= 78) leverage = 2;
        leverage = Math.Clamp(leverage, 1, 3);

        var stopLossPct = Math.Round(Math.Clamp((decimal)atrPct * atrMultiplier, 1.5m, 10m), 2);
        var takeProfitPct = Math.Round(Math.Clamp(stopLossPct * rewardRisk, 3m, 30m), 2);

        var parameters = BuildStrategyParams(strategyCode, bar, riskProfile, allowShort);
        var directionBias = !allowShort && score < 0 ? "long" : score >= 15 ? "long" : score <= -15 ? "short" : "both";
        var expectedDirection = direction switch { "up" => "偏多", "down" => "偏空", _ => "双向/观望" };

        var rationale = new List<string>
        {
            $"EMA20={F(ema20Last)}，EMA60={F(ema60Last)}，价格{(last.Close > (decimal)ema20Last ? "站上" : "跌破")} EMA20；EMA20 近 10 根斜率 {slopePct:+0.00;-0.00;0.00}%。",
            $"MACD={F(macdLast)}，信号线={F(signalLast)}，柱体{(histLast > 0 ? "为正" : "为负")}且{(histLast > prevHist ? "动能增强" : "动能减弱")}。",
            $"RSI14={F(rsiLast)}，{(rsiLast >= 70 ? "接近超买区" : rsiLast <= 30 ? "接近超卖区" : "处于中性区间")}。",
            $"ATR14={F(atrLast)}（约 {atrPct:F2}%），20 根均量比 {volumeRatio:F2}，{(volumeRatio > 1.15 ? "量能放大" : volumeRatio < 0.85 ? "量能萎缩" : "量能平稳")}。",
            $"近 30 根支撑≈{F((decimal)support)}，阻力≈{F((decimal)resistance)}；综合趋势评分 {score:F0}/100。",
            $"策略选择：{strategy.Name}（{strategy.Category}），预期方向{expectedDirection}；参数与风控由本地引擎按 {RiskName(riskProfile)} 风险偏好生成。"
        };

        var warnings = new List<string>
        {
            "AI 生成方案仅用于策略研究和模拟盘验证，不构成投资建议，也不承诺收益。",
            "虚拟币波动剧烈，请先回测并小仓位模拟验证；杠杆会同时放大盈利与亏损。"
        };
        if (direction == "down" && allowShort)
            warnings.Add("当前趋势偏空，做空需要承担价格反向上涨造成的亏损风险。");
        if (!allowShort && direction == "down")
            warnings.Add("当前趋势偏空但已关闭做空，策略可能长时间空仓，请等待多头信号或重新开启做空。");
        if (leverage > 1)
            warnings.Add($"建议杠杆 {leverage}× 属于逐仓保证金，极端行情下该仓保证金可能全部损失。");

        var suggestion = new AiSuggestionDto
        {
            StrategyCode = strategy.Code,
            StrategyName = strategy.Name,
            DirectionBias = directionBias,
            Confidence = confidence,
            Leverage = leverage,
            SlippagePct = bar == "1D" ? 0.08m : 0.05m,
            Params = parameters,
            Risk = new TaskRiskDto
            {
                StopLossPct = stopLossPct,
                TakeProfitPct = takeProfitPct,
                MaxPositionPct = maxPositionPct
            },
            CapitalPct = maxPositionPct,
            Summary = $"{bar} 周期 {instId} 当前处于{directionLabel}（趋势评分 {score:F0}，强度 {strength}）。" +
                      $"建议使用「{strategy.Name}」，方向偏好 {DirectionName(directionBias)}；" +
                      $"{leverage}× 杠杆、止损 {stopLossPct:F2}%、止盈 {takeProfitPct:F2}%、单次仓位上限 {maxPositionPct:F0}%。" +
                      (direction == "sideways" ? " 当前更适合区间策略，等待突破确认后再加仓。" : ""),
            Rationale = rationale,
            Warnings = warnings
        };

        return new AiAnalysisResultDto
        {
            InstId = instId,
            Bar = bar,
            GeneratedAt = DateTime.UtcNow,
            Provider = "本地量化引擎",
            Model = "qts-local-v1",
            Candles = candles.Count,
            LastPrice = last.Close,
            ChangePct = Math.Round((decimal)changePct, 2),
            Trend = new AiTrendDto
            {
                Direction = direction,
                Label = directionLabel,
                Score = (int)Math.Round(score),
                Strength = strength
            },
            Indicators = new AiIndicatorsDto
            {
                Ema20 = Math.Round((decimal)ema20Last, 4),
                Ema60 = Math.Round((decimal)ema60Last, 4),
                Rsi14 = Math.Round((decimal)rsiLast, 2),
                Macd = Math.Round((decimal)macdLast, 4),
                MacdSignal = Math.Round((decimal)signalLast, 4),
                AtrPct = Math.Round((decimal)atrPct, 2),
                VolumeRatio = Math.Round((decimal)volumeRatio, 2),
                Support = Math.Round((decimal)support, 4),
                Resistance = Math.Round((decimal)resistance, 4)
            },
            Suggestion = suggestion
        };
    }

    private static string ChooseStrategy(double score, double atrPct, double volumeRatio)
    {
        var absScore = Math.Abs(score);
        if (absScore >= 45 && volumeRatio >= 1.15) return "macd_trend";
        if (absScore >= 32) return "ma_cross";
        if (atrPct >= 3.0) return "boll_breakout";
        return "rsi_reversion";
    }

    private static Dictionary<string, object?> BuildStrategyParams(
        string strategyCode, string bar, string riskProfile, bool allowShort)
    {
        var conservative = riskProfile == "conservative";
        return strategyCode switch
        {
            "ma_cross" => new Dictionary<string, object?>
            {
                // 周期越短噪声越大，用更快的均线；日线用更慢的均线，避免高频反手被手续费侵蚀。
                ["fastPeriod"] = bar switch { "1D" => 20, "4H" => 12, _ => 7 },
                ["slowPeriod"] = bar switch { "1D" => 60, "4H" => 48, _ => 25 },
                ["ignoreFlat"] = false,
                ["allowShort"] = allowShort
            },
            "macd_trend" => new Dictionary<string, object?>
            {
                ["fastPeriod"] = 12,
                ["slowPeriod"] = 26,
                ["signalPeriod"] = 9,
                ["minHoldBars"] = 2,
                ["allowShort"] = allowShort
            },
            "boll_breakout" => new Dictionary<string, object?>
            {
                ["period"] = 20,
                ["stdDevMultiplier"] = conservative ? 2.2m : 2.0m,
                ["tolerancePct"] = 0.1m,
                ["exitMode"] = "mid",
                ["allowShort"] = allowShort
            },
            _ => new Dictionary<string, object?>
            {
                ["period"] = 14,
                ["oversold"] = conservative ? 25m : 30m,
                ["overbought"] = conservative ? 75m : 70m,
                ["exitLevel"] = 50m,
                ["allowShort"] = allowShort
            }
        };
    }

    private async Task TryApplyLlmAsync(
        AiAnalysisResultDto result, List<CandlePoint> candles, string riskProfile, bool allowShort,
        AiOptions options, CancellationToken ct)
    {
        try
        {
            var plan = await RequestLlmPlanAsync(result, candles, riskProfile, allowShort, options, ct);
            if (plan is null) return;

            var selected = _registry.Get(plan.StrategyCode?.Trim() ?? "");
            if (selected is null || !string.Equals(selected.Code, plan.StrategyCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"大模型返回的策略代码“{plan.StrategyCode}”不存在");
            }

            var suggestion = result.Suggestion;
            suggestion.StrategyCode = selected.Code;
            suggestion.StrategyName = selected.Name;
            suggestion.Params = BuildStrategyParams(selected.Code, result.Bar, riskProfile, allowShort);

            if (plan.Leverage is > 0)
                suggestion.Leverage = Math.Clamp(plan.Leverage.Value, 1, 3);
            if (plan.Confidence is >= 0 and <= 100)
                suggestion.Confidence = plan.Confidence.Value;
            if (plan.Risk is not null)
            {
                suggestion.Risk.StopLossPct = Math.Clamp(plan.Risk.StopLossPct, 1m, 50m);
                suggestion.Risk.TakeProfitPct = Math.Clamp(plan.Risk.TakeProfitPct, 1m, 200m);
                suggestion.Risk.MaxPositionPct = Math.Clamp(plan.Risk.MaxPositionPct, 5m, 100m);
            }

            if (!string.IsNullOrWhiteSpace(plan.Summary))
                suggestion.Summary = plan.Summary.Trim();
            if (plan.Rationale is { Count: > 0 })
            {
                suggestion.Rationale = plan.Rationale
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Take(10)
                    .ToList();
            }
            suggestion.Warnings.Add("以上结论由大模型结合本地量化指标二次生成，参数与风控已由本地引擎校验。");

            result.Provider = $"{AiProviders.Label(options.Provider)} 大模型";
            result.Model = options.Model;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 大模型调用失败，回退本地引擎");
            result.Provider = "本地量化引擎（大模型回退）";
            result.Model = "qts-local-v1";
            result.Warning = JoinWarning(result.Warning, $"大模型调用失败，已回退本地引擎：{ex.Message}");
            result.Suggestion.Warnings.Add("大模型接口不可用，本次方案完全由本地量化引擎生成。");
        }
    }

    private async Task<LlmPlan?> RequestLlmPlanAsync(
        AiAnalysisResultDto local, List<CandlePoint> candles, string riskProfile, bool allowShort,
        AiOptions options, CancellationToken ct)
    {
        var context = new
        {
            instId = local.InstId,
            bar = local.Bar,
            lastPrice = local.LastPrice,
            changePct = local.ChangePct,
            trend = local.Trend,
            indicators = local.Indicators,
            riskProfile,
            allowShort,
            localSuggestion = new
            {
                local.Suggestion.StrategyCode,
                local.Suggestion.DirectionBias,
                local.Suggestion.Confidence,
                local.Suggestion.Leverage,
                local.Suggestion.Risk
            },
            recentCandles = candles.TakeLast(24).Select(c => new
            {
                t = c.OpenTime,
                o = c.Open,
                h = c.High,
                l = c.Low,
                c = c.Close,
                v = c.Volume
            })
        };

        var systemPrompt =
            "你是 QTS 量化交易系统的策略决策助手。禁止承诺收益，禁止给出投资建议式表述，只做量化研究。"
            + "只能从这些策略代码中选择：ma_cross、rsi_reversion、boll_breakout、macd_trend。"
            + "只输出一个 JSON 对象，不要 markdown，不要解释。JSON 字段：strategyCode、directionBias(long|short|both)、"
            + "confidence(0-100)、leverage(1-3)、risk{stopLossPct,takeProfitPct,maxPositionPct}、summary(中文)、"
            + "rationale(中文数组)、warnings(中文数组)。";
        var userPrompt =
            "请根据以下行情指标和最近K线，选择最合适的策略并给出风控参数：\n"
            + JsonSerializer.Serialize(context, _json);

        var payload = new
        {
            model = options.Model,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var baseUrl = options.BaseUrl.TrimEnd('/');
        var client = _httpClientFactory.CreateClient("ai");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("大模型返回内容为空");

        var json = StripCodeFence(content);
        return JsonSerializer.Deserialize<LlmPlan>(json, _json);
    }

    private static bool IsLlmConfigured(AiOptions options) =>
        options.Enabled
        && !string.IsNullOrWhiteSpace(options.BaseUrl)
        && !string.IsNullOrWhiteSpace(options.ApiKey)
        && !string.IsNullOrWhiteSpace(options.Model);

    private string NormalizeInstrument(string? raw)
    {
        var normalized = (raw ?? "").Trim().ToUpperInvariant().Replace("/", "-");
        var instrument = _market.GetInstruments().FirstOrDefault(x =>
            string.Equals(x.InstId, normalized, StringComparison.OrdinalIgnoreCase));
        if (instrument is null)
        {
            throw new BusinessException($"不支持的交易对：{raw}。可选 {string.Join("、", _market.GetInstruments().Select(x => x.InstId))}");
        }
        return instrument.InstId;
    }

    private static string NormalizeBar(string? raw)
    {
        var bar = MarketBars.Normalize(raw);
        if (!SupportedBars.Contains(bar, StringComparer.OrdinalIgnoreCase))
            throw new BusinessException($"AI 分析仅支持 {string.Join("/", SupportedBars)} 周期");
        return bar;
    }

    private static string NormalizeRiskProfile(string? raw)
    {
        var value = (raw ?? "").Trim().ToLowerInvariant();
        return RiskProfiles.Contains(value) ? value : "balanced";
    }

    private static string RiskName(string riskProfile) => riskProfile switch
    {
        "conservative" => "稳健",
        "aggressive" => "激进",
        _ => "平衡"
    };

    private static string DirectionName(string directionBias) => directionBias switch
    {
        "long" => "做多",
        "short" => "做空",
        _ => "多空双向"
    };

    private static string? BuildDataSourceWarning(string source, string? warning)
    {
        if (string.Equals(source, "synthetic", StringComparison.OrdinalIgnoreCase))
            return JoinWarning(warning, "当前数据为示例行情（OKX 不可达），分析结果只验证流程，不代表真实市场。");
        return warning;
    }

    private static string? JoinWarning(string? first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : $"{first}；{second}";

    private static string StripCodeFence(string content)
    {
        var text = content.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        var firstNewLine = text.IndexOf('\n');
        if (firstNewLine >= 0) text = text[(firstNewLine + 1)..];
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0) text = text[..lastFence];
        return text.Trim();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string F(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string F(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static double[] Ema(double[] values, int period)
    {
        var result = new double[values.Length];
        if (values.Length == 0) return result;
        var multiplier = 2d / (period + 1);
        result[0] = values[0];
        for (var i = 1; i < values.Length; i++)
            result[i] = (values[i] - result[i - 1]) * multiplier + result[i - 1];
        return result;
    }

    private static (double[] Macd, double[] Signal, double[] Hist) Macd(
        double[] values, int fastPeriod, int slowPeriod, int signalPeriod)
    {
        var fast = Ema(values, fastPeriod);
        var slow = Ema(values, slowPeriod);
        var macd = new double[values.Length];
        for (var i = 0; i < values.Length; i++) macd[i] = fast[i] - slow[i];
        var signal = Ema(macd, signalPeriod);
        var hist = new double[values.Length];
        for (var i = 0; i < values.Length; i++) hist[i] = macd[i] - signal[i];
        return (macd, signal, hist);
    }

    private static double[] Rsi(double[] closes, int period)
    {
        var result = new double[closes.Length];
        if (closes.Length <= period) return result;
        double gain = 0, loss = 0;
        for (var i = 1; i <= period; i++)
        {
            var diff = closes[i] - closes[i - 1];
            if (diff >= 0) gain += diff; else loss -= diff;
        }
        var avgGain = gain / period;
        var avgLoss = loss / period;
        result[period] = avgLoss == 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
        for (var i = period + 1; i < closes.Length; i++)
        {
            var diff = closes[i] - closes[i - 1];
            var currentGain = diff > 0 ? diff : 0;
            var currentLoss = diff < 0 ? -diff : 0;
            avgGain = (avgGain * (period - 1) + currentGain) / period;
            avgLoss = (avgLoss * (period - 1) + currentLoss) / period;
            result[i] = avgLoss == 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
        }
        return result;
    }

    private static double[] Atr(double[] highs, double[] lows, double[] closes, int period)
    {
        var result = new double[closes.Length];
        if (closes.Length <= period) return result;
        double sum = 0;
        for (var i = 1; i <= period; i++)
            sum += Math.Max(highs[i] - lows[i], Math.Max(Math.Abs(highs[i] - closes[i - 1]), Math.Abs(lows[i] - closes[i - 1])));
        var atr = sum / period;
        result[period] = atr;
        for (var i = period + 1; i < closes.Length; i++)
        {
            var tr = Math.Max(highs[i] - lows[i], Math.Max(Math.Abs(highs[i] - closes[i - 1]), Math.Abs(lows[i] - closes[i - 1])));
            atr = (atr * (period - 1) + tr) / period;
            result[i] = atr;
        }
        return result;
    }

    private sealed class LlmPlan
    {
        public string? StrategyCode { get; set; }
        public string? DirectionBias { get; set; }
        public int? Confidence { get; set; }
        public int? Leverage { get; set; }
        public TaskRiskDto? Risk { get; set; }
        public string? Summary { get; set; }
        public List<string>? Rationale { get; set; }
        public List<string>? Warnings { get; set; }
    }
}
