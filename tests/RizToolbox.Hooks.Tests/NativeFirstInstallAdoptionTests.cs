using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

/// <summary>ABI and rejection checks only. Native producer publication and the
/// actual game startup remain separate native/runtime verification obligations.</summary>
internal static class NativeFirstInstallAdoptionTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Native pre-init V7 common and listener prefixes preserve exact layouts and have no caller-created ready instance", ProducerLayout),
        ("Native pre-init receipt refuses nonready partial unknown and incomplete publications", ProducerRejections),
        ("native publication errors distinguish physical Tween sites, callback faults and legacy context failures", TweenPublicationDiagnostics),
        ("Runtime-invoke After frame and information retain the native 64 and 80 byte ABI", RuntimeLayout),
        ("Runtime-invoke adoption rejects foreign identities observer faults and incorrect attachment state", RuntimeRejections)
    };
    private static void Check(bool condition, string message = "Native producer adoption check failed")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Offset<T>(string field, int expected) where T : struct
        => Check(Marshal.OffsetOf<T>(field).ToInt32() == expected, "Native ABI offset changed: " + field);
    private static void TweenPublicationDiagnostics()
    {
        static string Describe(uint detail, uint reason = 4, uint phase = 4)
        {
            var info = Producer();
            info.Unknown = 1; info.Phase = phase; info.Reserved1 = ((ulong)detail << 32) | reason;
            return NativeFirstInstall.DescribePublicationFailure(0, info);
        }
        foreach (var site in Enum.GetValues<NativeTweenSourceSite>())
            Check(Describe(8 + (uint)site).Contains("physical Tween source site " + site + " failed identity"), "Missing physical site " + site);
        Check(Describe(5).Contains("source context became unknown") && Describe(0x01160114).Contains("PlayOnStart (Before)")
            && Describe(0x1b).Contains("physical Tween source site PlayOnStart")
            && !Describe(30).Contains("physical Tween") && !Describe(27, reason: 3).Contains("physical Tween")
            && !Describe(27, phase: 3).Contains("physical Tween"), "Physical and callback failure codes were conflated.");
    }
    private static void ProducerLayout()
    {
        Check(Marshal.SizeOf<NativeFirstInstallInformation>() == 144);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.VoidSlot), 16);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.ModuleBase), 24);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.Init), 40);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.RuntimeTarget), 48);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.RuntimeOriginal), 56);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.RuntimeEntry), 64);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.RuntimeEpoch), 72);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.UnityTarget), 80);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.UnityEpoch), 104);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.MidReturnPc), 112);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.Relay), 120);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.WritesStarted), 128);
        Offset<NativeFirstInstallInformation>(nameof(NativeFirstInstallInformation.Reserved1), 136);
        Check(typeof(NativeFirstInstall).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).All(value => value.IsPrivate),
            "A caller can create its own producer receipt.");
        Check(Marshal.SizeOf<NativeFirstInstallInformationV2>() == 416 && Marshal.SizeOf<NativeListenerInformation>() == 176);
        foreach (var (field, offset) in new[] {
            (nameof(NativeFirstInstallInformationV2.Common), 0), (nameof(NativeFirstInstallInformationV2.CapabilityVersion), 144),
            (nameof(NativeFirstInstallInformationV2.ListenerSlot), 148), (nameof(NativeFirstInstallInformationV2.ListenerEpoch), 152),
            (nameof(NativeFirstInstallInformationV2.PreEntry), 160), (nameof(NativeFirstInstallInformationV2.PreRelay), 176),
            (nameof(NativeFirstInstallInformationV2.SourcePc0), 192), (nameof(NativeFirstInstallInformationV2.SourceReturnPc0), 216),
            (nameof(NativeFirstInstallInformationV2.PostStub0), 240), (nameof(NativeFirstInstallInformationV2.PostTransportReturnPc0), 264),
            (nameof(NativeFirstInstallInformationV2.DirectTarget0), 288), (nameof(NativeFirstInstallInformationV2.FunctionTable), 304),
            (nameof(NativeFirstInstallInformationV2.FunctionTableBase), 312), (nameof(NativeFirstInstallInformationV2.UnwindInfo0), 320),
            (nameof(NativeFirstInstallInformationV2.SourceWidth0), 344), (nameof(NativeFirstInstallInformationV2.PostWidth0), 356),
            (nameof(NativeFirstInstallInformationV2.SourceProtection0), 368), (nameof(NativeFirstInstallInformationV2.PostProtection0), 380),
            (nameof(NativeFirstInstallInformationV2.RegisteredFunctionCount), 392), (nameof(NativeFirstInstallInformationV2.PrewriteLookupChecks), 396),
            (nameof(NativeFirstInstallInformationV2.PrewriteBoundaryChecks), 400), (nameof(NativeFirstInstallInformationV2.Reserved2), 404),
            (nameof(NativeFirstInstallInformationV2.AllocationSize), 408) }) Offset<NativeFirstInstallInformationV2>(field, offset);
        foreach (var (field, offset) in new[] { (nameof(NativeListenerInformation.Epoch), 16),
            (nameof(NativeListenerInformation.PreEntry), 32), (nameof(NativeListenerInformation.SourceReturnPc0), 48),
            (nameof(NativeListenerInformation.PostTransportReturnPc0), 72), (nameof(NativeListenerInformation.DirectTarget0), 96),
            (nameof(NativeListenerInformation.Entered), 112), (nameof(NativeListenerInformation.Unknown), 168) })
            Offset<NativeListenerInformation>(field, offset);
    }
    private static NativeFirstInstallInformation Producer() => new() { Version = 7, Size = 2528, Phase = 3,
        VoidSlot = NativeFirstInstall.ReservedVoidSlot, ModuleBase = 0x180000000, ImageSize = 0x243c000,
        Init = new(1), RuntimeTarget = new(2), RuntimeOriginal = new(3), RuntimeEntry = new(4), RuntimeEpoch = 5,
        UnityTarget = new(6), UnityOriginal = new(7), UnityEntry = new(8), UnityEpoch = 9, MidReturnPc = new(10), Relay = new(11), WritesStarted = 64 };
    private static void ProducerRejections()
    {
        bool Ready(NativeFirstInstallInformation value) => NativeFirstInstall.ReadyCommonShape(value, 7, 2528, 64);
        Check(Ready(Producer()));
        foreach (var phase in new uint[] { 0, 1, 2, 4, uint.MaxValue })
        { var value = Producer(); value.Phase = phase; Check(!Ready(value), "A nonready native phase was accepted."); }
        foreach (var writes in new ulong[] { 0, 1, 2, 3, 4, 8, 9, 10, 26, 27, 28, 29, 30, 33, 35, ulong.MaxValue })
        { var value = Producer(); value.WritesStarted = writes; Check(!Ready(value), "A partial or excess write receipt was accepted."); }
        foreach (var field in new[] { "version", "size", "unknown", "slot", "reserved0", "reserved1", "module", "sizeimage", "original", "epoch", "mid", "relay" })
        {
            var value = Producer();
            switch (field) {
                case "version": value.Version++; break; case "size": value.Size--; break; case "unknown": value.Unknown = 1; break;
                case "slot": value.VoidSlot--; break; case "reserved0": value.Reserved0 = 1; break; case "reserved1": value.Reserved1 = 1; break;
                case "module": value.ModuleBase = 0; break; case "sizeimage": value.ImageSize = 0x1564420; break;
                case "original": value.RuntimeOriginal = IntPtr.Zero; break; case "epoch": value.UnityEpoch = 0; break;
                case "mid": value.MidReturnPc = IntPtr.Zero; break; default: value.Relay = IntPtr.Zero; break;
            }
            Check(!Ready(value), "Incomplete native receipt was accepted: " + field);
        }
        var publication = Publication(); Check(NativeListenerPublication.HasShapeV7Prefix(publication));
        foreach (var field in new[] { "version", "size", "writes", "capability", "slot", "epoch", "pre", "source", "return", "stub", "transport",
            "direct", "table", "base", "unwind", "sourceWidth", "postWidth", "protection", "lookup", "boundary", "reserved", "allocation" })
        {
            var value = publication;
            switch (field) {
                case "version": value.Common.Version = 2; break;
                case "size": value.Common.Size = 416; break;
                case "writes": value.Common.WritesStarted = 9; break;
                case "capability": value.CapabilityVersion = 0; break; case "slot": --value.ListenerSlot; break;
                case "epoch": value.ListenerEpoch = 0; break; case "pre": value.PreRelay += 1; break;
                case "source": value.SourcePc2 += 1; break; case "return": value.SourceReturnPc2 -= 1; break;
                case "stub": value.PostStub1 = value.PostStub0; break; case "transport": value.PostTransportReturnPc1 += 1; break;
                case "direct": value.DirectTarget1 += 1; break; case "table": value.FunctionTable += 1; break;
                case "base": value.FunctionTableBase += 1; break; case "unwind": value.UnwindInfo0 += 1; break;
                case "sourceWidth": value.SourceWidth2 = 5; break; case "postWidth": value.PostWidth1 = 6; break;
                case "protection": value.SourceProtection1 |= 0x100; break; case "lookup": value.PrewriteLookupChecks = 11; break;
                case "boundary": value.PrewriteBoundaryChecks = 11; break; case "reserved": value.Reserved2 = 1; break;
                default: value.AllocationSize = 0; break;
            }
            Check(!NativeListenerPublication.HasShapeV7Prefix(value), "Incomplete V7 listener prefix was accepted: " + field);
        }
        var lane = new NativeListenerInformation { Version = 1, Size = 176, Slot = publication.ListenerSlot,
            Epoch = publication.ListenerEpoch, ParentEpoch = publication.Common.UnityEpoch, PreEntry = publication.PreEntry, PostEntry = publication.PostEntry,
            SourceReturnPc0 = publication.SourceReturnPc0, SourceReturnPc1 = publication.SourceReturnPc1, SourceReturnPc2 = publication.SourceReturnPc2,
            PostTransportReturnPc0 = publication.PostTransportReturnPc0, PostTransportReturnPc1 = publication.PostTransportReturnPc1,
            PostTransportReturnPc2 = publication.PostTransportReturnPc2, DirectTarget0 = publication.DirectTarget0, DirectTarget1 = publication.DirectTarget1 };
        Check(NativeListenerPublication.MatchesLane(lane, publication)); lane.Attached = 1;
        Check(NativeListenerPublication.MatchesLane(lane, publication), "A legal immutable observer attachment changed lane identity.");
        foreach (var field in new[] { "epoch", "parent", "source", "post", "target", "attached", "unknown", "fault", "overflow", "missing" })
        {
            var value = lane;
            switch (field) {
                case "epoch": ++value.Epoch; break; case "parent": ++value.ParentEpoch; break;
                case "source": value.SourceReturnPc1 += 1; break; case "post": value.PostTransportReturnPc2 += 1; break;
                case "target": value.DirectTarget1 += 1; break; case "attached": value.Attached = 2; break;
                case "unknown": value.Unknown = 1; break; case "fault": value.Faults = 1; break;
                case "overflow": value.Overflow = 1; break; default: value.MissingPost = 1; break;
            }
            Check(!NativeListenerPublication.MatchesLane(value, publication), "A changed or unknown listener lane was accepted: " + field);
        }
    }
    internal static NativeFirstInstallInformationV2 Publication()
    {
        var common = Producer(); common.Relay = new(0x184000520);
        return new() { Common = common, CapabilityVersion = 1, ListenerSlot = common.VoidSlot, ListenerEpoch = 2,
            PreEntry = new(0x101), PostEntry = new(0x102), PreRelay = new(0x184000500), PostRelay = new(0x184000540),
            SourcePc0 = new(0x181564470), SourcePc1 = new(0x1815644c2), SourcePc2 = new(0x18156451f),
            SourceReturnPc0 = new(0x181564475), SourceReturnPc1 = new(0x1815644c7), SourceReturnPc2 = new(0x181564525),
            PostStub0 = new(0x184000000), PostStub1 = new(0x184000100), PostStub2 = new(0x184000200),
            PostTransportReturnPc0 = new(0x184000005), PostTransportReturnPc1 = new(0x184000105), PostTransportReturnPc2 = new(0x184000205),
            DirectTarget0 = new(0x18154e5b0), DirectTarget1 = new(0x18154e5b0), FunctionTable = new(0x184000380), FunctionTableBase = common.ModuleBase,
            UnwindInfo0 = new(0x184000040), UnwindInfo1 = new(0x184000140), UnwindInfo2 = new(0x184000240),
            SourceWidth0 = 5, SourceWidth1 = 5, SourceWidth2 = 6, PostWidth0 = 6, PostWidth1 = 9, PostWidth2 = 9,
            SourceProtection0 = 0x20, SourceProtection1 = 0x20, SourceProtection2 = 0x20,
            PostProtection0 = 0x20, PostProtection1 = 0x20, PostProtection2 = 0x20,
            RegisteredFunctionCount = 3, PrewriteLookupChecks = 12, PrewriteBoundaryChecks = 12, AllocationSize = 4096 };
    }
    private static void RuntimeLayout()
    {
        Check(Marshal.SizeOf<NativeRuntimeInvokeFrame>() == 64 && Marshal.SizeOf<NativeRuntimeInvokeInformation>() == 80);
        foreach (var (field, offset) in new[] { (nameof(NativeRuntimeInvokeFrame.Method), 8), (nameof(NativeRuntimeInvokeFrame.Self), 16),
            (nameof(NativeRuntimeInvokeFrame.Arguments), 24), (nameof(NativeRuntimeInvokeFrame.Exception), 32), (nameof(NativeRuntimeInvokeFrame.Result), 40),
            (nameof(NativeRuntimeInvokeFrame.Caller), 48), (nameof(NativeRuntimeInvokeFrame.OsThreadId), 56), (nameof(NativeRuntimeInvokeFrame.Reserved), 60) })
            Offset<NativeRuntimeInvokeFrame>(field, offset);
        Offset<NativeRuntimeInvokeInformation>(nameof(NativeRuntimeInvokeInformation.Target), 16);
        Offset<NativeRuntimeInvokeInformation>(nameof(NativeRuntimeInvokeInformation.Epoch), 40);
        Offset<NativeRuntimeInvokeInformation>(nameof(NativeRuntimeInvokeInformation.InFlight), 64);
        Offset<NativeRuntimeInvokeInformation>(nameof(NativeRuntimeInvokeInformation.ObserverFaults), 72);
        var observer = typeof(NativeRuntimeInvokeBridge).GetNestedType("Observer", BindingFlags.NonPublic)!;
        Check(Marshal.SizeOf(observer) == 24 && Marshal.OffsetOf(observer, "After").ToInt32() == 8 && Marshal.OffsetOf(observer, "User").ToInt32() == 16);
    }
    private static void RuntimeRejections()
    {
        var valid = new NativeRuntimeInvokeInformation { Version = 1, Size = 80, Target = new(1), Original = new(2), Entry = new(3), Epoch = 4, InFlight = 1 };
        bool Matches(NativeRuntimeInvokeInformation value, bool attached = false)
            => NativeRuntimeInvokeBridge.MatchesInformation(value, new(1), new(2), new(3), 4, attached);
        Check(Matches(valid)); var attached = valid; attached.Attached = 1; Check(!Matches(attached) && Matches(attached, true));
        foreach (var field in new[] { "version", "size", "target", "original", "entry", "epoch", "unknown", "fault", "inflight", "attached" })
        {
            var value = valid;
            switch (field) {
                case "version": value.Version++; break; case "size": value.Size--; break; case "target": value.Target += 1; break;
                case "original": value.Original += 1; break; case "entry": value.Entry += 1; break; case "epoch": value.Epoch++; break;
                case "unknown": value.Unknown = 1; break; case "fault": value.ObserverFaults = 1; break;
                case "inflight": value.InFlight = ulong.MaxValue; break; default: value.Attached = 9; break;
            }
            Check(!Matches(value), "Incorrect runtime-invoke identity was accepted: " + field);
        }
        var frame = new NativeRuntimeInvokeFrame { Version = 1, Size = 64, Method = new(1), Caller = new(2), OsThreadId = 3 };
        Check(frame.IsValid && frame.Self == IntPtr.Zero && frame.Arguments == IntPtr.Zero && frame.Result == IntPtr.Zero,
            "Valid static zero-argument normal-null return was rejected.");
        frame.Reserved = 1; Check(!frame.IsValid);
    }
}
