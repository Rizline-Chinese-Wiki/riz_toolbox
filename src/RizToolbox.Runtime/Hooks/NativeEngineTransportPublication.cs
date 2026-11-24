using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

// An immutable owned wire copy, not a Ready/admission owner. Mutable ABI DTOs
// never escape by reference; every decode returns a distinct owned array graph.
internal sealed class NativeEngineTransportPublication
{
    private readonly byte[] _bytes;
    private NativeEngineTransportPublication(byte[] bytes) => _bytes = bytes;
    internal static NativeEngineTransportPublication Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != NativeEngineTransportPublicationLayout.ByteSize)
            throw new UnsupportedSignatureException("An exact complete V5 publication copy is required.");
        var value = new NativeEngineTransportPublication(bytes.ToArray());
        if (!NativeEngineTransportPublicationLayout.ValidateHeader(value.CopyWire()))
            throw new UnsupportedSignatureException("The copied native V5 schema, counts or reserved fields are invalid.");
        return value;
    }
    internal NativeEngineTransportPublicationV5 CopyWire()
    {
        var buffer = Marshal.AllocHGlobal(_bytes.Length);
        try { Marshal.Copy(_bytes, 0, buffer, _bytes.Length); return Marshal.PtrToStructure<NativeEngineTransportPublicationV5>(buffer); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    internal byte[] CopyBytes() => _bytes.ToArray();
    internal bool SameBytes(NativeEngineTransportPublication other)
        => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);
    internal bool IsZeroRange(int offset, int bytes)
    {
        if (offset < 0 || bytes < 0 || offset > _bytes.Length - bytes) return false;
        foreach (var value in _bytes.AsSpan(offset, bytes)) if (value != 0) return false;
        return true;
    }
}
