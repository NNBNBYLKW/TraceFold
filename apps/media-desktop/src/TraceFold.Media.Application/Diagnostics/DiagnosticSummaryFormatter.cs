namespace TraceFold.Media.Application.Diagnostics;

public sealed class DiagnosticSummaryFormatter : IDiagnosticSummaryFormatter
{
    public string Format(
        SystemDiagnosticsSnapshot snapshot,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(timeZone);

        DateTimeOffset localCompleted = TimeZoneInfo.ConvertTime(
            snapshot.CompletedAtUtc,
            timeZone);
        var lines = new List<string>
        {
            "TraceFold 系统诊断摘要",
            $"运行标识：{snapshot.RunId:D}",
            $"检查时间：{localCompleted:yyyy-MM-dd HH:mm:ss zzz}",
            $"总体状态：{SeverityText(snapshot.OverallSeverity)}"
        };

        foreach (SystemDiagnosticItem item in snapshot.Items
                     .OrderBy(item => item.Category)
                     .ThenByDescending(item => item.Severity)
                     .ThenBy(item => item.Order)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            lines.Add(string.Empty);
            lines.Add(
                $"[{CategoryText(item.Category)}] {item.Id} {item.Title}：{SeverityText(item.Severity)}");
            if (item.ErrorCode is not null)
            {
                lines.Add($"错误代码：{item.ErrorCode}");
            }

            foreach (SystemDiagnosticFact fact in item.Facts
                         .Where(fact => fact.Sensitivity
                             == SystemDiagnosticFactSensitivity.Shareable)
                         .OrderBy(fact => fact.Order)
                         .ThenBy(fact => fact.Key, StringComparer.Ordinal))
            {
                lines.Add($"{fact.Label}：{fact.Value}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string SeverityText(SystemDiagnosticSeverity severity) =>
        severity switch
        {
            SystemDiagnosticSeverity.Healthy => "状态正常",
            SystemDiagnosticSeverity.Attention => "需要注意",
            SystemDiagnosticSeverity.Unavailable => "部分功能不可用",
            _ => throw new ArgumentOutOfRangeException(nameof(severity))
        };

    private static string CategoryText(SystemDiagnosticCategory category) =>
        category switch
        {
            SystemDiagnosticCategory.FoldersAndStorage => "目录与磁盘",
            SystemDiagnosticCategory.Database => "数据库",
            SystemDiagnosticCategory.ProcessingTools => "前置处理工具",
            SystemDiagnosticCategory.BackgroundWork => "后台状态",
            SystemDiagnosticCategory.RuntimeAndLogs => "日志与运行环境",
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
}
