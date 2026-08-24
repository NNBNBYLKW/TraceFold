using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class IngestRecoveryDecisionTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Evidence_maps_to_one_safe_action(
        IngestOperationStage stage,
        IngestDisposition disposition,
        FileIdentityKind source,
        FileIdentityKind staged,
        FileIdentityKind target,
        FileIdentityKind backup,
        IngestBackupState backupState,
        bool databaseHasNewVersion,
        IngestRecoveryAction expected)
    {
        IngestRecoveryAction actual = IngestRecoveryDecision.Decide(
            Evidence(
                stage,
                disposition,
                source,
                staged,
                target,
                backup,
                backupState,
                databaseHasNewVersion));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(FileIdentityKind.Unknown, FileIdentityKind.Absent)]
    [InlineData(FileIdentityKind.ExpectedNew, FileIdentityKind.Unknown)]
    [InlineData(FileIdentityKind.ExpectedOld, FileIdentityKind.ExpectedBackup)]
    public void Unapproved_replacement_evidence_is_blocked(
        FileIdentityKind target,
        FileIdentityKind backup)
    {
        IngestRecoveryEvidence evidence = Evidence(
            IngestOperationStage.FileCommitted,
            IngestDisposition.ReplaceRegistered,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.ExpectedStaged,
            target,
            backup,
            IngestBackupState.Created,
            databaseHasNewVersion: false);

        Assert.Equal(
            IngestRecoveryAction.Block,
            IngestRecoveryDecision.Decide(evidence));
    }

    public static TheoryData<
        IngestOperationStage,
        IngestDisposition,
        FileIdentityKind,
        FileIdentityKind,
        FileIdentityKind,
        FileIdentityKind,
        IngestBackupState,
        bool,
        IngestRecoveryAction> Cases => new()
    {
        {
            IngestOperationStage.Created,
            IngestDisposition.Create,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            IngestBackupState.NotRequired,
            false,
            IngestRecoveryAction.ResumeExecution
        },
        {
            IngestOperationStage.Staged,
            IngestDisposition.KeepBoth,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.ExpectedStaged,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            IngestBackupState.NotRequired,
            false,
            IngestRecoveryAction.ResumeExecution
        },
        {
            IngestOperationStage.Staged,
            IngestDisposition.ReplaceRegistered,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.ExpectedStaged,
            FileIdentityKind.ExpectedOld,
            FileIdentityKind.Absent,
            IngestBackupState.Pending,
            false,
            IngestRecoveryAction.ResumeExecution
        },
        {
            IngestOperationStage.Staged,
            IngestDisposition.ReplaceRegistered,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.ExpectedStaged,
            FileIdentityKind.Absent,
            FileIdentityKind.ExpectedBackup,
            IngestBackupState.Created,
            false,
            IngestRecoveryAction.ResumeExecution
        },
        {
            IngestOperationStage.Staged,
            IngestDisposition.ReplaceRegistered,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            FileIdentityKind.ExpectedBackup,
            IngestBackupState.Created,
            false,
            IngestRecoveryAction.RestoreBackup
        },
        {
            IngestOperationStage.FileCommitted,
            IngestDisposition.ReplaceRegistered,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.ExpectedStaged,
            FileIdentityKind.ExpectedNew,
            FileIdentityKind.ExpectedBackup,
            IngestBackupState.Created,
            false,
            IngestRecoveryAction.CommitDatabase
        },
        {
            IngestOperationStage.DatabaseCommitted,
            IngestDisposition.ReplaceRegistered,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            FileIdentityKind.ExpectedNew,
            FileIdentityKind.ExpectedBackup,
            IngestBackupState.Created,
            true,
            IngestRecoveryAction.Cleanup
        },
        {
            IngestOperationStage.FileCommitted,
            IngestDisposition.ReplaceRegistered,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.ExpectedStaged,
            FileIdentityKind.Unknown,
            FileIdentityKind.ExpectedBackup,
            IngestBackupState.Created,
            false,
            IngestRecoveryAction.Block
        },
        {
            IngestOperationStage.FileCommitted,
            IngestDisposition.ReplaceRegistered,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.ExpectedStaged,
            FileIdentityKind.ExpectedNew,
            FileIdentityKind.Unknown,
            IngestBackupState.Created,
            false,
            IngestRecoveryAction.Block
        },
        {
            IngestOperationStage.Staged,
            IngestDisposition.ReplaceUnregistered,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.ExpectedStaged,
            FileIdentityKind.ExpectedOld,
            FileIdentityKind.Absent,
            IngestBackupState.Pending,
            false,
            IngestRecoveryAction.ResumeExecution
        },
        {
            IngestOperationStage.FileCommitted,
            IngestDisposition.Create,
            FileIdentityKind.ExpectedSource,
            FileIdentityKind.ExpectedStaged,
            FileIdentityKind.Unknown,
            FileIdentityKind.Absent,
            IngestBackupState.NotRequired,
            false,
            IngestRecoveryAction.Block
        },
        {
            IngestOperationStage.DatabaseCommitted,
            IngestDisposition.ReplaceRegistered,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            FileIdentityKind.ExpectedNew,
            FileIdentityKind.Absent,
            IngestBackupState.Deleted,
            true,
            IngestRecoveryAction.Cleanup
        },
        {
            IngestOperationStage.Created,
            IngestDisposition.Create,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            FileIdentityKind.Absent,
            IngestBackupState.NotRequired,
            false,
            IngestRecoveryAction.AbandonForRetry
        }
    };

    private static IngestRecoveryEvidence Evidence(
        IngestOperationStage stage,
        IngestDisposition disposition,
        FileIdentityKind source,
        FileIdentityKind staged,
        FileIdentityKind target,
        FileIdentityKind backup,
        IngestBackupState backupState,
        bool databaseHasNewVersion) =>
        new(
            stage,
            disposition,
            Identity(source),
            Identity(staged),
            Identity(target),
            Identity(backup),
            backupState,
            databaseHasNewVersion);

    private static ObservedFileIdentity Identity(FileIdentityKind kind) =>
        new(kind, null, null, null);
}
