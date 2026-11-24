namespace RizToolbox.Runtime.Hooks;

// A candidate build can supply the optional implementation from a generated
// source file. Missing generated facts stay unavailable. Neither these hashes
// nor a managed caller can grant the native execution-window admission.
internal static partial class NativeEngineProductionProfile
{
    internal static NativeEngineProductionProfileData? Read()
    {
        NativeEngineProductionProfileData? value = null;
        Populate(ref value);
        return value;
    }
    static partial void Populate(ref NativeEngineProductionProfileData? value);
}

internal sealed class NativeEngineProductionProfileData
{
    private readonly string[] _hashes;
    private readonly uint[] _functionRvas;
    internal NativeEngineProductionProfileData(string bootstrapSha256, string kernelSha256,
        string unitySha256, string executableSha256, string doorstopSha256, uint[] functionRvas)
    {
        var hashes = new[] { bootstrapSha256, kernelSha256, unitySha256, executableSha256, doorstopSha256 };
        if (hashes.Any(value => value is null || value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))))
            throw new ArgumentException("Five independently compiled SHA256 fingerprints are required.");
        ArgumentNullException.ThrowIfNull(functionRvas);
        if (functionRvas.Length != 4 || functionRvas.Where((rva, index) => rva != NativeEngineNodeProfile.Rva((NativeEngineNodeKind)index)).Any())
            throw new ArgumentException("Production engine functions must match the four fixed game RVAs.", nameof(functionRvas));
        _hashes = hashes.Select(value => value.ToLowerInvariant()).ToArray();
        _functionRvas = functionRvas.ToArray();
    }
    internal string Hash(int index) => _hashes[index is >= 0 and < 5 ? index : throw new ArgumentOutOfRangeException(nameof(index))];
    internal uint FunctionRva(NativeEngineNodeKind kind) => _functionRvas[(uint)kind < 4 ? (int)kind : throw new ArgumentOutOfRangeException(nameof(kind))];
}
