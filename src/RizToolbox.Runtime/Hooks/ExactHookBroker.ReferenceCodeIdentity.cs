using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // Only a completed installation below can append to this registry. Neither
    // a copied address/byte row nor a later live-image snapshot can issue a member.
    private static readonly Dictionary<IntPtr, ReferenceImageMember> ReferenceCodeInstallations = new();
    private static long _referenceCodeGeneration;

    // The stock clock can read the original Update through the actual retained
    // LOCAL branch. It owns no LOCAL callback or stock source permission.
    internal static bool TryVerifyLocalProgressUpdateBody(IntPtr module, IntPtr address, int length, string sha256)
    {
        const int updateRva = 0x400fe0, updateLength = 1728;
        const string updateHash = "850dc15418ec4e14c8981d2b56569c6399915fdf0d74a3f557e16f7bd33866c9";
        if (module == IntPtr.Zero || address != IntPtr.Add(module, updateRva) || length != updateLength
            || !updateHash.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            return false;
        lock (Gate)
        {
            if (!Entries.TryGetValue(address, out var entry) || entry.LocalProgressPolicy is not { } policy)
                return false;
            if (entry.Target.Abi != ExactHookAbi.InstanceVoid || policy.Targets.Progress.Address != address
                || policy.Targets.Progress.MethodInfo != entry.Target.MethodInfo
                || policy.Targets.Progress.DeclaringClass != entry.Target.DeclaringClass
                || !ReferenceCodeInstallations.TryGetValue(address, out var installed)
                || installed is not ReferenceCodeIdentity code || code.Module != module || !code.Owns(entry))
                throw new UnsupportedSignatureException("InLevel.ProgressControl.Update has no exact committed LOCAL trampoline and code owner.");
            code.VerifyOriginalBody(length, updateHash);
            return true;
        }
    }

    // Only the concrete CLR, native judge and native coroutine installation owners publish members.
    // The shared registry does not convert a native bridge into a CLR delegate.
    private abstract class ReferenceImageMember
    {
        internal abstract IntPtr Module { get; }
        internal abstract IntPtr Address { get; }
        internal abstract int PatchLength { get; }
        internal long Generation { get; private set; }
        internal abstract bool Owns(Entry entry);
        internal abstract void Verify();
        internal abstract void VerifyCanonicalPreimage(uint sectionRva, ReadOnlySpan<byte> expected);
        internal abstract void ApplyPatch(uint sectionRva, Span<byte> expected);
        protected void PublishInstallation()
        {
            if (!System.Threading.Monitor.IsEntered(Gate) || Generation != 0
                || ReferenceCodeInstallations.ContainsKey(Address) || _referenceCodeGeneration == long.MaxValue)
                throw new UnsupportedSignatureException("An installed image identity cannot be replaced or its generation exhausted.");
            Generation = _referenceCodeGeneration + 1;
            ReferenceCodeInstallations.Add(Address, this); _referenceCodeGeneration = Generation;
        }
        protected void VerifyPublication()
        {
            if (!ReferenceCodeInstallations.TryGetValue(Address, out var installed)
                || !ReferenceEquals(installed, this) || Generation <= 0 || Generation > _referenceCodeGeneration)
                throw new UnsupportedSignatureException("No committed image identity belongs to this broker entry.");
        }
    }

    /// <summary>Append-only membership of actual installed broker code owners.
    /// Every composition uses one fixed private snapshot while Gate is held.</summary>
    private sealed class ReferenceImageMembers
    {
        private readonly IntPtr _module;
        private readonly List<ReferenceImageMember> _members = new();
        private bool _unknown;
        private ReferenceImageMembers(IntPtr module) => _module = module;
        internal static ReferenceImageMembers Capture(IntPtr module)
        {
            lock (Gate)
            {
                var members = new ReferenceImageMembers(module); members.Verify(); return members;
            }
        }
        internal Snapshot CaptureSnapshot()
        {
            lock (Gate)
            {
                try
                {
                    if (_unknown) throw new UnsupportedSignatureException("The broker image membership is sticky unknown.");
                    var generation = _referenceCodeGeneration;
                    var current = Entries.Values.Select(entry =>
                        ReferenceCodeInstallations.TryGetValue(entry.Target.Address, out var code)
                            && code.Module == _module && code.Owns(entry) ? code : null)
                        .Where(code => code is not null).Cast<ReferenceImageMember>()
                        .OrderBy(code => code.Generation).ToArray();
                    if (current.Length < _members.Count)
                        throw new UnsupportedSignatureException("An installed broker image member was removed.");
                    // One explicit full-verification phase owns this local set.
                    // Different engines can contribute different Dobby seals;
                    // reference identity, not a path or file hash, selects reuse.
                    var verifiedDetours = new HashSet<NativePlaybackExecutableSeal>(ReferenceEqualityComparer.Instance);
                    for (var index = 0; index < current.Length; ++index)
                    {
                        if (index < _members.Count && !ReferenceEquals(_members[index], current[index]))
                            throw new UnsupportedSignatureException("An installed broker image member was replaced.");
                        VerifyMemberForPhase(current[index], verifiedDetours);
                    }
                    if (generation != _referenceCodeGeneration)
                        throw new UnsupportedSignatureException("Broker image installations changed during membership verification.");
                    _members.AddRange(current.Skip(_members.Count));
                    // Every current member was fully verified above. Preserve
                    // the fixed membership/generation check here; composition
                    // performs its next full member check at its actual exit.
                    var snapshot = new Snapshot(this, generation); snapshot.VerifyIdentity(); return snapshot;
                }
                catch { _unknown = true; throw; }
            }
        }
        internal void Verify() => CaptureSnapshot().Verify();
        private static void VerifyMemberForPhase(ReferenceImageMember member,
            HashSet<NativePlaybackExecutableSeal> verifiedDetours)
        {
            if (member is ReferenceCodeIdentity ordinary) ordinary.VerifyForSnapshotPhase(verifiedDetours);
            else if (member is NativeInitializationCodeIdentity initialization) initialization.Verify(verifiedDetours);
            else member.Verify();
        }
        internal (IntPtr Address, int Length)[] ImageRanges()
        {
            lock (Gate)
            {
                var snapshot = CaptureSnapshot(); var ranges = snapshot.ImageRanges(); snapshot.Verify(); return ranges;
            }
        }

        internal sealed class Snapshot
        {
            private readonly ReferenceImageMembers _owner;
            private readonly long _generation;
            private readonly ReferenceImageMember[] _members;
            // The independent composition API still owns one section. A group
            // traversal instead creates a distinct section state for each PE
            // executable section from this one entry-verified membership.
            private CompositionSection? _singleSection;
            internal Snapshot(ReferenceImageMembers owner, long generation)
                => (_owner, _generation, _members) = (owner, generation, owner._members.ToArray());
            internal void Verify()
            {
                lock (Gate)
                {
                    try
                    {
                        VerifyIdentity();
                        var verifiedDetours = new HashSet<NativePlaybackExecutableSeal>(ReferenceEqualityComparer.Instance);
                        foreach (var member in _members) VerifyMemberForPhase(member, verifiedDetours);
                        if (_generation != _referenceCodeGeneration)
                            throw new UnsupportedSignatureException("Broker image installations changed during composition verification.");
                    }
                    catch { _owner._unknown = true; throw; }
                }
            }
            internal void VerifyIdentity()
            {
                if (!System.Threading.Monitor.IsEntered(Gate) || _owner._unknown
                    || _generation != _referenceCodeGeneration || _owner._members.Count != _members.Length
                    || _members.Where((member, index) => !ReferenceEquals(member, _owner._members[index])).Any())
                    throw new UnsupportedSignatureException("The fixed broker image composition snapshot changed.");
            }
            internal (IntPtr Address, int Length)[] ImageRanges()
                => _members.Select(member => (member.Address, member.PatchLength)).ToArray();
            internal CompositionSection BeginCompositionSection(PlaybackSourceInstallGroup group,
                PlaybackSourceInstallGroup.VerificationTraversal traversal)
            {
                lock (Gate)
                {
                    try
                    {
                        traversal.VerifyIssuer(group); VerifyIdentity();
                        return new CompositionSection(this, group, traversal);
                    }
                    catch { _owner._unknown = true; throw; }
                }
            }
            private CompositionSection SingleSection
            {
                get { lock (Gate) return _singleSection ??= new CompositionSection(this); }
            }
            internal void VerifyCanonicalPreimages(uint sectionRva, byte[] expected,
                (IntPtr Address, int Length)[] otherWrites)
                => SingleSection.VerifyCanonicalPreimages(sectionRva, expected, otherWrites);
            internal void VerifyCanonicalPreimagesForComposition(uint sectionRva, byte[] expected,
                (IntPtr Address, int Length)[] otherWrites)
                => SingleSection.VerifyCanonicalPreimagesForComposition(sectionRva, expected, otherWrites);
            internal void ApplyPatches(uint sectionRva, byte[] expected)
                => SingleSection.ApplyPatches(sectionRva, expected);
            internal void ApplyPatchesForComposition(uint sectionRva, byte[] expected)
                => SingleSection.ApplyPatchesForComposition(sectionRva, expected);

            internal sealed class CompositionSection
            {
                private readonly Snapshot _snapshot;
                private readonly PlaybackSourceInstallGroup? _group;
                private readonly PlaybackSourceInstallGroup.VerificationTraversal? _traversal;
                private byte[]? _canonicalExpected;
                private uint _sectionRva;
                private bool _applied;
                internal CompositionSection(Snapshot snapshot, PlaybackSourceInstallGroup? group = null,
                    PlaybackSourceInstallGroup.VerificationTraversal? traversal = null)
                    => (_snapshot, _group, _traversal) = (snapshot, group, traversal);
                private void VerifyTraversal()
                {
                    if (_traversal is not null) _traversal.VerifyIssuer(_group!);
                }
                internal void VerifyCanonicalPreimages(uint sectionRva, byte[] expected,
                    (IntPtr Address, int Length)[] otherWrites)
                    => VerifyCanonicalPreimagesCore(sectionRva, expected, otherWrites, composition: false);
                internal void VerifyCanonicalPreimagesForComposition(uint sectionRva, byte[] expected,
                    (IntPtr Address, int Length)[] otherWrites)
                    => VerifyCanonicalPreimagesCore(sectionRva, expected, otherWrites, composition: true);
                private void VerifyCanonicalPreimagesCore(uint sectionRva, byte[] expected,
                    (IntPtr Address, int Length)[] otherWrites, bool composition)
                {
                    lock (Gate)
                    {
                        try
                        {
                            VerifyTraversal();
                            if (composition) _snapshot.VerifyIdentity(); else _snapshot.Verify();
                            if (_canonicalExpected is not null)
                                throw new InvalidOperationException("A broker image snapshot validates one canonical section once.");
                            for (var index = 0; index < _snapshot._members.Length; ++index)
                            {
                                var member = _snapshot._members[index];
                                foreach (var range in otherWrites)
                                    if (Snapshot.Overlaps(member.Address, member.PatchLength, range.Address, range.Length))
                                        throw new UnsupportedSignatureException("A broker branch overlaps another private image owner's actual write.");
                                for (var prior = 0; prior < index; ++prior)
                                    if (Snapshot.Overlaps(member.Address, member.PatchLength, _snapshot._members[prior].Address, _snapshot._members[prior].PatchLength))
                                        throw new UnsupportedSignatureException("Actual broker branch writes overlap.");
                                // All preimage64 records see this same untouched
                                // canonical section; no owner lends another its tail.
                                member.VerifyCanonicalPreimage(sectionRva, expected);
                            }
                            if (composition) _snapshot.VerifyIdentity(); else _snapshot.Verify();
                            _sectionRva = sectionRva; _canonicalExpected = expected;
                        }
                        catch { _snapshot._owner._unknown = true; throw; }
                    }
                }
                internal void ApplyPatches(uint sectionRva, byte[] expected)
                    => ApplyPatchesCore(sectionRva, expected, composition: false);
                internal void ApplyPatchesForComposition(uint sectionRva, byte[] expected)
                    => ApplyPatchesCore(sectionRva, expected, composition: true);
                private void ApplyPatchesCore(uint sectionRva, byte[] expected, bool composition)
                {
                    lock (Gate)
                    {
                        try
                        {
                            VerifyTraversal();
                            if (composition) _snapshot.VerifyIdentity(); else _snapshot.Verify();
                            if (_applied || !ReferenceEquals(expected, _canonicalExpected) || sectionRva != _sectionRva)
                                throw new UnsupportedSignatureException("Broker branches require their same validated canonical composition.");
                            foreach (var member in _snapshot._members) member.ApplyPatch(sectionRva, expected);
                            _applied = true;
                            if (composition) _snapshot.VerifyIdentity(); else _snapshot.Verify();
                        }
                        catch { _snapshot._owner._unknown = true; throw; }
                    }
                }
            }
            private static bool Overlaps(IntPtr first, int firstLength, IntPtr second, int secondLength)
                => firstLength > 0 && secondLength > 0 && first.ToInt64() < checked(second.ToInt64() + secondLength)
                    && second.ToInt64() < checked(first.ToInt64() + firstLength);
        }
    }

    /// <summary>Only the broker's real Dobby installation can retain this owner.
    /// Its original preimage is authenticated against the independent canonical
    /// image before its exact five/six-byte branch can participate in composition.</summary>
    private sealed class ReferenceCodeIdentity : ReferenceImageMember
    {
        private const int PrefixLength = 64;
        // Complete PC-independent instructions already closed by the existing
        // source prologue proofs and the observed ordinary late-install entries.
        // This is a finite declaration, not a partial instruction decoder.
        private static readonly byte[][] SupportedPrefixes =
        {
            new byte[] { 0x48, 0x89, 0x4c, 0x24, 0x08, 0x53 },
            new byte[] { 0x48, 0x89, 0x5c, 0x24, 0x08, 0x57 },
            new byte[] { 0x48, 0x89, 0x6c, 0x24, 0x18, 0x56 },
            // Hold MoveNext: MOV [RSP + 0x20], RSI; PUSH RBP (5 + 1 bytes).
            new byte[] { 0x48, 0x89, 0x74, 0x24, 0x20, 0x55 },
            new byte[] { 0x40, 0x53, 0x48, 0x83, 0xec, 0x20 },
            new byte[] { 0x40, 0x53, 0x48, 0x83, 0xec, 0x30 },
            new byte[] { 0x40, 0x53, 0x48, 0x83, 0xec, 0x40 },
            // Result Start: PUSH RBX; SUB RSP, 0x60 (2 + 4 bytes).
            new byte[] { 0x40, 0x53, 0x48, 0x83, 0xec, 0x60 },
            // Controller/Result Retry: PUSH RDI; SUB RSP, 0x20 (2 + 4 bytes).
            new byte[] { 0x40, 0x57, 0x48, 0x83, 0xec, 0x20 },
            // Local Delay MoveNext: PUSH RDI, then SUB RSP, 0x40 (2 + 4 bytes).
            new byte[] { 0x40, 0x57, 0x48, 0x83, 0xec, 0x40 },
            new byte[] { 0x40, 0x57, 0x48, 0x83, 0xec, 0x50 },
            // Local WaitUntil MoveNext and Controller Awake: PUSH RSI, then SUB RSP, 0x20 (2 + 4 bytes).
            new byte[] { 0x40, 0x56, 0x48, 0x83, 0xec, 0x20 },
            new byte[] { 0x48, 0x83, 0xec, 0x28, 0x38, 0x51, 0x42 },
            new byte[] { 0x48, 0x83, 0xec, 0x28, 0x48, 0x8b, 0x49, 0x28 },
            // PUSH RSI; PUSH RDI; SUB RSP, 0xd8 (2 + 1 + 7 bytes).
            new byte[] { 0x40, 0x56, 0x57, 0x48, 0x81, 0xec, 0xd8, 0x00, 0x00, 0x00 },
            // PUSH RDI; SUB RSP, 0x30 (2 + 4 bytes).
            new byte[] { 0x40, 0x57, 0x48, 0x83, 0xec, 0x30 },
            // SUB RSP, 0x28; CMP byte [RCX + 0xb4], 0 (4 + 7 bytes).
            new byte[] { 0x48, 0x83, 0xec, 0x28, 0x80, 0xb9, 0xb4, 0x00, 0x00, 0x00, 0x00 },
            // PUSH RBP; PUSH RDI; LEA RBP, [RSP - 0x38] (2 + 1 + 5 bytes).
            new byte[] { 0x40, 0x55, 0x57, 0x48, 0x8d, 0x6c, 0x24, 0xc8 },
            // Timeline instantaneous event: PUSH RBX/RBP/RSI/RDI; SUB RSP, 0x48 (2 + 1 + 1 + 1 + 4 bytes).
            new byte[] { 0x40, 0x53, 0x55, 0x56, 0x57, 0x48, 0x83, 0xec, 0x48 },
            // MOV RAX, [RCX]; MOV byte [RCX + 0x51], 1 (3 + 4 bytes).
            new byte[] { 0x48, 0x8b, 0x01, 0xc6, 0x41, 0x51, 0x01 }
        };
        private readonly Entry _entry;
        private readonly VerifiedHookTarget _target;
        private readonly DobbyDetourEngine _engine;
        private readonly IMainThreadDispatcher _main;
        private readonly Delegate _replacement;
        private readonly IntPtr _destination;
        private readonly NativePlaybackExecutableSeal _detour;
        private readonly NativePlaybackExecutableSeal.RangeSeal _before, _replacementMemory;
        private readonly byte[] _movedPrefix;
        private readonly (IntPtr Name, IntPtr ReturnType, IntPtr Parameters)? _timelineMetadata;
        private readonly byte[]? _timelineName;
        private NativePlaybackExecutableSeal.RangeSeal? _installed, _relay, _originalCode;
        private Delegate? _original;
        private IntPtr _originalAddress;
        private bool _committed, _unknown;
        internal override IntPtr Module { get; }
        internal override IntPtr Address => _target.Address;
        internal override int PatchLength => _installed!.Bytes.Length;
        internal override bool Owns(Entry entry) => ReferenceEquals(_entry, entry);

        internal static byte[]? MatchMovedPrefix(byte[] before)
            => SupportedPrefixes.FirstOrDefault(prefix => before.AsSpan(0, prefix.Length).SequenceEqual(prefix));

        internal static ReferenceCodeIdentity? TryCapture(Entry entry, DobbyDetourEngine engine,
            IMainThreadDispatcher main, ILogger log)
        {
            main.VerifyAccess();
            var timeline = TimelineDeclaration(entry.Target.Abi);
            if (!ReferenceEquals(entry.Engine, engine) || timeline.Rva == 0 && entry.Target.Abi is not (ExactHookAbi.InstanceVoid
                or ExactHookAbi.InstanceEnum32Void or ExactHookAbi.InstanceEnum32SingleVoid or ExactHookAbi.InstanceInt32SingleVoid
                or ExactHookAbi.InstanceReferenceReturn0 or ExactHookAbi.InstanceBooleanReturn0 or ExactHookAbi.InstanceBooleanVoid))
                throw new UnsupportedSignatureException("Ordinary image evidence requires its actual broker ABI and Dobby owner.");
            var module = IntPtr.Zero;
            byte[]? captured = null;
            try
            {
                module = Allocation(entry.Target.Address);
                HookTargetValidation.VerifyExecutable(entry.Target.Address, module);
                if (timeline.Rva != 0 && (entry.Target.Address != IntPtr.Add(module, timeline.Rva)
                    || entry.Target.DeclaringClass == IntPtr.Zero || entry.Target.ReturnClass != IntPtr.Zero
                    || !OwnsLocalTimelineEntry(entry)))
                    throw new UnsupportedSignatureException("The installed Timeline method has no exact LOCAL authority.");
                var before = NativePlaybackExecutableSeal.RangeSeal.Capture(entry.Target.Address, PrefixLength, module, true, true);
                captured = before.Bytes;
                var moved = MatchMovedPrefix(before.Bytes);
                if (moved is null)
                {
                    LogUnavailable(log, entry.Target, module, captured, "Unknown complete relocation prefix"); return null;
                }
                return new ReferenceCodeIdentity(entry, engine, main, module, before, moved);
            }
            catch (UnsupportedSignatureException error)
            {
                // This evidence is optional for ordinary hook delivery. A
                // short leaf or unavailable range grants no composition right.
                LogUnavailable(log, entry.Target, module, captured, error.Message); return null;
            }
        }
        private ReferenceCodeIdentity(Entry entry, DobbyDetourEngine engine, IMainThreadDispatcher main,
            IntPtr module, NativePlaybackExecutableSeal.RangeSeal before, byte[] moved)
        {
            (_entry, _target, _engine, _main, Module, _before, _replacement, _movedPrefix)
                = (entry, entry.Target, engine, main, module, before, entry.Replacement, moved);
            _destination = Marshal.GetFunctionPointerForDelegate(_replacement);
            // Marshal owns this executable thunk; it is not a SEC_IMAGE. The
            // permanent delegate and exact allocation/pointer retain its owner.
            _replacementMemory = NativePlaybackExecutableSeal.RangeSeal.Capture(_destination, 1, Allocation(_destination), false, true);
            _detour = engine.RetainPlaybackAuthority();
            var timeline = TimelineDeclaration(_target.Abi);
            if (timeline.Rva != 0)
            {
                var method = ReadMetadata();
                _timelineMetadata = (method.Name, method.ReturnType, method.Parameters);
                _timelineName = Encoding.ASCII.GetBytes(timeline.Name + "\0");
            }
            VerifyRegistration(); VerifyMetadata(); _before.Verify();
            if (!GetModuleHandleEx(4, Address, out var held) || held != Module)
                throw new UnsupportedSignatureException("Ordinary image evidence could not retain its exact mapped module.");
        }
        internal void Installed(Delegate original)
        {
            if (!System.Threading.Monitor.IsEntered(Gate) || _committed || _unknown)
                throw new InvalidOperationException("Ordinary image evidence installs once under the actual broker gate.");
            _original = original; _originalAddress = Marshal.GetFunctionPointerForDelegate(original);
            VerifyRegistration(); _detour.Verify(); _replacementMemory.VerifyMemoryIdentity(); VerifyMetadata();
            _originalCode = ProveOriginal(Address, _originalAddress, _destination, _movedPrefix);
            (_installed, _relay) = ProveInstalledBranch(Address, Module, _destination, _before, _movedPrefix.Length);
            VerifyEvidence();
            PublishInstallation(); _committed = true;
        }

        // Shared byte proofs grant no membership. The concrete owner must still
        // prove its CLR delegate or native bridge and publish its real install.
        internal static NativePlaybackExecutableSeal.RangeSeal ProveOriginal(IntPtr address, IntPtr original,
            IntPtr destination, byte[] movedPrefix)
        {
            if (original == IntPtr.Zero || original == address || original == destination)
                throw new UnsupportedSignatureException("The detour has no distinct original trampoline.");
            var code = NativePlaybackExecutableSeal.RangeSeal.Capture(original, movedPrefix.Length + 14,
                Allocation(original), false, true);
            if (!code.Bytes.AsSpan(0, movedPrefix.Length).SequenceEqual(movedPrefix)
                || !AbsoluteJump(code.Bytes, movedPrefix.Length, out var continuation)
                || continuation != IntPtr.Add(address, movedPrefix.Length))
                throw new UnsupportedSignatureException("The original is not its complete proved prefix and exact source continuation.");
            return code;
        }
        internal static (NativePlaybackExecutableSeal.RangeSeal Installed, NativePlaybackExecutableSeal.RangeSeal? Relay)
            ProveInstalledBranch(IntPtr address, IntPtr module, IntPtr destination,
                NativePlaybackExecutableSeal.RangeSeal before, int movedLength)
        {
            before.VerifyMemoryIdentity();
            var bytes = NativePlaybackExecutableSeal.RangeSeal.Read(address, PrefixLength);
            NativePlaybackExecutableSeal.RangeSeal? relay = null;
            int patchLength;
            if (bytes[0] == 0xe9)
            {
                patchLength = 5;
                var branchDestination = new IntPtr(checked(address.ToInt64() + 5 + BitConverter.ToInt32(bytes, 1)));
                if (branchDestination != destination)
                {
                    relay = NativePlaybackExecutableSeal.RangeSeal.Capture(branchDestination, 14, Allocation(branchDestination), false, true);
                    if (!AbsoluteJump(relay.Bytes, 0, out var relayDestination) || relayDestination != destination)
                        throw new UnsupportedSignatureException("The detour relay does not reach its retained replacement entry.");
                }
            }
            else if (bytes[0] == 0xff && bytes[1] == 0x25 && BitConverter.ToInt32(bytes, 2) != 0)
            {
                patchLength = 6;
                var slot = new IntPtr(checked(address.ToInt64() + 6 + BitConverter.ToInt32(bytes, 2)));
                relay = NativePlaybackExecutableSeal.RangeSeal.Capture(slot, 8, Allocation(slot), false);
                if (new IntPtr(BitConverter.ToInt64(relay.Bytes, 0)) != destination)
                    throw new UnsupportedSignatureException("The detour indirect slot does not name its retained replacement entry.");
            }
            else throw new UnsupportedSignatureException("The detour is not a supported exact five/six-byte Dobby branch.");
            if (patchLength > movedLength || !bytes.AsSpan(patchLength).SequenceEqual(before.Bytes.AsSpan(patchLength)))
                throw new UnsupportedSignatureException("The detour changed bytes beyond its actual branch.");
            // Adjacent broker entries may be less than 64 bytes apart. Only
            // the actual branch is byte-sealed; the original 64 bytes remain
            // canonical preimage evidence, never an installed tail authority.
            var installed = NativePlaybackExecutableSeal.RangeSeal.Capture(address, patchLength, module, true, true);
            if (!installed.Bytes.AsSpan().SequenceEqual(bytes.AsSpan(0, patchLength)))
                throw new UnsupportedSignatureException("The branch changed during installation readback.");
            return (installed, relay);
        }
        internal void RefuseComposition(ILogger log, Exception error)
        { _unknown = true; LogUnavailable(log, _target, Module, _before.Bytes, error.Message); }
        private static void LogUnavailable(ILogger log, VerifiedHookTarget target, IntPtr module, byte[]? before, string reason)
        {
            try
            {
                if (before is null)
                {
                    try { before = NativePlaybackExecutableSeal.RangeSeal.Capture(target.Address, 16, module, true, true).Bytes; }
                    catch (UnsupportedSignatureException) { }
                }
                log.Log(LogLevel.Warning, "Ordinary hook has no image composition identity: target=" + target.Id
                    + "; address=0x" + target.Address.ToInt64().ToString("x")
                    + "; rva=" + (module == IntPtr.Zero ? "unavailable" : "0x" + (target.Address.ToInt64() - module.ToInt64()).ToString("x"))
                    + "; before16=" + (before is null ? "unavailable" : Convert.ToHexString(before.AsSpan(0, 16)))
                    + "; reason=" + reason + ".");
            }
            catch { }
        }
        private void VerifyRegistration()
        {
            _main.VerifyAccess();
            if (_unknown || !Entries.TryGetValue(Address, out var registered) || !ReferenceEquals(registered, _entry)
                || !ReferenceEquals(_entry.Target, _target) || !ReferenceEquals(_entry.Engine, _engine)
                || !ReferenceEquals(_entry.Replacement, _replacement) || _entry.NativeInstallFailure is not null
                || Marshal.GetFunctionPointerForDelegate(_replacement) != _destination
                || _original is not null && (!ReferenceEquals(Volatile.Read(ref _entry.Original), _original)
                    || Marshal.GetFunctionPointerForDelegate(_original) != _originalAddress))
                throw new UnsupportedSignatureException("The ordinary image member lost its exact permanent broker owner.");
        }
        private static (int Rva, byte Parameters, string Name) TimelineDeclaration(ExactHookAbi abi) => abi switch
        {
            ExactHookAbi.StaticTimelineModeVoid => (0x48a950, 1, "SetTimelineGameMode"),
            ExactHookAbi.StaticTimelineInstantaneousHandle => (0x48a3e0, 6, "AddInstantaneousTimelineEvent"),
            ExactHookAbi.StaticTimelineEndRangeVoid => (0x48a730, 2, "EndRangeTimelineEvent"),
            ExactHookAbi.StaticTimelineTooltipVoid => (0x48a980, 2, "SetTimelineTooltip"),
            _ => (0, 0, "")
        };
        private static bool OwnsLocalTimelineEntry(Entry entry)
        {
            var targets = entry.LocalProgressPolicy?.Targets;
            return targets is not null && (entry.Target == targets.Mode || entry.Target == targets.Instantaneous
                    || entry.Target == targets.EndRange)
                || entry.LocalInitializationPolicy?.Targets.Tooltip == entry.Target;
        }
        private HookTargetValidation.MethodInfo2022 ReadMetadata()
        {
            HookTargetValidation.VerifyReadable(_target.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            return Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(_target.MethodInfo);
        }
        private void VerifyMetadata()
        {
            var method = ReadMetadata();
            if (method.MethodPointer != Address || method.Class != _target.DeclaringClass)
                throw new UnsupportedSignatureException("The ordinary metadata no longer names its exact installed source.");
            var timeline = TimelineDeclaration(_target.Abi);
            if (_timelineMetadata is { } metadata)
            {
                if ((method.Flags & (0x10 | 0x400)) != 0x10 || (method.Bitfield & 3) != 0
                    || method.ParameterCount != timeline.Parameters || method.Parameters == IntPtr.Zero || method.ReturnType == IntPtr.Zero
                    || (method.Name, method.ReturnType, method.Parameters) != metadata)
                    throw new UnsupportedSignatureException("The exact LOCAL Timeline metadata no longer defines its original static ABI.");
                var name = _timelineName!;
                HookTargetValidation.VerifyReadable(method.Name, (nuint)name.Length);
                if (!NativePlaybackExecutableSeal.RangeSeal.Read(method.Name, name.Length).AsSpan().SequenceEqual(name))
                    throw new UnsupportedSignatureException("The exact LOCAL Timeline method name changed.");
            }
        }
        private void VerifyEvidence(HashSet<NativePlaybackExecutableSeal>? verifiedDetours = null)
        {
            VerifyRegistration();
            if (verifiedDetours is null || verifiedDetours.Add(_detour)) _detour.Verify();
            _before.VerifyMemoryIdentity();
            _installed!.Verify(); _relay?.Verify(); _originalCode!.Verify();
            _replacementMemory.VerifyMemoryIdentity(); VerifyMetadata();
        }
        internal override void Verify() => VerifyCore(null);
        internal void VerifyForSnapshotPhase(HashSet<NativePlaybackExecutableSeal> verifiedDetours)
        {
            ArgumentNullException.ThrowIfNull(verifiedDetours);
            VerifyCore(verifiedDetours);
        }
        private void VerifyCore(HashSet<NativePlaybackExecutableSeal>? verifiedDetours)
        {
            lock (Gate)
            {
                try
                {
                    if (!_committed)
                        throw new UnsupportedSignatureException("No committed ordinary image identity belongs to this broker entry.");
                    VerifyPublication();
                    VerifyEvidence(verifiedDetours);
                }
                catch { _unknown = true; throw; }
            }
        }
        internal override void VerifyCanonicalPreimage(uint sectionRva, ReadOnlySpan<byte> expected)
        {
            var relative = Address.ToInt64() - Module.ToInt64() - sectionRva;
            if (relative >= expected.Length || relative + PrefixLength <= 0) return;
            if (relative < 0 || PrefixLength > expected.Length - relative
                || !expected.Slice(checked((int)relative), PrefixLength).SequenceEqual(_before.Bytes))
                throw new UnsupportedSignatureException("The ordinary owner's pre-install bytes differ from the pinned file plus DIR64.");
        }
        internal override void ApplyPatch(uint sectionRva, Span<byte> expected)
        {
            var relative = Address.ToInt64() - Module.ToInt64() - sectionRva;
            if (relative >= expected.Length || relative + PatchLength <= 0) return;
            if (relative < 0 || PatchLength > expected.Length - relative)
                throw new UnsupportedSignatureException("A canonical section splits an ordinary branch write.");
            _installed!.Bytes.AsSpan().CopyTo(expected.Slice(checked((int)relative), PatchLength));
        }
        internal void VerifyOriginalBody(int length, string sha256)
        {
            try
            {
                Verify();
                var actual = NativePlaybackExecutableSeal.RangeSeal.Capture(Address, length, Module, true, true);
                var original = (byte[])actual.Bytes.Clone();
                // Only the exact branch attested by this owner is replaced in
                // the bounded copy. The complete remaining body is live.
                _before.Bytes.AsSpan(0, PatchLength).CopyTo(original);
                var digest = Convert.ToHexString(SHA256.HashData(original));
                if (!digest.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("The complete original InLevel.ProgressControl.Update body differs from its fixed profile.");
                actual.Verify();
                Verify();
            }
            catch { _unknown = true; throw; }
        }
        internal static bool AbsoluteJump(byte[] bytes, int offset, out IntPtr destination)
        {
            destination = IntPtr.Zero;
            if (offset < 0 || bytes.Length - offset < 14 || bytes[offset] != 0xff || bytes[offset + 1] != 0x25
                || BitConverter.ToInt32(bytes, offset + 2) != 0) return false;
            destination = new IntPtr(BitConverter.ToInt64(bytes, offset + 6)); return destination != IntPtr.Zero;
        }
        internal static IntPtr Allocation(IntPtr address)
        {
            if (address == IntPtr.Zero || VirtualQuery(address, out var memory, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0
                || memory.AllocationBase == IntPtr.Zero)
                throw new UnsupportedSignatureException("The ordinary installation allocation identity is unavailable.");
            return memory.AllocationBase;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
        {
            internal IntPtr BaseAddress, AllocationBase;
            internal uint AllocationProtect;
            internal ushort PartitionId;
            internal nuint RegionSize;
            internal uint State, Protect, Type;
        }
        [DllImport("kernel32")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation memory, nuint length);
        [DllImport("kernel32", EntryPoint = "GetModuleHandleExW")]
        internal static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);
    }
}
