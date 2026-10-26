using QTS.Data;
using QTS.Domain;
using QTS.Services;
using SqlSugar;
using TaskStatus = QTS.Domain.TaskStatus;

namespace QTS.Workers;

/// <summary>
/// 交易任务调度器：每 30 秒轮询一次 Running 任务。
/// 交易引擎内部会判断“是否出现新 K 线”，因此轮询本身不会重复交易。
/// </summary>
public sealed class TradingWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TradingWorker> _logger;

    public TradingWorker(IServiceScopeFactory scopeFactory, ILogger<TradingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                var engine = scope.ServiceProvider.GetRequiredService<ITradingEngine>();

                var runningIds = await db.Queryable<TaskEntity>()
                    .Where(t => t.Status == TaskStatus.Running)
                    .OrderBy(t => t.Id)
                    .Select(t => t.Id)
                    .ToListAsync(stoppingToken);

                if (runningIds.Count > 0)
                {
                    _logger.LogDebug("交易调度：本轮处理 {Count} 个运行中任务", runningIds.Count);
                    foreach (var id in runningIds)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        await engine.ExecuteTaskAsync(id, stoppingToken);
                    }
                }

                await engine.RefreshPaperPositionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "交易调度循环执行失败，将在下一轮重试");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
