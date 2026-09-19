using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class BuildConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_and_build_paths_use_host_isolation_or_an_explicit_override(bool overrideRoot)
    {
        var repository = ArchitectureTests.Repository;
        var temporary = overrideRoot ? Directory.CreateTempSubdirectory("minos-build-paths-") : null;
        try
        {
            var expectedRoot = temporary?.FullName ?? Path.Combine(repository, "artifacts", OperatingSystem.IsWindows() ? "windows" : "linux");
            var dotnet = Environment.GetEnvironmentVariable("MINOS_DOTNET");
            var start = new ProcessStartInfo(string.IsNullOrWhiteSpace(dotnet) ? "dotnet" : dotnet)
            {
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("msbuild");
            start.ArgumentList.Add(Path.Combine(repository, "src", "Minos.Infrastructure", "Minos.Infrastructure.csproj"));
            start.ArgumentList.Add("-nologo");
            start.ArgumentList.Add("-getProperty:MSBuildProjectExtensionsPath,OutputPath");
            if (overrideRoot) start.ArgumentList.Add($"-p:ArtifactsPath={expectedRoot}");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start MSBuild validation.");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromSeconds(30));
            var stdout = process.StandardOutput.ReadToEndAsync(cancellation.Token);
            var stderr = process.StandardError.ReadToEndAsync(cancellation.Token);
            try { await process.WaitForExitAsync(cancellation.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            Assert.True(process.ExitCode == 0, await stderr);
            using var result = JsonDocument.Parse(await stdout);
            var properties = result.RootElement.GetProperty("Properties");
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var name in new[] { "MSBuildProjectExtensionsPath", "OutputPath" })
            {
                var evaluated = properties.GetProperty(name).GetString();
                Assert.False(string.IsNullOrWhiteSpace(evaluated));
                Assert.True(Path.GetFullPath(evaluated!).StartsWith(Path.TrimEndingDirectorySeparator(expectedRoot) + Path.DirectorySeparatorChar, comparison),
                    $"{name} must stay inside the host-specific or explicitly selected artifact root.");
            }
        }
        finally { temporary?.Delete(); }
    }
}
