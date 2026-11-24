using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Program
{
    internal static JsonElement Binding;
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("mode and exact batch binding required");
            using var doc = JsonDocument.Parse(File.ReadAllBytes(args[1])); Binding = doc.RootElement.Clone();
            foreach (var row in Binding.GetProperty("managed").EnumerateObject()) Verify(row.Value);
            AssemblyLoadContext.Default.Resolving += Resolve;
            return Invoke(args[0]);
        }
        catch (Exception error) { Console.WriteLine(JsonSerializer.Serialize(new { passed=false, bootstrapError=error.ToString() })); return 1; }
    }
    private static void Verify(JsonElement row)
    {
        var path = row.GetProperty("path").GetString()!;
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length != row.GetProperty("bytes").GetInt64()
            || Hash(path) != row.GetProperty("sha256").GetString()) throw new InvalidOperationException("Changed managed binding: " + path);
    }
    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (!Binding.GetProperty("managed").TryGetProperty(name.Name!, out var row)) return null;
        Verify(row); return context.LoadFromAssemblyPath(row.GetProperty("path").GetString()!);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static int Invoke(string mode) => HubCases.Run(mode);
    internal static void VerifyLoaded(Assembly assembly)
    {
        var row = Binding.GetProperty("managed").GetProperty(assembly.GetName().Name!); Verify(row);
        if (!Path.GetFullPath(assembly.Location).Equals(Path.GetFullPath(row.GetProperty("path").GetString()!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Assembly loaded from another batch: " + assembly.Location);
    }
}
