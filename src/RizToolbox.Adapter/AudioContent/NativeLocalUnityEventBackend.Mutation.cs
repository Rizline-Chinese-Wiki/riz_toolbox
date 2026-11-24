using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalUnityEventBackend
{
    private readonly List<ExactHookBroker.Binding> _mutationBindings = new();
    private readonly Dictionary<UnityEventOperation, VerifiedHookTarget> _mutationTargets = new();
    private IntPtr _reflectionMethodClass;
    private void InitializeMutationObservations(NativeLocalUnityEventLedger owner)
    {
        _reflectionMethodClass = Native(() => _api.Class("mscorlib.dll", "System.Reflection", "MethodInfo"));
        // Resolve the entire finite ABI set before publishing its first entry.
        foreach (var row in UnityEventOperationProfile.All)
            _mutationTargets.Add(row.Operation, Native(() => _api.ResolveUnityEventOperation(_module, row.Operation)));
        foreach (var row in UnityEventOperationProfile.All)
        {
            RequireObservationKnown();
            var operation = row.Operation;
            var binding = _broker.BindNativeOperationObservation(_mutationTargets[operation], row.Kind,
                frame => owner.CaptureMutation(operation, frame), owner.CompleteMutation,
                (frame, failure) => owner.ObserveMutationFault(operation, frame, failure));
            _mutationBindings.Add(binding);
            RequireObservationKnown();
        }
    }
    public NativeUnityEventGraph CaptureMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame)
    {
        Check();
        try
        {
            var row = UnityEventOperationProfile.Get(operation);
            if (!frame.IsValid || frame.Kind != row.Kind || !_mutationTargets.TryGetValue(operation, out var target)
                || frame.Method != IntPtr.Zero && frame.Method != target.MethodInfo)
                throw new InvalidOperationException("Mutation arguments have no verified operation declaration.");
            // All actual published object references are saved before Target,
            // metadata or graph reads. A partial allocation failure retains roots.
            var self = AdoptDispatchReference(frame.Self);
            var arg0 = frame.Arg0 == IntPtr.Zero ? 0 : AdoptDispatchReference(frame.Arg0);
            var arg1 = frame.Arg1 == IntPtr.Zero ? 0 : AdoptDispatchReference(frame.Arg1);
            RequireObservationKnown();
            Native(() => _api.RequireAssignable(target.DeclaringClass, ObjectClass(Pointer(self))));
            if (arg0 != 0) Native(() => _api.RequireAssignable(MutationArgumentClass(operation, 0), ObjectClass(Pointer(arg0))));
            if (arg1 != 0) Native(() => _api.RequireAssignable(MutationArgumentClass(operation, 1), ObjectClass(Pointer(arg1))));
            var graph = ReadGraph(eventSlot);
            if (row.Receiver == UnityEventOperationReceiver.Event && graph.Event != self
                || row.Receiver == UnityEventOperationReceiver.Calls && graph.Calls != self
                || row.Receiver == UnityEventOperationReceiver.PersistentGroup && (graph.PersistentGroup != self || graph.Calls != arg0 || graph.Event != arg1))
                throw new InvalidOperationException("Mutation receiver or persistent-call arguments differ from the registered event graph.");
            if (row.Receiver == UnityEventOperationReceiver.Invokable && ObjectClass(Pointer(self)) != _invokableClass)
                throw new UnsupportedSignatureException("A delegate writer must target the exact non-generic InvokableCall class.");
            RequireObservationKnown(); return graph;
        }
        catch { CleanupUnknown = true; throw; }
    }
    public NativeUnityEventDelegateMutationCapture CaptureDelegateMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame)
    {
        Check();
        try
        {
            if (UnityEventOperationProfile.Get(operation).Receiver != UnityEventOperationReceiver.Invokable)
                throw new UnsupportedSignatureException("This mutation has no invokable delegate field contract.");
            // CaptureMutation adopts actual self/action references before the
            // first metadata or graph access, including nullable UnityAction.
            var graph = CaptureMutation(eventSlot, operation, frame);
            return CaptureActualDelegateChain(graph, frame);
        }
        catch { CleanupUnknown = true; throw; }
    }
    public NativeUnityEventDelegateMutationCapture ReadDelegateMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame)
    {
        Check();
        try
        {
            if (UnityEventOperationProfile.Get(operation).Receiver != UnityEventOperationReceiver.Invokable
                || !_mutationTargets.TryGetValue(operation, out var target) || !frame.IsValid || frame.Kind != NativeOperationKind.Reference1
                || frame.Method != IntPtr.Zero && frame.Method != target.MethodInfo || !_rootSlots.ContainsKey(frame.Self.ToInt64())
                || frame.Arg0 != IntPtr.Zero && !_rootSlots.ContainsKey(frame.Arg0.ToInt64()))
                throw new InvalidOperationException("The final delegate read lost its exact retained operation inputs.");
            // The invokable may have been removed from every current list in
            // a nested mutation. Its pre-retained identity/root still belongs
            // to the event history and must be read directly after the call.
            var graph = ReadGraph(eventSlot);
            return CaptureActualDelegateChain(graph, frame);
        }
        catch { CleanupUnknown = true; throw; }
    }
    private NativeUnityEventDelegateMutationCapture CaptureActualDelegateChain(NativeUnityEventGraph graph, NativeOperationFrame frame)
    {
        var identity = frame.Self.ToInt64(); var pointer = Pointer(identity);
        if (ObjectClass(pointer) != _invokableClass)
            throw new UnsupportedSignatureException("The retained delegate writer receiver is not the exact InvokableCall.");
        var leaves = 0; var actual = ReadInvokable(identity, _invokableClass, ref leaves);
        var verifyLeaves = 0; var verified = ReadInvokable(identity, _invokableClass, ref verifyLeaves);
        if (!SameDelegateSnapshot(actual, verified))
            throw new InvalidOperationException("The actual invokable delegate changed between the paired final field reads.");
        foreach (var list in new[] { graph.PersistentCalls, graph.RuntimeCalls, graph.ExecutingCalls })
            foreach (var row in list.Rows.Where(row => row.Object == identity))
                if (!SameDelegateSnapshot(actual, row))
                    throw new InvalidOperationException("The actual final delegate differs from its simultaneously captured event list.");
        RequireObservationKnown(); return new(graph, actual);
    }
    private static bool SameDelegateSnapshot(NativeUnityEventInvokable first, NativeUnityEventInvokable second)
        => first.Object == second.Object && first.Class == second.Class && first.Opaque == second.Opaque
            && first.Delegate == second.Delegate && first.InvocationList == second.InvocationList
            && first.CachedBoolean == second.CachedBoolean && first.Leaves.SequenceEqual(second.Leaves);
    private IntPtr MutationArgumentClass(UnityEventOperation operation, int index) => operation switch {
        UnityEventOperation.AddListener or UnityEventOperation.AddDelegate or UnityEventOperation.RemoveDelegate when index == 0 => _actionClass,
        UnityEventOperation.AddCall or UnityEventOperation.AddRuntimeCall or UnityEventOperation.AddPersistentCall when index == 0 => _baseCallClass,
        UnityEventOperation.RemoveListener or UnityEventOperation.RemoveCall => index == 0 ? _objectClass : _reflectionMethodClass,
        UnityEventOperation.InitializePersistentCalls => index == 0 ? _callsClass : _eventBase,
        _ => throw new InvalidOperationException("Mutation has no argument at this ABI position.") };
}
