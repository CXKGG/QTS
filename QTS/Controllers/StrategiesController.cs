using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;
using QTS.Domain;
using QTS.Services;
using QTS.Strategies;

namespace QTS.Controllers;

/// <summary>策略库接口：返回内置策略的元信息、参数 schema 与默认参数。</summary>
[ApiController]
[Route("api/strategies")]
public sealed class StrategiesController : ControllerBase
{
    private readonly IStrategyRegistry _strategies;

    public StrategiesController(IStrategyRegistry strategies)
    {
        _strategies = strategies;
    }

    /// <summary>GET /api/strategies：策略列表（含 paramsSchema 与 defaultParams）。</summary>
    [HttpGet]
    public ActionResult<ApiResponse<List<StrategyDto>>> GetAll()
    {
        var items = _strategies.All.Select(ToDto).ToList();
        return Ok(ApiResponse<List<StrategyDto>>.Ok(items));
    }

    /// <summary>GET /api/strategies/{code}：按代码获取单个策略。</summary>
    [HttpGet("{code}")]
    public ActionResult<ApiResponse<StrategyDto>> GetByCode(string code)
    {
        var strategy = _strategies.Get(code)
            ?? throw new BusinessException($"策略不存在：{code}", statusCode: 404);
        return Ok(ApiResponse<StrategyDto>.Ok(ToDto(strategy)));
    }

    private static StrategyDto ToDto(IStrategy strategy)
    {
        return new StrategyDto
        {
            Code = strategy.Code,
            Name = strategy.Name,
            Category = strategy.Category,
            Description = strategy.Description,
            ParamsSchema = strategy.ParamsSchema.Select(ToParamDto).ToList(),
            DefaultParams = strategy.DefaultParams.ToDictionary(
                pair => pair.Key,
                pair => StrategyParamNormalizer.UnwrapValue(pair.Value),
                StringComparer.OrdinalIgnoreCase),
        };
    }

    private static StrategyParamDto ToParamDto(StrategyParamDescriptor descriptor)
    {
        return new StrategyParamDto
        {
            Code = descriptor.Code,
            Label = descriptor.Label,
            Type = descriptor.Type,
            Default = StrategyParamNormalizer.UnwrapValue(descriptor.Default),
            Min = descriptor.Min,
            Max = descriptor.Max,
            Step = descriptor.Step,
            Options = descriptor.Options?
                .Select(option => new StrategyOptionDto { Label = option.Label, Value = option.Value })
                .ToList(),
        };
    }
}
