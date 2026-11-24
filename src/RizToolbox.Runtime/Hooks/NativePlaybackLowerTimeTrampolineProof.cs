using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Mechanical proof for the fixed six-byte lower-time
/// prefix. Actual Dobby relocation, RX continuation and OS unwind must all
/// verify when prepared by the retained family owner.</summary>
internal sealed class NativePlaybackLowerTimeTrampolineProof
{
    internal const int PrefixLength = 6;
    private readonly NativePlaybackLowerPitchTarget.LowerTimePreflight _target;
    private readonly NativePlaybackExecutableSeal.RangeSeal _tail, _relocation, _thunk;
    private readonly NativePlaybackLowerTimeTrampolineUnwind _unwind;
    internal IntPtr OriginalAddress { get; }
    internal IntPtr InformationAddress => _unwind.InformationAddress;

    internal NativePlaybackLowerTimeTrampolineProof(NativePlaybackLowerPitchTarget.LowerTimePreflight target,
        ReadOnlySpan<byte> originalBody, IntPtr dobbyOriginal)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target; target.VerifyUninstalled();
        if (originalBody.Length != target.BodyLength || dobbyOriginal == IntPtr.Zero || dobbyOriginal == target.Address
            || Convert.ToHexString(SHA256.HashData(originalBody)).ToLowerInvariant() != target.BodySha256
            || !originalBody[..PrefixLength].SequenceEqual(new byte[] { 0x48, 0x89, 0x5c, 0x24, 0x10, 0x55 }))
            throw new UnsupportedSignatureException("The lower-time relocation requires its complete fixed body and MOV [RSP+10h],RBX; PUSH RBP prefix.");
        _tail = NativePlaybackExecutableSeal.RangeSeal.Capture(IntPtr.Add(target.Address, PrefixLength),
            target.BodyLength - PrefixLength, target.Module, true, true);
        if (!_tail.Bytes.AsSpan().SequenceEqual(originalBody[PrefixLength..]))
            throw new UnsupportedSignatureException("The complete lower-time continuation changed before relocation.");
        _relocation = NativePlaybackExecutableSeal.RangeSeal.Capture(dobbyOriginal, PrefixLength + 14, IntPtr.Zero, false, true);
        if (_relocation.StartProtection != 0x20
            || !_relocation.Bytes.AsSpan(0, PrefixLength).SequenceEqual(originalBody[..PrefixLength])
            || !_relocation.Bytes.AsSpan(PrefixLength, 6).SequenceEqual(new byte[] { 0xff, 0x25, 0, 0, 0, 0 })
            || BitConverter.ToInt64(_relocation.Bytes, PrefixLength + 6) != checked(target.Address.ToInt64() + PrefixLength))
            throw new UnsupportedSignatureException("The actual Dobby lower-time original is not the fixed RX six-byte prefix and target+6 continuation.");
        var code = new byte[PrefixLength + 13]; originalBody[..PrefixLength].CopyTo(code);
        code[PrefixLength] = 0x49; code[PrefixLength + 1] = 0xbb;
        BitConverter.GetBytes(checked(target.Address.ToInt64() + PrefixLength)).CopyTo(code, PrefixLength + 2);
        code[PrefixLength + 10] = 0x41; code[PrefixLength + 11] = 0xff; code[PrefixLength + 12] = 0xe3;
        OriginalAddress = VirtualAlloc(IntPtr.Zero, 8192, 0x3000, 4);
        if (OriginalAddress == IntPtr.Zero)
            throw new UnsupportedSignatureException("The private lower-time original thunk allocation failed.");
        Marshal.Copy(code, 0, OriginalAddress, code.Length);
        if (!VirtualProtect(OriginalAddress, 4096, 0x20, out _)
            || !FlushInstructionCache((IntPtr)(-1), OriginalAddress, (nuint)code.Length))
            throw new UnsupportedSignatureException("The private lower-time original thunk was not sealed RX and flushed.");
        _thunk = NativePlaybackExecutableSeal.RangeSeal.Capture(OriginalAddress, code.Length, OriginalAddress, false, true);
        _unwind = new(OriginalAddress, IntPtr.Add(OriginalAddress, 4096));
        Verify();
    }

    internal void Verify()
        => Verify(null);
    internal void Verify(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
        PlaybackSourceGroupTargets.TargetFamilyVerification? family = null)
    {
        _target.VerifySupportingEvidence(traversal, family);
        _tail.Verify(); _relocation.Verify(); _thunk.Verify(); _unwind.Verify();
    }
    [DllImport("kernel32")] private static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint allocationType, uint protection);
    internal void VerifyOperation()
    {
        _target.VerifyOperationSupportingEvidence();
        _tail.Verify(); _relocation.Verify(); _thunk.Verify(); _unwind.Verify();
    }
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint length);
}
