namespace QTS.Services;

/// <summary>
/// 大模型服务商预设。所有服务都走 OpenAI 兼容的 /chat/completions 协议，
/// 区别只在默认 Base URL 与常用模型名；用户仍可填写自定义地址/模型。
/// </summary>
public sealed record AiProviderPreset(
    string Key,
    string Label,
    string BaseUrl,
    string Model,
    string[] Models);

public static class AiProviders
{
    public static readonly AiProviderPreset[] All =
    [
        new("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini",
            ["gpt-4o-mini", "gpt-4o", "gpt-4.1-mini"]),
        new("deepseek", "DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat",
            ["deepseek-chat", "deepseek-reasoner"]),
        new("moonshot", "Moonshot Kimi", "https://api.moonshot.cn/v1", "moonshot-v1-8k",
            ["moonshot-v1-8k", "moonshot-v1-32k"]),
        new("qwen", "通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus",
            ["qwen-plus", "qwen-max", "qwen-turbo"]),
        new("zhipu", "智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash",
            ["glm-4-flash", "glm-4-plus"]),
        new("custom", "自定义（OpenAI 兼容）", "", "",
            [])
    ];

    public static AiProviderPreset Resolve(string? key)
    {
        var normalized = (key ?? "").Trim().ToLowerInvariant();
        return All.FirstOrDefault(x => x.Key == normalized) ?? All[0];
    }

    public static string Normalize(string? key) => Resolve(key).Key;

    public static string Label(string? key) => Resolve(key).Label;
}
