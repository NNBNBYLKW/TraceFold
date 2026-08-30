using TraceFold.Media.Application.Diagnostics;
using Xunit;

namespace TraceFold.Media.Application.Tests.Diagnostics;

public sealed class SystemDiagnosticsContractTests
{
    [Fact]
    public void Production_options_apply_the_approved_timeout_budget()
    {
        SystemDiagnosticsOptions options = SystemDiagnosticsOptions.Production;

        Assert.Equal(TimeSpan.FromSeconds(5), options.FastProbeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), options.DatabaseProbeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), options.BackgroundProbeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(20), options.WholeRunTimeout);
        Assert.Equal(
            TimeSpan.FromSeconds(15),
            options.GetProbeTimeout(SystemDiagnosticCategory.Database));
        Assert.Equal(
            TimeSpan.FromSeconds(15),
            options.GetProbeTimeout(SystemDiagnosticCategory.BackgroundWork));
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            options.GetProbeTimeout(SystemDiagnosticCategory.ProcessingTools));
    }

    [Theory]
    [InlineData(
        SystemDiagnosticActionKind.NavigateToFolderSettings,
        SystemDiagnosticActionTarget.None)]
    [InlineData(
        SystemDiagnosticActionKind.OpenRegisteredLogFolder,
        SystemDiagnosticActionTarget.InboxDirectory)]
    [InlineData(
        SystemDiagnosticActionKind.CopyErrorCode,
        SystemDiagnosticActionTarget.WorkDirectory)]
    public void Action_rejects_kind_target_combinations_that_could_carry_an_arbitrary_target(
        SystemDiagnosticActionKind kind,
        SystemDiagnosticActionTarget target)
    {
        Assert.Throws<ArgumentException>(() =>
            SystemDiagnosticAction.Create(kind, target, "执行"));
    }

    [Theory]
    [InlineData(SystemDiagnosticActionTarget.InboxDirectory)]
    [InlineData(SystemDiagnosticActionTarget.LibraryDirectory)]
    [InlineData(SystemDiagnosticActionTarget.WorkDirectory)]
    public void Folder_navigation_accepts_only_a_registered_folder_target(
        SystemDiagnosticActionTarget target)
    {
        SystemDiagnosticAction action = SystemDiagnosticAction.Create(
            SystemDiagnosticActionKind.NavigateToFolderSettings,
            target,
            "转到文件夹设置");

        Assert.Equal(target, action.Target);
    }

    [Fact]
    public void Item_rejects_duplicate_fact_keys()
    {
        SystemDiagnosticFact first = Fact("folder.path", "路径一");
        SystemDiagnosticFact duplicate = Fact("folder.path", "路径二");

        Assert.Throws<ArgumentException>(() => SystemDiagnosticItem.Create(
            "folders.inbox",
            SystemDiagnosticCategory.FoldersAndStorage,
            SystemDiagnosticSeverity.Healthy,
            order: 0,
            title: "待入库目录",
            conclusion: "可用",
            impact: null,
            guidance: null,
            errorCode: null,
            facts: [first, duplicate],
            actions: []));
    }

    [Fact]
    public void Nonhealthy_item_requires_impact_guidance_and_stable_error_code()
    {
        Assert.Throws<ArgumentException>(() => SystemDiagnosticItem.Create(
            "database.status",
            SystemDiagnosticCategory.Database,
            SystemDiagnosticSeverity.Unavailable,
            order: 0,
            title: "数据库",
            conclusion: "不可用",
            impact: null,
            guidance: null,
            errorCode: null,
            facts: [],
            actions: []));
    }

    [Theory]
    [InlineData("", "label", "value")]
    [InlineData("Not Stable", "label", "value")]
    [InlineData("fact.key", "", "value")]
    [InlineData("fact.key", "label", "")]
    public void Fact_rejects_unstable_or_blank_fields(
        string key,
        string label,
        string value)
    {
        Assert.Throws<ArgumentException>(() => SystemDiagnosticFact.Create(
            key,
            label,
            value,
            SystemDiagnosticFactSensitivity.Shareable,
            order: 0));
    }

    [Fact]
    public void Background_summary_rejects_negative_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BackgroundWorkSummary(
                QueuedCount: 0,
                RunningCount: 0,
                InterruptedCount: -1,
                FailedCount: 0,
                RecoveryPendingCount: 0));
    }

    [Fact]
    public void Snapshot_rejects_an_overall_severity_that_hides_an_issue()
    {
        SystemDiagnosticItem item = SystemDiagnosticItem.Create(
            "database.status",
            SystemDiagnosticCategory.Database,
            SystemDiagnosticSeverity.Unavailable,
            order: 0,
            title: "数据库",
            conclusion: "不可用",
            impact: "无法安全读取资料。",
            guidance: "请查看本地日志。",
            errorCode: "diagnostics.database.unavailable",
            facts: [],
            actions: []);

        Assert.Throws<ArgumentException>(() => SystemDiagnosticsSnapshot.Create(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
            DateTimeOffset.Parse("2026-08-30T08:00:01Z"),
            SystemDiagnosticSeverity.Healthy,
            [item]));
    }

    private static SystemDiagnosticFact Fact(string key, string value) =>
        SystemDiagnosticFact.Create(
            key,
            "事实",
            value,
            SystemDiagnosticFactSensitivity.LocalOnly,
            order: 0);
}
