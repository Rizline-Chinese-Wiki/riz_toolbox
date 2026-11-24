using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

/// <summary>Controlled managed ABI and frame-shape checks. Pointer values below
/// are identities only and are never dereferenced. Actual native-to-CLR calls,
/// frame copying, observer attachment and cleanup have a separate joint fixture.</summary>
internal static class NativeListenerContextBridgeTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Listener managed shapes preserve the 176 byte frame and information and 32 byte observer ABI", Layout),
        ("Listener adoption identity requires complete epochs entries and six distinct return PCs", CompleteIdentity),
        ("Listener lane matching rejects changed identity unknown faults and invalid attachment values", LaneIdentity),
        ("Listener Before shape pins source PC direct target arguments and aligned native stack", BeforeShape),
        ("Listener completion shapes distinguish returned abnormal missing-post and target-read failure", CompletionShapes),
        ("Listener controlled frame rejects a different current native OS thread", CurrentThread)
    };

    private static void Check(bool condition, string message = "Listener managed shape check failed")
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Layout()
    {
        Check(IntPtr.Size == 8, "Listener ABI checks require a 64 bit process.");
        Check(Marshal.SizeOf<NativeListenerFrame>() == 176);
        foreach (var (name, offset) in new[] {
            (nameof(NativeListenerFrame.Version), 0), (nameof(NativeListenerFrame.Size), 4),
            (nameof(NativeListenerFrame.Slot), 8), (nameof(NativeListenerFrame.Site), 12),
            (nameof(NativeListenerFrame.Stage), 16), (nameof(NativeListenerFrame.OsThreadId), 20),
            (nameof(NativeListenerFrame.Flags), 24), (nameof(NativeListenerFrame.Reserved0), 28),
            (nameof(NativeListenerFrame.Epoch), 32), (nameof(NativeListenerFrame.ParentSequence), 40),
            (nameof(NativeListenerFrame.ChildSequence), 48), (nameof(NativeListenerFrame.ParentManagedToken), 56),
            (nameof(NativeListenerFrame.Event), 64), (nameof(NativeListenerFrame.List), 72),
            (nameof(NativeListenerFrame.Invokable), 80), (nameof(NativeListenerFrame.ArgRcx), 88),
            (nameof(NativeListenerFrame.ArgRdx), 96), (nameof(NativeListenerFrame.ArgR8), 104),
            (nameof(NativeListenerFrame.ArgR9), 112), (nameof(NativeListenerFrame.ActualTarget), 120),
            (nameof(NativeListenerFrame.SourceReturnPc), 128), (nameof(NativeListenerFrame.TransportReturnPc), 136),
            (nameof(NativeListenerFrame.SourceRsp), 144), (nameof(NativeListenerFrame.Index), 152),
            (nameof(NativeListenerFrame.MachineFrame), 160), (nameof(NativeListenerFrame.Reserved1), 168) })
            Check(Marshal.OffsetOf<NativeListenerFrame>(name).ToInt32() == offset, "Listener frame offset changed: " + name);
        Check(Enum.GetUnderlyingType(typeof(NativeListenerStage)) == typeof(uint)
            && Enum.GetUnderlyingType(typeof(NativeListenerFlags)) == typeof(uint));
        Check(Marshal.SizeOf<NativeListenerInformation>() == 176);
        foreach (var (name, offset) in new[] {
            (nameof(NativeListenerInformation.Version), 0), (nameof(NativeListenerInformation.Size), 4),
            (nameof(NativeListenerInformation.Slot), 8), (nameof(NativeListenerInformation.Attached), 12),
            (nameof(NativeListenerInformation.Epoch), 16), (nameof(NativeListenerInformation.ParentEpoch), 24),
            (nameof(NativeListenerInformation.PreEntry), 32), (nameof(NativeListenerInformation.PostEntry), 40),
            (nameof(NativeListenerInformation.SourceReturnPc0), 48), (nameof(NativeListenerInformation.SourceReturnPc1), 56),
            (nameof(NativeListenerInformation.SourceReturnPc2), 64), (nameof(NativeListenerInformation.PostTransportReturnPc0), 72),
            (nameof(NativeListenerInformation.PostTransportReturnPc1), 80), (nameof(NativeListenerInformation.PostTransportReturnPc2), 88),
            (nameof(NativeListenerInformation.DirectTarget0), 96), (nameof(NativeListenerInformation.DirectTarget1), 104),
            (nameof(NativeListenerInformation.Entered), 112), (nameof(NativeListenerInformation.Exited), 120),
            (nameof(NativeListenerInformation.InFlight), 128), (nameof(NativeListenerInformation.Faults), 136),
            (nameof(NativeListenerInformation.Abnormal), 144), (nameof(NativeListenerInformation.Overflow), 152),
            (nameof(NativeListenerInformation.MissingPost), 160), (nameof(NativeListenerInformation.Unknown), 168),
            (nameof(NativeListenerInformation.Reserved), 172) })
            Check(Marshal.OffsetOf<NativeListenerInformation>(name).ToInt32() == offset, "Listener information offset changed: " + name);
        var observer = typeof(NativeListenerContextBridge).GetNestedType("Observer", BindingFlags.NonPublic)!;
        Check(Marshal.SizeOf(observer) == 32 && Marshal.OffsetOf(observer, "Version").ToInt32() == 0
            && Marshal.OffsetOf(observer, "Size").ToInt32() == 4 && Marshal.OffsetOf(observer, "Before").ToInt32() == 8
            && Marshal.OffsetOf(observer, "After").ToInt32() == 16 && Marshal.OffsetOf(observer, "User").ToInt32() == 24);
        Check(typeof(NativeListenerContextBridge).GetConstructors().Length == 0,
            "Listener bridge acquired a public constructor that bypasses configured identity adoption.");
    }

    private static NativeListenerAdoptionIdentity Identity() => new(19, 101, 77,
        new(0x110000), new(0x120000), new(0x210000), new(0x220000), new(0x230000),
        new(0x310000), new(0x320000), new(0x330000), new(0x410000), new(0x420000));

    private static void CompleteIdentity()
    {
        var identity = Identity();
        Check(identity.IsComplete && (identity with { Slot = 0 }).IsComplete && (identity with { Slot = 31 }).IsComplete);
        Check((identity with { Direct1 = identity.Direct0 }).IsComplete,
            "Two direct call sites may share the same actual target.");
        foreach (var (name, invalid) in new[] {
            ("slot", identity with { Slot = 32 }), ("epoch", identity with { Epoch = 0 }),
            ("parent epoch", identity with { ParentEpoch = 0 }), ("pre entry", identity with { PreEntry = IntPtr.Zero }),
            ("post entry", identity with { PostEntry = IntPtr.Zero }), ("entry collision", identity with { PostEntry = identity.PreEntry }),
            ("source 0", identity with { Source0 = IntPtr.Zero }), ("source 1", identity with { Source1 = IntPtr.Zero }),
            ("source 2", identity with { Source2 = IntPtr.Zero }), ("transport 0", identity with { Transport0 = IntPtr.Zero }),
            ("transport 1", identity with { Transport1 = IntPtr.Zero }), ("transport 2", identity with { Transport2 = IntPtr.Zero }),
            ("source collision 01", identity with { Source1 = identity.Source0 }),
            ("source collision 02", identity with { Source2 = identity.Source0 }),
            ("source collision 12", identity with { Source2 = identity.Source1 }),
            ("transport collision 01", identity with { Transport1 = identity.Transport0 }),
            ("transport collision 02", identity with { Transport2 = identity.Transport0 }),
            ("transport collision 12", identity with { Transport2 = identity.Transport1 }),
            ("direct 0", identity with { Direct0 = IntPtr.Zero }), ("direct 1", identity with { Direct1 = IntPtr.Zero }) })
            Check(!invalid.IsComplete, "Incomplete listener identity was accepted: " + name);
        for (uint source = 0; source != 3; ++source)
        {
            Check(identity.Source(source) != IntPtr.Zero && identity.Transport(source) != IntPtr.Zero);
            for (uint transport = 0; transport != 3; ++transport)
            {
                var collision = transport switch {
                    0 => identity with { Transport0 = identity.Source(source) },
                    1 => identity with { Transport1 = identity.Source(source) },
                    _ => identity with { Transport2 = identity.Source(source) }
                };
                Check(!collision.IsComplete, $"Cross-group listener PC collision was accepted: {source}/{transport}");
            }
        }
        Check(identity.Source(3) == IntPtr.Zero && identity.Transport(uint.MaxValue) == IntPtr.Zero);
    }

    private static NativeListenerInformation Information()
    {
        var identity = Identity();
        return new() { Version = 1, Size = 176, Slot = identity.Slot, Epoch = identity.Epoch, ParentEpoch = identity.ParentEpoch,
            PreEntry = identity.PreEntry, PostEntry = identity.PostEntry,
            SourceReturnPc0 = identity.Source0, SourceReturnPc1 = identity.Source1, SourceReturnPc2 = identity.Source2,
            PostTransportReturnPc0 = identity.Transport0, PostTransportReturnPc1 = identity.Transport1, PostTransportReturnPc2 = identity.Transport2,
            DirectTarget0 = identity.Direct0, DirectTarget1 = identity.Direct1 };
    }

    private static void LaneIdentity()
    {
        var identity = Identity(); var info = Information();
        Check(NativeListenerAdoptionIdentity.From(info) == identity && NativeListenerContextBridge.Matches(info, identity));
        // Both values are valid native states. Adoption/Attach separately require
        // their respective 0 -> 1 transition; this predicate does not attach.
        Check(NativeListenerContextBridge.Matches(info with { Attached = 1 }, identity));
        Check(NativeListenerContextBridge.Matches(info with { Entered = 9, Exited = 8, InFlight = 1, Abnormal = 2 }, identity),
            "Dynamic counters and historical abnormal completions are not identity changes.");
        foreach (var (name, invalid) in new[] {
            ("version", info with { Version = 2 }), ("size", info with { Size = 144 }),
            ("slot", info with { Slot = 18 }), ("attachment", info with { Attached = 2 }),
            ("epoch", info with { Epoch = 102 }), ("parent epoch", info with { ParentEpoch = 78 }),
            ("pre entry", info with { PreEntry = new(0x110008) }), ("post entry", info with { PostEntry = new(0x120008) }),
            ("source 0", info with { SourceReturnPc0 = new(0x210008) }), ("source 1", info with { SourceReturnPc1 = new(0x220008) }),
            ("source 2", info with { SourceReturnPc2 = new(0x230008) }),
            ("transport 0", info with { PostTransportReturnPc0 = new(0x310008) }),
            ("transport 1", info with { PostTransportReturnPc1 = new(0x320008) }),
            ("transport 2", info with { PostTransportReturnPc2 = new(0x330008) }),
            ("direct 0", info with { DirectTarget0 = new(0x410008) }), ("direct 1", info with { DirectTarget1 = new(0x420008) }),
            ("unknown", info with { Unknown = 1 }), ("unrecognized unknown", info with { Unknown = 7 }),
            ("reserved", info with { Reserved = 1 }), ("faults", info with { Faults = 1 }),
            ("overflow", info with { Overflow = 1 }), ("missing post", info with { MissingPost = 1 }) })
            Check(!NativeListenerContextBridge.Matches(invalid, identity), "Changed or unknown listener lane was accepted: " + name);
    }

    private static NativeListenerFrame Frame(uint site = 0)
    {
        var identity = Identity();
        return new() { Version = 1, Size = 176, Slot = identity.Slot, Site = site, Stage = NativeListenerStage.Before,
            OsThreadId = GetCurrentThreadId(), Epoch = identity.Epoch, ParentSequence = 1001, ChildSequence = 2001,
            ParentManagedToken = 3001, Event = new(0x510000), List = new(0x520000), Invokable = new(0x530000),
            ArgRcx = new(0x530000), ArgRdx = new(0x540000), ArgR8 = new(0x550000), ArgR9 = new(0x560000),
            ActualTarget = site == 0 ? identity.Direct0 : site == 1 ? identity.Direct1 : new(0x430000),
            SourceReturnPc = identity.Source(site), TransportReturnPc = identity.Source(site), SourceRsp = 0x700000,
            Index = 3, MachineFrame = new(0x710000) };
    }

    private static void BeforeShape()
    {
        var identity = Identity(); var frame = Frame();
        for (uint site = 0; site != 3; ++site)
            Check(NativeListenerContextBridge.ValidFrame(Frame(site), identity, true), "A configured Before site was rejected.");
        Check(NativeListenerContextBridge.ValidFrame(frame with { ParentManagedToken = 0, Index = int.MaxValue }, identity, true),
            "An independent native listener may have no managed parent observer token.");
        // Rdx/R8/R9 are opaque captured arguments, including zero. This shape
        // predicate cannot prove their agreement with actual machine memory.
        Check(NativeListenerContextBridge.ValidFrame(frame with { ArgRdx = IntPtr.Zero, ArgR8 = IntPtr.Zero, ArgR9 = IntPtr.Zero }, identity, true));
        foreach (var (name, invalid) in new[] {
            ("version", frame with { Version = 2 }), ("size", frame with { Size = 168 }),
            ("slot", frame with { Slot = 18 }), ("site", frame with { Site = 3 }), ("epoch", frame with { Epoch = 102 }),
            ("parent sequence", frame with { ParentSequence = 0 }), ("child sequence", frame with { ChildSequence = 0 }),
            ("missing thread", frame with { OsThreadId = 0 }), ("reserved 0", frame with { Reserved0 = 1 }),
            ("reserved 1", frame with { Reserved1 = 1 }), ("event", frame with { Event = IntPtr.Zero }),
            ("list", frame with { List = IntPtr.Zero }), ("invokable", frame with { Invokable = IntPtr.Zero }),
            ("argument identity", frame with { ArgRcx = new(0x530008) }), ("zero argument identity", frame with { ArgRcx = IntPtr.Zero }),
            ("zero stack", frame with { SourceRsp = 0 }), ("unaligned stack", frame with { SourceRsp = 0x700008 }),
            ("source PC", frame with { SourceReturnPc = identity.Source1 }),
            ("post transport in Before", frame with { TransportReturnPc = identity.Transport0 }),
            ("index", frame with { Index = (ulong)int.MaxValue + 1 }), ("unknown flags", frame with { Flags = (NativeListenerFlags)16 }),
            ("before fault", frame with { Flags = NativeListenerFlags.BeforeFaulted }),
            ("stage", frame with { Stage = NativeListenerStage.Returned }), ("unknown stage", frame with { Stage = (NativeListenerStage)4 }),
            ("zero target", frame with { ActualTarget = IntPtr.Zero }), ("wrong direct target", frame with { ActualTarget = identity.Direct1 }),
            ("machine frame", frame with { MachineFrame = IntPtr.Zero }) })
            Check(!NativeListenerContextBridge.ValidFrame(invalid, identity, true), "Invalid Before shape was accepted: " + name);
        Check(!NativeListenerContextBridge.ValidFrame(Frame(1) with { ActualTarget = identity.Direct0 }, identity, true));
        Check(!NativeListenerContextBridge.ValidFrame(Frame(2) with { ActualTarget = IntPtr.Zero }, identity, true));
        Check(!NativeListenerContextBridge.ValidFrame(frame, identity with { ParentEpoch = 0 }, true));
    }

    private static void CompletionShapes()
    {
        var identity = Identity();
        for (uint site = 0; site != 3; ++site)
        {
            var returned = Frame(site) with { Stage = NativeListenerStage.Returned, TransportReturnPc = identity.Transport(site) };
            Check(NativeListenerContextBridge.ValidFrame(returned, identity, false));
            Check(NativeListenerContextBridge.ValidFrame(returned with { Flags = NativeListenerFlags.BeforeFaulted }, identity, false),
                "A faulted Before still needs its native Returned cleanup.");
            foreach (var (name, invalid) in new[] {
                ("source transport", returned with { TransportReturnPc = identity.Source(site) }),
                ("different post transport", returned with { TransportReturnPc = identity.Transport((site + 1) % 3) }),
                ("missing source", returned with { SourceReturnPc = IntPtr.Zero }),
                ("missing machine", returned with { MachineFrame = IntPtr.Zero }),
                ("zero target", returned with { ActualTarget = IntPtr.Zero }),
                ("parent abnormal", returned with { Flags = NativeListenerFlags.ParentAbnormal }),
                ("missing post", returned with { Flags = NativeListenerFlags.MissingPost }),
                ("target read fault", returned with { Flags = NativeListenerFlags.TargetReadFaulted }),
                ("unknown flags", returned with { Flags = (NativeListenerFlags)16 }),
                ("unknown stage", returned with { Stage = (NativeListenerStage)0 }) })
                Check(!NativeListenerContextBridge.ValidFrame(invalid, identity, false), "Invalid Returned shape was accepted: " + name);
            var abnormal = returned with { Stage = NativeListenerStage.Abnormal, Flags = NativeListenerFlags.ParentAbnormal,
                MachineFrame = IntPtr.Zero, TransportReturnPc = IntPtr.Zero };
            Check(NativeListenerContextBridge.ValidFrame(abnormal, identity, false));
            Check(NativeListenerContextBridge.ValidFrame(abnormal with { Flags = NativeListenerFlags.ParentAbnormal | NativeListenerFlags.BeforeFaulted }, identity, false));
            Check(NativeListenerContextBridge.ValidFrame(abnormal with { Flags = NativeListenerFlags.MissingPost }, identity, false));
            var missingPost = returned with { Stage = NativeListenerStage.Abnormal, Flags = NativeListenerFlags.MissingPost };
            Check(NativeListenerContextBridge.ValidFrame(missingPost, identity, false));
            foreach (var (name, invalid) in new[] {
                ("unexplained abnormal", abnormal with { Flags = NativeListenerFlags.None }),
                ("BeforeFaulted alone", abnormal with { Flags = NativeListenerFlags.BeforeFaulted }),
                ("parent close with machine", returned with { Stage = NativeListenerStage.Abnormal, Flags = NativeListenerFlags.ParentAbnormal }),
                ("machine without transport", missingPost with { TransportReturnPc = IntPtr.Zero }),
                ("transport without machine", missingPost with { MachineFrame = IntPtr.Zero }),
                ("wrong missing-post transport", missingPost with { TransportReturnPc = identity.Source(site) }) })
                Check(!NativeListenerContextBridge.ValidFrame(invalid, identity, false), "Invalid Abnormal shape was accepted: " + name);
        }
        // The virtual target read can fault before any Before callback. The
        // eventual After may therefore have token zero and no machine snapshot.
        // ValidFrame checks that shape only; it does not execute this sequence.
        var readFault = Frame(2) with { Stage = NativeListenerStage.Abnormal,
            Flags = NativeListenerFlags.ParentAbnormal | NativeListenerFlags.TargetReadFaulted,
            ActualTarget = IntPtr.Zero, ParentManagedToken = 0, MachineFrame = IntPtr.Zero, TransportReturnPc = IntPtr.Zero };
        Check(NativeListenerContextBridge.ValidFrame(readFault, identity, false));
        Check(!NativeListenerContextBridge.ValidFrame(readFault with { Flags = NativeListenerFlags.TargetReadFaulted }, identity, false));
        Check(!NativeListenerContextBridge.ValidFrame(readFault with { Site = 0, SourceReturnPc = identity.Source0, ActualTarget = identity.Direct0 }, identity, false));
        Check(!NativeListenerContextBridge.ValidFrame(readFault with { Site = 1, SourceReturnPc = identity.Source1, ActualTarget = identity.Direct1 }, identity, false));
        Check(!NativeListenerContextBridge.ValidFrame(readFault, identity, true));
        Check(!NativeListenerContextBridge.ValidFrame(Frame(), identity, false));
    }

    private static void CurrentThread()
    {
        var identity = Identity(); var mainFrame = Frame(); Exception? workerFailure = null;
        Check(NativeListenerContextBridge.ValidFrame(mainFrame, identity, true));
        var worker = new Thread(() => {
            try
            {
                var workerId = GetCurrentThreadId();
                Check(workerId != 0 && workerId != mainFrame.OsThreadId);
                Check(!NativeListenerContextBridge.ValidFrame(mainFrame, identity, true),
                    "A copied main-thread frame was accepted on another native thread.");
                var returned = mainFrame with { Stage = NativeListenerStage.Returned, TransportReturnPc = identity.Transport0 };
                Check(!NativeListenerContextBridge.ValidFrame(returned, identity, false));
                Check(NativeListenerContextBridge.ValidFrame(mainFrame with { OsThreadId = workerId }, identity, true),
                    "Frame shape validation must describe the current native thread; main-thread policy is a separate broker check.");
            }
            catch (Exception error) { workerFailure = error; }
        }) { IsBackground = true, Name = "Listener managed shape check" };
        worker.Start();
        Check(worker.Join(TimeSpan.FromSeconds(5)), "The controlled thread shape check did not finish.");
        if (workerFailure is not null) throw new InvalidOperationException("Listener native thread shape check failed.", workerFailure);
        Check(NativeListenerContextBridge.ValidFrame(mainFrame, identity, true));
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
