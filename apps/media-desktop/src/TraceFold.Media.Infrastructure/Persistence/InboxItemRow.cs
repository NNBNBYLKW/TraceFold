using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class InboxItemRow
{
    public Guid Id { get; set; }

    public InboxItemState State { get; set; }

    public MediaKind MediaKind { get; set; }

    public required string ActualFilename { get; set; }

    public required string GroupingRule { get; set; }

    public string? RequiredCapability { get; set; }

    public string? ErrorCode { get; set; }

    public bool IsRetryable { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<SourceFileRow> SourceFiles { get; } = [];

    public MetadataDraftRow? MetadataDraft { get; set; }

    public ICollection<InboxDraftAuthorRow> DraftAuthors { get; } = [];

    public ICollection<InboxDraftTagRow> DraftTags { get; } = [];

    public ICollection<IngestOperationRow> IngestOperations { get; } = [];

    public ICollection<ProcessingJobRow> ProcessingJobs { get; } = [];
}
