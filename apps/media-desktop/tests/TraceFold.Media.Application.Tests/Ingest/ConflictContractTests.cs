using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Storage;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class ConflictContractTests
{
    [Fact]
    public void Replace_plan_requires_a_target_snapshot()
    {
        Assert.Throws<ArgumentException>(() =>
            IngestExecutionPlan.ReplaceRegistered(
                Guid.NewGuid(),
                Guid.NewGuid(),
                replacementMetadataVersion: 0,
                target: null!));
    }

    [Fact]
    public void Conflict_result_keeps_case_and_operation_ids_distinct()
    {
        Guid caseId = Guid.NewGuid();
        var result = new IngestResult(
            IngestOutcome.ConflictBlocked,
            null,
            null,
            caseId,
            "ingest.conflict");

        Assert.Null(result.OperationId);
        Assert.Null(result.AssetId);
        Assert.Equal(caseId, result.ConflictCaseId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("folder/photo.jpg")]
    [InlineData("../photo.jpg")]
    [InlineData("T:\\library\\photo.jpg")]
    public void Create_plan_rejects_blank_or_unsafe_filenames(string filename)
    {
        Assert.Throws<ArgumentException>(() =>
            IngestExecutionPlan.Create(filename));
    }

    [Fact]
    public void Keep_both_requires_a_persisted_case_id()
    {
        Assert.Throws<ArgumentException>(() =>
            IngestExecutionPlan.KeepBoth(Guid.Empty, "photo.jpg"));
    }

    [Fact]
    public void Registered_replace_requires_an_asset_id_and_nonnegative_version()
    {
        LibraryFileSnapshot target = Target("photo.jpg");

        Assert.Throws<ArgumentException>(() =>
            IngestExecutionPlan.ReplaceRegistered(
                Guid.NewGuid(), Guid.Empty, 0, target));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IngestExecutionPlan.ReplaceRegistered(
                Guid.NewGuid(), Guid.NewGuid(), -1, target));
    }

    [Fact]
    public void Replacement_plan_rejects_an_unsafe_target_relative_path()
    {
        LibraryFileSnapshot target = Target("folder/photo.jpg");

        Assert.Throws<ArgumentException>(() =>
            IngestExecutionPlan.ReplaceUnregistered(Guid.NewGuid(), target));
    }

    [Fact]
    public void Replacement_factories_encode_registered_and_unmanaged_identity()
    {
        Guid caseId = Guid.NewGuid();
        Guid assetId = Guid.NewGuid();
        LibraryFileSnapshot target = Target("photo.jpg");

        IngestExecutionPlan registered = IngestExecutionPlan.ReplaceRegistered(
            caseId, assetId, 7, target);
        IngestExecutionPlan unmanaged = IngestExecutionPlan.ReplaceUnregistered(
            caseId, target);

        Assert.Equal(IngestDisposition.ReplaceRegistered, registered.Disposition);
        Assert.Equal(assetId, registered.ReplacementAssetId);
        Assert.Equal(7, registered.ReplacementMetadataVersion);
        Assert.Equal(target, registered.Target);
        Assert.Equal(IngestDisposition.ReplaceUnregistered, unmanaged.Disposition);
        Assert.Null(unmanaged.ReplacementAssetId);
        Assert.Null(unmanaged.ReplacementMetadataVersion);
        Assert.Equal(target, unmanaged.Target);
    }

    private static LibraryFileSnapshot Target(string filename) =>
        new(
            $"T:\\library\\{filename}",
            filename,
            3,
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero),
            new string('A', 64));
}
