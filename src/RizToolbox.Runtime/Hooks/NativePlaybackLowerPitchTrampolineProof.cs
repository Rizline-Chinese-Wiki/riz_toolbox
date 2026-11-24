using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Exact native-code lower prefix relocation; no MethodInfo and no
/// caller-provided instruction/unwind shape is interpreted as authority.</summary>
internal sealed class NativePlaybackLowerPitchTrampolineProof
{
    internal const int PrefixLength = 7;
    private readonly NativePlaybackLowerPitchTarget _target;
    private readonly NativePlaybackLowerPitchTarget.EnginePitchPreflight? _pair;
    private readonly NativePlaybackExecutableSeal.RangeSeal? _tail;
    private readonly NativePlaybackExecutableSeal.RangeSeal _relocation, _thunk;
    private readonly NativePlaybackLowerPitchTrampolineUnwind _unwind;
    private readonly NativePlaybackSourceTrampolineProof.SourceFunctionSeal _functions;
    internal IntPtr OriginalAddress { get; }
    internal IntPtr InformationAddress => _unwind.InformationAddress;

    internal NativePlaybackLowerPitchTrampolineProof(NativePlaybackLowerPitchTarget target,
        ReadOnlySpan<byte> originalBody, IntPtr dobbyOriginal)
        : this(target, originalBody, dobbyOriginal, null) { }

    internal NativePlaybackLowerPitchTrampolineProof(NativePlaybackLowerPitchTarget target,
        ReadOnlySpan<byte> originalBody, IntPtr dobbyOriginal,
        NativePlaybackLowerPitchTarget.EnginePitchPreflight? pair)
    {
        _target = target; target.VerifyCurrent();
        if (pair is not null)
        {
            if (!ReferenceEquals(pair.Parent, target))
                throw new UnsupportedSignatureException("The lower relocation's fixed native pair has another private parent.");
            pair.VerifyUninstalled(); _pair = pair;
            _tail = NativePlaybackExecutableSeal.RangeSeal.Capture(IntPtr.Add(target.Address, PrefixLength),
                target.BodyLength - PrefixLength, target.Module, true, true);
        }
        if (originalBody.Length != target.BodyLength || dobbyOriginal == IntPtr.Zero || dobbyOriginal == target.Address
            || Convert.ToHexString(SHA256.HashData(originalBody)).ToLowerInvariant() != target.BodySha256
            || !originalBody[..PrefixLength].SequenceEqual(new byte[] { 0x48, 0x8b, 0xc4, 0x48, 0x89, 0x58, 0x10 }))
            throw new UnsupportedSignatureException("The lower relocation requires its exact issued full body and fixed two-instruction prefix.");
        _functions = new(target.Address, originalBody.Length, PrefixLength, NativePlaybackSourceRole.LowerSetPitch);
        _relocation = NativePlaybackExecutableSeal.RangeSeal.Capture(dobbyOriginal, PrefixLength + 14, IntPtr.Zero, false, true);
        if (!_relocation.Bytes.AsSpan(0, PrefixLength).SequenceEqual(originalBody[..PrefixLength])
            || !_relocation.Bytes.AsSpan(PrefixLength, 6).SequenceEqual(new byte[] { 0xff, 0x25, 0, 0, 0, 0 })
            || BitConverter.ToInt64(_relocation.Bytes, PrefixLength + 6) != checked(target.Address.ToInt64() + PrefixLength))
            throw new UnsupportedSignatureException("The actual Dobby lower relocation is not the complete fixed prefix and exact continuation.");
        var code = new byte[PrefixLength + 13]; originalBody[..PrefixLength].CopyTo(code);
        code[PrefixLength] = 0x49; code[PrefixLength + 1] = 0xbb;
        BitConverter.GetBytes(checked(target.Address.ToInt64() + PrefixLength)).CopyTo(code, PrefixLength + 2);
        code[PrefixLength + 10] = 0x41; code[PrefixLength + 11] = 0xff; code[PrefixLength + 12] = 0xe3;
        OriginalAddress = VirtualAlloc(IntPtr.Zero, 8192, 0x3000, 4);
        if (OriginalAddress == IntPtr.Zero) throw new UnsupportedSignatureException("The private lower original thunk allocation failed.");
        Marshal.Copy(code, 0, OriginalAddress, code.Length);
        if (!VirtualProtect(OriginalAddress, 4096, 0x20, out _)
            || !FlushInstructionCache((IntPtr)(-1), OriginalAddress, (nuint)code.Length))
            throw new UnsupportedSignatureException("The private lower original thunk was not sealed RX and flushed.");
        _thunk = NativePlaybackExecutableSeal.RangeSeal.Capture(OriginalAddress, code.Length, OriginalAddress, false, true);
        _unwind = new(OriginalAddress, IntPtr.Add(OriginalAddress, 4096)); Verify();
    }
    internal void Verify()
        => Verify(null);
    internal void Verify(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
        PlaybackSourceGroupTargets.TargetFamilyVerification? family = null)
    {
        // The closed pair separately composes both actual patches into the
        // complete pinned image. Its mechanical relocation must not recurse
        // through the old single-exclusion image after the engine is patched.
        if (_pair is null)
        {
            if (family is not null)
                throw new UnsupportedSignatureException("Only the actual native pair can share TargetFamily ancestors.");
            _target.VerifyCurrent();
        }
        else
        {
            // The lower proof keeps its own target's local supporting checks;
            // only the entry-verified stock parent is shared. The engine's
            // supporting evidence then consumes that same explicit ticket.
            if (traversal is not null || family is not null) _target.VerifySupportingEvidence(traversal, family);
            _pair.VerifySupportingEvidence(traversal, family);
        }
        _tail?.Verify(); _relocation.Verify(); _thunk.Verify(); _functions.Verify(); _unwind.Verify();
    }
    [DllImport("kernel32")] private static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint allocationType, uint protection);
    internal void VerifyOperation()
    {
        _target.VerifyOperationSupportingEvidence();
        _pair?.VerifyOperationSupportingEvidence();
        _tail?.Verify(); _relocation.Verify(); _thunk.Verify(); _functions.Verify(); _unwind.Verify();
    }
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint length);
}
