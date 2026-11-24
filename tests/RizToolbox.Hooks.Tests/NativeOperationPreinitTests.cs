using System.Runtime.InteropServices;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class NativeOperationPreinitTests
{
    internal static readonly (string Name, Action Run)[] Cases = new (string Name, Action Run)[] {
        ("Operation pre-init V7 layout binds twenty-six exact operation rows and its embedded listener data", Layout),
        ("Operation pre-init receipt requires all twenty-six declared identities and both complete masks", Receipt),
        ("Operation pre-init adopted information rejects changed native claims and faults", Information),
        ("Single V7 publication rejects partial spans and changed native parent or retained block identities", SingleReceipt),
        ("Operation production binding refuses late first installation without the native producer", NoLateInstall)
    }.Concat(NativeTweenAdmissionTests.Cases).ToArray();
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Operation pre-init contract check failed."); }
    private static void Reject(Action action)
    { try { action(); } catch (UnsupportedSignatureException) { return; } throw new InvalidOperationException("Missing exact identity rejection."); }
    private static void Layout()
    {
        Check(Marshal.SizeOf<NativeFirstInstallInformationV7>() == 2528 && Marshal.SizeOf<NativeOperationPublicationRow>() == 64
            && Marshal.SizeOf<NativeOperationBootstrapInformation>() == 96
            && Marshal.SizeOf<NativeTweenSingleEventPublication>() == 416
            && Marshal.OffsetOf<NativeFirstInstallInformationV7>(nameof(NativeFirstInstallInformationV7.SingleEvent)).ToInt32() == 2112);
        foreach (var item in new[] { (nameof(NativeTweenSingleEventPublication.Epoch), 32),
            (nameof(NativeTweenSingleEventPublication.MidEntry), 48), (nameof(NativeTweenSingleEventPublication.Allocation), 72),
            (nameof(NativeTweenSingleEventPublication.SpanPc0), 120), (nameof(NativeTweenSingleEventPublication.SourceReturnPc0), 176),
            (nameof(NativeTweenSingleEventPublication.MidReturnPc), 200), (nameof(NativeTweenSingleEventPublication.PostStub0), 208),
            (nameof(NativeTweenSingleEventPublication.PostTransportReturnPc0), 232), (nameof(NativeTweenSingleEventPublication.FunctionTable), 256),
            (nameof(NativeTweenSingleEventPublication.UnwindInfo0), 272), (nameof(NativeTweenSingleEventPublication.SpanWidth0), 296),
            (nameof(NativeTweenSingleEventPublication.Protection0), 324), (nameof(NativeTweenSingleEventPublication.StubSize0), 352),
            (nameof(NativeTweenSingleEventPublication.UnwindSize0), 364), (nameof(NativeTweenSingleEventPublication.RegisteredFunctionCount), 376),
            (nameof(NativeTweenSingleEventPublication.ConfiguredMask), 392), (nameof(NativeTweenSingleEventPublication.AttemptedWrites), 400),
            (nameof(NativeTweenSingleEventPublication.CompletedWrites), 408) })
            Check(Marshal.OffsetOf<NativeTweenSingleEventPublication>(item.Item1).ToInt32() == item.Item2);
        Check(Marshal.OffsetOf<NativeFirstInstallInformationV7>(nameof(NativeFirstInstallInformationV7.OperationCapabilityVersion)).ToInt32() == 416
            && Marshal.OffsetOf<NativeFirstInstallInformationV7>(nameof(NativeFirstInstallInformationV7.ConfiguredMask)).ToInt32() == 432
            && Marshal.OffsetOf<NativeFirstInstallInformationV7>(nameof(NativeFirstInstallInformationV7.PublishedMask)).ToInt32() == 440
            && Marshal.OffsetOf<NativeFirstInstallInformationV7>(nameof(NativeFirstInstallInformationV7.Row0)).ToInt32() == 448
            && Marshal.OffsetOf<NativeFirstInstallInformationV7>(nameof(NativeFirstInstallInformationV7.Row25)).ToInt32() == 2048);
        var declarations = Enumerable.Range(0,26).Select(i => NativeOperationPublication.Declaration((uint)i)).ToArray();
        Check(declarations.Select(d => d.Rva).Distinct().Count() == 26
            && declarations.Select(d => (d.Kind,d.Slot)).Distinct().Count() == 26
            && declarations.Count(d => d.Kind == NativeOperationKind.Void0) == 9
            && declarations.Count(d => d.Kind == NativeOperationKind.Reference1) == 13
            && declarations.Count(d => d.Kind == NativeOperationKind.Reference2) == 3
            && declarations.Count(d => d.Kind == NativeOperationKind.Single1) == 1);
        var add = UnityEventOperationProfile.Get(UnityEventOperation.AddDelegate);
        var remove = UnityEventOperationProfile.Get(UnityEventOperation.RemoveDelegate);
        Check(UnityEventOperationProfile.All.Count == 14 && add.Receiver == UnityEventOperationReceiver.Invokable
            && remove.Receiver == UnityEventOperationReceiver.Invokable && add.Class == "InvokableCall" && remove.Class == add.Class
            && add.Method == "add_Delegate" && remove.Method == "remove_Delegate"
            && add.Rva == 0x154E890 && remove.Rva == 0x154E930
            && add.Parameters.SequenceEqual(new[] { "UnityEngine.Events.UnityAction" }) && remove.Parameters.SequenceEqual(add.Parameters)
            && add.Kind == NativeOperationKind.Reference1 && remove.Kind == add.Kind
            && declarations[12] == (NativeOperationKind.Reference1, 27u, 0x154E890u)
            && declarations[13] == (NativeOperationKind.Reference1, 26u, 0x154E930u));
        var tweenRows = TweenOperationProfile.All;
        Check(tweenRows.Count == 5 && tweenRows.Select(row => row.Rva).SequenceEqual(new[] { 0x3956F0, 0x396BE0, 0x396E40, 0x398D60, 0x396CF0 })
            && declarations[20] == (NativeOperationKind.Void0, 25u, 0x3956F0u)
            && declarations[21] == (NativeOperationKind.Reference1, 20u, 0x396BE0u)
            && declarations[22] == (NativeOperationKind.Reference1, 19u, 0x396E40u)
            && declarations[23] == (NativeOperationKind.Void0, 24u, 0x398D60u)
            && declarations[24] == (NativeOperationKind.Void0, 23u, 0x396CF0u)
            && declarations[25] == (NativeOperationKind.Single1, 31u, 0xbc0ff0u)
            && tweenRows.Where(row => row.Kind == NativeOperationKind.Reference1).All(row => row.Parameters.SequenceEqual(new[] { "TweenInfo" }))
            && tweenRows.Where(row => row.Kind == NativeOperationKind.Void0).All(row => row.Parameters.Count == 0));
    }
    private static NativeFirstInstallInformationV7 Publication()
    {
        var listener = NativeFirstInstallAdoptionTests.Publication();
        var value = new NativeFirstInstallInformationV7 { Listener = listener, OperationCapabilityVersion = 4,
            OperationCount = 26, ExpectedWrites = 64, ConfiguredMask = 0x3ffffff, PublishedMask = 0x3ffffff };
        for (uint i = 0; i < 26; ++i) {
            var d = NativeOperationPublication.Declaration(i);
            value = SetRow(value, i, new() { Id = i, Kind = (uint)d.Kind, Slot = d.Slot, Rva = d.Rva,
                Target = new(unchecked((long)listener.Common.ModuleBase + d.Rva)), Original = new(0x710000 + i * 0x100),
                Entry = new(0x810000 + i * 0x100), Epoch = i + 1, BodySize = 64, UnwindRva = 0x2000000, Protection = 0x20 });
        }
        value.SingleEvent = SinglePublication(value);
        return value;
    }
    private static NativeTweenSingleEventPublication SinglePublication(NativeFirstInstallInformationV7 parent)
    {
        var module = new IntPtr(unchecked((long)parent.Listener.Common.ModuleBase));
        var block = IntPtr.Add(module, 0x3000000);
        var value = new NativeTweenSingleEventPublication {
            CapabilityVersion = 1, ByteSize = 416, Phase = 3, OperationId = 25, Kind = 3, Slot = 31, ExpectedWrites = 7,
            Epoch = 101, ParentEpoch = parent.Row25.Epoch, MidEntry = new(0x910001), PreEntry = new(0x910101), PostEntry = new(0x910201),
            Allocation = block, AllocationSize = 4096, MidRelay = block + 0x500, PreRelay = block + 0x520, PostRelay = block + 0x540,
            DirectTarget = module + 0x154e5b0, FunctionTable = block + 0x380, FunctionTableBase = parent.Listener.Common.ModuleBase,
            MidReturnPc = module + 0xbc108c, RegisteredFunctionCount = 3, LookupChecks = 15, BoundaryChecks = 15,
            ConfiguredMask = 127, PublishedMask = 127, AttemptedWrites = 7, CompletedWrites = 7 };
        int[] rvas = { 0xbc1087, 0xbc1107, 0xbc110d, 0xbc115d, 0xbc1162, 0xbc121b, 0xbc1221 };
        uint[] widths = { 9, 6, 9, 5, 9, 6, 9 };
        object boxed = value;
        void Set(string field, object item) => typeof(NativeTweenSingleEventPublication).GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(boxed, item);
        for (var i = 0; i < 7; ++i) { Set("SpanPc" + i, module + rvas[i]); Set("SpanWidth" + i, widths[i]); Set("Protection" + i, 0x20u); }
        for (var i = 0; i < 3; ++i) {
            var stub = block + i * 0x100;
            Set("SourceReturnPc" + i, module + rvas[1 + i * 2] + checked((int)widths[1 + i * 2]));
            Set("PostStub" + i, stub); Set("PostTransportReturnPc" + i, stub + 5); Set("UnwindInfo" + i, stub + 0x40);
            Set("StubSize" + i, 21u); Set("UnwindSize" + i, 36u);
        }
        return (NativeTweenSingleEventPublication)boxed;
    }
    private static void SingleReceipt()
    {
        var good = Publication(); Check(NativeOperationPublication.HasShape(good));
        foreach (var field in new[] { "CapabilityVersion", "ByteSize", "Phase", "Unknown", "OperationId", "Kind", "Slot", "ExpectedWrites",
            "Epoch", "ParentEpoch", "MidEntry", "PreEntry", "PostEntry", "Allocation", "AllocationSize", "MidRelay", "PreRelay", "PostRelay",
            "DirectTarget", "FunctionTable", "FunctionTableBase", "MidReturnPc", "RegisteredFunctionCount", "LookupChecks", "BoundaryChecks",
            "Reserved", "ConfiguredMask", "PublishedMask", "AttemptedWrites", "CompletedWrites" }
            .Concat(Enumerable.Range(0, 7).SelectMany(i => new[] { "SpanPc" + i, "SpanWidth" + i, "Protection" + i }))
            .Concat(Enumerable.Range(0, 3).SelectMany(i => new[] { "SourceReturnPc" + i, "PostStub" + i, "PostTransportReturnPc" + i,
                "UnwindInfo" + i, "StubSize" + i, "UnwindSize" + i })))
        {
            var changed = good; object boxed = changed.SingleEvent;
            var property = typeof(NativeTweenSingleEventPublication).GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var current = property.GetValue(boxed)!;
            property.SetValue(boxed, current switch { uint number => number == 0 ? 1u : 0u,
                ulong number => number == 0 ? 1UL : 0UL, IntPtr _ => IntPtr.Zero, _ => throw new InvalidOperationException(field) });
            changed.SingleEvent = (NativeTweenSingleEventPublication)boxed;
            Check(!NativeOperationPublication.HasShape(changed));
        }
        for (var i = 0; i < 7; ++i) {
            var changed = good; changed.SingleEvent.PublishedMask &= ~(1u << i); Check(!NativeOperationPublication.HasShape(changed));
            changed = good; changed.SingleEvent.ConfiguredMask &= ~(1u << i); Check(!NativeOperationPublication.HasShape(changed));
        }
        var excess = good; excess.SingleEvent.PublishedMask |= 128; Check(!NativeOperationPublication.HasShape(excess));
        excess = good; excess.SingleEvent.CompletedWrites = 8; Check(!NativeOperationPublication.HasShape(excess));
        excess = good; excess.SingleEvent.ParentEpoch++; Check(!NativeOperationPublication.HasShape(excess));
    }
    private static NativeFirstInstallInformationV7 SetRow(NativeFirstInstallInformationV7 value, uint index, NativeOperationPublicationRow row)
    {
        object boxed = value;
        typeof(NativeFirstInstallInformationV7).GetField("Row" + index, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(boxed,row);
        return (NativeFirstInstallInformationV7)boxed;
    }
    private static void Receipt()
    {
        var good = Publication(); Check(NativeOperationPublication.HasShape(good));
        Check(NativeListenerPublication.HasShapeV7Prefix(good.Listener));
        for (uint i = 0; i < 26; ++i) {
            foreach (var field in new[] { "slot", "kind", "target", "epoch", "reserved", "rva", "body", "unwind", "protection", "duplicate" }) {
                var row = NativeOperationPublication.Row(good,i);
                switch (field) {
                    case "slot": row.Slot ^= 1; break; case "kind": row.Kind = (row.Kind + 1) % 3; break;
                    case "target": row.Target += 8; break; case "epoch": row.Epoch = 0; break;
                    case "reserved": row.Reserved = 1; break; case "rva": row.Rva += 1; break;
                    case "body": row.BodySize = uint.MaxValue; break; case "unwind": row.UnwindRva = 0; break;
                    case "protection": row.Protection |= 0x100; break;
                    default: row.Original = NativeOperationPublication.Row(good,(i + 1) % 26).Original; break;
                }
                Check(!NativeOperationPublication.HasShape(SetRow(good,i,row)));
            }
            var missing = good; missing.ConfiguredMask &= ~(1UL << (int)i); Check(!NativeOperationPublication.HasShape(missing));
            missing = good; missing.PublishedMask &= ~(1UL << (int)i); Check(!NativeOperationPublication.HasShape(missing));
        }
        foreach (var field in new[] { "version", "size", "writes", "count", "capability", "unknown", "reserved", "extra", "previous-version", "previous-capability" }) {
            var value = good;
            switch (field) {
                case "version": value.Listener.Common.Version = 2; break; case "size": value.Listener.Common.Size = 416; break;
                case "writes": value.Listener.Common.WritesStarted = 9; break; case "count": value.OperationCount--; break;
                case "capability": value.OperationCapabilityVersion = 0; break; case "unknown": value.Listener.Common.Unknown = 1; break;
                case "reserved": value.Reserved = 1; break;
                case "previous-version": value.Listener.Common.Version = 4; value.Listener.Common.Size = 1728; break;
                case "previous-capability": value.OperationCapabilityVersion = 2; break;
                default: value.PublishedMask |= 1UL << 26; break;
            }
            Check(!NativeOperationPublication.HasShape(value));
        }
    }
    private static void Information()
    {
        var row = NativeOperationPublication.Row(Publication(),0); var id = row.Identity;
        var good = new NativeOperationBootstrapInformation { Version = 1, Size = 96, Kind = row.Kind, Slot = row.Slot,
            Target = row.Target, Original = row.Original, Entry = row.Entry, Epoch = row.Epoch };
        Check(NativeBootstrappedOperationBridge.MatchesInformation(good,id,false)); good.Attached = 1;
        Check(NativeBootstrappedOperationBridge.MatchesInformation(good,id,true));
        foreach (var field in new[] { "version", "size", "kind", "slot", "attached", "unknown", "target", "original", "entry", "epoch", "enterFault", "exitFault", "overflow" }) {
            var value = good;
            switch (field) {
                case "version": value.Version++; break; case "size": value.Size--; break; case "kind": value.Kind++; break;
                case "slot": value.Slot--; break; case "attached": value.Attached = 2; break; case "unknown": value.Unknown = 1; break;
                case "target": value.Target += 1; break; case "original": value.Original += 1; break; case "entry": value.Entry += 1; break;
                case "epoch": value.Epoch++; break; case "enterFault": value.EnterFaults = 1; break; case "exitFault": value.ExitFaults = 1; break;
                default: value.InFlight = ulong.MaxValue; break;
            }
            Check(!NativeBootstrappedOperationBridge.MatchesInformation(value,id,true));
        }
    }
    private static void NoLateInstall()
    {
        var engine = new Engine(); var broker = new ExactHookBroker(engine,new Main(),new LoggerStub());
        var target = new VerifiedHookTarget("operation.no.late.install",new(0x7ffe00001000),new(0x123),new(0x456),ExactHookAbi.OperationVoid0);
        Reject(() => broker.BindNativeOperationObservation<Context>(target,NativeOperationKind.Void0,_ => new(),(_,_,_) => {},(_,_) => {}));
        Check(engine.Calls == 0);
    }
    private sealed class Context : IDisposable { public void Dispose() {} }
    private sealed class Engine : INativeDetourEngine, IPreparedNativeDetourEngine {
        internal int Calls;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) { ++Calls; throw new Exception("Late Install"); }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) { ++Calls; throw new Exception("Late Prepare"); }
        public void Commit(VerifiedHookTarget target) { ++Calls; throw new Exception("Late Commit"); }
    }
    private sealed class Main : IMainThreadDispatcher {
        public bool IsMainThread => true; public void VerifyAccess() {}
        public Task InvokeAsync(Action action,CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class LoggerStub : ILogger { public void Log(LogLevel level,string message,Exception? exception = null) {} }
}
