using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;

namespace TraceFold.Media.Desktop.Lifecycle;

public static class SingleInstanceCoordinator
{
    public static int RedirectToPrimary(
        AppInstance primary,
        AppActivationArguments activationArguments)
    {
        nint completed = CreateEvent(
            0,
            manualReset: true,
            initialState: false,
            name: null);
        if (completed == 0)
        {
            Debug.WriteLine("DesktopActivationRedirectEventCreationFailed");
            return 1;
        }

        Exception? redirectError = null;
        try
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await primary.RedirectActivationToAsync(activationArguments);
                }
                catch (Exception exception)
                {
                    redirectError = exception;
                }
                finally
                {
                    SetEvent(completed);
                }
            });

            uint waitResult = CoWaitForMultipleObjects(
                flags: 0,
                milliseconds: 30_000,
                handleCount: 1,
                handles: [completed],
                out _);
            if (waitResult == 0 && redirectError is null)
            {
                return 0;
            }

            Debug.WriteLine(
                $"DesktopActivationRedirectFailed WaitResult={waitResult} Error={redirectError}");
            return 1;
        }
        finally
        {
            CloseHandle(completed);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateEvent(
        nint eventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState,
        string? name);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(nint handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(
        uint flags,
        uint milliseconds,
        ulong handleCount,
        nint[] handles,
        out uint index);
}
