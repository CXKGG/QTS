namespace QTS.Services;

/// <summary>
/// 可选的大模型配置。未配置时系统使用离线可用的本地量化决策引擎；
/// 配置后通过 OpenAI 兼容的 /chat/completions 接口生成策略建议。
/// 可通过 appsettings.json 的 Ai 节点或环境变量 Ai__ApiKey / Ai__BaseUrl / Ai__Model 配置。
/// </summary>
public sealed class AiOptions
{
    public bool Enabled { get; set; }
    /// <summary>openai / deepseek / moonshot / qwen / zhipu / custom</summary>
    public string Provider { get; set; } = "openai";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public int TimeoutSeconds { get; set; } = 30;
}
