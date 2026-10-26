using System.Text.Json;
using Microsoft.Extensions.Logging;
using QTS.Domain;
using QTS.Infrastructure;
using QTS.Infrastructure.Auth;
using QTS.Services;
using SqlSugar;

namespace QTS.Data;

/// <summary>初始化数据库结构与最小种子数据：管理员、模拟账户、内置策略。</summary>
public static class DbInitializer
{
    /// <summary>
    /// 兼容旧库：为新增的杠杆/滑点/方向列补齐合理默认值。
    /// CodeFirst 新增列在 MySQL 中默认是 0，会让历史任务显示 0x/0% 滑点。
    /// </summary>
    public static void MigrateDefaults(ISqlSugarClient db, ILogger logger)
    {
        try
        {
            db.Ado.ExecuteCommand("UPDATE qts_user SET Role = 'user' WHERE Role IS NULL OR Role = ''");
            db.Ado.ExecuteCommand("UPDATE qts_user SET Role = 'admin' WHERE Username = 'admin'");
            var taskRows = db.Ado.ExecuteCommand(
                "UPDATE qts_task SET Leverage = 1 WHERE Leverage IS NULL OR Leverage < 1");
            db.Ado.ExecuteCommand(
                "UPDATE qts_task SET SlippagePct = 0.05 WHERE SlippagePct IS NULL OR SlippagePct <= 0");
            var posRows = db.Ado.ExecuteCommand(
                "UPDATE qts_position SET Leverage = 1 WHERE Leverage IS NULL OR Leverage < 1");
            var orderRows = db.Ado.ExecuteCommand(
                "UPDATE qts_order SET Leverage = 1 WHERE Leverage IS NULL OR Leverage < 1");
            var btRows = db.Ado.ExecuteCommand(
                "UPDATE qts_backtest SET Leverage = 1 WHERE Leverage IS NULL OR Leverage < 1");
            db.Ado.ExecuteCommand(
                "UPDATE qts_backtest_trade SET Side = 'long' WHERE Side IS NULL OR Side = ''");
            db.Ado.ExecuteCommand(
                "UPDATE qts_position SET CtVal = 1 WHERE CtVal IS NULL OR CtVal <= 0");
            db.Ado.ExecuteCommand(
                "UPDATE qts_order SET CtVal = 1 WHERE CtVal IS NULL OR CtVal <= 0");
            if (taskRows + posRows + orderRows + btRows > 0)
                logger.LogInformation(
                    "已补齐历史数据默认杠杆/方向：tasks {Tasks}，positions {Positions}，orders {Orders}，backtests {Backtests}",
                    taskRows, posRows, orderRows, btRows);
        }
        catch (Exception ex)
        {
            // 迁移失败不应阻断启动；新版数据库首次创建时不会走到这些表的历史数据。
            logger.LogWarning(ex, "历史数据默认值迁移失败（可忽略）");
        }
    }

    public static void Seed(ISqlSugarClient db, IStrategyRegistry registry, PasswordHasher hasher, ILogger logger)
    {
        var now = DateTime.UtcNow;

        if (!db.Queryable<UserEntity>().Any(x => x.Username == "admin"))
        {
            db.Insertable(new UserEntity
            {
                Username = "admin",
                PasswordHash = hasher.Hash("admin123"),
                DisplayName = "量化管理员",
                Role = "admin",
                CreatedAt = now
            }).ExecuteCommand();
            logger.LogInformation("已创建默认管理员 admin / admin123");
        }

        if (!db.Queryable<PaperAccountEntity>().Any(x => x.Id == 1))
        {
            db.Insertable(new PaperAccountEntity
            {
                Id = 1,
                InitialBalance = 100m,
                Balance = 100m,
                RealizedPnl = 0m,
                UpdatedAt = now
            }).ExecuteCommand();
            db.Insertable(new AccountSnapshotEntity
            {
                Mode = TradingMode.Paper,
                Equity = 100m,
                Available = 100m,
                PositionValue = 0m,
                UnrealizedPnl = 0m,
                RealizedPnl = 0m,
                CreatedAt = now
            }).ExecuteCommand();
            logger.LogInformation("已初始化模拟账户 100 USDT");
        }

        var seedOrder = 0;
        foreach (var strategy in registry.All)
        {
            seedOrder++;
            if (db.Queryable<StrategyEntity>().Any(x => x.Code == strategy.Code)) continue;
            db.Insertable(new StrategyEntity
            {
                Code = strategy.Code,
                Name = strategy.Name,
                Category = strategy.Category,
                Description = strategy.Description,
                ParamsSchemaJson = JsonSerializer.Serialize(strategy.ParamsSchema, QtsJson.Options),
                DefaultParamsJson = JsonSerializer.Serialize(strategy.DefaultParams, QtsJson.Options),
                Enabled = true,
                SortOrder = seedOrder,
                CreatedAt = now,
                Remark = "内置策略"
            }).ExecuteCommand();
            logger.LogInformation("已登记内置策略 {Code}", strategy.Code);
        }
    }
}
