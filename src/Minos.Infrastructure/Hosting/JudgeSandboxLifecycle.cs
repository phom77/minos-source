using Minos.Infrastructure.Evaluation;

namespace Minos.Infrastructure.Hosting;

// Single-host startup owns the runner lock and removes abandoned sandboxes before claiming jobs.
public sealed class JudgeSandboxLifecycle
{
    private readonly DockerExecutionBackend backend;
    internal JudgeSandboxLifecycle(DockerExecutionBackend backend) => this.backend = backend;
    public Task InitializeAsync(CancellationToken token) => backend.EnsureInitializedAsync(token);
}
