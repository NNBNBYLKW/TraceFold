using System.Diagnostics;
using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Services;

public sealed class WindowsRecoveryRevealService(
    IRecoveryLocationResolver resolver,
    IProcessStarter processStarter) : IRecoveryRevealService
{
    public async Task<bool> RevealAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        RecoveryLocationDto? location = await resolver.ResolveAsync(
            inboxItemId,
            cancellationToken);
        if (location is null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/select,");
        startInfo.ArgumentList.Add(location.TrustedFullPath);
        processStarter.Start(startInfo);
        return true;
    }
}
