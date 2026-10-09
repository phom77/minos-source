using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Minos.Infrastructure.Evaluation;

// A pidfd refers to this process, even after exit/PID reuse. SIGINT lets Isolate clean up and report evidence.
internal sealed class LinuxProcessSignal : IDisposable
{
    private readonly SafeFileHandle? handle;

    internal LinuxProcessSignal(int processId)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The pinned native runner requires x86-64 Linux.");
        var descriptor = Open(434, processId, 0); // pidfd_open, Linux x86-64
        if (descriptor < 0)
        {
            if (Marshal.GetLastPInvokeError() != 3) throw new IOException("Could not retain the sandbox manager process.");
            return; // Already exited; buffered output may still need draining.
        }
        handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
    }

    internal void Interrupt()
    {
        if (handle is null) return;
        var result = Send(424, handle, 2, IntPtr.Zero, 0); // pidfd_send_signal(SIGINT)
        if (result < 0 && Marshal.GetLastPInvokeError() != 3)
            throw new IOException("Could not interrupt the sandbox manager.");
    }

    public void Dispose() => handle?.Dispose();

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long Open(long number, int processId, uint flags);
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long Send(long number, SafeFileHandle descriptor, int signal, IntPtr info, uint flags);
}
