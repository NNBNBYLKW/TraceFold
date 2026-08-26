using TraceFold.Media.Application.Library;
using Xunit;

namespace TraceFold.Media.Application.Tests.Library;

public sealed class LibrarySortPreferenceServiceTests
{
    [Fact]
    public async Task Invalid_persisted_sort_falls_back_to_created_descending()
    {
        var store = new StubSortStore(
            new PersistedLibrarySortPreference("unknown", false));
        var service = new LibrarySortPreferenceService(store);

        LibrarySortPreference result = await service.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(LibrarySortPreference.Default, result);
    }

    private sealed class StubSortStore(PersistedLibrarySortPreference persisted)
        : ILibrarySortPreferenceStore
    {
        public Task<PersistedLibrarySortPreference> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(persisted);

        public Task<string?> SaveAsync(
            LibrarySortPreference preference,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}
