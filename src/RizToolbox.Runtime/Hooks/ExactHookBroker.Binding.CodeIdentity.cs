using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class Binding
    {
        internal IntPtr TargetAddress { get { _sink.Main.VerifyAccess(); return _entry.Target.Address; } }

        /// <summary>Verify the complete pre-install method body through this
        /// exact retained binding. Only this installation's actual branch is
        /// reconstructed; every other byte remains part of the body hash.</summary>
        internal void VerifyOriginalBody(IntPtr module, IntPtr address, int length, string sha256)
        {
            _sink.Main.VerifyAccess();
            lock (Gate)
            {
                if (!_sink.Active || !_entry.Sinks.Contains(_sink) || _entry.NativeInstallFailure is not null
                    || !Entries.TryGetValue(address, out var registered) || !ReferenceEquals(registered, _entry)
                    || _entry.Target.Address != address
                    || !ReferenceCodeInstallations.TryGetValue(address, out var installed)
                    || installed is not CoroutineCodeIdentity code || !code.Owns(_entry))
                    throw new UnsupportedSignatureException("The original-body check has no exact active coroutine installation owner.");
                try { code.VerifyOriginalBody(module, length, sha256); }
                catch
                {
                    Interlocked.Exchange(ref _entry.CoroutineUnknown, 1);
                    throw;
                }
            }
        }
    }

    // Only the actual native Coroutine Prepare/Configure/Commit path can issue
    // this member. It shares the same private append-only registry as ordinary
    // and native judge owners, without borrowing a CLR replacement identity.
    private sealed class CoroutineCodeIdentity : ReferenceImageMember
    {
        private const int PrefixLength = 64;
        private readonly Entry _entry;
        private readonly VerifiedHookTarget _target;
        private readonly DobbyDetourEngine _engine;
        private readonly NativeCoroutineContextBridge _bridge;
        private readonly IMainThreadDispatcher _main;
        private readonly NativeCoroutineKind _kind;
        private readonly uint _slot;
        private readonly IntPtr _destination, _methodAlias;
        private readonly NativePlaybackExecutableSeal _detour, _helper;
        private readonly NativePlaybackExecutableSeal.RangeSeal _before;
        private readonly byte[] _movedPrefix;
        private NativePlaybackExecutableSeal.RangeSeal? _installed, _relay, _originalCode;
        private IntPtr _preparedOriginal;
        private bool _committed, _unknown;
        internal override IntPtr Module { get; }
        internal override IntPtr Address => _target.Address;
        internal override int PatchLength => _installed!.Bytes.Length;
        internal override bool Owns(Entry entry) => ReferenceEquals(entry, _entry);

        internal static CoroutineCodeIdentity? TryCapture(Entry entry, DobbyDetourEngine engine,
            NativeCoroutineContextBridge bridge, IMainThreadDispatcher main, ILogger log)
        {
            main.VerifyAccess();
            if (!System.Threading.Monitor.IsEntered(Gate) || !ReferenceEquals(entry.Engine, engine)
                || !ReferenceEquals(entry.CoroutineBridge, bridge) || !MatchesAbi(entry.Target, bridge.Kind))
                throw new UnsupportedSignatureException("Native coroutine image evidence requires its actual broker, Dobby, concrete bridge and exact ABI.");
            var module = IntPtr.Zero;
            byte[]? before = null;
            try
            {
                module = ReferenceCodeIdentity.Allocation(entry.Target.Address);
                HookTargetValidation.VerifyExecutable(entry.Target.Address, module);
                var source = NativePlaybackExecutableSeal.RangeSeal.Capture(entry.Target.Address, PrefixLength, module, true, true);
                before = source.Bytes;
                var moved = ReferenceCodeIdentity.MatchMovedPrefix(before);
                if (moved is null)
                {
                    LogUnavailable(log, entry.Target, bridge, module, before, "Unknown complete relocation prefix"); return null;
                }
                return new CoroutineCodeIdentity(entry, engine, bridge, main, module, source, moved);
            }
            catch (UnsupportedSignatureException error)
            {
                // Unknown or unavailable preimage evidence grants no composition
                // identity. Ordinary native forwarding retains its existing path.
                LogUnavailable(log, entry.Target, bridge, module, before, error.Message); return null;
            }
        }
        private CoroutineCodeIdentity(Entry entry, DobbyDetourEngine engine, NativeCoroutineContextBridge bridge,
            IMainThreadDispatcher main, IntPtr module, NativePlaybackExecutableSeal.RangeSeal before, byte[] moved)
        {
            (_entry, _target, _engine, _bridge, _main, Module, _before, _movedPrefix)
                = (entry, entry.Target, engine, bridge, main, module, before, moved);
            _destination = bridge.EntryAddress; _kind = bridge.Kind; _slot = bridge.Slot;
            _methodAlias = entry.CoroutineMethodAlias;
            _detour = engine.RetainPlaybackAuthority(); _helper = bridge.RetainCodeAuthority();
            VerifyUninstalled();
            if (!ReferenceCodeIdentity.GetModuleHandleEx(4, Address, out var held) || held != Module)
                throw new UnsupportedSignatureException("Native coroutine image evidence could not retain its exact source module.");
        }
        private static bool MatchesAbi(VerifiedHookTarget target, NativeCoroutineKind kind)
            => kind switch
            {
                NativeCoroutineKind.Start => target.Abi == ExactHookAbi.CoroutineStartReference1 && target.ReturnClass != IntPtr.Zero,
                NativeCoroutineKind.MoveNext => target.Abi == ExactHookAbi.InstanceBooleanReturn0 && target.ReturnClass == IntPtr.Zero,
                NativeCoroutineKind.Factory => target.Abi == ExactHookAbi.CoroutineFactoryReference0 && target.ReturnClass != IntPtr.Zero,
                _ => false
            };
        internal void Prepared(IntPtr original)
        {
            try
            {
                if (_committed || _preparedOriginal != IntPtr.Zero)
                    throw new InvalidOperationException("Native coroutine image evidence retains one actual Prepare result.");
                VerifyUninstalled();
                _preparedOriginal = original;
                _originalCode = ReferenceCodeIdentity.ProveOriginal(Address, original, _destination, _movedPrefix);
                VerifyUninstalled(); _originalCode.Verify();
            }
            catch { MarkUnknown(); throw; }
        }
        internal void VerifyBeforeCommit()
        {
            try
            {
                if (_committed || _preparedOriginal == IntPtr.Zero || _originalCode is null)
                    throw new InvalidOperationException("The native coroutine source has no one-time proved Prepare result.");
                _bridge.VerifyConfiguredOriginal(_preparedOriginal);
                VerifyUninstalled(); _originalCode.Verify();
                _bridge.VerifyConfiguredOriginal(_preparedOriginal);
            }
            catch { MarkUnknown(); throw; }
        }
        internal void Installed()
        {
            try
            {
                if (_committed || _preparedOriginal == IntPtr.Zero || _originalCode is null)
                    throw new InvalidOperationException("Native coroutine image evidence commits one actual prepared installation.");
                VerifyRegistration(); _bridge.VerifyConfiguredOriginal(_preparedOriginal); _originalCode.Verify();
                (_installed, _relay) = ReferenceCodeIdentity.ProveInstalledBranch(Address, Module,
                    _destination, _before, _movedPrefix.Length);
                VerifyEvidence(); PublishInstallation(); _committed = true;
            }
            catch { MarkUnknown(); throw; }
        }
        private void VerifyRegistration()
        {
            _main.VerifyAccess();
            if (!System.Threading.Monitor.IsEntered(Gate) || _unknown
                || !Entries.TryGetValue(Address, out var actual) || !ReferenceEquals(actual, _entry)
                || !ReferenceEquals(_entry.Target, _target) || !ReferenceEquals(_entry.Engine, _engine)
                || !ReferenceEquals(_entry.CoroutineBridge, _bridge) || !ReferenceEquals(_entry.NativeMain, _main)
                || _entry.NativeInstallFailure is not null || _entry.Original is not null || _entry.Replacement is not null
                || _entry.CoroutineMethodAlias != _methodAlias || _bridge.EntryAddress != _destination
                || _bridge.Kind != _kind || _bridge.Slot != _slot || !MatchesAbi(_target, _kind))
                throw new UnsupportedSignatureException("The native coroutine image member lost its actual permanent broker, engine, kind, slot or bridge owner.");
        }
        private void VerifyMetadata()
        {
            VerifyMethod(_target.MethodInfo);
            if (_methodAlias != IntPtr.Zero) VerifyMethod(_methodAlias);
        }
        private void VerifyMethod(IntPtr metadata)
        {
            HookTargetValidation.VerifyReadable(metadata, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var method = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(metadata);
            if (method.MethodPointer != Address || method.Class != _target.DeclaringClass)
                throw new UnsupportedSignatureException("The native coroutine metadata no longer names its exact source entry.");
        }
        private void VerifyUninstalled()
        {
            VerifyRegistration(); _detour.Verify(); _helper.Verify(); _before.Verify(); VerifyMetadata();
        }
        private void VerifyEvidence()
        {
            VerifyRegistration(); _bridge.VerifyConfiguredOriginal(_preparedOriginal);
            _detour.Verify(); _helper.Verify(); _before.VerifyMemoryIdentity();
            _installed!.Verify(); _relay?.Verify(); _originalCode!.Verify(); VerifyMetadata();
            _bridge.VerifyConfiguredOriginal(_preparedOriginal);
        }
        internal override void Verify()
        {
            lock (Gate)
            {
                try
                {
                    if (!_committed)
                        throw new UnsupportedSignatureException("No committed native coroutine image identity belongs to this broker entry.");
                    VerifyPublication(); VerifyEvidence();
                }
                catch { MarkUnknown(); throw; }
            }
        }
        internal void VerifyOriginalBody(IntPtr module, int length, string sha256)
        {
            try
            {
                Verify();
                if (module != Module || length < PatchLength || length > 1024 * 1024 || string.IsNullOrWhiteSpace(sha256))
                    throw new UnsupportedSignatureException("The requested method body does not belong to the retained source image.");
                var actual = NativePlaybackExecutableSeal.RangeSeal.Capture(Address, length, Module, true, true);
                var original = (byte[])actual.Bytes.Clone();
                _before.Bytes.AsSpan(0, PatchLength).CopyTo(original);
                if (!Convert.ToHexString(SHA256.HashData(original)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("The complete original coroutine method body differs from its fixed profile.");
                actual.Verify(); Verify();
            }
            catch { MarkUnknown(); throw; }
        }
        internal override void VerifyCanonicalPreimage(uint sectionRva, ReadOnlySpan<byte> expected)
        {
            var relative = Address.ToInt64() - Module.ToInt64() - sectionRva;
            if (relative >= expected.Length || relative + PrefixLength <= 0) return;
            if (relative < 0 || PrefixLength > expected.Length - relative
                || !expected.Slice(checked((int)relative), PrefixLength).SequenceEqual(_before.Bytes))
                throw new UnsupportedSignatureException("The native coroutine pre-Prepare bytes differ from the pinned file plus DIR64.");
        }
        internal override void ApplyPatch(uint sectionRva, Span<byte> expected)
        {
            var relative = Address.ToInt64() - Module.ToInt64() - sectionRva;
            if (relative >= expected.Length || relative + PatchLength <= 0) return;
            if (relative < 0 || PatchLength > expected.Length - relative)
                throw new UnsupportedSignatureException("A canonical section splits a native coroutine branch write.");
            _installed!.Bytes.AsSpan().CopyTo(expected.Slice(checked((int)relative), PatchLength));
        }
        private void MarkUnknown()
        { _unknown = true; Interlocked.Exchange(ref _entry.CoroutineUnknown, 1); }
        private static void LogUnavailable(ILogger log, VerifiedHookTarget target, NativeCoroutineContextBridge bridge,
            IntPtr module, byte[]? before, string reason)
        {
            try
            {
                if (before is null && module != IntPtr.Zero)
                {
                    try { before = NativePlaybackExecutableSeal.RangeSeal.Capture(target.Address, 16, module, true, true).Bytes; }
                    catch (UnsupportedSignatureException) { }
                }
                log.Log(LogLevel.Warning, "Native coroutine hook has no image composition identity: target=" + target.Id
                    + "; kind=" + bridge.Kind + "; slot=" + bridge.Slot
                    + "; address=0x" + target.Address.ToInt64().ToString("x")
                    + "; rva=" + (module == IntPtr.Zero ? "unavailable" : "0x" + (target.Address.ToInt64() - module.ToInt64()).ToString("x"))
                    + "; before16=" + (before is null ? "unavailable" : Convert.ToHexString(before.AsSpan(0, Math.Min(16, before.Length))))
                    + "; reason=" + reason + ".");
            }
            catch { }
        }
    }
}
