using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using QTS.Contracts;
using QTS.Domain;
using QTS.Services;

namespace QTS.Controllers;

[ApiController]
[Route("api")]
public sealed class OrdersController : ControllerBase
{
    private readonly ISqlSugarClient _db;

    public OrdersController(ISqlSugarClient db)
    {
        _db = db;
    }

    [HttpGet("orders")]
    public async Task<ApiResponse<PagedResult<OrderDto>>> Orders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] long? taskId = null,
        [FromQuery] string? mode = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Queryable<OrderEntity>();
        if (taskId is > 0) query = query.Where(o => o.TaskId == taskId.Value);
        if (Enum.TryParse<TradingMode>(mode, true, out var parsedMode)) query = query.Where(o => o.Mode == parsedMode);
        if (Enum.TryParse<OrderStatus>(status, true, out var parsedStatus)) query = query.Where(o => o.Status == parsedStatus);

        RefAsync<int> total = 0;
        var rows = await query.OrderBy(o => o.Id, OrderByType.Desc).ToPageListAsync(page, pageSize, total, ct);
        var items = rows.Select(Map).ToList();
        return ApiResponse<PagedResult<OrderDto>>.Ok(new PagedResult<OrderDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("positions")]
    public async Task<ApiResponse<List<PositionDto>>> Positions(
        [FromQuery] string status = "open",
        CancellationToken ct = default)
    {
        var query = _db.Queryable<PositionEntity>();
        if (string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
            query = query.Where(p => p.Status == PositionStatus.Open);
        else if (string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase))
            query = query.Where(p => p.Status == PositionStatus.Closed);

        var rows = await query.OrderBy(p => p.Id, OrderByType.Desc).ToListAsync(ct);
        if (rows.Count == 0) return ApiResponse<List<PositionDto>>.Ok([]);

        var taskIds = rows.Select(p => p.TaskId).Distinct().ToList();
        var tasks = await _db.Queryable<TaskEntity>()
            .Where(t => taskIds.Contains(t.Id))
            .ToListAsync(ct);
        var taskNames = tasks.ToDictionary(t => t.Id, t => t.Name);

        var items = rows.Select(p => new PositionDto
        {
            Id = p.Id,
            TaskId = p.TaskId,
            TaskName = p.TaskId == 0
                ? "手动交易"
                : taskNames.TryGetValue(p.TaskId, out var name) ? name : "",
            InstId = p.InstId,
            Mode = p.Mode,
            Side = string.IsNullOrWhiteSpace(p.Side) ? "long" : p.Side,
            Quantity = p.Quantity,
            AvgEntryPrice = p.AvgEntryPrice,
            LastPrice = p.LastPrice,
            UnrealizedPnl = p.UnrealizedPnl,
            UnrealizedPnlPct = p.AvgEntryPrice > 0
                ? Math.Round(
                    (string.Equals(p.Side, "short", StringComparison.OrdinalIgnoreCase)
                        ? p.AvgEntryPrice - p.LastPrice
                        : p.LastPrice - p.AvgEntryPrice) / p.AvgEntryPrice * 100m, 4)
                : 0m,
            Margin = p.Margin,
            Leverage = p.Leverage,
            StopLoss = p.StopLoss,
            TakeProfit = p.TakeProfit,
            OpenedAt = p.OpenedAt
        }).ToList();

        return ApiResponse<List<PositionDto>>.Ok(items);
    }

    private static OrderDto Map(OrderEntity o) => new()
    {
        Id = o.Id,
        TaskId = o.TaskId,
        TaskName = o.TaskName,
        InstId = o.InstId,
        Mode = o.Mode,
        Side = o.Side,
        OrderType = o.OrderType,
        Price = o.Price,
        Quantity = o.Quantity,
        FilledQuantity = o.FilledQuantity,
        AvgFillPrice = o.AvgFillPrice,
        Fee = o.Fee,
        Leverage = o.Leverage,
        Status = o.Status,
        OkxOrderId = o.OkxOrderId,
        Error = o.Error,
        Reason = o.Reason,
        CreatedAt = o.CreatedAt,
        FilledAt = o.FilledAt
    };
}
