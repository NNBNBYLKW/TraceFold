namespace TraceFold.Media.Application.Library;

public sealed record LibrarySortPreference(
    LibrarySortField Field,
    LibrarySortDirection Direction)
{
    public static LibrarySortPreference Default { get; } =
        new(LibrarySortField.CreatedAt, LibrarySortDirection.Descending);
}

public sealed record PersistedLibrarySortPreference(
    string Field,
    bool Descending);

public interface ILibrarySortPreferenceStore
{
    Task<PersistedLibrarySortPreference> LoadAsync(
        CancellationToken cancellationToken);

    Task<string?> SaveAsync(
        LibrarySortPreference preference,
        CancellationToken cancellationToken);
}

public interface ILibrarySortPreferenceService
{
    Task<LibrarySortPreference> LoadAsync(CancellationToken cancellationToken);

    Task<string?> SaveAsync(
        LibrarySortPreference preference,
        CancellationToken cancellationToken);
}

public sealed class LibrarySortPreferenceService(ILibrarySortPreferenceStore store)
    : ILibrarySortPreferenceService
{
    public async Task<LibrarySortPreference> LoadAsync(
        CancellationToken cancellationToken)
    {
        PersistedLibrarySortPreference persisted =
            await store.LoadAsync(cancellationToken);

        if (!TryParseField(persisted.Field, out LibrarySortField field))
        {
            return LibrarySortPreference.Default;
        }

        return new LibrarySortPreference(
            field,
            persisted.Descending
                ? LibrarySortDirection.Descending
                : LibrarySortDirection.Ascending);
    }

    public Task<string?> SaveAsync(
        LibrarySortPreference preference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preference);
        if (!Enum.IsDefined(preference.Field))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preference),
                preference.Field,
                "The library sort field is invalid.");
        }

        if (!Enum.IsDefined(preference.Direction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preference),
                preference.Direction,
                "The library sort direction is invalid.");
        }

        return store.SaveAsync(preference, cancellationToken);
    }

    private static bool TryParseField(
        string? field,
        out LibrarySortField result)
    {
        switch (field)
        {
            case "created_at":
                result = LibrarySortField.CreatedAt;
                return true;
            case "display_name":
                result = LibrarySortField.DisplayName;
                return true;
            case "actual_filename":
                result = LibrarySortField.ActualFilename;
                return true;
            case "media_kind":
                result = LibrarySortField.MediaKind;
                return true;
            default:
                result = default;
                return false;
        }
    }
}
