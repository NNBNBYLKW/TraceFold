using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;
using Xunit;

namespace TraceFold.Media.Application.Tests.Intake;

public sealed class InboxQueryServiceTests
{
    [Fact]
    public async Task Search_matches_actual_or_display_name_case_insensitively()
    {
        var store = new InMemoryInboxQueryStore(
            Card("旅行记录.MP4", "杭州夜景"),
            Card("cat.jpg", null));
        var service = new InboxQueryService(store);

        IReadOnlyList<InboxCardDto> result = await service.SearchAsync(
            new InboxQuery("杭州", null),
            TestContext.Current.CancellationToken);

        Assert.Equal("旅行记录.MP4", Assert.Single(result).ActualFilename);
    }

    [Fact]
    public async Task Search_applies_state_filter_and_display_fallback()
    {
        var store = new InMemoryInboxQueryStore(
            Card("ready.jpg", null, InboxItemState.ReadyToTag),
            Card("missing.mp4", null, InboxItemState.SourceMissing));
        var service = new InboxQueryService(store);

        IReadOnlyList<InboxCardDto> result = await service.SearchAsync(
            new InboxQuery(null, InboxItemState.ReadyToTag),
            TestContext.Current.CancellationToken);

        InboxCardDto card = Assert.Single(result);
        Assert.Equal("ready.jpg", card.DisplayName);
        Assert.True(card.CanIngest);
    }

    [Theory]
    [InlineData("check_sources", "检查源文件")]
    [InlineData("merge", "合并音视频")]
    [InlineData("verify_output", "验证输出")]
    [InlineData("future_step", "正在处理")]
    public void Processing_summary_uses_one_stable_step_text_mapper(
        string stepKey,
        string expected)
    {
        var summary = new ProcessingSummaryDto(
            Guid.NewGuid(),
            ProcessingJobStatus.Running,
            "合并视频和音频",
            ProcessingPresentationMapper.StepText(stepKey),
            63,
            null,
            false);

        Assert.Equal(expected, summary.StepText);
    }

    private static InboxCardDto Card(
        string actualFilename,
        string? displayName,
        InboxItemState state = InboxItemState.ReadyToTag) =>
        new(
            Guid.NewGuid(),
            actualFilename,
            displayName ?? actualFilename,
            MediaKind.Image,
            state,
            state.ToString(),
            state == InboxItemState.ReadyToTag);

    private sealed class InMemoryInboxQueryStore(params InboxCardDto[] cards)
        : IInboxQueryStore
    {
        public Task<IReadOnlyList<InboxCardDto>> SearchAsync(
            InboxQuery query,
            CancellationToken cancellationToken)
        {
            IEnumerable<InboxCardDto> result = cards;
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                result = result.Where(card =>
                    card.ActualFilename.Contains(
                        query.Search,
                        StringComparison.OrdinalIgnoreCase)
                    || card.DisplayName.Contains(
                        query.Search,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (query.State.HasValue)
            {
                result = result.Where(card => card.State == query.State.Value);
            }

            return Task.FromResult<IReadOnlyList<InboxCardDto>>(result.ToArray());
        }

        public Task<InboxDetailsDto?> GetDetailsAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<InboxDetailsDto?>(null);

        public Task ClearMissingAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
