using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // One permanent member is issued by the actual broker Prepare/Configure/
    // Commit path and its concrete native publication. Logical policies may
    // leave or rebind without conferring new native authority.
    private sealed class NativeInitializationCodeIdentity : ReferenceImageMember
    {
        private const int PreimageLength = 64;
        private static readonly Site Start = new(0x3ea580, "Start", 0, ExactHookAbi.InstanceVoid,
            new byte[] { 0x48, 0x8b, 0xc4, 0x48, 0x89, 0x48, 0x08 },
            "91b194a3295a8d13f96239afe34184f4029962dae2e7973ed3a527ef8ccf42be");
        private static readonly Site Getter = new(0x3bedf0, "GetInsideLevelParams", 1, ExactHookAbi.LocalInitializationReference1,
            new byte[] { 0x40, 0x53, 0x57, 0x48, 0x83, 0xec, 0x78 },
            "999644175601ad558a75caf9ef921cde73032f75a8d06eb1ea4f966699589c75");
        private sealed record Site(int Rva, string Name, byte Parameters, ExactHookAbi Abi, byte[] Prefix, string PreimageSha256);

        private readonly Entry _entry;
        private readonly VerifiedHookTarget _target;
        private readonly DobbyDetourEngine _engine;
        private readonly NativeInitializationContextBridge _bridge;
        private readonly IMainThreadDispatcher _main;
        private readonly NativeInitializationKind _kind;
        private readonly Site _site;
        private readonly IntPtr _destination;
        private readonly NativePlaybackExecutableSeal _detour, _helper;
        private readonly NativePlaybackExecutableSeal.RangeSeal _before, _methodName;
        private readonly HookTargetValidation.MethodInfo2022 _metadata;
        private NativePlaybackExecutableSeal.RangeSeal? _installed, _relay, _originalCode;
        private IntPtr _original;
        private bool _committed, _unknown;

        internal override IntPtr Module { get; }
        internal override IntPtr Address => _target.Address;
        internal override int PatchLength => _installed!.Bytes.Length;
        internal override bool Owns(Entry entry) => ReferenceEquals(_entry, entry);

        internal NativeInitializationCodeIdentity(Entry entry, DobbyDetourEngine engine,
            NativeInitializationContextBridge bridge, IMainThreadDispatcher main)
        {
            (_entry, _target, _engine, _bridge, _main, _kind, _destination) =
                (entry, entry.Target, engine, bridge, main, bridge.Kind, bridge.EntryAddress);
            (_site, Module, _before) = CaptureSource(_target, _kind);
            VerifyOwner();
            _metadata = ReadMetadata();
            var exactName = Encoding.ASCII.GetBytes(_site.Name + "\0");
            _methodName = NativePlaybackExecutableSeal.RangeSeal.Capture(_metadata.Name, exactName.Length, IntPtr.Zero, false);
            if (!_methodName.Bytes.AsSpan().SequenceEqual(exactName))
                throw new UnsupportedSignatureException("The native LOCAL initialization method name differs from its exact source.");
            VerifyMetadata();

            _detour = engine.RetainPlaybackAuthority();
            _helper = bridge.RetainCodeAuthority();
            if (!ReferenceCodeIdentity.GetModuleHandleEx(4, Address, out var held) || held != Module)
                throw new UnsupportedSignatureException("The native LOCAL initialization cannot retain its exact source image.");
        }

        internal static void Preflight(VerifiedHookTarget target, NativeInitializationKind kind)
            => CaptureSource(target, kind);

        private static (Site Site, IntPtr Module, NativePlaybackExecutableSeal.RangeSeal Before) CaptureSource(
            VerifiedHookTarget target, NativeInitializationKind kind)
        {
            var site = kind switch
            {
                NativeInitializationKind.Start => Start,
                NativeInitializationKind.Getter => Getter,
                _ => throw new UnsupportedSignatureException("Unknown typed LOCAL initialization source.")
            };
            var module = ReferenceCodeIdentity.Allocation(target.Address);
            if (target.Address != IntPtr.Add(module, site.Rva) || target.Abi != site.Abi
                || target.DeclaringClass == IntPtr.Zero || (target.ReturnClass == IntPtr.Zero) != (kind == NativeInitializationKind.Start))
                throw new UnsupportedSignatureException("The native LOCAL " + site.Name + " source is at a different fixed RVA or ABI.");
            var before = NativePlaybackExecutableSeal.RangeSeal.Capture(target.Address, PreimageLength, module, true, true);
            if (!Convert.ToHexString(SHA256.HashData(before.Bytes)).Equals(site.PreimageSha256, StringComparison.OrdinalIgnoreCase)
                || !before.Bytes.AsSpan(0, site.Prefix.Length).SequenceEqual(site.Prefix))
                throw new UnsupportedSignatureException("The original native LOCAL " + site.Name + " source differs from its fixed preimage.");
            return (site, module, before);
        }

        internal void Prepared(IntPtr original)
        {
            if (_original != IntPtr.Zero || _committed)
                throw new UnsupportedSignatureException("The native LOCAL initialization owns exactly one Prepare result.");
            VerifyOwner();
            _before.Verify();
            _originalCode = ReferenceCodeIdentity.ProveOriginal(Address, original, _destination, _site.Prefix);
            _original = original;
        }

        internal void VerifyBeforeCommit()
        {
            VerifyOwner();
            if (_committed || _original == IntPtr.Zero || _originalCode is null)
                throw new UnsupportedSignatureException("The native LOCAL initialization has no prepared original.");
            _bridge.VerifyPublishedConfiguration(_original);
            _before.Verify();
            _originalCode.Verify();
            _detour.Verify();
            _helper.Verify();
            VerifyMetadata();
            _bridge.VerifyPublishedConfiguration(_original);
        }

        internal void Installed()
        {
            VerifyOwner();
            if (_committed || _originalCode is null)
                throw new UnsupportedSignatureException("The native LOCAL initialization can publish only its actual prepared commit.");
            (_installed, _relay) = ReferenceCodeIdentity.ProveInstalledBranch(Address, Module, _destination, _before, _site.Prefix.Length);
            VerifyInstalledEvidence(null);
            PublishInstallation();
            _committed = true;
        }

        private void VerifyOwner()
        {
            _main.VerifyAccess();
            var current = _entry.LocalInitializationPolicy?.Targets;
            var source = _kind == NativeInitializationKind.Start ? current?.Start : current?.Getter;
            if (!Monitor.IsEntered(Gate) || _unknown || !Entries.TryGetValue(Address, out var entry) || !ReferenceEquals(entry, _entry)
                || !ReferenceEquals(_entry.Target, _target) || !ReferenceEquals(_entry.Engine, _engine)
                || !ReferenceEquals(_entry.NativeInitializationBridge, _bridge) || _bridge.Kind != _kind || _bridge.EntryAddress != _destination
                || _entry.NativeInstallFailure is not null || _entry.Replacement is not null || _entry.Original is not null
                || source is null || source.Address != Address || source.MethodInfo != _target.MethodInfo
                || source.DeclaringClass != _target.DeclaringClass || source.ReturnClass != _target.ReturnClass || source.Abi != _target.Abi)
                throw new UnsupportedSignatureException("The native LOCAL initialization lost its exact permanent broker and bridge owner.");
        }

        private HookTargetValidation.MethodInfo2022 ReadMetadata()
        {
            HookTargetValidation.VerifyReadable(_target.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            return Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(_target.MethodInfo);
        }

        private void VerifyMetadata()
        {
            var current = ReadMetadata();
            if (current.MethodPointer != Address || current.Class != _target.DeclaringClass
                || (current.Flags & (0x10 | 0x400)) != 0 || (current.Bitfield & 3) != 0
                || current.ParameterCount != _site.Parameters || current.ReturnType == IntPtr.Zero
                || _site.Parameters != 0 && current.Parameters == IntPtr.Zero
                || current.Name != _metadata.Name || current.ReturnType != _metadata.ReturnType || current.Parameters != _metadata.Parameters)
                throw new UnsupportedSignatureException("The native LOCAL initialization metadata no longer names its exact declared source.");
            _methodName.Verify();
        }

        private void VerifyInstalledEvidence(HashSet<NativePlaybackExecutableSeal>? verifiedImages)
        {
            VerifyOwner();
            _bridge.VerifyPublishedConfiguration(_original);
            if (verifiedImages is null || verifiedImages.Add(_detour)) _detour.Verify();
            if (verifiedImages is null || verifiedImages.Add(_helper)) _helper.Verify();
            _before.VerifyMemoryIdentity();
            _installed!.Verify();
            _relay?.Verify();
            _originalCode!.Verify();
            VerifyMetadata();
            _bridge.VerifyPublishedConfiguration(_original);
        }

        internal override void Verify() => Verify(null);
        internal void Verify(HashSet<NativePlaybackExecutableSeal>? verifiedImages)
        {
            lock (Gate)
            {
                try
                {
                    if (!_committed) throw new UnsupportedSignatureException("No committed native LOCAL initialization member exists.");
                    VerifyPublication();
                    VerifyInstalledEvidence(verifiedImages);
                }
                catch
                {
                    _unknown = true;
                    _bridge.MarkCodeIdentityUnknown();
                    throw;
                }
            }
        }

        internal override void VerifyCanonicalPreimage(uint sectionRva, ReadOnlySpan<byte> expected)
        {
            var relative = Address.ToInt64() - Module.ToInt64() - sectionRva;
            if (relative >= expected.Length || relative + PreimageLength <= 0) return;
            if (relative < 0 || PreimageLength > expected.Length - relative
                || !expected.Slice(checked((int)relative), PreimageLength).SequenceEqual(_before.Bytes))
                throw new UnsupportedSignatureException("The native LOCAL initialization preimage differs from the pinned file and relocations.");
        }

        internal override void ApplyPatch(uint sectionRva, Span<byte> expected)
        {
            var relative = Address.ToInt64() - Module.ToInt64() - sectionRva;
            if (relative >= expected.Length || relative + PatchLength <= 0) return;
            if (relative < 0 || PatchLength > expected.Length - relative)
                throw new UnsupportedSignatureException("A canonical section splits the native LOCAL initialization branch.");
            _installed!.Bytes.AsSpan().CopyTo(expected.Slice(checked((int)relative), PatchLength));
        }
    }
}
