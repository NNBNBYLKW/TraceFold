namespace TraceFold.Media.Application.Library;

public sealed record NamingPresetDto(
    Guid Id,
    string Name,
    NamingDefinition? Definition,
    bool CanExecute,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NamingPresetSaveRequest(
    Guid? Id,
    string Name,
    NamingDefinition Definition,
    bool OverwriteExisting);

public sealed record NamingPresetSaveResult(
    bool Succeeded,
    NamingPresetDto? Preset,
    string? ErrorCode);

public sealed record NamingPresetDeleteResult(
    bool Succeeded,
    string? ErrorCode);

public interface INamingPresetStore
{
    Task<IReadOnlyList<NamingPresetDto>> ListAsync(
        CancellationToken cancellationToken);

    Task<NamingPresetDto?> LoadAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<NamingPresetSaveResult> SaveAsync(
        NamingPresetSaveRequest request,
        CancellationToken cancellationToken);

    Task<NamingPresetDeleteResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken);
}

public interface INamingPresetService
{
    Task<IReadOnlyList<NamingPresetDto>> ListAsync(
        CancellationToken cancellationToken);

    Task<NamingPresetDto?> LoadAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<NamingPresetSaveResult> SaveAsync(
        NamingPresetSaveRequest request,
        CancellationToken cancellationToken);

    Task<NamingPresetDeleteResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken);
}
