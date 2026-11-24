using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;

// Actual request, engine identity and node ledgers. Frames and scalar providers
// remain controlled inputs; no Unity, native hook, cancellation or root release.
internal static class NativeCoroutineCancellationEngineAssociationTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Stop associates exact open engine Detach and preserves request and node generation identities", Exact),
        ("Nested owned Stop wrappers associate only the innermost physical request and retain ancestry", Nested),
        ("Foreign Stop masks do not read providers or count requests and restore the exact outer scope", ForeignMask),
        ("Owned Detach under a foreign Stop mask cannot borrow an owned ancestor", MaskedDetach),
        ("Detach candidates reject changed receiver provider owner Enumerator slot and generation", ChangedIdentity),
        ("Detach association rejects wrong native tuple and mismatched Stop arguments", ChangedNodeAndArgument),
        ("Detach candidates reject closed forged non-Detach and non-current engine scopes", PhysicalScopes),
        ("Worker Detach association reads no providers and retains owned Unknown responsibility", Worker),
        ("Provider reentry and tuple mutation cannot publish a Detach candidate", ProviderDrift),
        ("Named Stop records only an observed Detach subset and retains unproven matching", Named),
        ("Abnormal or unpaired foreign Stop masks retain responsibility and exact cleanup", MaskCompletion),
        ("Detach association and physical masks preserve duplicate and bounded overflow obligations", Bounds),
        ("Stop immediately after Start upgrades the exact returned handle without waiting for a frame pump", ImmediateStop),
        ("Cancellation terminal candidates require actual Detach Complete Dispose and final node cleanup", TerminalCandidate),
        ("Cancellation exact request coverage includes nested ancestry and rejects unmatched populations", TerminalCoverage),
        ("Named cancellation subset and copied candidate cannot issue terminal authority", TerminalSubset),
        ("Historical terminal candidates retain their own generation through native address reuse", TerminalGeneration),
        ("Identical scalar node and request identities cannot borrow another actual identity ledger", TerminalLedgerOwner)
    };
    private static void Check(bool value, string message = "Stop engine association assertion failed.")
    { if (!value) throw new InvalidOperationException(message); }
    private sealed class Provider
    {
        internal readonly object Owner = new();
        internal readonly long Receiver;
        internal readonly List<NativeCoroutineCancellationIdentity> Rows = new();
        internal int Reads, Currents;
        internal Action? DuringRead;
        internal Provider(long receiver) => Receiver = receiver;
        internal NativeCoroutineCancellationIdentityProvider Description => new(Owner, Receiver, () => {
            ++Reads; DuringRead?.Invoke(); return Rows.ToArray();
        }, row => { ++Currents; return Rows.Any(current => current.Equals(row)); });
        internal void Add(int slot = 0, long generation = 9, long enumerator = 1001)
            => Rows.Add(new(Owner, Receiver, slot, generation, enumerator, 0, false, false, true, false, false, true));
    }
    private sealed class Harness : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Provider Provider = new(101);
        internal readonly NativeEngineCoroutineNodeLedger Nodes;
        internal readonly NativeEngineCoroutineIdentityLedger Engine;
        internal readonly NativeCoroutineCancellationLedger Requests;
        internal const long Owner = 0x12340000;
        private ulong _engineSequence, _requestSequence;
        internal Harness()
        {
            Provider.Add(); Nodes = new(Main);
            Engine = new(Main, Nodes, new[] { Provider.Description }, new[] { new NativeEngineCoroutineOwnerPair(101, Owner) });
            Requests = new(Main, new[] { Provider.Description });
        }
        internal NativeEngineNodeFrame Start(long owner = Owner, long enumerator = 1001) => new() {
            Version=1, Size=112, Kind=NativeEngineNodeKind.Start, Arg0=new(owner), Arg1=new(enumerator),
            Arg3=new(0x9000), Caller=new(0x8000), Sequence=++_engineSequence };
        internal NativeEngineNodeFrame Node(NativeEngineNodeKind kind = NativeEngineNodeKind.Detach,
            long node = 301, long owner = Owner, long enumerator = 1001, ulong handle = 401) => new() {
            Version=1, Size=112, Kind=kind, Arg0=new(node), Node=new(node), Owner=new(owner),
            EnumeratorCache=new(enumerator), EnumeratorHandle=handle, EnumeratorType=2, ReferenceCount=17,
            Caller=new(0x8000), Sequence=++_engineSequence,
            SnapshotFlags=NativeEngineNodeSnapshotFlags.Requested|NativeEngineNodeSnapshotFlags.Valid };
        internal NativeEngineCoroutineIdentityLedger.Call Begin(bool refreshTracker = true)
        {
            var start=Engine.Capture(Start()); var execute=Engine.Capture(Node(NativeEngineNodeKind.Execute));
            End(execute); End(start, output:301);
            Provider.Rows[0]=Provider.Rows[0] with { StartReturned=true, ObservedHandle=5001, HandleConfirmed=true };
            if (refreshTracker) Engine.RefreshTrackedIdentities(); Check(!Engine.Unknown); return start;
        }
        internal NativeCoroutineCancellationLedger.RequestContext Stop(NativeCoroutineCancellationOperation operation = NativeCoroutineCancellationOperation.StopEnumerator,
            long argument = 1001, long receiver = 101)
        {
            var frame = new NativeOperationFrame { Version=1, Size=64,
                Kind=operation==NativeCoroutineCancellationOperation.StopAll?NativeOperationKind.Void0:NativeOperationKind.Reference1,
                Self=new(receiver), Arg0=operation==NativeCoroutineCancellationOperation.StopAll?IntPtr.Zero:new(argument),
                Method=new(0x123), Caller=new(0x7000), Sequence=++_requestSequence };
            return Requests.Capture(operation,frame) ?? throw new InvalidOperationException("Expected a physical Stop scope.");
        }
        internal void End(NativeCoroutineCancellationLedger.RequestContext context,
            NativeOperationExitFlags flags = NativeOperationExitFlags.Returned)
        { Requests.Complete(context,context.Frame,flags);context.Dispose(); }
        internal void End(NativeEngineCoroutineIdentityLedger.Call context, long output = 0, uint? raw = null)
        {
            var kind=context.Frame.Kind;
            Engine.Complete(context,context.Frame,new NativeEngineNodeCompletion { Version=1,Size=32,
                Flags=NativeEngineNodeExitFlags.Returned|(kind==NativeEngineNodeKind.Detach?0:NativeEngineNodeExitFlags.ReturnByteValid),
                RawReturn=kind==NativeEngineNodeKind.Detach?0u:raw??(kind==NativeEngineNodeKind.Execute?0x12345678u:1u),
                StartOutputNode=new(output), OutputFlags=kind==NativeEngineNodeKind.Start?NativeEngineNodeOutputFlags.Requested|NativeEngineNodeOutputFlags.Read:0 });
            context.Dispose();
        }
        internal IReadOnlyList<NativeCoroutineCancellationDetachCandidate> Candidates => Requests.Read().DetachCandidates!;
        public void Dispose() => Main.Dispose();
    }
    private static void Exact()
    {
        foreach(var operation in new[] { NativeCoroutineCancellationOperation.StopEnumerator, NativeCoroutineCancellationOperation.StopHandle,
            NativeCoroutineCancellationOperation.StopAll, NativeCoroutineCancellationOperation.StopManagedEnumerator, NativeCoroutineCancellationOperation.StopManagedHandle })
        {
            using var f=new Harness(); var start=f.Begin(); var request=f.Stop(operation,
                operation is NativeCoroutineCancellationOperation.StopHandle or NativeCoroutineCancellationOperation.StopManagedHandle?5001:1001);
            var call=f.Engine.Capture(f.Node()); Check(f.Requests.ObserveEngineDetach(call), operation+": "+f.Requests.Read().Failure);
            var row=f.Candidates.Single(); var saved=f.Requests.Read();
            Check(row.RequestId==request.RequestId && row.ParentRequestId==0 && row.RootRequestId==request.RequestId
                && row.RequestSequence==request.Frame.Sequence && row.DetachSequence==call.Frame.Sequence
                && row.StartSequence==start.Frame.Sequence && row.EngineIdentityId==call.Identity!.Id
                && row.Node==301 && row.NodeGeneration==1 && row.NativeOwner==Harness.Owner && row.EnumeratorHandle==401
                && row.Tracker.Equals(f.Provider.Rows.Single()) && !row.NamedMatchUnproven);
            Check(!call.Completed && !call.Disposed && !call.NodeCall!.Node!.DetachReturned);
            f.End(call); f.End(request);
            var state=f.Requests.Read(); Check(state is { Unknown:false,Pending:true,EngineCancelled:false,Outstanding:0 }
                && state.Requests.Single().Disposed && f.Engine.Read() is { Unknown:false,Outstanding:0,EngineCancelled:false });
            Check(!saved.Requests.Single().NativeCompleted && saved.DetachCandidates!.Count==1);
        }
    }
    private static void ImmediateStop()
    {
        using var f = new Harness(); f.Begin(refreshTracker: false);
        Check(f.Engine.Read().Identities.Single().Tracker.ObservedHandle == 0);
        var request = f.Stop(NativeCoroutineCancellationOperation.StopHandle, 5001);
        var call = f.Engine.Capture(f.Node());
        Check(f.Requests.ObserveEngineDetach(call), f.Requests.Read().Failure ?? "Missing immediate Stop association.");
        Check(call.Identity!.HandleUpgraded && call.Identity.Tracker.ObservedHandle == 5001
            && f.Candidates.Single().Tracker.HandleConfirmed && !f.Requests.Unknown && !f.Engine.Unknown);
        f.End(call); f.End(request); Check(f.Requests.Read().Pending && !f.Requests.Read().EngineCancelled);
    }
    private static void TerminalCandidate()
    {
        using var f = new Harness(); f.Begin(); var request = f.Stop(); var detach = f.Engine.Capture(f.Node());
        Check(f.Requests.ObserveEngineDetach(detach)); var candidate = f.Requests.ReadCompletionCandidates().Single();
        Check(!candidate.TryReadTerminal(f.Requests, out _));
        f.Engine.Complete(detach, detach.Frame, new NativeEngineNodeCompletion { Version=1,Size=32,Flags=NativeEngineNodeExitFlags.Returned });
        Check(!candidate.TryReadTerminal(f.Requests, out _)); detach.Dispose(); f.End(request);
        Check(!candidate.TryReadTerminal(f.Requests, out _));
        var release = f.Engine.Capture(f.Node(NativeEngineNodeKind.Release));
        f.End(release, raw: 0);
        Check(candidate.TryReadTerminal(f.Requests, out var selected) && selected is { Node:301,EngineGeneration:1 }
            && selected.StartSequence == candidate.Identity.StartSequence);
        Check(f.Requests.HasCompleteRequestCoverage(new[] { candidate }));
        var state = f.Requests.Read();
        Check(state is { Pending:true,EngineCancelled:false,Unknown:false,Outstanding:0 }
            && state.Completions!.Count == 0, "Local final-node proof cannot substitute for missing actual Inline/M/N and future-source closure.");
        var inline = new NativeEngineCoroutineInlineLedger(f.Main, f.Engine, f.Nodes);
        Check(!inline.HasCompletedStart(selected!), "A Start return without the actual side observer completion stays unproven.");
    }
    private static void TerminalCoverage()
    {
        using (var f = new Harness())
        {
            f.Begin(); var outer = f.Stop(NativeCoroutineCancellationOperation.StopHandle,5001); var inner = f.Stop();
            var detach=f.Engine.Capture(f.Node()); Check(f.Requests.ObserveEngineDetach(detach)); f.End(detach); f.End(inner); f.End(outer);
            var candidates=f.Requests.ReadCompletionCandidates();
            Check(f.Requests.HasCompleteRequestCoverage(candidates), "Exact descendant Detach may satisfy the same frozen tracker in its physical Stop wrappers.");
            Check(!f.Requests.HasCompleteRequestCoverage(Array.Empty<NativeCoroutineCancellationLedger.CompletionCandidate>()));
        }
        using (var f = new Harness())
        {
            f.Begin(); f.Provider.Add(slot:1,generation:10,enumerator:1002);
            var request=f.Stop(NativeCoroutineCancellationOperation.StopAll); var detach=f.Engine.Capture(f.Node());
            Check(f.Requests.ObserveEngineDetach(detach));f.End(detach);f.End(request);
            Check(!f.Requests.HasCompleteRequestCoverage(f.Requests.ReadCompletionCandidates()),
                "One exact Detach cannot complete another matched tracker in StopAll.");
        }
    }
    private static void TerminalSubset()
    {
        using var f=new Harness();f.Begin();var request=f.Stop(NativeCoroutineCancellationOperation.StopNamed,7001);
        var call=f.Engine.Capture(f.Node());Check(f.Requests.ObserveEngineDetach(call));f.End(call);f.End(request);
        var candidates=f.Requests.ReadCompletionCandidates();
        Check(!f.Requests.HasCompleteRequestCoverage(candidates) && f.Requests.Read() is {Pending:true,EngineCancelled:false});
        using var other=new Harness();
        Check(!candidates.Single().TryReadTerminal(other.Requests,out _) && !other.Requests.HasCompleteRequestCoverage(candidates));
        var copied=f.Candidates.Single() with { CandidateId=999,NodeGeneration=999 };
        Check(candidates.Single().Identity.CandidateId!=copied.CandidateId && candidates.Single().Identity.NodeGeneration==1);
    }
    private static void TerminalGeneration()
    {
        using var f=new Harness();f.Begin();var request=f.Stop();var detach=f.Engine.Capture(f.Node());
        Check(f.Requests.ObserveEngineDetach(detach));f.End(detach);f.End(request);
        var release=f.Engine.Capture(f.Node(NativeEngineNodeKind.Release));f.End(release,raw:0);
        var candidate=f.Requests.ReadCompletionCandidates().Single();Check(candidate.TryReadTerminal(f.Requests,out var first));
        f.Provider.Rows[0]=f.Provider.Rows[0] with { Generation=10,Enumerator=1002,ObservedHandle=0,HandleConfirmed=false,StartReturned=false };
        var start=f.Engine.Capture(f.Start(enumerator:1002));var execute=f.Engine.Capture(f.Node(NativeEngineNodeKind.Execute,enumerator:1002,handle:402));
        f.End(execute);f.End(start,output:301);
        Check(candidate.TryReadTerminal(f.Requests,out var retained) && retained!.EngineGeneration==first!.EngineGeneration
            && f.Engine.Read().Identities.Last().EngineGeneration!=retained.EngineGeneration
            && f.Requests.Read() is {Pending:true,EngineCancelled:false}, "Pointer reuse cannot upgrade or relabel an old request's exact node generation.");
    }
    private static void TerminalLedgerOwner()
    {
        using var first=new Harness();using var second=new Harness();
        foreach(var f in new[]{first,second})
        {
            f.Begin();var request=f.Stop();var detach=f.Engine.Capture(f.Node());
            Check(f.Requests.ObserveEngineDetach(detach));f.End(detach);f.End(request);
            var release=f.Engine.Capture(f.Node(NativeEngineNodeKind.Release));f.End(release,raw:0);
        }
        var actual=first.Requests.ReadCompletionCandidates().Single();var other=second.Requests.ReadCompletionCandidates().Single();
        Check(actual.Identity.EngineIdentityId==other.Identity.EngineIdentityId && actual.Identity.Node==other.Identity.Node
            && actual.Identity.StartSequence==other.Identity.StartSequence && actual.Identity.NodeGeneration==other.Identity.NodeGeneration);
        Check(actual.IsForIdentityLedger(first.Engine) && !actual.IsForIdentityLedger(second.Engine)
            && other.IsForIdentityLedger(second.Engine) && !other.IsForIdentityLedger(first.Engine),
            "An independently complete same-valued physical ledger is still a different completion issuer.");
    }
    private static void Nested()
    {
        using var f=new Harness();f.Begin();var outer=f.Stop(NativeCoroutineCancellationOperation.StopHandle,5001);
        var inner=f.Stop(NativeCoroutineCancellationOperation.StopManagedHandle,5001);
        var call=f.Engine.Capture(f.Node());Check(f.Requests.ObserveEngineDetach(call));f.End(call);f.End(inner);
        call=f.Engine.Capture(f.Node());Check(f.Requests.ObserveEngineDetach(call));f.End(call);f.End(outer);
        var rows=f.Candidates;Check(rows.Count==2 && rows[0].RequestId==inner.RequestId && rows[0].ParentRequestId==outer.RequestId
            && rows[0].RootRequestId==outer.RequestId && rows[1].RequestId==outer.RequestId && rows[1].ParentRequestId==0
            && rows[0].NodeGeneration==rows[1].NodeGeneration && rows[0].DetachSequence!=rows[1].DetachSequence
            && f.Requests.Read() is { Unknown:false,ObservedRequests:2,NativeReturnedRequests:2,Outstanding:0 });
    }
    private static void ForeignMask()
    {
        using var f=new Harness();f.Begin();var outer=f.Stop();var reads=f.Provider.Reads;var currentReads=f.Provider.Currents;
        var foreign=f.Stop(receiver:999);var nestedForeign=f.Stop(receiver:998);
        Check(foreign.ForeignMask && nestedForeign.ForeignMask && f.Provider.Reads==reads && f.Provider.Currents==currentReads
            && f.Requests.Read() is { ObservedRequests:1,PhysicalCalls:3,ForeignMasks:2,Outstanding:3 });
        f.End(nestedForeign);
        var inner=f.Stop();var call=f.Engine.Capture(f.Node());Check(f.Requests.ObserveEngineDetach(call));f.End(call);f.End(inner);
        Check(f.Candidates.Single().ParentRequestId==outer.RequestId && f.Candidates.Single().RootRequestId==outer.RequestId);
        f.End(foreign);call=f.Engine.Capture(f.Node());Check(f.Requests.ObserveEngineDetach(call));f.End(call);f.End(outer);
        Check(f.Requests.Read() is { Unknown:false,ObservedRequests:2,NativeReturnedRequests:2,Outstanding:0,ForeignMasks:2 });
        var standalone=new NativeOperationFrame {Version=1,Size=64,Kind=NativeOperationKind.Void0,Self=new(999),Sequence=999};
        Check(f.Requests.Capture(NativeCoroutineCancellationOperation.StopAll,standalone) is null);
    }
    private static void MaskedDetach()
    {
        using var f=new Harness();f.Begin();var outer=f.Stop();var mask=f.Stop(receiver:999);var reads=f.Provider.Reads;
        var call=f.Engine.Capture(f.Node());Check(!f.Requests.ObserveEngineDetach(call) && f.Requests.Unknown && f.Candidates.Count==0
            && f.Provider.Reads==reads && !call.Completed && !call.Disposed);
        f.End(call);f.End(mask);f.End(outer);
        Check(f.Requests.Read() is { Unknown:true,Pending:true,EngineCancelled:false,Outstanding:0,ObservedRequests:1 }
            && f.Engine.Read() is { Unknown:false,Outstanding:0 });
    }
    private static void ChangedIdentity()
    {
        foreach(var mutation in new[] { "receiver", "owner", "enum", "slot", "generation", "handle" })
        {
            using var f=new Harness();f.Begin();var request=f.Stop();var call=f.Engine.Capture(f.Node());var old=call.Identity!.Tracker;
            call.Identity.Tracker=mutation switch {
                "receiver"=>old with {Receiver=202}, "owner"=>old with {Owner=new object()}, "enum"=>old with {Enumerator=1002},
                "slot"=>old with {Slot=1}, "generation"=>old with {Generation=10}, _=>old with {ObservedHandle=5002} };
            Check(!f.Requests.ObserveEngineDetach(call) && f.Requests.Unknown && f.Candidates.Count==0);
            call.Identity.Tracker=old;f.End(call);f.End(request);Check(f.Requests.Read().Outstanding==0);
        }
        using(var f=new Harness())
        {
            f.Begin();var request=f.Stop();var call=f.Engine.Capture(f.Node());f.Provider.Rows[0]=f.Provider.Rows[0] with {Generation=10};
            Check(!f.Requests.ObserveEngineDetach(call) && f.Requests.Unknown && f.Candidates.Count==0);f.End(call);f.End(request);
        }
    }
    private static void ChangedNodeAndArgument()
    {
        foreach(var mutation in new[] { "owner", "enum", "handle", "node" })
        {
            using var f=new Harness();f.Begin();var request=f.Stop();
            var frame=mutation switch { "owner"=>f.Node(owner:Harness.Owner+1), "enum"=>f.Node(enumerator:1002),
                "handle"=>f.Node(handle:402), _=>f.Node(node:302) };
            var call=f.Engine.Capture(frame);Check(!f.Requests.ObserveEngineDetach(call) && f.Requests.Unknown && f.Candidates.Count==0);
            f.End(call);f.End(request);Check(f.Requests.Read().Outstanding==0);
        }
        using(var f=new Harness())
        {
            f.Begin();var request=f.Stop(argument:1002);var call=f.Engine.Capture(f.Node());
            Check(!f.Requests.ObserveEngineDetach(call) && f.Requests.Unknown && f.Candidates.Count==0);f.End(call);f.End(request);
        }
    }
    private static void PhysicalScopes()
    {
        foreach(var mode in new[] { "closed", "completed", "forged", "execute", "not-top", "start-sequence" })
        {
            using var f=new Harness();f.Begin();var request=f.Stop();var call=f.Engine.Capture(f.Node());
            NativeEngineCoroutineIdentityLedger.Call? child=null;var observed=call;
            if(mode=="closed")f.End(call);
            if(mode=="completed")f.Engine.Complete(call,call.Frame,new() {Version=1,Size=32,Flags=NativeEngineNodeExitFlags.Returned});
            if(mode=="forged")observed=new(f.Engine,call.Frame,true) {Identity=call.Identity,NodeCall=call.NodeCall};
            if(mode=="execute") {f.End(call);observed=f.Engine.Capture(f.Node(NativeEngineNodeKind.Execute));}
            if(mode=="not-top")child=f.Engine.Capture(f.Start(owner:999,enumerator:999));
            NativeEngineCoroutineNodeLedger.Call? savedStart=null;
            if(mode=="start-sequence") { savedStart=call.NodeCall!.Node!.Start;
                call.NodeCall.Node=new(1,301,call.Frame,new(f.Nodes,f.Start())); }
            Check(!f.Requests.ObserveEngineDetach(observed) && f.Requests.Unknown && f.Candidates.Count==0);
            if(mode=="start-sequence") {call.NodeCall!.Node=call.Identity!.Node;Check(savedStart is not null);}
            if(child is not null)f.End(child);
            if(mode=="completed")call.Dispose();else if(mode!="closed")f.End(mode=="execute"?observed:call);
            f.End(request);Check(f.Requests.Read().Outstanding==0);
        }
        using(var f=new Harness()) {f.Begin();var call=f.Engine.Capture(f.Node());Check(!f.Requests.ObserveEngineDetach(call)
            && !f.Requests.Unknown && f.Candidates.Count==0);f.End(call);}
    }
    private static void Worker()
    {
        using var f=new Harness();f.Begin();var request=f.Stop();var call=f.Engine.Capture(f.Node());var reads=f.Provider.Reads;var currents=f.Provider.Currents;
        Task.Run(()=>Check(!f.Requests.ObserveEngineDetach(call))).GetAwaiter().GetResult();
        Check(f.Requests.Unknown && f.Provider.Reads==reads && f.Provider.Currents==currents && f.Candidates.Count==0
            && !call.Completed && !call.Disposed);f.End(call);f.End(request);Check(f.Requests.Read().Outstanding==0);
    }
    private static void ProviderDrift()
    {
        foreach(var mode in new[] { "tracker", "current", "nested", "close-engine" })
        {
            using var f=new Harness();f.Begin();var request=f.Stop();var call=f.Engine.Capture(f.Node());var old=call.Identity!.Tracker;
            f.Provider.DuringRead=()=> {
                f.Provider.DuringRead=null;
                if(mode=="tracker")call.Identity.Tracker=old with {NaturalTerminal=true};
                if(mode=="current")f.Provider.Rows[0]=old with {Generation=10};
                if(mode=="nested") {var child=f.Stop();f.End(child);}
                if(mode=="close-engine")f.End(call);
            };
            var added=f.Requests.ObserveEngineDetach(call);
            // A nested Stop attempts a provider read while association is busy,
            // so its own sticky fault also prevents publishing the outer tuple.
            Check(!added && f.Requests.Unknown && f.Candidates.Count==0);
            call.Identity!.Tracker=old;if(mode!="close-engine")f.End(call);f.End(request);
            Check(f.Requests.Read().Outstanding==0);
        }
    }
    private static void Named()
    {
        using var f=new Harness();f.Begin();var request=f.Stop(NativeCoroutineCancellationOperation.StopNamed,7001);
        var call=f.Engine.Capture(f.Node());Check(f.Requests.ObserveEngineDetach(call));f.End(call);f.End(request);
        var state=f.Requests.Read();Check(f.Candidates.Single().NamedMatchUnproven && state.Requests.Single() is {MatchUnproven:true,Matched.Count:0}
            && state is {Unknown:false,Pending:true,EngineCancelled:false});
    }
    private static void MaskCompletion()
    {
        using(var f=new Harness())
        {
            f.Begin();var outer=f.Stop();var mask=f.Stop(receiver:999);f.End(outer);
            Check(f.Requests.Read().Outstanding==2 && !outer.Disposed && !outer.NativeCompleted && f.Requests.Unknown);
            f.End(mask);f.End(outer);Check(f.Requests.Read() is {Outstanding:0,ObservedRequests:1,NativeReturnedRequests:1});
        }
        using(var f=new Harness())
        {
            f.Begin();var outer=f.Stop();var mask=f.Stop(receiver:999);f.End(mask,NativeOperationExitFlags.Abnormal);f.End(outer);
            Check(f.Requests.Read() is {Unknown:true,Outstanding:0,ObservedRequests:1,AbnormalRequests:0,NativeReturnedRequests:1});
        }
    }
    private static void Bounds()
    {
        using(var f=new Harness())
        {
            f.Begin();var request=f.Stop();var call=f.Engine.Capture(f.Node());Check(f.Requests.ObserveEngineDetach(call));
            Check(!f.Requests.ObserveEngineDetach(call) && f.Requests.Unknown && f.Candidates.Count==1);f.End(call);f.End(request);
        }
        using(var f=new Harness())
        {
            f.Begin();var request=f.Stop();var reads=f.Provider.Reads;
            for(var index=1;index<NativeCoroutineCancellationLedger.MaximumPhysicalCalls;++index)f.End(f.Stop(receiver:999));
            var overflow=new NativeOperationFrame {Version=1,Size=64,Kind=NativeOperationKind.Void0,Self=new(999),Sequence=9999};
            Check(f.Requests.Capture(NativeCoroutineCancellationOperation.StopAll,overflow) is null && f.Requests.Unknown);
            f.End(request);Check(f.Requests.Read() is {ObservedRequests:1,Outstanding:1,UntrackedRequests:1}
                && f.Provider.Reads==reads && f.Candidates.Count==0);
        }
    }
}
