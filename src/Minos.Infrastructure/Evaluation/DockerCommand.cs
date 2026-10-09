using System.Diagnostics;
using System.Text;

namespace Minos.Infrastructure.Evaluation;

internal static class DockerCommand
{
    public static Process Start(IEnumerable<string> arguments, bool redirectInput = false)
    {
        var info = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            UseShellExecute = false
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException("Could not start the Docker client.");
        return process;
    }

    public static async Task<byte[]> CaptureAsync(Stream stream, int limit, Action exceeded, CancellationToken token = default)
    {
        using var retained = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            var allowed = Math.Min(count, Math.Max(0, limit - (int)retained.Length));
            if (allowed > 0) retained.Write(buffer, 0, allowed);
            if (allowed < count) exceeded();
        }
        return retained.ToArray();
    }

    public static async Task<(int ExitCode, byte[] Output, string Error)> RunAsync(
        IEnumerable<string> arguments,
        int outputLimit = 16 * 1024,
        TimeSpan? timeout = null)
    {
        using var process = Start(arguments);
        var overflow = 0;
        var stdout = CaptureAsync(process.StandardOutput.BaseStream, outputLimit,
            () => Interlocked.Exchange(ref overflow, 1));
        var stderr = CaptureAsync(process.StandardError.BaseStream, 16 * 1024,
            () => Interlocked.Exchange(ref overflow, 1));
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        var output = await stdout;
        var error = Encoding.UTF8.GetString(await stderr);
        if (overflow != 0) throw new IOException("Trusted Docker command output exceeded its capture bound.");
        return (process.ExitCode, output, error);
    }
}
