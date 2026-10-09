namespace Minos.Infrastructure.Hosting;

// Single-host startup owns the runner lock and removes abandoned sandboxes before claiming jobs.
public sealed class JudgeSandboxLifecycle
{
    private readonly Func<CancellationToken, Task> initialize;
    internal JudgeSandboxLifecycle(Func<CancellationToken, Task> initialize) => this.initialize = initialize;
    public Task InitializeAsync(CancellationToken token) => initialize(token);
}
