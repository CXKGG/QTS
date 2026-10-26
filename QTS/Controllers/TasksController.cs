using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;
using QTS.Infrastructure;
using QTS.Services;

namespace QTS.Controllers;

[ApiController]
[Route("api/tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly ITaskService _tasks;
    private readonly ITradingEngine _engine;

    public TasksController(ITaskService tasks, ITradingEngine engine)
    {
        _tasks = tasks;
        _engine = engine;
    }

    [HttpGet]
    public async Task<ApiResponse<PagedResult<TaskDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var result = await _tasks.ListAsync(page, pageSize, status, ct);
        return ApiResponse<PagedResult<TaskDto>>.Ok(result);
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResponse<TaskDto>> Get(long id, CancellationToken ct)
    {
        var task = await _tasks.GetAsync(id, ct);
        if (task is null) throw new BusinessException("交易任务不存在", "TASK_NOT_FOUND", StatusCodes.Status404NotFound);
        return ApiResponse<TaskDto>.Ok(task);
    }

    [HttpPost]
    public async Task<ApiResponse<TaskDto>> Create([FromBody] TaskCreateRequest request, CancellationToken ct)
    {
        var task = await _tasks.CreateAsync(request, User.CurrentUserId(), ct);
        return ApiResponse<TaskDto>.Ok(task, task.Status == Domain.TaskStatus.Running ? "任务已创建并启动" : "任务已创建");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResponse<TaskDto>> Update(long id, [FromBody] TaskCreateRequest request, CancellationToken ct)
    {
        var task = await _tasks.UpdateAsync(id, request, ct);
        return ApiResponse<TaskDto>.Ok(task);
    }

    [HttpPost("{id:long}/start")]
    public async Task<ApiResponse<TaskDto>> Start(long id, CancellationToken ct)
    {
        var task = await _tasks.StartAsync(id, ct);
        return ApiResponse<TaskDto>.Ok(task, "任务已启动");
    }

    [HttpPost("{id:long}/stop")]
    public async Task<ApiResponse<TaskDto>> Stop(long id, CancellationToken ct)
    {
        var task = await _tasks.StopAsync(id, ct);
        return ApiResponse<TaskDto>.Ok(task, "任务已停止");
    }

    [HttpPost("{id:long}/close-position")]
    public async Task<ApiResponse<bool>> ClosePosition(long id, CancellationToken ct)
    {
        var ok = await _engine.CloseTaskPositionAsync(id, ct);
        return ApiResponse<bool>.Ok(ok, "持仓已平仓");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResponse<bool>> Delete(long id, CancellationToken ct)
    {
        var ok = await _tasks.DeleteAsync(id, ct);
        if (!ok) throw new BusinessException("交易任务不存在", "TASK_NOT_FOUND", StatusCodes.Status404NotFound);
        return ApiResponse<bool>.Ok(true, "任务已删除");
    }
}
