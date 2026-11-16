using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace FocusApp.Contracts;

public sealed partial class NamedPipeIpcClient
{
    [SupportedOSPlatform("windows")]
    private async Task<NamedPipeClientStream> ConnectWindowsPipeAsync(CancellationToken cancellationToken)
    {
        const int openExisting = 3;
        const int overlapped = 0x40000000;
        const int securitySqosPresent = 0x00100000;
        const int securityIdentification = 0x00010000;
        const int fileNotFound = 2;
        const int pipeBusy = 231;
        // Generic write also requests permission to create server instances. Request client rights only.
        const PipeAccessRights clientRights = PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize;
        var path = $@"\\.\pipe\{_pipeName}";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = CreateFile(path, (int)clientRights, 0, IntPtr.Zero, openExisting,
                overlapped | securitySqosPresent | securityIdentification, IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                try { return new NamedPipeClientStream(PipeDirection.InOut, isAsync: true, isConnected: true, handle); }
                catch { handle.Dispose(); throw; }
            }

            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error is not (fileNotFound or pipeBusy))
                throw new IOException("无法打开 FocusApp 命名管道。", new Win32Exception(error));

            await Task.Delay(50, cancellationToken);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafePipeHandle CreateFile(string fileName, int desiredAccess, int shareMode,
        IntPtr securityAttributes, int creationDisposition, int flagsAndAttributes, IntPtr templateFile);
}
