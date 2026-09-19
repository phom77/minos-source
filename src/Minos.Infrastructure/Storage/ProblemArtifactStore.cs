using System.IO.Compression;
using System.Security.Cryptography;
using Minos.Application;
using Minos.Application.Problems;
using Minos.Domain.Problems;
using Minos.Infrastructure.Hosting;

namespace Minos.Infrastructure.Storage;

internal sealed class ProblemArtifactStore : IProblemArtifactStore
{
    private readonly string artifactRoot;
    private readonly string stagingRoot;
    private readonly string testsRoot;

    public ProblemArtifactStore(PrivateArtifactDirectory privateDirectory)
    {
        artifactRoot = CreatePrivateDirectory(Path.Combine(privateDirectory.RootPath, "problem-artifacts"));
        stagingRoot = CreatePrivateDirectory(Path.Combine(artifactRoot, ".staging"));
        testsRoot = CreatePrivateDirectory(Path.Combine(artifactRoot, "tests"));
    }

    public async Task<StoredProblemTestArtifacts> StorePackageAsync(Stream package, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(package);
        var uploadId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(stagingRoot, uploadId);
        var content = Path.Combine(staging, "content");
        var finalId = Guid.NewGuid().ToString("N");
        var final = Path.Combine(testsRoot, finalId);
        var promoted = false;
        CreateNewPrivateDirectory(staging);
        CreateNewPrivateDirectory(content);
        try
        {
            var packagePath = Path.Combine(staging, "package.zip");
            await CopyBoundedAsync(package, packagePath, ProblemArtifactLimits.MaximumPackageBytes, null, token);
            ProblemArtifactReference input;
            ProblemArtifactReference output;
            try
            {
                using var archive = ZipFile.OpenRead(packagePath);
                if (archive.Entries.Count != 2)
                    throw InvalidPackage();
                if (archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != 2)
                    throw InvalidPackage();
                var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
                if (entries.Count != 2 || !entries.TryGetValue("input.txt", out var inputEntry) ||
                    !entries.TryGetValue("output.txt", out var outputEntry))
                    throw InvalidPackage();
                input = await ExtractAsync(inputEntry, Path.Combine(content, "input.bin"), $"tests/{finalId}/input.bin", token);
                output = await ExtractAsync(outputEntry, Path.Combine(content, "output.bin"), $"tests/{finalId}/output.bin", token);
            }
            catch (InvalidDataException)
            {
                throw InvalidPackage();
            }

            File.Delete(packagePath);
            if (Directory.Exists(final)) throw new IOException("Artifact destination collision.");
            Directory.Move(content, final);
            promoted = true;
            return new(input, output);
        }
        catch
        {
            if (promoted) DeleteManagedDirectory(final);
            throw;
        }
        finally
        {
            if (Directory.Exists(staging)) DeleteManagedDirectory(staging);
        }
    }

    public async Task VerifyAsync(IEnumerable<ProblemArtifactReference> artifacts, CancellationToken token)
    {
        foreach (var artifact in artifacts)
        {
            token.ThrowIfCancellationRequested();
            var path = ResolveKey(artifact.Key);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(path).Length != artifact.SizeBytes)
                throw new ArtifactValidationException("A required private test artifact is missing or invalid.");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var checksum = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(checksum), Convert.FromHexString(artifact.Sha256)))
                throw new ArtifactValidationException("A required private test artifact is missing or invalid.");
        }
    }

    public Task DeleteAsync(IEnumerable<string> keys, CancellationToken token)
    {
        foreach (var directory in keys.Select(key => Path.GetDirectoryName(ResolveKey(key))!).Distinct(PathComparer()))
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(directory)) DeleteManagedDirectory(directory);
        }
        return Task.CompletedTask;
    }

    public Task CleanupAbandonedAsync(IReadOnlySet<string> referencedKeys, DateTimeOffset olderThanUtc, CancellationToken token)
    {
        CleanupDirectories(stagingRoot, olderThanUtc, _ => false, token);
        CleanupDirectories(testsRoot, olderThanUtc, directory =>
        {
            var id = Path.GetFileName(directory);
            return referencedKeys.Contains($"tests/{id}/input.bin") || referencedKeys.Contains($"tests/{id}/output.bin");
        }, token);
        return Task.CompletedTask;
    }

    private static async Task<ProblemArtifactReference> ExtractAsync(
        ZipArchiveEntry entry,
        string destination,
        string key,
        CancellationToken token)
    {
        if (entry.FullName != entry.Name || entry.Name is not ("input.txt" or "output.txt") ||
            ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || entry.Length > ProblemArtifactReference.MaximumBytes)
            throw InvalidPackage();
        await using var source = entry.Open();
        using var hash = SHA256.Create();
        var (size, checksum) = await CopyBoundedAsync(source, destination, ProblemArtifactReference.MaximumBytes, hash, token);
        return ProblemArtifactReference.Create(key, checksum!, size);
    }

    private static async Task<(long Size, string? Checksum)> CopyBoundedAsync(
        Stream source,
        string destination,
        long limit,
        HashAlgorithm? hash,
        CancellationToken token)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var target = new FileStream(destination, options);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, token);
            if (read == 0) break;
            total = checked(total + read);
            if (total > limit) throw new ArtifactValidationException("Test package or artifact exceeds its size limit.");
            await target.WriteAsync(buffer.AsMemory(0, read), token);
            hash?.TransformBlock(buffer, 0, read, null, 0);
        }
        await target.FlushAsync(token);
        hash?.TransformFinalBlock([], 0, 0);
        return (total, hash is null ? null : Convert.ToHexString(hash.Hash!).ToLowerInvariant());
    }

    private string ResolveKey(string key)
    {
        var parts = key.Split('/');
        if (parts is not ["tests", var id, "input.bin" or "output.bin"] ||
            id.Length != 32 || id.Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
            throw new ArtifactValidationException("Artifact key is invalid.");
        var path = Path.GetFullPath(Path.Combine(artifactRoot, parts[0], parts[1], parts[2]));
        if (!path.StartsWith(testsRoot + Path.DirectorySeparatorChar, PathComparison()))
            throw new ArtifactValidationException("Artifact key is invalid.");
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory) && (new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArtifactValidationException("Artifact storage contains an unsafe path.");
        return path;
    }

    private static void CleanupDirectories(
        string parent,
        DateTimeOffset olderThanUtc,
        Func<string, bool> retained,
        CancellationToken token)
    {
        foreach (var directory in Directory.EnumerateDirectories(parent))
        {
            token.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Private artifact storage contains an unsafe symbolic link.");
            if (info.LastWriteTimeUtc >= olderThanUtc.UtcDateTime || retained(directory)) continue;
            DeleteManagedDirectory(directory);
        }
    }

    private static string CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Private artifact storage contains an unsafe symbolic link.");
        return info.FullName;
    }

    private static void CreateNewPrivateDirectory(string path)
    {
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Artifact staging collision.");
        CreatePrivateDirectory(path);
    }

    private static void DeleteManagedDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Refusing to delete an unsafe artifact path.");
        info.Delete(true);
    }

    private static ArtifactValidationException InvalidPackage() =>
        new("Test package must be a ZIP containing exactly input.txt and output.txt at its root.");

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
