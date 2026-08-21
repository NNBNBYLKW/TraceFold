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
}
