using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfNamingPresetStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory,
    TimeProvider? timeProvider = null) : INamingPresetStore
{
    private readonly TimeProvider _timeProvider =
        timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<NamingPresetDto>> ListAsync(
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        NamingPresetRow[] rows = await context.NamingPresets
            .AsNoTracking()
            .OrderBy(row => row.DisplayName)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(rows.Select(ToDto).ToArray());
    }

    public async Task<NamingPresetDto?> LoadAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        NamingPresetRow? row = await context.NamingPresets
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return row is null ? null : ToDto(row);
    }

    public async Task<NamingPresetSaveResult> SaveAsync(
        NamingPresetSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Definition is null)
        {
            return Failure("naming.preset.definition_invalid");
        }

        TaxonomyName name;
        try
        {
            name = TaxonomyName.Create(request.Name);
        }
        catch (ArgumentException)
        {
            return Failure("naming.preset.name_required");
        }

        if (name.Display.Length > 512)
        {
            return Failure("naming.preset.name_too_long");
        }

        string source;
        string rulesJson;
        try
        {
            source = NamingRuleSerializer.SerializeSource(
                request.Definition.Source);
            rulesJson = NamingRuleSerializer.SerializeRules(request.Definition);
        }
        catch (Exception error) when (error is ArgumentException
            or InvalidOperationException)
        {
            return Failure("naming.preset.definition_invalid");
        }

        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        NamingPresetRow? sameName = await context.NamingPresets
            .SingleOrDefaultAsync(
                row => row.NormalizedName == name.Normalized,
                cancellationToken);
        NamingPresetRow? sameId = request.Id.HasValue
            ? await context.NamingPresets.SingleOrDefaultAsync(
                row => row.Id == request.Id.Value,
                cancellationToken)
            : null;
        NamingPresetRow? target;
        if (sameName is not null && sameName.Id != request.Id)
        {
            if (!request.OverwriteExisting)
            {
                return Failure("naming.preset.name_conflict");
            }

            target = sameName;
        }
        else if (request.Id.HasValue)
        {
            if (sameId is null)
            {
                return Failure("naming.preset.not_found");
            }

            target = sameId;
        }
        else
        {
            target = null;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (target is null)
        {
            target = new NamingPresetRow
            {
                Id = Guid.NewGuid(),
                DisplayName = name.Display,
                NormalizedName = name.Normalized,
                SchemaVersion = request.Definition.SchemaVersion,
                Source = source,
                RulesJson = rulesJson,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            context.NamingPresets.Add(target);
        }
        else
        {
            target.DisplayName = name.Display;
            target.NormalizedName = name.Normalized;
            target.SchemaVersion = request.Definition.SchemaVersion;
            target.Source = source;
            target.RulesJson = rulesJson;
            target.UpdatedAtUtc = now;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (IsUniqueConstraint(error))
        {
            return Failure("naming.preset.name_conflict");
        }

        return new NamingPresetSaveResult(true, ToDto(target), null);
    }

    public async Task<NamingPresetDeleteResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        NamingPresetRow? row = await context.NamingPresets
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return new NamingPresetDeleteResult(
                false,
                "naming.preset.not_found");
        }

        context.NamingPresets.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
        return new NamingPresetDeleteResult(true, null);
    }

    private static NamingPresetDto ToDto(NamingPresetRow row)
    {
        NamingRuleDeserializationResult result =
            NamingRuleSerializer.Deserialize(
                row.SchemaVersion,
                row.Source,
                row.RulesJson);
        return new NamingPresetDto(
            row.Id,
            row.DisplayName,
            result.Definition,
            result.ErrorCode is null,
            result.ErrorCode,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
    }

    private static NamingPresetSaveResult Failure(string errorCode) =>
        new(false, null, errorCode);

    private static bool IsUniqueConstraint(DbUpdateException error) =>
        error.InnerException is SqliteException sqlite
        && sqlite.SqliteErrorCode == 19
        && sqlite.SqliteExtendedErrorCode == 2067;
}
