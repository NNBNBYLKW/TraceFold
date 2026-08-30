using System.Globalization;
using TraceFold.Media.Application.Diagnostics;

namespace TraceFold.Media.Infrastructure.Diagnostics;

public sealed class BackgroundWorkDiagnosticProbe(
    IBackgroundWorkSummaryStore store) : ISystemDiagnosticProbe
{
    public SystemDiagnosticProbeDescriptor Descriptor { get; } =
        SystemDiagnosticProbeDescriptor.Create(
            "background",
            SystemDiagnosticCategory.BackgroundWork,
            order: 0,
            failureSeverity: SystemDiagnosticSeverity.Attention,
            failureTitle: "后台状态检查失败",
            failureConclusion: "无法读取后台任务汇总",
            failureImpact: "当前无法判断是否存在中断、失败或等待恢复的工作。",
            failureGuidance: "请重新检查；问题持续时查看本地日志。",
            timeoutErrorCode: "diagnostics.probe.timeout");

    public async Task<IReadOnlyList<SystemDiagnosticItem>> CheckAsync(
        CancellationToken cancellationToken)
    {
        BackgroundWorkSummary summary;
        try
        {
            summary = await store.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [CreateAttention(
                "diagnostics.background.query_failed",
                "无法读取后台任务汇总",
                "当前无法判断是否存在中断、失败或等待恢复的工作。",
                "请重新检查；问题持续时查看本地日志。",
                facts: [])];
        }

        IReadOnlyList<SystemDiagnosticFact> facts = CreateFacts(summary);
        bool needsAttention = summary.InterruptedCount > 0
            || summary.FailedCount > 0
            || summary.RecoveryPendingCount > 0;
        if (needsAttention)
        {
            return [CreateAttention(
                "diagnostics.background.attention",
                "存在需要人工留意的后台工作",
                "部分前置处理、入库恢复或清理工作尚未正常结束。",
                "请在对应功能页查看状态；诊断本身不会重试或修改任务。",
                facts)];
        }

        return [SystemDiagnosticItem.Create(
            "background.summary",
            SystemDiagnosticCategory.BackgroundWork,
            SystemDiagnosticSeverity.Healthy,
            order: 0,
            title: "后台状态",
            conclusion: "未发现中断、失败或等待恢复的后台工作",
            impact: null,
            guidance: null,
            errorCode: null,
            facts,
            actions: [])];
    }

    private static IReadOnlyList<SystemDiagnosticFact> CreateFacts(
        BackgroundWorkSummary summary) =>
    [
        CountFact(
            "background.processing.queued",
            "排队处理",
            summary.QueuedCount,
            order: 0),
        CountFact(
            "background.processing.running",
            "正在处理",
            summary.RunningCount,
            order: 1),
        CountFact(
            "background.processing.interrupted",
            "处理已中断",
            summary.InterruptedCount,
            order: 2),
        CountFact(
            "background.processing.failed",
            "处理失败",
            summary.FailedCount,
            order: 3),
        CountFact(
            "background.ingest.recovery_pending",
            "等待恢复或清理",
            summary.RecoveryPendingCount,
            order: 4)
    ];

    private static SystemDiagnosticItem CreateAttention(
        string errorCode,
        string conclusion,
        string impact,
        string guidance,
        IReadOnlyList<SystemDiagnosticFact> facts) =>
        SystemDiagnosticItem.Create(
            "background.summary",
            SystemDiagnosticCategory.BackgroundWork,
            SystemDiagnosticSeverity.Attention,
            order: 0,
            title: "后台状态",
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

    private static SystemDiagnosticFact CountFact(
        string key,
        string label,
        int value,
        int order) =>
        SystemDiagnosticFact.Create(
            key,
            label,
            value.ToString(CultureInfo.InvariantCulture),
            SystemDiagnosticFactSensitivity.Shareable,
            order);
}
