using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        private readonly object _gate = new();
        private readonly VerifiedHookTarget _target;
        private readonly IntPtr _module;
        private readonly DobbyDetourEngine _engine;
        private NativePlaybackExecutableSeal? _image;
        private readonly NativePlaybackExecutableSeal _dobby;
        private PlaybackSourceInstallGroup? _group;
        private NativePlaybackExecutableSeal? _helper;
        private readonly NativePlaybackExecutableSeal.RangeSeal _method, _methodName, _returnType;
        private readonly byte[] _originalBody;
        private readonly NativePlaybackSourceTrampolineProof.SourceFunctionSeal _sourceFunctions;
        private readonly string _helperPath, _helperHash;
        private readonly Func<NativePlaybackSingleReturnSample> _readClock;
        private readonly Action<NativePlaybackSingleReturnFault> _faultOwner;
        private readonly Action<NativePlaybackSingleReturnFrame>? _before;
        private readonly Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? _after;
        private NativePlaybackSingleReturnBridge? _bridge;
        private NativePlaybackExecutableSeal.RangeSeal? _source, _relocation, _relay, _originalThunk;
        private NativePlaybackTrampolineUnwind? _unwind;
        private IntPtr _original;
        private IntPtr _dobbyOriginal;
        private int _active, _committed, _notified;
        private string? _fault;
        internal IntPtr TargetAddress => _target.Address;
        internal string OriginalBodySha256 { get; }
        internal int OriginalBodyLength => _originalBody.Length;
        internal int PatchedByteCount { get; private set; }
        internal IntPtr OriginalAddress => _original;
        internal IntPtr DobbyOriginalAddress => _dobbyOriginal;
        internal IntPtr TrampolineUnwindAddress => _unwind?.InformationAddress ?? IntPtr.Zero;
        internal IntPtr EntryAddress => _bridge?.EntryAddress ?? IntPtr.Zero;
        internal bool IsActive => Volatile.Read(ref _active) == 1 && Volatile.Read(ref _fault) is null && (_group is null || _group.IsActive);
        internal IntPtr Module => _module;
        internal IntPtr MethodInfo => _target.MethodInfo;
        internal PlaybackSourceInstallGroup? Group => _group;
        internal bool HasEngine(DobbyDetourEngine engine) => ReferenceEquals(engine, _engine);

        private PlaybackOwner(VerifiedHookTarget target, IntPtr module, string expectedImageSha256,
            int bodyLength, string bodyHash, DobbyDetourEngine engine, string helperPath, string helperHash,
            Func<NativePlaybackSingleReturnSample> readClock, Action<NativePlaybackSingleReturnFault> faultOwner,
            Action<NativePlaybackSingleReturnFrame>? before, Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? after,
            PlaybackSourceInstallGroup? group = null)
        {
            if (target.Address == IntPtr.Zero || target.MethodInfo == IntPtr.Zero || target.DeclaringClass == IntPtr.Zero
                || bodyLength is < 32 or > 65536 || bodyHash.Length != 64)
                throw new UnsupportedSignatureException("GetTime requires a bounded exact native body and nonzero metadata identity.");
            (_target, _module, _engine, _helperPath, _helperHash, _readClock, _faultOwner, _before, _after)
                = (target, module, engine, helperPath, helperHash, readClock, faultOwner, before, after);
            HookTargetValidation.VerifyExecutable(target.Address, module);
            _group = group;
            if (group is null) _image = NativePlaybackExecutableSeal.Capture(module, expectedImageSha256, target.Address, bodyLength);
            _dobby = engine.RetainPlaybackAuthority();
            _source = NativePlaybackExecutableSeal.RangeSeal.Capture(target.Address, bodyLength, module, true, true);
            _originalBody = (byte[])_source.Bytes.Clone();
            OriginalBodySha256 = Convert.ToHexString(SHA256.HashData(_originalBody)).ToLowerInvariant();
            if (!OriginalBodySha256.Equals(bodyHash, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("GetTime original body differs from its required prehook fingerprint.");
            _sourceFunctions = new NativePlaybackSourceTrampolineProof.SourceFunctionSeal(target.Address, bodyLength, 6);
            HookTargetValidation.VerifyReadable(target.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var metadata = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(target.MethodInfo);
            if (metadata.MethodPointer != target.Address || metadata.Class != target.DeclaringClass || metadata.ParameterCount != 0
                || (metadata.Flags & 0x10) != 0 || (metadata.Bitfield & 3) != 0 || metadata.ReturnType == IntPtr.Zero || metadata.Name == IntPtr.Zero)
                throw new UnsupportedSignatureException("GetTime metadata is not the exact nongeneric instance zero-argument method.");
            _method = NativePlaybackExecutableSeal.RangeSeal.Capture(target.MethodInfo,
                Marshal.SizeOf<HookTargetValidation.MethodInfo2022>(), IntPtr.Zero, false);
            _methodName = NativePlaybackExecutableSeal.RangeSeal.Capture(metadata.Name, 8, IntPtr.Zero, false);
            _returnType = NativePlaybackExecutableSeal.RangeSeal.Capture(metadata.ReturnType, 16, IntPtr.Zero, false);
            if (!_methodName.Bytes.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes("GetTime\0")))
                throw new UnsupportedSignatureException("GetTime metadata name differs.");
            var typeBits = BitConverter.ToUInt32(_returnType.Bytes, 8);
            if (((typeBits >> 16) & 0xff) != 0x0c || (typeBits & 0x40000000) != 0)
                throw new UnsupportedSignatureException("GetTime return metadata is not a non-byref System.Single primitive.");
        }
        internal static PlaybackOwner Preflight(VerifiedHookTarget target, IntPtr module, string expectedImageSha256,
            int bodyLength, string bodyHash, DobbyDetourEngine engine, string helperPath, string helperHash,
            Func<NativePlaybackSingleReturnSample> readClock, Action<NativePlaybackSingleReturnFault> faultOwner,
            Action<NativePlaybackSingleReturnFrame>? before, Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? after)
            => new(target, module, expectedImageSha256, bodyLength, bodyHash, engine, helperPath, helperHash, readClock, faultOwner, before, after);
        internal static PlaybackOwner PreflightGroup(PlaybackSourceInstallGroup group, VerifiedHookTarget target, IntPtr module,
            string imageHash, int bodyLength, string bodyHash, DobbyDetourEngine engine, string helperPath, string helperHash,
            Func<NativePlaybackSingleReturnSample> readClock, Action<NativePlaybackSingleReturnFault> faultOwner,
            Action<NativePlaybackSingleReturnFrame>? before, Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? after)
            => new(target, module, imageHash, bodyLength, bodyHash, engine, helperPath, helperHash, readClock, faultOwner, before, after, group);
        internal void AdoptIntoGroup(PlaybackSourceInstallGroup group)
        {
            group.VerifySealIssuer(_module);
            if (_group is not null || !OwnsPlaybackInstallation(this))
                throw new UnsupportedSignatureException("GetTime is already grouped or lacks its actual retained broker owner.");
            VerifyCurrent(_module, TargetAddress, _target.MethodInfo);
            if (_bridge!.ReadCurrent().Active || _bridge.ReadHealth().InFlight != 0)
                throw new UnsupportedSignatureException("A live GetTime policy or actual in-flight call prevents group adoption.");
            _group = group;
        }
        internal void ComposeIntoPinnedFile(uint sectionRva, byte[] expected,
            PlaybackSourceInstallGroup.VerificationTraversal? traversal = null)
        {
            VerifyMemberEvidence(traversal);
            var relative = TargetAddress.ToInt64() - _module.ToInt64() - sectionRva;
            if (relative < 0 || relative >= expected.Length) return;
            if (_originalBody.Length > expected.Length - relative
                || !expected.AsSpan(checked((int)relative), _originalBody.Length).SequenceEqual(_originalBody))
                throw new UnsupportedSignatureException("The GetTime owner's private original differs from the pinned file plus DIR64.");
            _source!.Bytes.CopyTo(expected, checked((int)relative));
        }
        private NativePlaybackExecutableSeal CurrentImage => _group?.Image ?? _image!;
        internal bool MatchesOriginalBody(IntPtr module, IntPtr address, IntPtr methodInfo, string hash, int length)
        {
            VerifyCurrent(module, address, methodInfo);
            // The retained complete body and its hash cannot change after preflight.
            return length == OriginalBodyLength && OriginalBodySha256.Equals(hash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
