using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // Only BindNativeJudgeContext's actual Prepare/Configure/Commit can create
    // this owner. No interface bridge, copied packet or CLR delegate issues it.
    private sealed class NativeJudgeCodeIdentity : ReferenceImageMember
    {
        private const int PrefixLength = 64;
        private readonly Entry _entry;
        private readonly VerifiedHookTarget _target;
        private readonly DobbyDetourEngine _engine;
        private readonly NativeJudgeContextBridge _bridge;
        private readonly IMainThreadDispatcher _main;
        private readonly IntPtr _destination;
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

        internal static NativeJudgeCodeIdentity? TryCapture(Entry entry, DobbyDetourEngine engine,
            NativeJudgeContextBridge bridge, IMainThreadDispatcher main, ILogger log)
        {
            main.VerifyAccess();
            if (!System.Threading.Monitor.IsEntered(Gate) || !ReferenceEquals(entry.Engine, engine)
                || !ReferenceEquals(entry.NativeJudgeBridge, bridge)
                || entry.Target.Abi is not (ExactHookAbi.InstanceEnum32SingleVoid or ExactHookAbi.InstanceInt32SingleVoid)
                || entry.Target.ReturnClass != IntPtr.Zero)
                throw new UnsupportedSignatureException("Native judge image evidence requires its actual broker, Dobby and concrete bridge.");
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
                    LogUnavailable(log, entry.Target, module, before, "Unknown complete relocation prefix"); return null;
                }
                return new NativeJudgeCodeIdentity(entry, engine, bridge, main, module, source, moved);
            }
            catch (UnsupportedSignatureException error)
            {
                // An unproved prefix has no composition right; observation
                // still follows the existing physical installation path.
                LogUnavailable(log, entry.Target, module, before, error.Message); return null;
            }
        }
        private NativeJudgeCodeIdentity(Entry entry, DobbyDetourEngine engine, NativeJudgeContextBridge bridge,
            IMainThreadDispatcher main, IntPtr module, NativePlaybackExecutableSeal.RangeSeal before, byte[] moved)
        {
            (_entry, _target, _engine, _bridge, _main, Module, _before, _movedPrefix)
                = (entry, entry.Target, engine, bridge, main, module, before, moved);
            _destination = bridge.EntryAddress;
            _detour = engine.RetainPlaybackAuthority(); _helper = bridge.RetainCodeAuthority();
            VerifyUninstalled();
            if (!ReferenceCodeIdentity.GetModuleHandleEx(4, Address, out var held) || held != Module)
                throw new UnsupportedSignatureException("Native judge image evidence could not retain its exact source module.");
        }
        internal void Prepared(IntPtr original)
        {
            try
            {
                if (_committed || _preparedOriginal != IntPtr.Zero)
                    throw new InvalidOperationException("Native judge image evidence retains one actual Prepare result.");
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
                    throw new InvalidOperationException("The native judge source has no one-time proved Prepare result.");
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
                    throw new InvalidOperationException("Native judge image evidence commits one actual prepared installation.");
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
                || !ReferenceEquals(_entry.NativeJudgeBridge, _bridge) || !ReferenceEquals(_entry.NativeMain, _main)
                || _entry.NativeInstallFailure is not null || _entry.Original is not null || _entry.Replacement is not null
                || _bridge.EntryAddress != _destination)
                throw new UnsupportedSignatureException("The native judge image member lost its actual permanent broker/bridge owner.");
        }
        private void VerifyMetadata()
        {
            HookTargetValidation.VerifyReadable(_target.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var method = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(_target.MethodInfo);
            if (method.MethodPointer != Address || method.Class != _target.DeclaringClass)
                throw new UnsupportedSignatureException("The native judge metadata no longer names its exact source entry.");
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
                        throw new UnsupportedSignatureException("No committed native judge image identity belongs to this broker entry.");
                    VerifyPublication(); VerifyEvidence();
                }
                catch { MarkUnknown(); throw; }
            }
        }
        internal override void VerifyCanonicalPreimage(uint sectionRva, ReadOnlySpan<byte> expected)
        {
            var relative = Address.ToInt64() - Module.ToInt64() - sectionRva;
            if (relative >= expected.Length || relative + PrefixLength <= 0) return;
            if (relative < 0 || PrefixLength > expected.Length - relative
                || !expected.Slice(checked((int)relative), PrefixLength).SequenceEqual(_before.Bytes))
                throw new UnsupportedSignatureException("The native judge pre-Prepare bytes differ from the pinned file plus DIR64.");
        }
        internal override void ApplyPatch(uint sectionRva, Span<byte> expected)
        {
            var relative = Address.ToInt64() - Module.ToInt64() - sectionRva;
            if (relative >= expected.Length || relative + PatchLength <= 0) return;
            if (relative < 0 || PatchLength > expected.Length - relative)
                throw new UnsupportedSignatureException("A canonical section splits a native judge branch write.");
            _installed!.Bytes.AsSpan().CopyTo(expected.Slice(checked((int)relative), PatchLength));
        }
        private void MarkUnknown()
        { _unknown = true; Interlocked.Exchange(ref _entry.JudgeActivityUnknown, 1); }
        private static void LogUnavailable(ILogger log, VerifiedHookTarget target, IntPtr module, byte[]? before, string reason)
        {
            try
            {
                log.Log(LogLevel.Warning, "Native judge hook has no image composition identity: target=" + target.Id
                    + "; address=0x" + target.Address.ToInt64().ToString("x")
                    + "; rva=" + (module == IntPtr.Zero ? "unavailable" : "0x" + (target.Address.ToInt64() - module.ToInt64()).ToString("x"))
                    + "; before16=" + (before is null ? "unavailable" : Convert.ToHexString(before.AsSpan(0, 16)))
                    + "; reason=" + reason + ".");
            }
            catch { }
        }
    }
}
