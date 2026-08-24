using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public enum IngestRecoveryAction
{
    ResumeExecution,
    RestoreBackup,
    CommitDatabase,
    Cleanup,
    AbandonForRetry,
    Block
}

public sealed record IngestRecoveryEvidence(
    IngestOperationStage Stage,
    IngestDisposition Disposition,
    ObservedFileIdentity Source,
    ObservedFileIdentity Staged,
    ObservedFileIdentity Target,
    ObservedFileIdentity Backup,
    IngestBackupState BackupState,
    bool DatabaseHasNewVersion)
{
    public bool RequiresBackup => Disposition is
        IngestDisposition.ReplaceRegistered or
        IngestDisposition.ReplaceUnregistered;
}

public static class IngestRecoveryDecision
{
    public static IngestRecoveryAction Decide(IngestRecoveryEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence switch
        {
            {
                DatabaseHasNewVersion: true,
                RequiresBackup: false,
                Target.Kind: FileIdentityKind.ExpectedNew
            } => IngestRecoveryAction.Cleanup,
            {
                DatabaseHasNewVersion: true,
                RequiresBackup: true,
                Target.Kind: FileIdentityKind.ExpectedNew,
                BackupState: IngestBackupState.Created,
                Backup.Kind: FileIdentityKind.ExpectedBackup
            } => IngestRecoveryAction.Cleanup,
            {
                DatabaseHasNewVersion: true,
                RequiresBackup: true,
                Target.Kind: FileIdentityKind.ExpectedNew,
                BackupState: IngestBackupState.Deleted,
                Backup.Kind: FileIdentityKind.Absent
            } => IngestRecoveryAction.Cleanup,
            {
                Stage: IngestOperationStage.FileCommitted,
                RequiresBackup: false,
                DatabaseHasNewVersion: false,
                Target.Kind: FileIdentityKind.ExpectedNew
            } => IngestRecoveryAction.CommitDatabase,
            {
                Stage: IngestOperationStage.FileCommitted,
                RequiresBackup: true,
                DatabaseHasNewVersion: false,
                Target.Kind: FileIdentityKind.ExpectedNew,
                BackupState: IngestBackupState.Created,
                Backup.Kind: FileIdentityKind.ExpectedBackup
            } => IngestRecoveryAction.CommitDatabase,
            {
                RequiresBackup: true,
                Backup.Kind: FileIdentityKind.ExpectedBackup,
                Target.Kind: FileIdentityKind.Absent,
                Staged.Kind: FileIdentityKind.ExpectedStaged
            } => IngestRecoveryAction.ResumeExecution,
            {
                RequiresBackup: true,
                Source.Kind: FileIdentityKind.ExpectedSource,
                Staged.Kind: FileIdentityKind.ExpectedStaged,
                Target.Kind: FileIdentityKind.ExpectedOld,
                Backup.Kind: FileIdentityKind.Absent
            } => IngestRecoveryAction.ResumeExecution,
            {
                RequiresBackup: true,
                Backup.Kind: FileIdentityKind.ExpectedBackup,
                Target.Kind: FileIdentityKind.Absent
            } => IngestRecoveryAction.RestoreBackup,
            {
                RequiresBackup: false,
                DatabaseHasNewVersion: false,
                Staged.Kind: FileIdentityKind.ExpectedStaged,
                Target.Kind: FileIdentityKind.Absent
            } => IngestRecoveryAction.ResumeExecution,
            {
                Stage: IngestOperationStage.Created,
                Disposition: IngestDisposition.Create,
                DatabaseHasNewVersion: false,
                Source.Kind: FileIdentityKind.ExpectedSource,
                Staged.Kind: FileIdentityKind.Absent,
                Target.Kind: FileIdentityKind.Absent,
                Backup.Kind: FileIdentityKind.Absent
            } => IngestRecoveryAction.ResumeExecution,
            {
                Stage: IngestOperationStage.Created,
                Disposition: IngestDisposition.KeepBoth,
                DatabaseHasNewVersion: false,
                Source.Kind: FileIdentityKind.ExpectedSource,
                Staged.Kind: FileIdentityKind.Absent,
                Backup.Kind: FileIdentityKind.Absent
            } => IngestRecoveryAction.ResumeExecution,
            {
                Stage: IngestOperationStage.Created,
                RequiresBackup: true,
                DatabaseHasNewVersion: false,
                Source.Kind: FileIdentityKind.ExpectedSource,
                Staged.Kind: FileIdentityKind.Absent,
                Target.Kind: FileIdentityKind.ExpectedOld,
                Backup.Kind: FileIdentityKind.Absent
            } => IngestRecoveryAction.ResumeExecution,
            {
                Stage: IngestOperationStage.Created,
                DatabaseHasNewVersion: false,
                Source.Kind: FileIdentityKind.Absent,
                Staged.Kind: FileIdentityKind.Absent,
                Target.Kind: FileIdentityKind.Absent
                    or FileIdentityKind.ExpectedOld,
                Backup.Kind: FileIdentityKind.Absent
            } => IngestRecoveryAction.AbandonForRetry,
            _ => IngestRecoveryAction.Block
        };
    }
}
