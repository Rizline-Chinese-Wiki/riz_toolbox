using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using RizToolbox.Sdk;

namespace RizToolbox.Hosting;

internal sealed class PackageLoadContext : AssemblyLoadContext
{
    private static readonly Assembly SharedSdk = typeof(IRizPlugin).Assembly;
    internal static readonly string SdkName = SharedSdk.GetName().Name!;
    private static readonly FrameworkAssemblyCatalog Platform = FrameworkAssemblyCatalog.Create(typeof(object).Assembly.Location);
    private readonly IReadOnlyDictionary<string, PackageAssembly> _assemblies;

    public PackageLoadContext(PackageCandidate candidate) : base($"mod:{candidate.Metadata.Manifest.Id}:{Guid.NewGuid():N}", isCollectible: true)
        => _assemblies = candidate.Assemblies;

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == SdkName)
        {
            if (!ExactIdentity(name, SharedSdk.GetName())) throw new FileLoadException($"SDK identity mismatch: {name}.");
            return SharedSdk;
        }
        if (IsProtected(name.Name)) throw new FileLoadException($"Host implementation assembly '{name.Name}' is not a plugin API.");
        if (IsPlatform(name.Name)) return Platform.Load(name);
        if (name.Name is not null && _assemblies.TryGetValue(name.Name, out var file))
        {
            if (!ExactIdentity(name, file.Identity)) throw new FileLoadException($"Private dependency identity mismatch: requested {name}, packaged {file.Identity}.");
            return LoadFromAssemblyPath(file.Path);
        }
        // Throw rather than return null: null would allow Default.Resolving to import another package's dependency.
        throw new FileNotFoundException($"Private dependency '{name}' was not found in this package.");
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        => throw new DllNotFoundException($"Package P/Invoke '{unmanagedDllName}' is not supported by this SDK. Use adapter services.");

    internal static bool IsPlatform(string? name) => Platform.Contains(name);
    internal static bool IsProtected(string? name)
        => name is not null && (name.Equals(SdkName, StringComparison.OrdinalIgnoreCase) ||
            new[] { "RizToolbox.Core", "RizToolbox.Loader", "RizToolbox.Runtime", "RizToolbox.Adapter", "RizToolbox.Adapters", "RizToolbox.Hosting" }
                .Any(x => name.Equals(x, StringComparison.OrdinalIgnoreCase) || name.StartsWith(x + ".", StringComparison.OrdinalIgnoreCase)));

    internal static void ValidateReferences(PackageCandidate candidate)
    {
        foreach (var assembly in candidate.Assemblies.Values)
        foreach (var reference in assembly.References)
        {
            if (reference.Name == SdkName)
            {
                if (!ExactIdentity(reference, SharedSdk.GetName())) throw new PackageException("sdk-identity", $"'{assembly.Identity.Name}' requires incompatible SDK assembly '{reference}'.");
                continue;
            }
            if (IsProtected(reference.Name)) throw new PackageException("host-reference", $"'{assembly.Identity.Name}' references host implementation '{reference.Name}'; plugins may reference SDK only.");
            if (IsPlatform(reference.Name))
            {
                if (!Platform.IsCompatible(reference)) throw new PackageException("framework-identity", $"'{assembly.Identity.Name}' requires framework assembly '{reference}' not provided by this runtime.");
                continue;
            }
            if (reference.Name is null || !candidate.Assemblies.TryGetValue(reference.Name, out var dependency))
                throw new PackageException("dependency-missing", $"'{assembly.Identity.Name}' requires missing private assembly '{reference}'.");
            if (!ExactIdentity(reference, dependency.Identity)) throw new PackageException("dependency-version", $"Private dependency '{reference}' does not match packaged '{dependency.Identity}'.");
        }
    }

    internal static PackageAssembly Inspect(string path)
    {
        var name = AssemblyName.GetAssemblyName(path);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly) throw new PackageException("assembly-invalid", $"'{Path.GetFileName(path)}' is not an assembly.");
        var refs = metadata.AssemblyReferences.Select(handle =>
        {
            var reference = metadata.GetAssemblyReference(handle);
            var identity = new AssemblyName { Name = metadata.GetString(reference.Name), Version = reference.Version,
                CultureName = reference.Culture.IsNil ? null : metadata.GetString(reference.Culture) };
            var key = metadata.GetBlobBytes(reference.PublicKeyOrToken);
            if ((reference.Flags & AssemblyFlags.PublicKey) != 0) identity.SetPublicKey(key); else identity.SetPublicKeyToken(key);
            return identity;
        }).ToArray();
        return new(path, name, refs);
    }

    private static bool ExactIdentity(AssemblyName a, AssemblyName b)
        => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) && a.Version == b.Version &&
            string.Equals(a.CultureName ?? "", b.CultureName ?? "", StringComparison.OrdinalIgnoreCase) &&
            (a.GetPublicKeyToken() ?? Array.Empty<byte>()).SequenceEqual(b.GetPublicKeyToken() ?? Array.Empty<byte>());

}
