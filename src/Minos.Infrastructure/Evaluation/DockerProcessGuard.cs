using System.Diagnostics;

namespace Minos.Infrastructure.Evaluation;

// Reserve one non-root process before untrusted code can exhaust the PID budget.
// Its shell builtin can terminate that UID's descendants without creating another process.
internal sealed class DockerProcessGuard(Process process) : IDisposable
{
    private bool signalled;

    public static async Task<DockerProcessGuard> StartAsync(string container, string user)
    {
        var process = DockerCommand.Start([
            "exec", "-i", "--user", user, container, "/bin/sh", "-c",
            // With no submitted processes left, kill(-1) sees only the protected root
            // supervisor and returns EPERM. That is the normal successful-exit case.
            "printf 'ready\\n'; IFS= read -r command; kill -KILL -1 2>/dev/null || :"
        ], redirectInput: true);
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
            if (ready != "ready") throw new IOException("The process guard could not start.");
            return new(process);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public async Task TerminateAsync()
    {
        if (signalled) return;
        if (process.HasExited) throw new IOException("The process guard exited unexpectedly.");
        signalled = true;
        await process.StandardInput.WriteLineAsync("stop");
        await process.StandardInput.FlushAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (process.ExitCode is not (0 or 137)) throw new IOException("The process guard could not terminate submitted processes.");
    }

    public void Dispose()
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        process.Dispose();
    }
}
