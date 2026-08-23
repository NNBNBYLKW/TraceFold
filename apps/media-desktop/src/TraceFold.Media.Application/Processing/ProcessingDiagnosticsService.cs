namespace TraceFold.Media.Application.Processing;

public sealed class ProcessingDiagnosticsService(IMediaTool mediaTool)
    : IProcessingDiagnosticsService
{
    public async Task<ProcessingDiagnosticsDto> CheckAsync(
        CancellationToken cancellationToken)
    {
        MediaToolAvailability availability = await mediaTool.CheckAvailabilityAsync(
            cancellationToken);
        return new ProcessingDiagnosticsDto(
            availability.Available,
            availability.Version,
            availability.ErrorCode);
    }
}
