using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Minos.Infrastructure.Hosting;

// M2 only establishes a private host-local root. Artifact upload/lifecycle APIs belong to M4.
public sealed class PrivateArtifactDirectory
{
    private readonly string root;
    private const string ConfigurationError = "Minos:Artifacts:Root must be an existing, absolute, private directory outside the repository, without symbolic links.";

    public PrivateArtifactDirectory(string? configuredRoot, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot) || !Path.IsPathFullyQualified(configuredRoot))
            throw new InvalidOperationException(ConfigurationError);
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot));
            if (!Directory.Exists(root)) throw new InvalidOperationException(ConfigurationError);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            for (var location = new DirectoryInfo(contentRoot); location is not null; location = location.Parent)
            {
                if (!File.Exists(Path.Combine(location.FullName, "Minos.sln")) && !Directory.Exists(Path.Combine(location.FullName, ".git"))) continue;
                if (root.Equals(location.FullName, comparison) || root.StartsWith(location.FullName + Path.DirectorySeparatorChar, comparison))
                    throw new InvalidOperationException(ConfigurationError);
                break;
            }
            ValidatePrivacy();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new InvalidOperationException(ConfigurationError); }
    }

    private void ValidatePrivacy()
    {
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException(ConfigurationError);
            if (File.Exists(Path.Combine(directory.FullName, "Minos.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                throw new InvalidOperationException(ConfigurationError);
            if (!OperatingSystem.IsWindows())
            {
                var ancestorMode = File.GetUnixFileMode(directory.FullName);
                if ((ancestorMode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0 &&
                    (ancestorMode & UnixFileMode.StickyBit) == 0)
                    throw new InvalidOperationException(ConfigurationError);
            }
        }
        if (OperatingSystem.IsWindows()) ValidateWindowsPrivacy(root);
        else
        {
            var mode = File.GetUnixFileMode(root);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new InvalidOperationException(ConfigurationError);
            // WSL Windows mounts do not provide the Linux permission boundary required here.
            if (OperatingSystem.IsLinux())
            {
                var mount = File.ReadLines("/proc/self/mountinfo")
                    .Select(line => line.Split(' ')).Where(parts => root == parts[4] || root.StartsWith(parts[4].TrimEnd('/') + "/", StringComparison.Ordinal))
                    .OrderByDescending(parts => parts[4].Length).FirstOrDefault();
                if (mount is not null && mount.Skip(Array.IndexOf(mount, "-") + 1).First() is "9p" or "drvfs" or "cifs" or "fuseblk")
                    throw new InvalidOperationException(ConfigurationError);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateWindowsPrivacy(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var allowed = new[] { identity.User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) };
        var acl = new DirectoryInfo(path).GetAccessControl();
        if (!allowed.Contains((SecurityIdentifier)acl.GetOwner(typeof(SecurityIdentifier))!))
            throw new InvalidOperationException(ConfigurationError);
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !allowed.Contains((SecurityIdentifier)rule.IdentityReference))
                throw new InvalidOperationException(ConfigurationError);
    }

    public async Task CheckWritableAsync(CancellationToken cancellationToken)
    {
        ValidatePrivacy();
        var probe = Path.Combine(root, $".minos-probe-{Guid.NewGuid():N}");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite,
                Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var file = new FileStream(probe, options);
            await file.WriteAsync(new byte[] { 42 }, cancellationToken);
            await file.FlushAsync(cancellationToken);
            file.Position = 0;
            var evidence = new byte[1];
            if (await file.ReadAsync(evidence, cancellationToken) != 1 || evidence[0] != 42)
                throw new InvalidOperationException("Private artifact storage failed its read/write check.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Private artifact storage is unavailable or not writable."); }
    }
}
