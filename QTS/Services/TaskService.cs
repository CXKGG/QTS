using System.Text.Json;
using QTS.Contracts;
using QTS.Domain;
using QTS.Infrastructure;
using QTS.Services;
using SqlSugar;
using TaskStatus = QTS.Domain.TaskStatus;

namespace QTS.Services;

public sealed class TaskService : ITaskService
{
    private readonly ISqlSugarClient _db;
    private readonly IStrategyRegistry _strategies;
    private readonly IMarketDataService _market;
    private readonly IPaperAccountService _paper;
    private readonly IOkxConfigService _okxConfig;
    private readonly ILogger<TaskService> _logger;

    public TaskService(
        ISqlSugarClient db,
        IStrategyRegistry strategies,
        IMarketDataService market,
        IPaperAccountService paper,
        IOkxConfigService okxConfig,
        ILogger<TaskService> logger)
    {
        _db = db;
        _strategies = strategies;
        _market = market;
        _paper = paper;
        _okxConfig = okxConfig;
        _logger = logger;
    }

    public async Task<PagedResult<TaskDto>> ListAsync(int page, int pageSize, string? status, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.Queryable<TaskEntity>();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TaskStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);

        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var items = new List<TaskDto>(rows.Count);
        foreach (var row in rows) items.Add(await MapAsync(row));
        return new PagedResult<TaskDto> { Items = items, Total = total, Page = page, PageSize = pageSize };
    }

    public async Task<TaskDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var task = await _db.Queryable<TaskEntity>().FirstAsync(x => x.Id == id);
        return task is null ? null : await MapAsync(task);
    }

    public async Task<TaskDto> CreateAsync(TaskCreateRequest request, long userId, CancellationToken ct = default)
    {
        var validated = await ValidateAsync(request, ct);
        var now = DateTime.UtcNow;
        var task = new TaskEntity
        {
            Name = validated.Name,
            StrategyCode = validated.StrategyCode,
            InstId = validated.InstId,
            Bar = validated.Bar,
            Mode = validated.Mode,
            Status = request.StartImmediately ? TaskStatus.Running : TaskStatus.Stopped,
            AllocatedCapital = validated.AllocatedCapital,
            ParamsJson = validated.ParamsJson,
            RiskJson = validated.RiskJson,
            Leverage = validated.Leverage,
            SlippagePct = validated.SlippagePct,
            TotalPnl = 0m,
            LastSignal = "FLAT",
            CreatedAt = now,
            UpdatedAt = now,
            UserId = userId
        };
        task.Id = await _db.Insertable(task).ExecuteReturnBigIdentityAsync();
        _logger.LogInformation("创建任务 {TaskId} {Name}，模式 {Mode}", task.Id, task.Name, task.Mode);
        return await MapAsync(task);
    }

    public async Task<TaskDto> UpdateAsync(long id, TaskCreateRequest request, CancellationToken ct = default)
    {
        var task = await _db.Queryable<TaskEntity>().FirstAsync(x => x.Id == id)
                   ?? throw new BusinessException("任务不存在", "NOT_FOUND", 404);
        if (task.Status is TaskStatus.Running or TaskStatus.Paused)
            throw new BusinessException("运行中的任务不能编辑，请先停止任务");

        var validated = await ValidateAsync(request, ct);
        task.Name = validated.Name;
        task.StrategyCode = validated.StrategyCode;
        task.InstId = validated.InstId;
        task.Bar = validated.Bar;
        task.Mode = validated.Mode;
        task.AllocatedCapital = validated.AllocatedCapital;
        task.ParamsJson = validated.ParamsJson;
        task.RiskJson = validated.RiskJson;
        task.Leverage = validated.Leverage;
        task.SlippagePct = validated.SlippagePct;
        task.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(task)
            .UpdateColumns(x => new
            {
                x.Name, x.StrategyCode, x.InstId, x.Bar, x.Mode,
                x.AllocatedCapital, x.ParamsJson, x.RiskJson, x.Leverage, x.SlippagePct, x.UpdatedAt
            })
            .ExecuteCommandAsync();
        return await MapAsync(task);
    }

    public async Task<TaskDto> StartAsync(long id, CancellationToken ct = default)
    {
        var task = await _db.Queryable<TaskEntity>().FirstAsync(x => x.Id == id)
                   ?? throw new BusinessException("任务不存在", "NOT_FOUND", 404);
        if (task.Status == TaskStatus.Running) return await MapAsync(task);
        if (task.Status == TaskStatus.Error) task.LastError = null;

        if (task.Mode == TradingMode.Paper)
            throw new BusinessException("纸面交易已关闭，请使用实盘 API 账户模式；历史纸面任务仅保留记录");

        if (task.Mode == TradingMode.Live)
        {
            var config = await _okxConfig.GetAsync(ct);
            if (!config.HasCredentials)
                throw new BusinessException("实盘任务未配置欧易 API 凭据，请先到设置页配置");
            if (!config.LiveTradingEnabled)
                throw new BusinessException("实盘交易开关未开启，请先到设置页主动开启后再启动任务");
        }

        task.Status = TaskStatus.Running;
        task.LastError = null;
        task.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(task)
            .UpdateColumns(x => new { x.Status, x.LastError, x.UpdatedAt })
            .ExecuteCommandAsync();
        return await MapAsync(task);
    }

    public async Task<TaskDto> StopAsync(long id, CancellationToken ct = default)
    {
        var task = await _db.Queryable<TaskEntity>().FirstAsync(x => x.Id == id)
                   ?? throw new BusinessException("任务不存在", "NOT_FOUND", 404);
        task.Status = TaskStatus.Stopped;
        task.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(task)
            .UpdateColumns(x => new { x.Status, x.UpdatedAt })
            .ExecuteCommandAsync();
        return await MapAsync(task);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var task = await _db.Queryable<TaskEntity>().FirstAsync(x => x.Id == id);
        if (task is null) return false;
        if (task.Status == TaskStatus.Running)
            throw new BusinessException("任务正在运行，请先停止后再删除");

        var positions = await _db.Queryable<PositionEntity>()
            .Where(x => x.TaskId == id && x.Status == PositionStatus.Open)
            .ToListAsync();
        if (positions.Count > 0)
        {
            if (task.Mode == TradingMode.Live)
                throw new BusinessException("该实盘任务仍有未平仓持仓，请先在欧易平台手动平仓后再删除");

            var price = await _market.GetLatestPriceAsync(task.InstId, ct);
            foreach (var position in positions)
            {
                if (price <= 0)
                    price = position.LastPrice > 0 ? position.LastPrice : position.AvgEntryPrice;
                var (order, error) = await _paper.CloseAsync(
                    task.Id, task.Name, position, price, task.SlippagePct,
                    "任务删除前自动平仓（模拟盘）", ct);
                if (order is null)
                    throw new BusinessException($"自动平仓失败，任务未删除：{error}");
                _logger.LogInformation("删除任务 {TaskId} 前自动平仓 {PositionId}，价格 {Price}", id, position.Id, price);
            }
        }

        await _db.Deleteable<PositionEntity>().Where(x => x.TaskId == id).ExecuteCommandAsync();
        await _db.Deleteable<OrderEntity>().Where(x => x.TaskId == id).ExecuteCommandAsync();
        await _db.Deleteable<TaskEntity>().Where(x => x.Id == id).ExecuteCommandAsync();
        return true;
    }

    private async Task<TaskDto> MapAsync(TaskEntity task)
    {
        var position = await _db.Queryable<PositionEntity>()
            .FirstAsync(x => x.TaskId == task.Id && x.Status == PositionStatus.Open);

        JsonElement? paramsElement = null;
        if (!string.IsNullOrWhiteSpace(task.ParamsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(task.ParamsJson);
                paramsElement = document.RootElement.Clone();
            }
            catch
            {
                paramsElement = null;
            }
        }

        TaskRiskDto risk;
        try
        {
            risk = JsonSerializer.Deserialize<TaskRiskDto>(task.RiskJson, QtsJson.Options) ?? new TaskRiskDto();
        }
        catch
        {
            risk = new TaskRiskDto();
        }

        return new TaskDto
        {
            Id = task.Id,
            Name = task.Name,
            StrategyCode = task.StrategyCode,
            StrategyName = _strategies.Get(task.StrategyCode)?.Name ?? task.StrategyCode,
            InstId = task.InstId,
            Bar = task.Bar,
            Mode = task.Mode,
            Status = task.Status,
            AllocatedCapital = task.AllocatedCapital,
            Params = paramsElement,
            Risk = risk,
            Leverage = task.Leverage,
            SlippagePct = task.SlippagePct,
            TotalPnl = task.TotalPnl,
            LastSignal = task.LastSignal,
            LastRunAt = task.LastRunAt,
            LastError = task.LastError,
            PositionQty = position?.Quantity ?? 0m,
            PositionAvgPrice = position?.AvgEntryPrice ?? 0m,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
    }

    private async Task<ValidatedTask> ValidateAsync(TaskCreateRequest request, CancellationToken ct)
    {
        var strategy = _strategies.Get(request.StrategyCode?.Trim() ?? "")
                       ?? throw new BusinessException($"策略不存在：{request.StrategyCode}");

        string bar;
        try
        {
            bar = MarketBars.Normalize(request.Bar);
        }
        catch (BusinessException ex)
        {
            throw new BusinessException(ex.Message);
        }

        var instId = (request.InstId ?? "").Trim().ToUpperInvariant();
        if (!_market.GetInstruments().Any(x => x.InstId == instId))
            throw new BusinessException($"不支持的交易对：{request.InstId}");

        if (request.AllocatedCapital < 1m || request.AllocatedCapital > 100_000_000m)
            throw new BusinessException("分配资金必须在 1 到 1 亿 USDT 之间");

        var risk = request.Risk ?? new TaskRiskDto();
        risk.StopLossPct = Math.Clamp(risk.StopLossPct, 0m, 50m);
        risk.TakeProfitPct = Math.Clamp(risk.TakeProfitPct, 0m, 200m);
        risk.MaxPositionPct = Math.Clamp(risk.MaxPositionPct, 1m, 100m);

        var leverage = Math.Clamp(request.Leverage, 1, 20);
        var slippagePct = Math.Clamp(request.SlippagePct, 0m, 5m);

        if (request.Mode == TradingMode.Paper)
            throw new BusinessException("纸面交易已关闭，请使用实盘 API 账户模式");

        if (request.Mode == TradingMode.Live)
        {
            var config = await _okxConfig.GetAsync(ct);
            if (!config.HasCredentials)
                throw new BusinessException("创建实盘任务前，请先在设置页配置欧易 API 凭据");
            if (!config.LiveTradingEnabled)
                throw new BusinessException("实盘交易开关未开启，无法创建实盘任务");
        }

        var name = string.IsNullOrWhiteSpace(request.Name)
            ? $"{strategy.Name} · {instId}"
            : request.Name.Trim();
        if (name.Length > 120) name = name[..120];

        return new ValidatedTask
        {
            Name = name,
            StrategyCode = strategy.Code,
            InstId = instId,
            Bar = bar,
            Mode = request.Mode,
            AllocatedCapital = request.AllocatedCapital,
            ParamsJson = JsonSerializer.Serialize(request.Params ?? new Dictionary<string, JsonElement>(), QtsJson.Options),
            RiskJson = JsonSerializer.Serialize(risk, QtsJson.Options),
            Leverage = leverage,
            SlippagePct = slippagePct
        };
    }

    private sealed class ValidatedTask
    {
        public string Name { get; init; } = "";
        public string StrategyCode { get; init; } = "";
        public string InstId { get; init; } = "";
        public string Bar { get; init; } = "";
        public TradingMode Mode { get; init; }
        public decimal AllocatedCapital { get; init; }
        public string ParamsJson { get; init; } = "{}";
        public string RiskJson { get; init; } = "{}";
        public int Leverage { get; init; } = 1;
        public decimal SlippagePct { get; init; } = 0.05m;
    }
}
