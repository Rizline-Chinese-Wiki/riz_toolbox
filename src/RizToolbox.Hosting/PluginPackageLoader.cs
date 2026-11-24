using System.Reflection;
using RizToolbox.Sdk;

namespace RizToolbox.Hosting;

/// <summary>Loads independent SDK plugin packages. Loading trusted local DLLs is not a security sandbox.</summary>
public static class PluginPackageLoader
{
    public static PluginLoadResult LoadDirectory(string modsRoot, ILogger log)
        => LoadDiscovered(DiscoverDirectory(modsRoot, log), log);

    /// <summary>Validates package metadata, paths and assembly references without loading assemblies or running constructors.</summary>
    public static PluginDiscoveryResult DiscoverDirectory(string modsRoot, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(modsRoot);
        ArgumentNullException.ThrowIfNull(log);
        var errors = new List<PluginPackageDiagnostic>();
        var candidates = new List<PackageCandidate>();
        string root;
        try
        {
            root = Path.GetFullPath(modsRoot);
            if (!Directory.Exists(root)) return new(candidates, errors);
            RejectReparseAncestors(root);
        }
        catch (Exception ex) { Report(modsRoot, ex); return new(candidates, errors); }

        string[] folders;
        try
        {
            folders = Directory.GetDirectories(root).OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal).ToArray();
            foreach (var file in Directory.GetFiles(root, "*.dll").OrderBy(x => x, StringComparer.Ordinal))
                Report(file, new PackageException("legacy-flat-unsupported", "Legacy flat DLL is not loaded. Move it into an independent package directory with plugin.json; flat DLL dependencies are not isolated."));
        }
        catch (Exception ex) { Report(root, ex); return new(candidates, errors); }

