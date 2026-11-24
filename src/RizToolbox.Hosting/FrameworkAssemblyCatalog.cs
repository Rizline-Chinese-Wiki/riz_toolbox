using System.Reflection;
using System.Runtime.Loader;

namespace RizToolbox.Hosting;

internal sealed record FrameworkAssembly(string Path, AssemblyName Identity);

/// <summary>Concrete assemblies beside the running CoreCLR's CoreLib, independent of host-specific TPA setup.</summary>
internal sealed class FrameworkAssemblyCatalog
{
    private readonly IReadOnlyDictionary<string, FrameworkAssembly> _assemblies;
    private FrameworkAssemblyCatalog(IReadOnlyDictionary<string, FrameworkAssembly> assemblies) => _assemblies = assemblies;

    internal static FrameworkAssemblyCatalog Create(string coreAssemblyLocation)
    {
        if (string.IsNullOrEmpty(coreAssemblyLocation)) throw new InvalidOperationException("CoreLib has no on-disk location; this runtime layout is unsupported.");
        var runtimeDirectory = Path.GetDirectoryName(Path.GetFullPath(coreAssemblyLocation))!;
        var coreIdentity = AssemblyName.GetAssemblyName(coreAssemblyLocation);
        if (coreIdentity.Name != "System.Private.CoreLib" ||
            !(coreIdentity.GetPublicKeyToken() ?? Array.Empty<byte>()).SequenceEqual(typeof(object).Assembly.GetName().GetPublicKeyToken() ?? Array.Empty<byte>()))
            throw new InvalidOperationException("The running CoreLib identity could not be verified.");
        var assemblies = new Dictionary<string, FrameworkAssembly>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(runtimeDirectory, "*.dll").OrderBy(x => x, StringComparer.Ordinal))
        {
            AssemblyName identity;
            try { identity = AssemblyName.GetAssemblyName(file); }
            catch (BadImageFormatException) { continue; } // CoreCLR, hostpolicy and native diagnostics libraries share this directory.
            if (identity.Name is null || !identity.Name.Equals(Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) continue;
            // Runtime distributions contain signed framework libraries. An unsigned application DLL is never promoted to a platform import.
            if ((identity.GetPublicKeyToken()?.Length ?? 0) == 0) continue;
            assemblies.Add(identity.Name, new(Path.GetFullPath(file), identity));
        }
        return new(assemblies);
    }

    internal bool Contains(string? name) => name is not null && _assemblies.ContainsKey(name);

    internal bool IsCompatible(AssemblyName requested)
        => requested.Name is not null && _assemblies.TryGetValue(requested.Name, out var file) && Compatible(requested, file.Identity);

    internal Assembly Load(AssemblyName requested)
    {
        if (requested.Name is null || !_assemblies.TryGetValue(requested.Name, out var file) || !Compatible(requested, file.Identity))
            throw new FileLoadException($"Framework identity '{requested}' is not provided by the running CoreCLR directory.");
        // Loading this known file avoids Default.Resolving fallback into application or plugin directories.
        var loaded = AssemblyLoadContext.Default.LoadFromAssemblyPath(file.Path);
        if (!Compatible(file.Identity, loaded.GetName()) || !SamePath(loaded.Location, file.Path))
            throw new FileLoadException($"Framework '{requested.Name}' resolved outside its verified runtime location.");
        return loaded;
    }

    private static bool Compatible(AssemblyName requested, AssemblyName available)
    {
        if (!string.Equals(requested.Name, available.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (requested.Version is not null && (available.Version is null || available.Version < requested.Version)) return false;
        if (!string.IsNullOrEmpty(requested.CultureName) && !string.Equals(requested.CultureName, available.CultureName, StringComparison.OrdinalIgnoreCase)) return false;
        var requestedToken = requested.GetPublicKeyToken();
        return requestedToken is null || requestedToken.Length == 0 || requestedToken.SequenceEqual(available.GetPublicKeyToken() ?? Array.Empty<byte>());
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
