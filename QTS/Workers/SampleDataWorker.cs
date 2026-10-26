using QTS.Contracts;
using QTS.Domain;
using QTS.Services;
using SqlSugar;

namespace QTS.Workers;

/// <summary>
/// 首次启动时生成一个明确标注“示例行情”的回测，让仪表盘/回测页有内容可看。
/// 只有在数据库里没有任何回测记录时才执行；失败只记录日志，不影响系统启动。
/// </summary>
public sealed class SampleDataWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SampleDataWorker> _logger;

    public SampleDataWorker(IServiceScopeFactory scopeFactory, ILogger<SampleDataWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            if (await db.Queryable<BacktestEntity>().AnyAsync(stoppingToken)) return;

            var backtests = scope.ServiceProvider.GetRequiredService<IBacktestService>();
            var end = DateTime.UtcNow.AddDays(-240);
            var result = await backtests.RunAsync(new BacktestCreateRequest
            {
                Name = "示例回测 · 双均线策略（示例行情）",
                StrategyCode = "ma_cross",
                InstId = "BTC-USDT",
                Bar = "1D",
                StartTime = end.AddDays(-365),
                EndTime = end,
                InitialCapital = 100m,
                FeeRatePct = 0.1m,
                SlippagePct = 0.05m,
                PositionSizePct = 95m,
                PreferLocalData = true
            }, 1, stoppingToken);

            _logger.LogInformation("已生成示例回测 {Id}，数据源：{Source}", result.Id, result.DataSource);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停机
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "生成示例回测失败（不影响系统启动）");
        }
    }
}
