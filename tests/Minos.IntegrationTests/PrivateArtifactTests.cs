using Microsoft.AspNetCore.Mvc.Testing;
using Minos.Infrastructure.Hosting;
using Xunit;
using System.Security.AccessControl;
using System.Security.Principal;

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
}
