using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeEngineTransportFirstInstall
{
    private void VerifyFamilies(out byte[] activity)
    {
        var counters = new List<ulong>();
        for (var family = 0; family < 12; ++family)
        {
            var row = _wire.Families[family]; var node = family < 4; var inline = family == 4; var scheduler = family is >= 5 and <= 10;
            var kind = node ? checked((uint)family) : scheduler ? checked((uint)family - 5) : uint.MaxValue;
            var typed = node ? family : scheduler ? family - 1 : -1;
            var familyKind = node ? 0u : inline ? 1u : scheduler ? 2u : 3u;
            var frame = node ? 112u : inline ? 144u : scheduler ? 256u : 240u;
            var completion = node ? 32u : inline ? 0u : 48u; var info = node ? 112u : inline ? 160u : scheduler ? 144u : 176u;
            var config = node ? 24u : inline ? 88u : scheduler ? 64u : 432u; var observer = node ? 32u : inline ? 24u : 40u;
            var typedMask = typed >= 0 ? 1UL << typed : inline ? 1UL : 7UL << 10;
            var siteMask = inline ? 7UL : family == 9 ? 3UL << 3 : family == 11 ? ((1UL << 18) - 1) << 5 : 0;
            var windowMask = typed >= 0 ? 1UL << typed : 0;
            if (inline) windowMask = 7UL << 13; else if (family == 9) windowMask |= 3UL << 16; else if (family == 11) windowMask = (7UL << 10) | (((1UL << 18) - 1) << 18);
            Require(row.Version == 5 && row.ByteSize == 176 && row.Index == family && row.FamilyKind == familyKind && row.ChannelKind == kind
                && row.Slot == 28 && row.AbiVersion == (node ? 2u : 1u) && row.FrameBytes == frame && row.CompletionBytes == completion
                && row.InfoBytes == info && row.ConfigBytes == config && row.ObserverBytes == observer && row.Module == Address(KernelModule)
                && row.TypedMask == typedMask && row.SiteMask == siteMask && row.WindowMask == windowMask
                && row.Epoch is > 0 and <= long.MaxValue && row.ConfigureAttempts == 1 && row.ConfigureSucceeded == 1 && row.Flags == 7
                && row.RetainedClaim == 1 && row.HelperUnknown == 0 && row.MainOsThread == (node ? 0 : _wire.MainThread)
                && row.Target == (typed >= 0 ? _wire.Typed[typed].Target : 0)
                && row.Original == (typed >= 0 ? _wire.Typed[typed].Original : 0)
                && row.Entry == (typed >= 0 ? _wire.Typed[typed].Entry : 0), "A V5 configured family has a different complete ABI/member/epoch tuple.");
            var bytes = ConfigurationBytes(family);
            Require(bytes.Length == config && row.ConfigSha256.AsSpan().SequenceEqual(SHA256.HashData(bytes)),
                "A V5 family hash differs from independently reconstructed zero-initialized actual Configure bytes.");
            var attached = false;
            void Add(params ulong[] values)
            {
                Require(values.All(x => x <= long.MaxValue), "A V5 activity counter is negative or outside its native signed bound.");
                counters.Add(checked((ulong)family)); counters.AddRange(values);
            }
            if (node)
            {
                var identity = NodeIdentityUnchecked((NativeEngineNodeKind)family); var value = new NativeEngineNodeInformation { Version = 1, Size = 112 };
                Require(_nodeInfo(kind, row.Slot, ref value) == 0 && NativeEngineNodeContextBridge.MatchesInformation(value, identity, null)
                    && _nodeEntry(kind, row.Slot) == identity.Entry, "The actual V5 Node Kernel tuple differs from its publication.");
                attached = value.Attached != 0;
                Add(value.Entered, value.Exited, value.InFlight, value.EnterFaults, value.ExitFaults, value.SnapshotFaults, value.OutputFaults);
            }
            else if (inline)
            {
                var identity = InlineIdentityUnchecked(); var value = new NativeEngineInlineInformation { Version = 1, Size = 160 };
                Require(_inlineInfo(row.Slot, ref value) == 0 && value.Attached <= 1
                    && NativeEngineInlineContextBridge.MatchesInformation(value, identity, value.Attached != 0)
                    && _inlineEntry() == identity.Entry, "The actual V5 Inline Kernel tuple differs from its shared Start identity.");
                attached = value.Attached != 0;
                Add(value.StartsEntered, value.StartsExited, value.StartsInFlight, value.ProbesInFlight, value.Begins, value.EndsFree, value.EndsRetained,
                    value.Faults, value.SnapshotFaults, value.OrphanProbes, value.UnmatchedStarts, value.Events);
            }
            else if (scheduler)
            {
                var identity = SchedulerIdentityUnchecked((NativeEngineSchedulerKind)kind); var value = new NativeEngineSchedulerInformation { Version = 1, Size = 144 };
                Require(_schedulerInfo(kind, row.Slot, ref value) == 0 && value.Attached <= 1
                    && NativeEngineSchedulerContextBridge.MatchesInformation(value, identity, value.Attached != 0)
                    && _schedulerEntry(kind, row.Slot) == identity.Entry && _rowEntry() == identity.RowEntry,
                    "The actual V5 Scheduler Kernel tuple differs from its publication.");
                attached = value.Attached != 0;
                Add(value.Entered, value.Exited, value.InFlight, value.RowBegins, value.RowEnds, value.RowsInFlight, value.Faults, value.SnapshotFaults, value.UnmatchedRows);
            }
            else
            {
                var identity = WaitIdentityUnchecked(); var value = new NativeEngineWaitInformation { Version = 1, Size = 176 };
                Require(_waitInfo(row.Slot, ref value) == 0 && value.Attached <= 1
                    && NativeEngineWaitContextBridge.MatchesInformation(value, identity, value.Attached != 0)
                    && Enumerable.Range(0, 3).All(i => _waitEntry(checked((uint)i), row.Slot) == identity.Entry(i)) && _phaseEntry() == identity.PhaseEntry,
                    "The actual V5 Wait Kernel tuple differs from its thirteen exact targets.");
                attached = value.Attached != 0;
                Add(value.Entered, value.Exited, value.InFlight, value.Phases, value.Faults, value.SnapshotFaults, value.Unmatched);
            }
            Require(!_attached[family] || attached, "An attached V5 family moved backwards to unattached.");
            if (attached) _attached[family] = true;
        }
        activity = new byte[checked(counters.Count * 8)];
        for (var i = 0; i < counters.Count; ++i) BinaryPrimitives.WriteUInt64LittleEndian(activity.AsSpan(i * 8), counters[i]);
    }
    private bool FamilyIdle(int family)
    {
        var row = _wire.Families[family];
        if (family < 4)
        {
            var value = new NativeEngineNodeInformation { Version = 1, Size = 112 };
            return _nodeInfo(checked((uint)family), row.Slot, ref value) == 0 && value.InFlight == 0 && value.Attached == 0 && value.Unknown == 0;
        }
        if (family == 4)
        {
            var value = new NativeEngineInlineInformation { Version = 1, Size = 160 };
            return _inlineInfo(row.Slot, ref value) == 0 && value.StartsInFlight == 0 && value.ProbesInFlight == 0 && value.Attached == 0 && value.Unknown == 0;
        }
        if (family < 11)
        {
            var value = new NativeEngineSchedulerInformation { Version = 1, Size = 144 };
            return _schedulerInfo(checked((uint)family - 5), row.Slot, ref value) == 0 && value.InFlight == 0 && value.RowsInFlight == 0 && value.Attached == 0 && value.Unknown == 0;
        }
        var wait = new NativeEngineWaitInformation { Version = 1, Size = 176 };
        return _waitInfo(row.Slot, ref wait) == 0 && wait.InFlight == 0 && wait.Attached == 0 && wait.Unknown == 0;
    }
    private byte[] ConfigurationBytes(int family)
    {
        var row = _wire.Families[family];
        if (family < 4)
        {
            var bytes = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 24);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), _wire.Typed[family].Target);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), _wire.Typed[family].Original); return bytes;
        }
        if (family == 4)
        {
            var identity = InlineIdentityUnchecked();
            return MarshalBytes(new NativeEngineInlineConfiguration { Version = 1, Size = 88, StartSlot = identity.Start.Slot,
                MainThreadId = identity.MainThreadId, ExpectedStartEpoch = identity.Start.Epoch, ExpectedStartTarget = identity.Start.Target,
                Begin = identity.Begin, EndFree = identity.EndFree, EndRetained = identity.EndRetained });
        }
        if (family < 11)
        {
            var identity = SchedulerIdentityUnchecked((NativeEngineSchedulerKind)(family - 5));
            return MarshalBytes(new NativeEngineSchedulerConfiguration { Version = 1, Size = 64, MainThreadId = identity.MainThreadId,
                Target = identity.Target, Original = identity.Original, Begin = identity.Begin, End = identity.End });
        }
        var wait = WaitIdentityUnchecked();
        return MarshalBytes(new NativeEngineWaitConfiguration { Version = 1, Size = 432,
            YieldTarget = wait.Target(NativeEngineWaitKind.Yield), DispatchTarget = wait.Target(NativeEngineWaitKind.Dispatch), BatchTarget = wait.Target(NativeEngineWaitKind.Batch),
            YieldOriginal = wait.Original(0), DispatchOriginal = wait.Original(1), BatchOriginal = wait.Original(2),
            NodeStart = wait.Target(NativeEngineWaitKind.NodeStart), NodeExecute = wait.Target(NativeEngineWaitKind.NodeExecute),
            NodeRelease = wait.Target(NativeEngineWaitKind.NodeRelease), NodeDetach = wait.Target(NativeEngineWaitKind.NodeDetach),
            SchedulerRegister = wait.Target(NativeEngineWaitKind.SchedulerRegister), SchedulerRemove = wait.Target(NativeEngineWaitKind.SchedulerRemove),
            SchedulerConsume = wait.Target(NativeEngineWaitKind.SchedulerConsume), SchedulerPump = wait.Target(NativeEngineWaitKind.SchedulerPump),
            SchedulerDrain = wait.Target(NativeEngineWaitKind.SchedulerDrain), SchedulerCallback = wait.Target(NativeEngineWaitKind.SchedulerCallback),
            MainThreadId = wait.MainThreadId, Sites = Enumerable.Range(0, 18).Select(i => wait.Site((NativeEngineWaitPhase)i)).ToArray() });
    }
    private static byte[] MarshalBytes<T>(T value) where T : struct
    {
        var size = Marshal.SizeOf<T>(); var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, buffer, size); Marshal.StructureToPtr(value, buffer, false);
            var bytes = new byte[size]; Marshal.Copy(buffer, bytes, 0, size); Marshal.DestroyStructure<T>(buffer); return bytes;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
