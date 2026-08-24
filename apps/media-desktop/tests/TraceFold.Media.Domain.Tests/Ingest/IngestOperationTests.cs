using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Domain.Tests.Ingest;

public sealed class IngestOperationTests
{
    [Fact]
    public void Ingest_operation_rejects_skipping_durable_stages()
    {
        var operation = new IngestOperation(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(
            () => operation.AdvanceTo(IngestOperationStage.DatabaseCommitted));
    }

    [Fact]
    public void Ingest_operation_advances_through_the_durable_happy_path()
    {
        var operation = new IngestOperation(Guid.NewGuid(), Guid.NewGuid());

        operation.AdvanceTo(IngestOperationStage.Staged);
        operation.AdvanceTo(IngestOperationStage.FileCommitted);
        operation.AdvanceTo(IngestOperationStage.DatabaseCommitted);
        operation.AdvanceTo(IngestOperationStage.Completed);

        Assert.Equal(IngestOperationStage.Completed, operation.Stage);
    }

    [Fact]
    public void Recovery_block_remembers_the_last_durable_stage()
    {
        var operation = new IngestOperation(Guid.NewGuid(), Guid.NewGuid());
        operation.AdvanceTo(IngestOperationStage.Staged);

        operation.BlockRecovery();

        Assert.Equal(IngestOperationStage.RecoveryBlocked, operation.Stage);
        Assert.Equal(IngestOperationStage.Staged, operation.BlockedFromStage);
    }

    [Fact]
    public void Recovery_resume_restores_the_last_durable_stage_once()
    {
        var operation = new IngestOperation(Guid.NewGuid(), Guid.NewGuid());
        operation.AdvanceTo(IngestOperationStage.Staged);
        operation.BlockRecovery();

        operation.ResumeRecovery();

        Assert.Equal(IngestOperationStage.Staged, operation.Stage);
        Assert.Null(operation.BlockedFromStage);
        Assert.Throws<InvalidOperationException>(operation.ResumeRecovery);
    }

    [Theory]
    [InlineData(IngestOperationStage.Completed)]
    [InlineData(IngestOperationStage.Failed)]
    public void Terminal_operation_cannot_be_recovery_blocked(IngestOperationStage terminalStage)
    {
        var operation = new IngestOperation(Guid.NewGuid(), Guid.NewGuid());
        operation.AdvanceTo(IngestOperationStage.Staged);
        operation.AdvanceTo(IngestOperationStage.FileCommitted);
        operation.AdvanceTo(IngestOperationStage.DatabaseCommitted);

        if (terminalStage == IngestOperationStage.Failed)
        {
            operation.AdvanceTo(IngestOperationStage.Failed);
        }
        else
        {
            operation.AdvanceTo(IngestOperationStage.Completed);
        }

        Assert.Throws<InvalidOperationException>(operation.BlockRecovery);
    }
}
