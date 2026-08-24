using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Infrastructure.Filesystem;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfRecoveryLocationResolver(
    IDbContextFactory<MediaDesktopDbContext> contextFactory,
    IDirectorySettingsStore settingsStore) : IRecoveryLocationResolver
{
    public async Task<RecoveryLocationDto?> ResolveAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        LibraryFolders? folders = await settingsStore.LoadAsync(cancellationToken);
        if (folders is null)
        {
            return null;
        }

        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow? operation = await context.IngestOperations
            .AsNoTracking()
            .Where(row => row.InboxItemId == inboxItemId)
            .OrderByDescending(row => row.ActiveInboxItemId.HasValue)
            .ThenByDescending(row => row.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (operation is null)
        {
            return null;
        }

        if (operation.Stage == IngestOperationStage.RecoveryBlocked
            && TryResolveBackup(folders, operation) is string backup)
        {
            return new RecoveryLocationDto(backup);
        }

        if (TryResolveTarget(folders, operation) is string target)
        {
            return new RecoveryLocationDto(target);
        }

        return TryResolveSource(folders, operation.SourcePath) is string source
            ? new RecoveryLocationDto(source)
            : null;
    }

    private static string? TryResolveBackup(
        LibraryFolders folders,
        IngestOperationRow operation)
    {
        if (string.IsNullOrWhiteSpace(operation.BackupRelativePath)
            || Path.IsPathRooted(operation.BackupRelativePath))
        {
            return null;
        }

        string expected = Path.Combine(
            folders.LibraryDirectory,
            ".tracefold",
            "recovery",
            operation.Id.ToString("N"),
            $"original{Path.GetExtension(operation.TargetFilename)}");
        string candidate = Path.Combine(
            folders.LibraryDirectory,
            operation.BackupRelativePath);
        return TryExistingGuarded(
            folders.LibraryDirectory,
            candidate,
            directChild: false,
            expected);
    }

    private static string? TryResolveTarget(
        LibraryFolders folders,
        IngestOperationRow operation) =>
        TryExistingGuarded(
            folders.LibraryDirectory,
            Path.Combine(folders.LibraryDirectory, operation.TargetFilename),
            directChild: true,
            expected: null);

    private static string? TryResolveSource(
        LibraryFolders folders,
        string sourcePath)
    {
        string? inbox = TryExistingGuarded(
            folders.InboxDirectory,
            sourcePath,
            directChild: true,
            expected: null);
        if (inbox is not null)
        {
            return inbox;
        }

        try
        {
            string processingRoot = ControlledPathGuard.RequireDescendant(
                folders.WorkDirectory,
                Path.Combine(folders.WorkDirectory, ".tracefold", "processing"));
            string output = ControlledPathGuard.RequireDescendant(
                processingRoot,
                sourcePath);
            string? jobDirectory = Path.GetDirectoryName(output);
            if (jobDirectory is null
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetDirectoryName(jobDirectory),
                    processingRoot)
                || !Guid.TryParseExact(
                    Path.GetFileName(jobDirectory),
                    "N",
                    out _))
            {
                return null;
            }

            return TryExistingGuarded(
                folders.WorkDirectory,
                output,
                directChild: false,
                expected: null);
        }
        catch (ControlledPathException)
        {
            return null;
        }
    }

    private static string? TryExistingGuarded(
        string root,
        string candidate,
        bool directChild,
        string? expected)
    {
        try
        {
            string guarded = directChild
                ? ControlledPathGuard.RequireDirectChild(root, candidate)
                : ControlledPathGuard.RequireDescendant(root, candidate);
            if (expected is not null
                && !StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetFullPath(expected),
                    guarded))
            {
                return null;
            }

            if (!File.Exists(guarded))
            {
                return null;
            }

            ControlledReparseGuard.RejectPath(
                root,
                guarded,
                includeCandidate: true);
            return guarded;
        }
        catch (Exception error) when (error is
            ControlledPathException or
            ArgumentException or
            NotSupportedException)
        {
            return null;
        }
    }
}
