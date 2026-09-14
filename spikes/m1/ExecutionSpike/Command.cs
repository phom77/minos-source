using System.Diagnostics;
using System.Text;

namespace Minos.ExecutionSpike;

internal static class Command
{
    public static Process Start(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException($"Could not start {executable}.");
        return process;
    }

    public static async Task<byte[]> CaptureAsync(Stream stream, int limit, Action exceeded)
    {
        using var retained = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer)) != 0)
        {
            var allowed = Math.Min(count, limit - (int)retained.Length);
            if (allowed > 0) retained.Write(buffer, 0, allowed);
            if (allowed < count) exceeded();
            // Continue draining, retaining only the bound, while the controller terminates the container.
        }
        return retained.ToArray();
    }

    public static async Task<(int Exit, byte[] Output, string Error)> RunAsync(string executable,
        IEnumerable<string> arguments, int outputLimit = 16384, TimeSpan? timeout = null)
    {
        using var process = Start(executable, arguments);
        var overflow = 0;
        var stdout = CaptureAsync(process.StandardOutput.BaseStream, outputLimit, () => Interlocked.Exchange(ref overflow, 1));
        var stderr = CaptureAsync(process.StandardError.BaseStream, 16384, () => Interlocked.Exchange(ref overflow, 1));
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        var output = await stdout;
        var error = Encoding.UTF8.GetString(await stderr);
        if (overflow != 0) throw new IOException("Trusted command output exceeded its capture bound.");
        return (process.ExitCode, output, error);
    }
}
