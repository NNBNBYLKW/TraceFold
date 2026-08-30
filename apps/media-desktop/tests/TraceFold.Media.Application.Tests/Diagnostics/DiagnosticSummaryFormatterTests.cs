using TraceFold.Media.Application.Diagnostics;
using Xunit;

namespace TraceFold.Media.Application.Tests.Diagnostics;

public sealed class DiagnosticSummaryFormatterTests
{
    private static readonly Guid RunId =
        Guid.Parse("0f05e949-c2bc-4182-9acb-99f96c16aa31");

    [Fact]
    public void Format_includes_only_shareable_facts_in_deterministic_order()
    {
        SystemDiagnosticsSnapshot snapshot = Snapshot([
            HealthyItem(
                "runtime.status",
                SystemDiagnosticCategory.RuntimeAndLogs,
                order: 20,
                facts:
                [
                    LocalFact("runtime.logs.path", "日志目录", @"C:\Users\PRIVATE\secret"),
                    SharedFact("runtime.dotnet", ".NET", ".NET 10.0.8", order: 1)
                ]),
            HealthyItem(
                "folders.inbox",
                SystemDiagnosticCategory.FoldersAndStorage,
                order: 10,
                facts:
                [
                    SharedFact("folders.inbox.free", "剩余空间", "120 GiB", order: 0)
                ])
        ]);
        var formatter = new DiagnosticSummaryFormatter();

        string first = formatter.Format(snapshot, TimeZoneInfo.Utc);
        string second = formatter.Format(snapshot, TimeZoneInfo.Utc);

        Assert.Equal(first, second);
        Assert.Contains($"运行标识：{RunId:D}", first, StringComparison.Ordinal);
        Assert.Contains("总体状态：状态正常", first, StringComparison.Ordinal);
        Assert.Contains("120 GiB", first, StringComparison.Ordinal);
        Assert.Contains(".NET 10.0.8", first, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", first, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            first.IndexOf("folders.inbox", StringComparison.Ordinal)
            < first.IndexOf("runtime.status", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_converts_the_completion_time_to_the_requested_time_zone()
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone(
            "Test-China",
            TimeSpan.FromHours(8),
            "Test-China",
            "Test-China");
        SystemDiagnosticsSnapshot snapshot = Snapshot([
            HealthyItem(
                "database.status",
                SystemDiagnosticCategory.Database,
                order: 0,
                facts: [])
        ]);

        string result = new DiagnosticSummaryFormatter().Format(snapshot, zone);

        Assert.Contains(
            "检查时间：2026-08-30 16:00:01 +08:00",
            result,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_includes_stable_error_codes_but_not_private_issue_details()
    {
        SystemDiagnosticItem item = SystemDiagnosticItem.Create(
            "database.status",
            SystemDiagnosticCategory.Database,
            SystemDiagnosticSeverity.Unavailable,
            order: 0,
            title: "数据库",
            conclusion: "数据库不可用",
            impact: "无法读取文件资料。",
            guidance: "请查看日志。",
            errorCode: "diagnostics.database.unavailable",
            facts:
            [
                LocalFact(
                    "database.exception",
                    "内部错误",
                    "PRIVATE_EXCEPTION_MESSAGE")
            ],
            actions: []);

        string result = new DiagnosticSummaryFormatter().Format(
            Snapshot([item]),
            TimeZoneInfo.Utc);

        Assert.Contains(
            "错误代码：diagnostics.database.unavailable",
            result,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PRIVATE_EXCEPTION_MESSAGE",
            result,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PRIVATE_USERNAME")]
    [InlineData("PRIVATE_COMPUTER")]
    [InlineData(@"\\PRIVATE_HOST\media\movie.mp4")]
    [InlineData(@"T:\Private\media-desktop.db")]
    [InlineData("tracefold-private.log")]
    [InlineData("private-movie.webp")]
    [InlineData("PRIVATE_AUTHOR_SERIES_TAG")]
    [InlineData("A4B83F28EB6D83CC47B3714278E187B913FAEA5C856FAA1FC521B24AF75BA320")]
    [InlineData("2fd685e7-0852-458a-802f-029ca89210b4")]
    [InlineData(".tracefold-write-probe-private.tmp")]
    public void Format_excludes_every_local_only_privacy_sentinel(string sentinel)
    {
        SystemDiagnosticsSnapshot snapshot = Snapshot([
            HealthyItem(
                "folders.inbox",
                SystemDiagnosticCategory.FoldersAndStorage,
                order: 0,
                facts:
                [
                    LocalFact("folders.inbox.private", "本机信息", sentinel),
                    SharedFact("folders.inbox.logical", "逻辑目录", "待入库目录", order: 1)
                ])
        ]);

        string result = new DiagnosticSummaryFormatter().Format(
            snapshot,
            TimeZoneInfo.Utc);

        Assert.DoesNotContain(sentinel, result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("逻辑目录：待入库目录", result, StringComparison.Ordinal);
    }

    private static SystemDiagnosticsSnapshot Snapshot(
        IReadOnlyList<SystemDiagnosticItem> items) =>
        SystemDiagnosticsSnapshot.Create(
            RunId,
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
            DateTimeOffset.Parse("2026-08-30T08:00:01Z"),
            items.Max(item => item.Severity),
            items);

    private static SystemDiagnosticItem HealthyItem(
        string id,
        SystemDiagnosticCategory category,
        int order,
        IReadOnlyList<SystemDiagnosticFact> facts) =>
        SystemDiagnosticItem.Create(
            id,
            category,
            SystemDiagnosticSeverity.Healthy,
            order,
            title: id,
            conclusion: "正常",
            impact: null,
            guidance: null,
            errorCode: null,
            facts,
            actions: []);

    private static SystemDiagnosticFact SharedFact(
        string key,
        string label,
        string value,
        int order) =>
        SystemDiagnosticFact.Create(
            key,
            label,
            value,
            SystemDiagnosticFactSensitivity.Shareable,
            order);

    private static SystemDiagnosticFact LocalFact(
        string key,
        string label,
        string value) =>
        SystemDiagnosticFact.Create(
            key,
            label,
            value,
            SystemDiagnosticFactSensitivity.LocalOnly,
            order: 0);
}
