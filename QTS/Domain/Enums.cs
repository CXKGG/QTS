namespace QTS.Domain;

/// <summary>交易模式：纸面模拟 / 欧易实盘。</summary>
public enum TradingMode
{
    Paper = 1,
    Live = 2
}

/// <summary>交易任务状态。</summary>
public enum TaskStatus
{
    Stopped = 1,
    Running = 2,
    Paused = 3,
    Error = 4
}

public enum OrderSide
{
    Buy = 1,
    Sell = 2
}

public enum OrderStatus
{
    Pending = 1,
    Submitted = 2,
    Filled = 3,
    Failed = 4,
    Canceled = 5
}

public enum BacktestStatus
{
    Running = 1,
    Completed = 2,
    Failed = 3
}

public enum PositionStatus
{
    Open = 1,
    Closed = 2
}
