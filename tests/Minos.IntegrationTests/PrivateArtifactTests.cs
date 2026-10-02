using Microsoft.AspNetCore.Mvc.Testing;
using Minos.Infrastructure.Hosting;
using Xunit;
using System.Security.AccessControl;
using System.Security.Principal;
using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Minos.Application;
using Minos.Application.Problems;
using Minos.Infrastructure;

namespace Minos.IntegrationTests;

public sealed class PrivateArtifactTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsLinux => OperatingSystem.IsLinux();
    [Theory]
    [InlineData("")]
    [InlineData("relative-private-path")]
    [InlineData("invalid\0sentinel")]
    public void Invalid_storage_configuration_is_rejected_without_echoing_values(string root)
    {
        var original = Environment.GetEnvironmentVariable("Minos__Artifacts__Root");
        Environment.SetEnvironmentVariable("Minos__Artifacts__Root", root);
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            var failure = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("Minos:Artifacts:Root", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel", failure.Message, StringComparison.Ordinal);
        }
        finally { Environment.SetEnvironmentVariable("Minos__Artifacts__Root", original); }
    }

    [Fact]
    public async Task Private_storage_probe_leaves_no_files_and_observes_cancellation()
    {
        var root = PostgresPrivilegeTests.Connection("Minos__Artifacts__Root");
        var store = new PrivateArtifactDirectory(root, ArchitectureTests.Repository);
        var before = Directory.GetFiles(root).Order().ToArray();
        await store.CheckWritableAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, Directory.GetFiles(root).Order());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CheckWritableAsync(cancellation.Token));
        Assert.Equal(before, Directory.GetFiles(root).Order());
    }

    [Fact]
    public void Repository_is_never_an_artifact_root() => Assert.Throws<InvalidOperationException>(() =>
        new PrivateArtifactDirectory(ArchitectureTests.Repository, ArchitectureTests.Repository));

    [Fact(Skip = "Windows ACL validation runs on Windows.", SkipUnless = nameof(IsWindows))]
    public async Task Windows_storage_requires_a_private_acl_and_passes_real_io()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("minos-artifact-test-");
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.SetOwner(identity.User!);
            acl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
            var store = new PrivateArtifactDirectory(directory.FullName, ArchitectureTests.Repository);
            await store.CheckWritableAsync(TestContext.Current.CancellationToken);
            Assert.Empty(directory.GetFiles());
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            directory.SetAccessControl(acl);
            Assert.Throws<InvalidOperationException>(() => new PrivateArtifactDirectory(directory.FullName, ArchitectureTests.Repository));
        }
        finally { directory.Delete(); }
    }

    [Fact(Skip = "Linux permission validation runs on Linux.", SkipUnless = nameof(IsLinux))]
    public void Linux_storage_rejects_public_permissions_and_symlinked_ancestors()
    {
        if (!OperatingSystem.IsLinux()) return;
        var parent = Directory.CreateTempSubdirectory("minos-artifact-test-");
        try
        {
            var privateRoot = Path.Combine(parent.FullName, "private");
            Directory.CreateDirectory(privateRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(privateRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
            Assert.Throws<InvalidOperationException>(() => new PrivateArtifactDirectory(privateRoot, ArchitectureTests.Repository));
            File.SetUnixFileMode(privateRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(parent.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);
            Assert.Throws<InvalidOperationException>(() => new PrivateArtifactDirectory(privateRoot, ArchitectureTests.Repository));
            File.SetUnixFileMode(parent.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var link = Path.Combine(parent.FullName, "linked");
            Directory.CreateSymbolicLink(link, privateRoot);
            Assert.Throws<InvalidOperationException>(() => new PrivateArtifactDirectory(link, ArchitectureTests.Repository));
            Directory.Delete(link);
            var child = Path.Combine(privateRoot, "child");
            Directory.CreateDirectory(child, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateSymbolicLink(link, privateRoot);
            Assert.Throws<InvalidOperationException>(() => new PrivateArtifactDirectory(Path.Combine(link, "child"), ArchitectureTests.Repository));
            Directory.Delete(link);
            Directory.Delete(child);
            Directory.Delete(privateRoot);
        }
        finally { parent.Delete(); }
    }

    [Fact(Skip = "Linux artifact link validation runs on Linux.", SkipUnless = nameof(IsLinux))]
    public async Task Verified_test_reads_reject_changed_files_and_symbolic_links()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("minos-verified-artifacts-");
        try
        {
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var services = new ServiceCollection();
            services.AddMinosInfrastructure("Host=localhost;Database=unused;Username=unused");
            services.AddSingleton(new PrivateArtifactDirectory(directory.FullName, ArchitectureTests.Repository));
            await using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<IProblemArtifactStore>();
            var token = TestContext.Current.CancellationToken;
            using var package = new MemoryStream();
            using (var archive = new ZipArchive(package, ZipArchiveMode.Create, true))
                foreach (var name in new[] { "input.txt", "output.txt" })
                    await using (var stream = archive.CreateEntry(name).Open())
                        await stream.WriteAsync("42\n"u8.ToArray(), token);
            package.Position = 0;
            var stored = await store.StorePackageAsync(package, token);
            Assert.Equal("42\n"u8.ToArray(), await store.ReadVerifiedAsync(stored.Input, token));
            var path = Path.Combine(directory.FullName, "problem-artifacts", stored.Input.Key);
            await File.WriteAllTextAsync(path, "99\n", token);
            await Assert.ThrowsAsync<ArtifactValidationException>(() => store.ReadVerifiedAsync(stored.Input, token));
            await File.WriteAllTextAsync(path, "oversized", token);
            await Assert.ThrowsAsync<ArtifactValidationException>(() => store.ReadVerifiedAsync(stored.Input, token));
            File.Delete(path);
            var outside = Path.Combine(directory.FullName, "unmanaged.bin");
            await File.WriteAllTextAsync(outside, "42\n", token);
            File.CreateSymbolicLink(path, outside);
            await Assert.ThrowsAsync<ArtifactValidationException>(() => store.ReadVerifiedAsync(stored.Input, token));
            File.Delete(path);
        }
        finally { directory.Delete(true); }
    }
}
