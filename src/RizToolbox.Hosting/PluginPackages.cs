using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using RizToolbox.Sdk;

namespace RizToolbox.Hosting;

public sealed record PluginPackageMetadata(PluginManifest Manifest, string EntryAssembly, string EntryType);
public sealed record PluginPackageDiagnostic(string PackagePath, string Code, string Message);
public sealed record PluginPackageUpdateStatus(bool RestartRequired, IReadOnlyList<string> ChangedFiles, string Message);

/// <summary>Validated package metadata and file hashes. Discovery neither loads assemblies nor executes plugin code.</summary>
public sealed class DiscoveredPluginPackage
{
    internal PackageCandidate Candidate { get; }
    public string DirectoryPath => Candidate.DirectoryPath;
    public PluginPackageMetadata Metadata => Candidate.Metadata;
    public IReadOnlyDictionary<string, string> Fingerprints => Candidate.Fingerprints;

    internal DiscoveredPluginPackage(PackageCandidate candidate) => Candidate = candidate;
}

/// <summary>A deterministic discovery snapshot that can be planned before loading its packages.</summary>
public sealed class PluginDiscoveryResult
{
    public IReadOnlyList<DiscoveredPluginPackage> Packages { get; }
    public IReadOnlyList<PluginPackageDiagnostic> Diagnostics { get; }

    internal PluginDiscoveryResult(IEnumerable<PackageCandidate> candidates, IEnumerable<PluginPackageDiagnostic> diagnostics)
    {
        Packages = Array.AsReadOnly(candidates.Select(x => new DiscoveredPluginPackage(x)).ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
}

/// <summary>A package owns one collectible load context. Stop its plugins before releasing the package.</summary>
public sealed class LoadedPluginPackage : IDisposable
{
    private AssemblyLoadContext? _context;
    private readonly IReadOnlyDictionary<string, string> _fingerprints;
    public string DirectoryPath { get; }
    public PluginPackageMetadata Metadata { get; }
    public IRizPlugin Plugin { get; }

    internal LoadedPluginPackage(string directoryPath, PluginPackageMetadata metadata,
        IRizPlugin plugin, AssemblyLoadContext context, IReadOnlyDictionary<string, string> fingerprints)
        => (DirectoryPath, Metadata, Plugin, _context, _fingerprints) = (directoryPath, metadata, plugin, context, fingerprints);

    /// <summary>Detects disk changes only. It never overwrites, reloads or bypasses installer ownership/hash checks.</summary>
    public PluginPackageUpdateStatus CheckForUpdates()
    {
        var changed = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            var current = PackageFileSnapshot.Capture(DirectoryPath);
            foreach (var file in current.Keys.Concat(_fingerprints.Keys).Distinct(StringComparer.Ordinal))
                if (!current.TryGetValue(file, out var now) || !_fingerprints.TryGetValue(file, out var before) || now != before) changed.Add(file);
        }
        catch (Exception ex)
        {
            return new(true, Array.AsReadOnly(new[] { "<package-unverifiable>" }),
                "Package disk state cannot be verified. Restart is required after restoring/updating through the ownership-checking installer: " + ex.Message);
        }
        return new(changed.Count != 0, Array.AsReadOnly(changed.ToArray()), changed.Count == 0 ? "Package files match the loaded snapshot." :
            "Package files changed on disk; restart is required. Apply updates through the ownership-checking installer; live DLL replacement is unsupported.");
    }

    /// <summary>Requests context unloading; collection completes after callers release all plugin/type references.</summary>
    public void Dispose() => Interlocked.Exchange(ref _context, null)?.Unload();
}

public sealed class PluginLoadResult : IDisposable
{
    /// <summary>A stateless result for hosts that reject plugin execution before loading packages.</summary>
    public static PluginLoadResult Empty { get; } = new(Array.Empty<LoadedPluginPackage>(), Array.Empty<PluginPackageDiagnostic>());
    public IReadOnlyList<LoadedPluginPackage> Packages { get; }
    public IReadOnlyList<IRizPlugin> Plugins { get; }
    public IReadOnlyList<PluginPackageDiagnostic> Diagnostics { get; }

    internal PluginLoadResult(IEnumerable<LoadedPluginPackage> packages, IEnumerable<PluginPackageDiagnostic> diagnostics)
    {
        Packages = Array.AsReadOnly(packages.ToArray());
        Plugins = Array.AsReadOnly(Packages.Select(x => x.Plugin).ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public void Dispose()
    {
        foreach (var package in Packages.Reverse()) package.Dispose();
    }
}

internal sealed class PackageException : Exception
{
    public string Code { get; }
    public PackageException(string code, string message) : base(message) => Code = code;
}

internal sealed record PackageAssembly(string Path, AssemblyName Identity, AssemblyName[] References);
internal sealed record PackageCandidate(string DirectoryPath, PluginPackageMetadata Metadata,
    string EntryPath, IReadOnlyDictionary<string, PackageAssembly> Assemblies, IReadOnlyDictionary<string, string> Fingerprints);

internal static class PackageFileSnapshot
{
    internal static IReadOnlyDictionary<string, string> Capture(string root)
    {
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new PackageException("path-invalid", "Package paths cannot contain reparse points.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Visit(root, 0); return new ReadOnlyDictionary<string, string>(result);
        void Visit(string directory, int depth)
        {
            if (depth > 16) throw new PackageException("path-invalid", "Package nesting exceeds 16 levels.");
            foreach (var path in Directory.GetFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new PackageException("path-invalid", "Package paths cannot contain reparse points.");
                if ((attributes & FileAttributes.Directory) != 0) Visit(path, depth + 1);
                else
                {
                    using var stream = File.OpenRead(path);
                    using var hash = SHA256.Create();
                    result.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), Convert.ToHexString(hash.ComputeHash(stream)));
                }
            }
        }
    }

    internal static bool Matches(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
        => expected.Count == actual.Count && expected.All(file => actual.TryGetValue(file.Key, out var hash) && file.Value == hash);
}
