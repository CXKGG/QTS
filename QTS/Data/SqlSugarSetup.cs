using SqlSugar;
using QTS.Domain;

namespace QTS.Data;

public static class SqlSugarSetup
{
    public static SqlSugarScope Create(string connectionString)
    {
        return new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            MoreSettings = new ConnMoreSettings
            {
                IsAutoRemoveDataCache = true
            }
        });
    }

    public static void InitSchema(ISqlSugarClient db)
    {
        db.CodeFirst.InitTables(
            typeof(UserEntity),
            typeof(StrategyEntity),
            typeof(KlineEntity),
            typeof(BacktestEntity),
            typeof(BacktestEquityEntity),
            typeof(BacktestTradeEntity),
            typeof(TaskEntity),
            typeof(PositionEntity),
            typeof(OrderEntity),
            typeof(PaperAccountEntity),
            typeof(AccountSnapshotEntity),
            typeof(AppSettingEntity),
            typeof(AuditLogEntity));
    }
}
