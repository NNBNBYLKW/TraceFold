using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public sealed class BatchMetadataService(
    IBatchMetadataStore store,
    IInboxActivityNotifier notifier) : IBatchMetadataService
{
    public Task<BatchMetadataEditorDto?> PrepareAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken) =>
        store.PrepareAsync(selection, cancellationToken);

    public async Task<BatchMetadataResult> ApplyAsync(
        BatchMetadataCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        BatchMetadataCommand validated;
        try
        {
            validated = BatchMetadataCommand.Create(
                command.Selection,
                command.Mode,
                command.Authors,
                command.Series,
                command.Tags);
        }
        catch (ArgumentException)
        {
            return Failure(
                command.Selection?.Count ?? 0,
                "batch.metadata.selection_invalid");
        }

        BatchMetadataWrite write;
        try
        {
            write = new BatchMetadataWrite(
                validated.Selection,
                validated.Mode,
                validated.Authors.Operation,
                Convert(validated.Authors.Values),
                validated.Series.Operation,
                validated.Series.Value is null
                    ? null
                    : Convert(validated.Series.Value),
                validated.Tags.Operation,
                Convert(validated.Tags.Values));
        }
        catch (ArgumentException)
        {
            return Failure(
                validated.Selection.Count,
                "batch.metadata.taxonomy_invalid");
        }

        BatchMetadataResult result = await store.ApplyAsync(
            write, cancellationToken);
        if (result.Succeeded && result.ChangedCount > 0)
        {
            notifier.Publish(InboxActivityKind.ItemChanged);
        }

        return result;
    }

    private static BatchMetadataResult Failure(int affectedCount, string errorCode) =>
        new(false, 0, 0, affectedCount, errorCode);

    private static IReadOnlyList<BatchTaxonomyToken> Convert(
        IReadOnlyList<BatchTaxonomyReference> values)
    {
        var seen = new HashSet<TokenKey>();
        var tokens = new List<BatchTaxonomyToken>(values.Count);
        foreach (BatchTaxonomyReference value in values)
        {
            BatchTaxonomyToken token = Convert(value);
            var key = new TokenKey(
                token.Kind,
                token.ExistingId,
                token.NewName?.Normalized);
            if (seen.Add(key))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static BatchTaxonomyToken Convert(BatchTaxonomyReference value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ExistingId.HasValue)
        {
            if (value.ExistingId == Guid.Empty || value.NewName is not null)
            {
                throw new ArgumentException("The taxonomy reference is invalid.");
            }

            return new BatchTaxonomyToken(value.Kind, value.ExistingId, null);
        }

        return new BatchTaxonomyToken(
            value.Kind,
            null,
            TaxonomyName.Create(value.NewName!));
    }

    private readonly record struct TokenKey(
        TaxonomyKind Kind,
        Guid? ExistingId,
        string? NormalizedName);
}
