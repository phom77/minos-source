using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Text.Json;

namespace Minos.Infrastructure.Evaluation;

// Inspect the sandboxed process itself: an inherited output pipe must not extend its wall time.
internal sealed class DockerExecutionSession : IDisposable
{
    private readonly HttpClient engine;
    private readonly string id;
    private readonly HttpResponseMessage attachment;
    private readonly CancellationTokenSource captureCancellation = new();
    private bool disposed;

    private DockerExecutionSession(HttpClient engine, string id, HttpResponseMessage attachment)
    {
        this.engine = engine;
        this.id = id;
        this.attachment = attachment;
    }

    public static async Task<DockerExecutionSession> StartAsync(
        HttpClient engine, string container, string user, string[] command, CancellationToken token)
    {
        using var created = await engine.PostAsJsonAsync($"containers/{container}/exec", new
        {
            AttachStdout = true, AttachStderr = true, Tty = false, User = user, Cmd = command
        }, token);
        created.EnsureSuccessStatusCode();
        using var definition = JsonDocument.Parse(await created.Content.ReadAsStringAsync(token));
        var id = definition.RootElement.GetProperty("Id").GetString()!;
        using var start = new HttpRequestMessage(HttpMethod.Post, $"exec/{id}/start")
        {
            Content = JsonContent.Create(new { Detach = false, Tty = false })
        };
        var attachment = await engine.SendAsync(start, HttpCompletionOption.ResponseHeadersRead, token);
        try
        {
            attachment.EnsureSuccessStatusCode();
            return new(engine, id, attachment);
        }
        catch { attachment.Dispose(); throw; }
    }

    public async Task<(bool Running, int ExitCode, long ProcessId)> InspectAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            while (true)
            {
                using var response = await engine.GetAsync($"exec/{id}/json", deadline.Token);
                response.EnsureSuccessStatusCode();
                using var state = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
                var running = state.RootElement.GetProperty("Running").GetBoolean();
                var exit = state.RootElement.GetProperty("ExitCode");
                // The daemon sends attachment headers before starting exec. Not-running
                // with a null exit code is transitional, never proof of successful completion.
                if (!running && exit.ValueKind == JsonValueKind.Null)
                {
                    await Task.Delay(25, deadline.Token);
                    continue;
                }
                return (running, running ? 0 : exit.GetInt32(), state.RootElement.GetProperty("Pid").GetInt64());
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("Docker did not publish bounded exec state evidence.");
        }
    }

    public async Task<(byte[] Stdout, byte[] Stderr)> CaptureAsync(int stdoutLimit, int stderrLimit, Action exceeded)
    {
        var token = captureCancellation.Token;
        await using var stream = await attachment.Content.ReadAsStreamAsync(token);
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        var header = new byte[8];
        var buffer = new byte[81920];
        while (true)
        {
            var headerLength = await stream.ReadAtLeastAsync(header, 8, throwOnEndOfStream: false, token);
            if (headerLength == 0) break;
            if (headerLength != 8 || header[0] is not (1 or 2) || header[1] != 0 || header[2] != 0 || header[3] != 0)
                throw new IOException("Docker returned an invalid output frame.");
            var remaining = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
            var target = header[0] == 1 ? stdout : stderr;
            var limit = header[0] == 1 ? stdoutLimit : stderrLimit;
            while (remaining > 0)
            {
                var count = (int)Math.Min(remaining, (uint)buffer.Length);
                await stream.ReadExactlyAsync(buffer.AsMemory(0, count), token);
                var allowed = Math.Min(count, Math.Max(0, limit - (int)target.Length));
                if (allowed > 0) target.Write(buffer, 0, allowed);
                if (allowed < count) exceeded();
                remaining -= (uint)count;
            }
        }
        return (stdout.ToArray(), stderr.ToArray());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        captureCancellation.Cancel();
        attachment.Dispose();
        captureCancellation.Dispose();
    }
}
