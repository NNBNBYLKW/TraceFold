using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Infrastructure.Persistence;

namespace TraceFold.Media.Infrastructure.Diagnostics;

public sealed record DatabaseDiagnosticRead(
    bool CanOpen,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> AvailableMigrations,
    IReadOnlyList<string> QuickCheckRows,
    string? DatabasePath);

public interface IDatabaseDiagnosticReader
{
    Task<DatabaseDiagnosticRead> ReadAsync(CancellationToken cancellationToken);
}

public sealed class EfDatabaseDiagnosticReader(
    IReadOnlyDiagnosticDbContextFactory contextFactory)
    : IDatabaseDiagnosticReader
{
    public async Task<DatabaseDiagnosticRead> ReadAsync(
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);

        string[] appliedMigrations =
            [.. await context.Database.GetAppliedMigrationsAsync(cancellationToken)];
        string[] availableMigrations = [.. context.Database.GetMigrations()];
        DbConnection connection = context.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check";
        var quickCheckRows = new List<string>();
        await using DbDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            quickCheckRows.Add(reader.GetString(0));
        }

        return new DatabaseDiagnosticRead(
            CanOpen: true,
            appliedMigrations,
            availableMigrations,
            quickCheckRows,
            string.IsNullOrWhiteSpace(connection.DataSource)
                ? null
                : connection.DataSource);
    }
}

public sealed class DatabaseDiagnosticProbe(
    IDatabaseDiagnosticReader reader) : ISystemDiagnosticProbe
{
    public SystemDiagnosticProbeDescriptor Descriptor { get; } =
        SystemDiagnosticProbeDescriptor.Create(
            "database",
            SystemDiagnosticCategory.Database,
            order: 0,
            failureSeverity: SystemDiagnosticSeverity.Unavailable,
            failureTitle: "数据库检查失败",
            failureConclusion: "无法完成数据库检查",
            failureImpact: "待入库、正式库和资料管理暂时不可用。",
            failureGuidance: "请重新检查；问题持续时查看本地日志。",
            timeoutErrorCode: "diagnostics.database.timeout");

    public async Task<IReadOnlyList<SystemDiagnosticItem>> CheckAsync(
        CancellationToken cancellationToken)
    {
        DatabaseDiagnosticRead observation;
        try
        {
            observation = await reader.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [CreateUnavailable(
                "diagnostics.database.unavailable",
                "数据库无法读取",
                "文件管理所需的资料和任务状态当前不可用。",
                "请重新检查；问题持续时打开日志目录进行排查。",
                facts: [])];
        }

        IReadOnlyList<SystemDiagnosticFact> facts = CreateFacts(observation);
        if (!observation.CanOpen)
        {
            return [CreateUnavailable(
                "diagnostics.database.unavailable",
                "数据库无法打开",
                "文件管理所需的资料和任务状态当前不可用。",
                "请确认程序数据目录可访问，然后重新检查。",
                facts)];
        }

        if (!observation.AppliedMigrations.SequenceEqual(
                observation.AvailableMigrations,
                StringComparer.Ordinal))
        {
            return [CreateUnavailable(
                "diagnostics.database.migration_mismatch",
                "数据库版本与当前程序不一致",
                "为避免错误读写，依赖数据库的文件管理功能不可用。",
                "请关闭程序并使用与该数据库匹配的正式版本；诊断不会自动迁移。",
                facts)];
        }

        if (observation.QuickCheckRows.Count != 1
            || !string.Equals(
                observation.QuickCheckRows[0],
                "ok",
                StringComparison.Ordinal))
        {
            return [CreateUnavailable(
                "diagnostics.database.quick_check_failed",
                "数据库完整性检查未通过",
                "继续读写可能造成资料或任务状态异常。",
                "请停止入库和资料修改，并保留本地日志进行人工排查。",
                facts)];
        }

        return [SystemDiagnosticItem.Create(
            "database.sqlite",
            SystemDiagnosticCategory.Database,
            SystemDiagnosticSeverity.Healthy,
            order: 0,
            title: "SQLite 数据库",
            conclusion: "数据库可读、版本一致且完整性检查正常",
            impact: null,
            guidance: null,
            errorCode: null,
            facts,
            actions: [])];
    }

    private static IReadOnlyList<SystemDiagnosticFact> CreateFacts(
        DatabaseDiagnosticRead observation)
    {
        var facts = new List<SystemDiagnosticFact>
        {
            Fact(
                "database.can_open",
                "可打开",
                observation.CanOpen ? "是" : "否",
                SystemDiagnosticFactSensitivity.Shareable,
                order: 0),
            Fact(
                "database.migration_count",
                "已应用迁移数",
                observation.AppliedMigrations.Count.ToString(
                    CultureInfo.InvariantCulture),
                SystemDiagnosticFactSensitivity.Shareable,
                order: 1),
            Fact(
                "database.available_migration_count",
                "程序迁移数",
                observation.AvailableMigrations.Count.ToString(
                    CultureInfo.InvariantCulture),
                SystemDiagnosticFactSensitivity.Shareable,
                order: 2),
            Fact(
                "database.migrations_match",
                "版本一致",
                observation.AppliedMigrations.SequenceEqual(
                    observation.AvailableMigrations,
                    StringComparer.Ordinal)
                    ? "是"
                    : "否",
                SystemDiagnosticFactSensitivity.Shareable,
                order: 3),
            Fact(
                "database.quick_check",
                "完整性检查",
                observation.QuickCheckRows.Count == 1
                    && string.Equals(
                        observation.QuickCheckRows[0],
                        "ok",
                        StringComparison.Ordinal)
                    ? "ok"
                    : "未通过",
                SystemDiagnosticFactSensitivity.Shareable,
                order: 4)
        };
        if (!string.IsNullOrWhiteSpace(observation.DatabasePath))
        {
            facts.Add(Fact(
                "database.path",
                "数据库路径",
                observation.DatabasePath,
                SystemDiagnosticFactSensitivity.LocalOnly,
                order: 5));
        }

        return facts;
    }

    private static SystemDiagnosticItem CreateUnavailable(
        string errorCode,
        string conclusion,
        string impact,
        string guidance,
        IReadOnlyList<SystemDiagnosticFact> facts) =>
        SystemDiagnosticItem.Create(
            "database.sqlite",
            SystemDiagnosticCategory.Database,
            SystemDiagnosticSeverity.Unavailable,
            order: 0,
            title: "SQLite 数据库",
            conclusion,
            impact,
            guidance,
            errorCode,
            facts,
            actions:
            [
                SystemDiagnosticAction.Create(
                    SystemDiagnosticActionKind.CopyErrorCode,
                    SystemDiagnosticActionTarget.None,
                    "复制错误代码")
            ]);

    private static SystemDiagnosticFact Fact(
        string key,
        string label,
        string value,
        SystemDiagnosticFactSensitivity sensitivity,
        int order) =>
        SystemDiagnosticFact.Create(key, label, value, sensitivity, order);
}
