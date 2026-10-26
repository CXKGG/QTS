using QTS.Contracts;
using QTS.Domain;
using QTS.Services;
using SqlSugar;
using TaskStatus = QTS.Domain.TaskStatus;

namespace QTS.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly ISqlSugarClient _db;
    private readonly IPaperAccountService _paper;
    private readonly IOkxConfigService _okxConfig;
    private readonly IOkxClient _okx;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        ISqlSugarClient db,
        IPaperAccountService paper,
        IOkxConfigService okxConfig,
        IOkxClient okx,
        ILogger<DashboardService> logger)
    {
        _db = db;
        _paper = paper;
        _okxConfig = okxConfig;
        _okx = okx;
        _logger = logger;
    }

    public async Task<DashboardOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var paper = await _paper.GetSnapshotAsync(ct);
        var tasks = await _db.Queryable<TaskEntity>().ToListAsync();
        var openPositions = await _db.Queryable<PositionEntity>()
            .Where(x => x.Status == PositionStatus.Open)
            .ToListAsync();
        var orders = await _db.Queryable<OrderEntity>()
            .OrderByDescending(x => x.Id)
            .Take(8)
            .ToListAsync();

        var todayStart = DateTime.UtcNow.Date;
        var todayOrders = await _db.Queryable<OrderEntity>()
            .Where(x => x.CreatedAt >= todayStart && x.Status == OrderStatus.Filled)
            .CountAsync();
        var todaySnapshots = await _db.Queryable<AccountSnapshotEntity>()
            .Where(x => x.Mode == TradingMode.Paper && x.CreatedAt >= todayStart)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
        var todayBase = todaySnapshots.Count > 0 ? todaySnapshots[0].Equity : paper.InitialBalance;
        var todayPnl = Math.Round(paper.Equity - todayBase, 4);

        var closedPositions = await _db.Queryable<PositionEntity>()
            .Where(x => x.Status == PositionStatus.Closed)
            .ToListAsync();
        var winRate = closedPositions.Count == 0
            ? 0m
            : Math.Round(closedPositions.Count(x => x.RealizedPnl > 0m) * 100m / closedPositions.Count, 2);

        var targetProgress = paper.InitialBalance >= TargetEquity
            ? 0m
            : Math.Clamp((paper.Equity - paper.InitialBalance) / (TargetEquity - paper.InitialBalance) * 100m, 0m, 100m);

        var live = await BuildLiveAsync(ct);
        var curve = await GetEquityCurveAsync("7d", ct);

        var taskNames = tasks.ToDictionary(x => x.Id, x => x.Name);
        var topPositions = openPositions
            .OrderByDescending(x => x.UnrealizedPnl)
            .Take(5)
            .Select(x => MapPosition(x, taskNames))
            .ToList();

        return new DashboardOverviewDto
        {
            Paper = new PaperSummaryDto
            {
                InitialBalance = paper.InitialBalance,
                Equity = paper.Equity,
                Available = paper.Available,
                UsedMargin = paper.UsedMargin,
                PositionValue = paper.PositionValue,
                UnrealizedPnl = paper.UnrealizedPnl,
                RealizedPnl = paper.RealizedPnl,
                ReturnPct = paper.ReturnPct
            },
            Live = live,
            Target = new TargetProgressDto
            {
                Initial = paper.InitialBalance,
                Target = TargetEquity,
                CurrentEquity = paper.Equity,
                ProgressPct = Math.Round(targetProgress, 4)
            },
            Stats = new DashboardStatsDto
            {
                RunningTasks = tasks.Count(x => x.Status == TaskStatus.Running),
                TotalTasks = tasks.Count,
                OpenPositions = openPositions.Count,
                TodayPnl = todayPnl,
                TotalPnl = Math.Round(paper.Equity - paper.InitialBalance, 4),
                TodayTrades = todayOrders,
                WinRate = winRate
            },
            EquityCurve = curve,
            RecentOrders = orders.Select(MapOrder).ToList(),
            TopPositions = topPositions
        };
    }

    public async Task<List<EquityPointDto>> GetEquityCurveAsync(string range, CancellationToken ct = default)
    {
        var days = (range ?? "30d").Trim().ToLowerInvariant() switch
        {
            "7d" => 7,
            "30d" => 30,
            "90d" => 90,
            "all" => 0,
            _ => 30
        };
        var query = _db.Queryable<AccountSnapshotEntity>().Where(x => x.Mode == TradingMode.Paper);
        if (days > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-days);
            query = query.Where(x => x.CreatedAt >= cutoff);
        }

        var rows = await query.OrderBy(x => x.CreatedAt).ToListAsync();
        if (rows.Count == 0)
        {
            var snapshot = await _paper.GetSnapshotAsync(ct);
            return
            [
                new EquityPointDto
                {
                    Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Equity = snapshot.Equity,
                    DrawdownPct = 0m
                }
            ];
        }

        var points = new List<EquityPointDto>(rows.Count);
        var peak = 0m;
        foreach (var row in rows)
        {
            if (row.Equity > peak) peak = row.Equity;
            points.Add(new EquityPointDto
            {
                Time = new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                Equity = row.Equity,
                DrawdownPct = peak <= 0m ? 0m : Math.Round((peak - row.Equity) / peak * 100m, 4)
            });
        }

        // 控制图表点数，最多约 240 个，避免前端渲染压力。
        if (points.Count > 240)
        {
            var step = (int)Math.Ceiling(points.Count / 240.0);
            var sampled = points.Where((_, index) => index % step == 0).ToList();
            if (sampled[^1] != points[^1]) sampled.Add(points[^1]);
            points = sampled;
        }
        return points;
    }

    private async Task<LiveSummaryDto> BuildLiveAsync(CancellationToken ct)
    {
        var summary = new LiveSummaryDto();
        try
        {
            var config = await _okxConfig.GetAsync(ct);
            summary.Configured = config.HasCredentials;
            summary.Connected = config.Connected;
            if (!config.HasCredentials) return summary;

            if (!config.Connected)
            {
                summary.Error = config.ConnectionError ?? "实盘 API 尚未测试连接";
                return summary;
            }

            var account = await _okx.GetAccountAsync(ct);
            if (account.Success && account.Data is not null)
            {
                summary.TotalEquity = account.Data.TotalEquity;
                summary.AvailableUsdt = account.Data.AvailableUsdt;
                summary.UnrealizedPnl = account.Data.UnrealizedPnl;
            }
            else
            {
                summary.Connected = false;
                summary.Error = account.Error ?? "获取欧易账户失败";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取欧易账户失败");
            summary.Error = ex.Message;
        }
        return summary;
    }

    private static OrderDto MapOrder(OrderEntity order) => new()
    {
        Id = order.Id,
        TaskId = order.TaskId,
        TaskName = order.TaskName,
        InstId = order.InstId,
        Mode = order.Mode,
        Side = order.Side,
        OrderType = order.OrderType,
        Price = order.Price,
        Quantity = order.Quantity,
        FilledQuantity = order.FilledQuantity,
        AvgFillPrice = order.AvgFillPrice,
        Fee = order.Fee,
        Leverage = order.Leverage,
        Status = order.Status,
        OkxOrderId = order.OkxOrderId,
        Error = order.Error,
        Reason = order.Reason,
        CreatedAt = order.CreatedAt,
        FilledAt = order.FilledAt
    };

    private static PositionDto MapPosition(PositionEntity position, IReadOnlyDictionary<long, string> taskNames) => new()
    {
        Id = position.Id,
        TaskId = position.TaskId,
        TaskName = position.TaskId == 0
            ? "手动交易"
            : taskNames.TryGetValue(position.TaskId, out var name) ? name : $"任务 #{position.TaskId}",
        InstId = position.InstId,
        Mode = position.Mode,
        Side = string.IsNullOrWhiteSpace(position.Side) ? "long" : position.Side,
        Quantity = position.Quantity,
        AvgEntryPrice = position.AvgEntryPrice,
        LastPrice = position.LastPrice,
        UnrealizedPnl = position.UnrealizedPnl,
        UnrealizedPnlPct = position.AvgEntryPrice <= 0m
            ? 0m
            : Math.Round(
                (string.Equals(position.Side, "short", StringComparison.OrdinalIgnoreCase)
                    ? position.AvgEntryPrice - position.LastPrice
                    : position.LastPrice - position.AvgEntryPrice) / position.AvgEntryPrice * 100m, 4),
        Margin = position.Margin,
        Leverage = position.Leverage,
        StopLoss = position.StopLoss,
        TakeProfit = position.TakeProfit,
        OpenedAt = position.OpenedAt
    };

    private const decimal TargetEquity = 10_000m;
}
