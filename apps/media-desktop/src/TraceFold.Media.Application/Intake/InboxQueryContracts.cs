using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;

namespace TraceFold.Media.Application.Intake;

public sealed record InboxQuery(string? Search, InboxItemState? State);

public sealed record InboxCardDto(
    Guid Id,
    string ActualFilename,
    string DisplayName,
    MediaKind MediaKind,
    InboxItemState State,
    string StatusText,
    bool CanIngest,
    ProcessingSummaryDto? Processing = null);

public sealed record ProcessingSummaryDto(
    Guid JobId,
    ProcessingJobStatus Status,
    string ProcessorDisplayName,
    string StepText,
    int ProgressPercent,
    string? ErrorCode,
    bool IsRetryable);

public static class ProcessingPresentationMapper
{
    public static string StepText(string? stepKey) => stepKey switch
    {
        "check_sources" => "检查源文件",
        "merge" => "合并音视频",
        "verify_output" => "验证输出",
        _ => "正在处理"
    };
}

public sealed record InboxSourceFileDto(
    string Filename,
    string Extension,
    long Size,
    bool IsPresent,
    bool IsPrimary);

public sealed record InboxDetailsDto(
    InboxCardDto Card,
    IReadOnlyList<InboxSourceFileDto> SourceFiles,
    PreviewDescriptor? Preview,
    MetadataDocument Metadata,
    InboxAction AllowedActions,
    string? ErrorCode,
    string? RequiredCapability,
    bool IsRetryable,
    ProcessingSummaryDto? Processing = null);

public interface IInboxQueryStore
{
    Task<IReadOnlyList<InboxCardDto>> SearchAsync(
        InboxQuery query,
        CancellationToken cancellationToken);

    Task<InboxDetailsDto?> GetDetailsAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task ClearMissingAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}

public interface IInboxQueryService
{
    Task<IReadOnlyList<InboxCardDto>> SearchAsync(
        InboxQuery query,
        CancellationToken cancellationToken);

    Task<InboxDetailsDto?> GetDetailsAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task ClearMissingAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}