        // All manifest/path/reference checks finish before the first plugin constructor executes.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            try
            {
                var candidate = InspectPackage(folder);
                if (!ids.Add(candidate.Metadata.Manifest.Id)) throw new PackageException("duplicate-id", $"Duplicate plugin ID '{candidate.Metadata.Manifest.Id}'. Earlier package directory wins.");
                candidates.Add(candidate);
            }
            catch (Exception ex) { Report(folder, ex); }
        }
        ReportMissingPluginDependencies(candidates, Report);
        return new(candidates, errors);

        void Report(string path, Exception error) => ReportDiagnostic(path, error, errors, log);
    }

    /// <summary>Loads the selected discovered candidates, rejecting packages whose files changed since discovery.</summary>
    public static PluginLoadResult LoadDiscovered(PluginDiscoveryResult discovery, ILogger log,
        Func<DiscoveredPluginPackage, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(log);
        var loaded = new List<LoadedPluginPackage>();
        var errors = discovery.Diagnostics.ToList();
        foreach (var package in discovery.Packages)
        {
            var candidate = package.Candidate;
            PackageLoadContext? context = null;
            try
            {
                if (include is not null && !include(package)) continue;
                try
                {
                    if (!PackageFileSnapshot.Matches(candidate.Fingerprints, PackageFileSnapshot.Capture(candidate.DirectoryPath)))
                        throw new IOException("The package file snapshot no longer matches discovery.");
                }
                catch (Exception ex)
                {
                    throw new PackageException("package-changed", "Package files changed or cannot be verified since discovery; rediscover before loading. " + ex.Message);
                }
                context = new(candidate);
                var assembly = context.LoadFromAssemblyPath(candidate.EntryPath);
                var type = assembly.GetType(candidate.Metadata.EntryType, throwOnError: false, ignoreCase: false);
                if (type is null || type.Assembly != assembly || !type.IsClass || type.IsAbstract || !type.IsVisible || type.ContainsGenericParameters ||
                    !typeof(IRizPlugin).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
                    throw new PackageException("entry-type", "entryType must name a public concrete IRizPlugin class with a public parameterless constructor.");
                var plugin = (IRizPlugin)Activator.CreateInstance(type)!;
                if (!PackageManifestReader.Equivalent(plugin.Manifest, candidate.Metadata.Manifest))
                    throw new PackageException("manifest-mismatch", "IRizPlugin.Manifest differs from plugin.json metadata.");
                loaded.Add(new(candidate.DirectoryPath, candidate.Metadata, plugin, context, candidate.Fingerprints));
                context = null;
            }
            catch (Exception ex) { ReportDiagnostic(candidate.DirectoryPath, ex, errors, log); }
            finally { context?.Unload(); }
        }
        return new(loaded, errors);
    }

    private static void ReportDiagnostic(string path, Exception error, List<PluginPackageDiagnostic> errors, ILogger log)
    {
        var unwrapped = error is TargetInvocationException { InnerException: not null } invocation ? invocation.InnerException : error;
        string code = unwrapped is PackageException package ? package.Code : unwrapped switch
        {
            System.Text.Json.JsonException => "manifest-invalid",
            BadImageFormatException => "assembly-invalid",
            _ => "package-load"
        };
        var diagnostic = new PluginPackageDiagnostic(path, code, unwrapped!.Message);
        errors.Add(diagnostic);
        try { log.Log(LogLevel.Error, $"Plugin package '{Path.GetFileName(path)}' [{code}]: {diagnostic.Message}"); }
        catch { /* A failing diagnostic sink must not break discovery of unrelated packages. */ }
    }

    private static PackageCandidate InspectPackage(string folder)
    {
        RejectReparseAncestors(folder);
        var fingerprints = PackageFileSnapshot.Capture(folder);
        var files = EnumerateSafeFiles(folder).ToArray();
        var manifestPath = Path.Combine(folder, "plugin.json");
        var metadata = PackageManifestReader.Read(manifestPath);
        var relative = metadata.EntryAssembly;
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\\') ||
            relative.Split('/').Any(x => x is "" or "." or "..") || !relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new PackageException("path-invalid", "entryAssembly must be a package-relative DLL path using '/' and no traversal.");
        var entryPath = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        if (!entryPath.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !File.Exists(entryPath))
            throw new PackageException("path-invalid", "Entry assembly is missing or outside the package.");
        var dllFiles = files.Where(x => Path.GetExtension(x).Equals(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (dllFiles.Length > 128) throw new PackageException("assembly-invalid", "A package may contain at most 128 managed assemblies.");
        var assemblies = new Dictionary<string, PackageAssembly>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in dllFiles)
        {
            if (PackageLoadContext.IsProtected(Path.GetFileNameWithoutExtension(file)) || PackageLoadContext.IsPlatform(Path.GetFileNameWithoutExtension(file)))
                throw new PackageException("host-replacement", $"Package-local framework or host assembly '{Path.GetFileName(file)}' is forbidden.");
            var item = PackageLoadContext.Inspect(file);
            string name = item.Identity.Name!;
            if (PackageLoadContext.IsProtected(name) || PackageLoadContext.IsPlatform(name))
                throw new PackageException("host-replacement", $"Package-local framework or host assembly '{name}' is forbidden, including renamed copies.");
            if (!assemblies.TryAdd(name, item)) throw new PackageException("assembly-duplicate", $"Multiple files define private assembly '{name}'.");
        }
        var candidate = new PackageCandidate(folder, metadata, entryPath, assemblies, fingerprints);
        PackageLoadContext.ValidateReferences(candidate);
        if (!PackageFileSnapshot.Matches(fingerprints, PackageFileSnapshot.Capture(folder)))
            throw new PackageException("package-changed", "Package files changed during discovery; rediscover before loading.");
        return candidate;
    }

    private static IEnumerable<string> EnumerateSafeFiles(string directory, int depth = 0)
    {
        if (depth > 16) throw new PackageException("path-invalid", "Package directory nesting exceeds 16 levels.");
        foreach (var path in Directory.GetFileSystemEntries(directory).OrderBy(x => x, StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new PackageException("path-invalid", "Symbolic links and reparse points are forbidden inside plugin packages.");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var file in EnumerateSafeFiles(path, depth + 1)) yield return file;
            }
            else yield return path;
        }
    }

    private static void RejectReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new PackageException("path-invalid", "Plugin root/package paths cannot contain symbolic links or reparse points.");
    }

    private static void ReportMissingPluginDependencies(List<PackageCandidate> candidates, Action<string, Exception> report)
    {
        var byId = candidates.ToDictionary(x => x.Metadata.Manifest.Id, StringComparer.Ordinal);
        foreach (var candidate in candidates)
            foreach (var dependency in candidate.Metadata.Manifest.Dependencies!)
            {
                if (byId.TryGetValue(dependency.Id, out var target) && target.Metadata.Manifest.Version >= dependency.MinVersion &&
                    (dependency.MaxVersionExclusive is null || target.Metadata.Manifest.Version < dependency.MaxVersionExclusive)) continue;
                report(candidate.DirectoryPath, new PackageException("plugin-dependency", $"DependencyUnsatisfied: {candidate.Metadata.Manifest.Id} -> {dependency.Id}: required plugin is missing or outside the declared version range. The valid package remains registered and waits in PluginHost."));
                break;
            }
    }
}
