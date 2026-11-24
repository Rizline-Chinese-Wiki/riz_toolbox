using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class NativeFirstInstallMappedRegression
{
    private static readonly int[] RizOperationRvas = {
        0x15640a0,0x15630e0,0x1563c50,0x1563b80,0x15631a0,0x1563b40,
        0x154ded0,0x154df90,0x154e280,0x154e110,0x154e050,0x1550ee0,0x154e890,0x154e930,
        0x153d900,0x153d7f0,0x153d6c0,0x153d750,0x153d700,0x153d7a0,
        0x3956f0,0x396be0,0x396e40,0x398d60,0x396cf0,0xbc0ff0 };
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint FirstInfoV7Native(ref NativeFirstInstallInformationV7 info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint OperationInfoNative(uint kind, uint slot, ref NativeOperationBootstrapInformation info);
    private sealed record OperationAdoptionEvidence(bool ProductionV7Verified, int DeclaredChannels, int ReadOnlyChannels,
        int AttachedChannels, int SharedPeerSinks, int LogicalDetachChecks, int RejectedWrongTargets, int RejectedWrongKinds,
        int RejectedConflictingMetadata, bool UnchangedInstructions, bool ZeroEngineInstallCalls, bool MetadataControlled,
        bool ActualGameCallbacksVerified, bool SingleEventPublicationVerified, bool SingleEventMemoryVerified,
        bool SingleEventReadonlyAdoption);

    private static bool ListenerViewAgrees(NativeFirstInstallInformationV2 prefix, NativeFirstInstallInformationV2 v2)
    {
        if (prefix.Common.Version != 7 || prefix.Common.Size != 2528 || prefix.Common.WritesStarted != 64) return false;
        return prefix.Equals(v2);
    }
    private static OperationAdoptionEvidence ValidateOperationPublication(NativeFirstInstall producer, IntPtr helper, IntPtr module,
        string mode, Logger logger, Counters counters, NeverInstallEngine engine, Action<bool,string> check)
    {
        var read = Export<FirstInfoV7Native>(helper, "RizFirstInstall_GetInfoV7");
        var readLane = Export<OperationInfoNative>(helper, "RizOperationBootstrap_GetInfo");
        var publication = new NativeFirstInstallInformationV7 { Listener = new() { Common = new() { Version = 7, Size = 2528 } } };
        check(read(ref publication) == 0 && NativeOperationPublication.HasShape(publication)
            && publication.Equals(producer.OperationPublication), "The actual V7 publication differs from the private production owner.");
        var before = CodeSnapshot(module); var attached = 0; var shared = 0; var detached = 0;
        var wrongTarget = 0; var wrongKind = 0; var metadata = 0;
        var main = new MainDispatcher(); var broker = new ExactHookBroker(engine, main, logger); var peer = new ExactHookBroker(engine, main, logger);
        Owners.Add(main); Owners.Add(broker); Owners.Add(peer);
        NativeOperationBootstrapInformation Info(NativeOperationPublicationRow row)
        {
            var value = new NativeOperationBootstrapInformation { Version = 1, Size = 96 };
            check(readLane(row.Kind, row.Slot, ref value) == 0, "Actual configured operation information is missing.");
            return value;
        }
        for (uint i = 0; i < NativeOperationPublication.Count; ++i)
        {
            var row = NativeOperationPublication.Row(publication, i); var identity = row.Identity;
            check(row.Id == i && row.Rva == RizOperationRvas[i] && row.Target == IntPtr.Add(module, RizOperationRvas[i]), "V7 declaration order or pinned target differs.");
            check(NativeBootstrappedOperationBridge.MatchesInformation(Info(row), identity, false), "An initially unattached permanent operation identity changed.");
            if (mode != "happy") continue;
            // These metadata values deliberately are opaque controlled carriers.
            // Only real production resolvers can authorize actual IL2CPP metadata.
            var target = new VerifiedHookTarget("mapped.operation." + i, row.Target, new(0x340000 + i * 0x100),
                new(0x450000 + i * 0x100), ExactHookBroker.OperationAbi(identity.Kind));
            ExactHookBroker.Binding Bind(ExactHookBroker owner, VerifiedHookTarget requested, NativeOperationKind kind)
                => owner.BindNativeOperationObservation<ObservationContext>(requested, kind,
                    _ => { ++counters.Captures; return new(counters); }, (_, _, _) => ++counters.Afters, (_, _) => ++counters.Faults);
            check(Rejected<UnsupportedSignatureException>(() => Bind(broker, target with { Address = target.Address + 16 }, identity.Kind)), "A nonallowlisted address consumed a production operation slot."); ++wrongTarget;
            var otherKind = identity.Kind == NativeOperationKind.Void0 ? NativeOperationKind.Reference1 : NativeOperationKind.Void0;
            check(Rejected<UnsupportedSignatureException>(() => Bind(broker, target with { Abi = ExactHookBroker.OperationAbi(otherKind) }, otherKind)), "A mismatched operation ABI consumed the permanent native slot."); ++wrongKind;
            check(NativeBootstrappedOperationBridge.MatchesInformation(Info(row), identity, false), "A rejected declaration attached a native operation observer.");
            var first = Bind(broker, target, identity.Kind); Owners.Add(first); ++attached;
            check(first.ReadActivity() is { Known: true, InFlight: 0 }
                && NativeBootstrappedOperationBridge.MatchesInformation(Info(row), identity, true), "Production operation adoption failed to attach its idle real channel.");
            var second = Bind(peer, target with { Id = "mapped.operation.peer." + i }, identity.Kind); Owners.Add(second); ++shared;
            check(Rejected<UnsupportedSignatureException>(() => Bind(peer, target with { MethodInfo = target.MethodInfo + 8 }, identity.Kind)), "A metadata alias shared an adopted operation channel."); ++metadata;
            first.Dispose();
            check(second.ReadActivity() is { Known: true, InFlight: 0 }
                && NativeBootstrappedOperationBridge.MatchesInformation(Info(row), identity, true), "Logical detach changed a permanent operation transport or peer."); ++detached;
            second.Dispose();
            var after = Info(row);
            check(after.Entered == 0 && after.Exited == 0 && after.InFlight == 0 && after.EnterFaults == 0 && after.ExitFaults == 0,
                "A read-only mapped adoption unexpectedly called game code or a native original.");
        }
        check(before.SequenceEqual(CodeSnapshot(module)), "Operation CLR adoption modified published instructions.");
        check(engine.InstallCalls == 0 && engine.PrepareCalls == 0 && engine.CommitCalls == 0, "Production operation adoption attempted a second instruction installation.");
        producer.VerifyReady();
        return new(true, 26, 26, attached, shared, detached, wrongTarget, wrongKind, metadata, true, true, true, false, true, true, true);
    }
}
