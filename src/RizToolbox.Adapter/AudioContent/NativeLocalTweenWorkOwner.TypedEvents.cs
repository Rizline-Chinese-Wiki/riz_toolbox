using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenSingleEventSnapshot(long Tween, long Event, long OperationSequence,
    NativeTweenOperation Operation, uint ValueBits, long Caller, long Method, ulong NativeSequence,
    bool NativeReturned, bool Abnormal, bool Disposed, bool Production, string? Failure = null);

internal interface INativeTweenTypedEventSource
{
    void BorrowTypedTweenEvents(NativeLocalTweenWorkOwner owner,
        Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt);
    void BindTypedTweenEvents(NativeLocalTweenWorkOwner owner, int processCapacity,
        Func<NativeOperationFrame, IDisposable?> capture,
        Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> complete,
        Action<NativeOperationFrame, string> fault);
}

internal interface INativeTweenDynamicTypedEventSource
{
    void BorrowDynamicTypedTweenEvents(NativeLocalTweenWorkOwner owner, long tween,
        Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt);
}

internal interface INativeTweenTypedEventGraphSource
{
    NativeSingleEventGraph ReadTypedEventGraph(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle eventRoot);
    long ReadTypedEventWriterGeneration(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle eventRoot);
    ExactHookBroker.ManagedBoundaryActivity ReadTypedEventActivity();
}

internal sealed partial class NativeLocalTweenWorkOwner
{
    private sealed class TypedEventInvocation : IDisposable
    {
        private readonly NativeLocalTweenWorkOwner _owner;
        internal readonly object Issuer;
        internal readonly TypedEventMembership Membership;
        internal readonly OperationContext Parent;
        internal readonly TypedEventInvocation? Previous;
        internal readonly NativeOperationFrame Frame;
        internal readonly NativeTweenSingleEventInvocation Observation;
        internal readonly NativeLocalTweenSingleEventTracker Executing;
        internal bool Returned, Abnormal, Disposed, ForeignChildInvoked;
        internal string? Failure;
        internal NativeSingleEventGraph? PreGraph, MidGraph, PostGraph, DisposeGraph;
        internal long PreWriterGeneration, MidWriterGeneration, PostWriterGeneration, DisposeWriterGeneration;
        internal EmptySingleListenerEffectsReceipt? EmptyReceipt;
        internal TypedEventInvocation(object issuer, NativeLocalTweenWorkOwner owner, TypedEventMembership membership,
            OperationContext parent, NativeOperationFrame frame)
        {
            (Issuer, _owner, Membership, Parent, Previous, Frame, Observation) = (issuer, owner, membership, parent,
                owner._typedEventTop, frame, new(frame.Self, frame.ScalarBits, frame.Method, frame.Caller, frame.Sequence));
            Executing = new(frame);
        }
        public void Dispose() => _owner.DisposeTyped(this);
        internal NativeTweenSingleEventSnapshot Snapshot() => new(Membership.Tween, Membership.Event,
            checked((long)Parent.Frame.Sequence), Parent.Operation, Observation.ValueBits, Observation.Caller.ToInt64(),
            Observation.Method.ToInt64(), Observation.Sequence, Returned, Abnormal, Disposed,
            Membership.Production, Failure);
    }

    internal sealed class TypedEventMembership
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly INativeTweenTypedEventSource _source;
        private readonly LocalRunLaunchCommitState? _launch;
        internal long Tween { get; }
        internal long Event { get; }
        internal NativeExactReadField Field { get; }
        internal NativeRuntime.RootHandle TweenRoot { get; }
        internal NativeRuntime.RootHandle EventRoot { get; }
        internal bool Production => _launch is not null;
        internal TypedEventMembership(object issuer, NativeLocalTweenWorkOwner owner, INativeTweenTypedEventSource source,
            NativeRuntime.RootHandle tweenRoot, NativeExactReadField field, NativeRuntime.RootHandle eventRoot)
        {
            if (!ReferenceEquals(issuer, owner._typedEventIssuer) || !owner._borrowingTypedEvents
                || !ReferenceEquals(owner._backend, source)
                || !owner._sources.Contains(tweenRoot.Target.ToInt64()))
                throw new InvalidOperationException("Only this backend's active typed-event borrowing can issue membership.");
            (_issuer, _owner, _source, _launch, TweenRoot, EventRoot, Field) =
                (issuer, owner, source, owner._launch, tweenRoot, eventRoot, field);
            Tween = tweenRoot.Target.ToInt64(); Event = eventRoot.Target.ToInt64();
        }
        internal void RequireFor(NativeLocalTweenWorkOwner owner, INativeTweenTypedEventSource source)
        {
            owner._main.VerifyAccess(); owner.Known();
            if (!ReferenceEquals(_issuer, owner._typedEventIssuer) || !ReferenceEquals(_owner, owner)
                || !ReferenceEquals(_source, source) || !ReferenceEquals(_launch, owner._launch)
                || !ReferenceEquals(owner._backend, source) || !owner._typedEvents.Contains(this)
                || TweenRoot.Target.ToInt64() != Tween || EventRoot.Target.ToInt64() != Event
                || !owner._sources.Contains(Tween) || Production != (owner._launch is not null))
                throw new InvalidOperationException("Typed Tween event membership crossed its owner, launch, source or event root.");
        }
        internal bool IsFor(NativeLocalTweenWorkOwner owner, INativeTweenTypedEventSource source)
            => ReferenceEquals(_owner, owner) && ReferenceEquals(_source, source);
    }

    private readonly object _typedEventIssuer = new();
    private readonly List<TypedEventMembership> _typedEvents = new();
    private readonly List<TypedEventInvocation> _typedInvocations = new();
    private readonly HashSet<long> _typedEventSources = new();
    private long[] _typedEventIdentities = Array.Empty<long>();
    private TypedEventInvocation? _typedEventTop;
    private bool _borrowingTypedEvents, _typedEventsAdopted, _typedEventBinding;

    internal IReadOnlyList<long> RequireActualTypedEventBorrowing(INativeTweenTypedEventSource source)
    {
        VerifyTweenSourceOwner();
        if (!ReferenceEquals(_backend, source) || !_initializing || _ready
            || !_borrowingTypedEvents || _top is not null || _typedEventSources.Count != 0)
            throw new InvalidOperationException("Typed Tween events require the exact idle preactivation production backend.");
        return Array.AsReadOnly(_sources.ToArray());
    }

    internal void AdoptTypedEvents(INativeTweenTypedEventSource source)
    {
        VerifyTweenSourceOwner();
        if (!ReferenceEquals(_backend, source) || !_initializing
            || _ready || _typedEventsAdopted || _typedEventBinding)
            throw new InvalidOperationException("Typed Tween events install once through this exact preactivation backend.");
        _borrowingTypedEvents = true;
        try
        {
            source.BorrowTypedTweenEvents(this, (tweenRoot, field, eventRoot)
                => AdoptTypedEvent(source, tweenRoot, field, eventRoot));
            if (!_typedEventSources.SetEquals(_sources))
                throw new InvalidOperationException("The complete Tween source set has not had its nullable onTween field checked.");
            _borrowingTypedEvents = false;
            source.BindTypedTweenEvents(this, _sources.Length,
                frame => CaptureTyped(frame), (context, frame, flags) => CompleteTyped(context, frame, flags),
                (frame, failure) => ObserveTypedFault(frame, failure));
            _typedEventBinding = true; _typedEventsAdopted = true;
            RequireTypedEventBinding(source);
        }
        catch (Exception error) { Fault("Typed Tween events: " + error.Message); throw; }
        finally { _borrowingTypedEvents = false; }
    }

    internal void RequireTypedEventBinding(INativeTweenTypedEventSource source)
    {
        VerifyTweenSourceOwner();
        if (!ReferenceEquals(_backend, source) || !_initializing || _ready || !_typedEventsAdopted || !_typedEventBinding
            || _top is not null || !_typedEventSources.SetEquals(_sources))
            throw new InvalidOperationException("Typed Tween event binding is not the exact preactivation process owner.");
    }

    internal IReadOnlyList<TypedEventMembership> RequireTypedEventMemberships(INativeTweenTypedEventSource source, int? processCapacity = null)
    {
        VerifyTweenSourceOwner();
        if (!ReferenceEquals(_backend, source) || !_initializing || _ready || _borrowingTypedEvents
            || !_typedEventSources.SetEquals(_sources) || _typedEventBinding
            || processCapacity is { } capacity && capacity != _sources.Length)
            throw new InvalidOperationException("Typed Tween event membership is not the exact unbound preactivation source set.");
        foreach (var membership in _typedEvents) membership.RequireFor(this, source);
        return Array.AsReadOnly(_typedEvents.ToArray());
    }

    private void AdoptTypedEvent(INativeTweenTypedEventSource source, NativeRuntime.RootHandle tweenRoot,
        NativeExactReadField field, NativeRuntime.RootHandle? eventRoot)
    {
        Known(); ArgumentNullException.ThrowIfNull(tweenRoot); ArgumentNullException.ThrowIfNull(field);
        var tween = tweenRoot.Target.ToInt64();
        if (!_borrowingTypedEvents || !_sources.Contains(tween) || !_typedEventSources.Add(tween)
            || field.Name != "onTween" || field.TypeName != "UnityEngine.Events.UnityEvent<System.Single>" || field.Offset != 0x98
            || field.IsStatic || field.FieldClass == IntPtr.Zero || (field.Flags & (0x10u | 0x40u)) != 0)
            throw new InvalidOperationException("A typed event repeated a source or changed its exact nullable onTween field.");
        if (eventRoot is null) return;
        if (eventRoot.Target == IntPtr.Zero)
            throw new InvalidOperationException("A nonnull onTween membership has no retained event identity.");
        var membership = new TypedEventMembership(_typedEventIssuer, this, source, tweenRoot, field, eventRoot);
        _typedEvents.Add(membership);
        Volatile.Write(ref _typedEventIdentities, _typedEvents.Select(value => value.Event).Distinct().ToArray());
    }

    internal void AdoptDynamicTypedEvent(long tween, NativeRuntime.RootHandle tweenRoot)
    {
        _main.VerifyAccess(); Known(); ArgumentNullException.ThrowIfNull(tweenRoot);
        if (!_ready || !_promotingDynamicSource || !_sources.Contains(tween) || tweenRoot.Target.ToInt64() != tween
            || _sealRequested || _admissionSealed || !_typedEventsAdopted || !_typedEventBinding
            || _backend is not INativeTweenTypedEventSource source
            || _backend is not INativeTweenDynamicTypedEventSource dynamicSource
            || _top is { Completed: true } or { Disposed: true })
            throw new InvalidOperationException("Dynamic typed membership requires this exact retained source promotion boundary.");
        _borrowingTypedEvents = true;
        try
        {
            dynamicSource.BorrowDynamicTypedTweenEvents(this, tween, (root, field, eventRoot) => {
                if (!ReferenceEquals(root, tweenRoot))
                    throw new InvalidOperationException("Dynamic onTween borrowing changed its already retained source root.");
                AdoptTypedEvent(source, root, field, eventRoot);
            });
            if (!_typedEventSources.Contains(tween))
                throw new InvalidOperationException("Dynamic source omitted its nullable onTween field.");
        }
        catch (Exception error) { Fault("Dynamic typed Tween event: " + error.Message); throw; }
        finally { _borrowingTypedEvents = false; }
    }

    internal void AdoptControlledDynamicTypedEventForTests(long tween, NativeRuntime.RootHandle tweenRoot)
    {
        VerifyTweenSourceOwner(); ArgumentNullException.ThrowIfNull(tweenRoot);
        if (_backend is NativeLocalTweenBackend || _launch is not null || _listeners is not null
            || !_ready || !_typedEventsAdopted || !_typedEventBinding || _promotingDynamicSource
            || _sources.Contains(tween) || _sources.Length >= MaximumSources || tweenRoot.Target.ToInt64() != tween
            || _top is not { Captured: true, Completed: false, Disposed: false } parent
            || parent.Operation is not (NativeTweenOperation.Play or NativeTweenOperation.ExecuteTween)
            || !_sources.Contains((parent.Operation == NativeTweenOperation.Play ? parent.Frame.Self : parent.Frame.Arg0).ToInt64()))
            throw new InvalidOperationException("Controlled dynamic typed adoption requires an actual active owned nonproduction parent frame.");
        Volatile.Write(ref _sources, _sources.Append(tween).ToArray());
        _promotingDynamicSource = true;
        try { AdoptDynamicTypedEvent(tween, tweenRoot); }
        finally { _promotingDynamicSource = false; }
    }

    internal TypedEventMembership? MatchTypedEvent(INativeTweenTypedEventSource source, NativeOperationFrame frame,
        out NativeTweenOperation parentOperation)
    {
        parentOperation = default;
        // The hook observes a globally shared generic method. Foreign events do
        // not belong to this owner, including calls made without a Tween parent.
        if (!HasTypedEventIdentity(frame.Self)) return null;
        try
        {
            _main.VerifyAccess(); Known();
            if (!ReferenceEquals(_backend, source) || !_ready || !_typedEventsAdopted || !_typedEventBinding
                || !frame.IsValid || frame.Kind != NativeOperationKind.Single1 || frame.Method == IntPtr.Zero
                || _top is not { } parent || !parent.Captured || parent.Completed || parent.Disposed
                || parent.Operation is not (NativeTweenOperation.Play or NativeTweenOperation.ExecuteTween))
                throw new InvalidOperationException("Owned typed Tween event has no valid active Play/Execute parent or native frame.");
            var tween = parent.Operation == NativeTweenOperation.Play ? parent.Frame.Self : parent.Frame.Arg0;
            var membership = _typedEvents.SingleOrDefault(value => value.Event == frame.Self.ToInt64() && value.Tween == tween.ToInt64())
                ?? throw new InvalidOperationException("Owned typed Tween event crossed its actual parent receiver.");
            membership.RequireFor(this, source); parentOperation = parent.Operation; return membership;
        }
        catch (Exception error) { Fault("Typed Tween event entry: " + error.Message); throw; }
    }

    // A scalar membership prefilter permits snapshot enrollment before the first root/metadata read.
    internal bool HasTypedEventIdentity(IntPtr identity) => Volatile.Read(ref _typedEventIdentities).Contains(identity.ToInt64());

    internal void ObserveForeignTypedChildInvocation()
    {
        _main.VerifyAccess();
        for (var current = _typedEventTop; current is not null; current = current.Previous)
            current.ForeignChildInvoked = true;
    }

    internal IDisposable? CaptureTyped(NativeOperationFrame frame)
    {
        if (_backend is not INativeTweenTypedEventSource source) return null;
        var membership = MatchTypedEvent(source, frame, out _);
        if (membership is null)
        {
            ObserveForeignTypedChildInvocation();
            return null;
        }
        try
        {
            if (_typedInvocations.Count >= MaximumOperations)
                throw new InvalidOperationException("Typed Tween event retained invocation capacity exhausted.");
            var invocation = new TypedEventInvocation(_typedEmptyReceiptIssuer, this, membership, _top!, frame);
            _typedInvocations.Add(invocation); _typedEventTop = invocation;
            if (_backend is INativeTweenTypedEventGraphSource graphSource)
            {
                invocation.PreGraph = graphSource.ReadTypedEventGraph(this, membership.EventRoot);
                invocation.PreWriterGeneration = graphSource.ReadTypedEventWriterGeneration(this, membership.EventRoot);
                if (invocation.PreGraph.Event != membership.Event)
                    throw new InvalidOperationException("Typed Single pre event graph identity differs from its membership.");
            }
            CaptureTypedEffect(invocation); Known(); return invocation;
        }
        catch (Exception error) { Fault("Typed Tween event capture: " + error.Message); throw; }
    }

    internal void CompleteTyped(IDisposable context, NativeOperationFrame frame, NativeOperationExitFlags flags)
    {
        var invocation = context as TypedEventInvocation;
        try
        {
            _main.VerifyAccess();
            if (invocation is null || !_typedInvocations.Contains(invocation)
                || !invocation.Membership.IsFor(this, (INativeTweenTypedEventSource)_backend!)
                || invocation.Disposed || invocation.Returned || invocation.Abnormal
                || !ReferenceEquals(_typedEventTop, invocation) || !ReferenceEquals(_top, invocation.Parent)
                || invocation.Parent.Completed || invocation.Parent.Disposed || !SameFrame(invocation.Frame, frame))
                throw new InvalidOperationException("Typed Tween event completion differs from its exact native entry.");
            if (flags is not (NativeOperationExitFlags.Returned or NativeOperationExitFlags.Abnormal))
                throw new InvalidOperationException("Typed Tween event completion has no unique native outcome.");
            invocation.Returned = flags == NativeOperationExitFlags.Returned;
            invocation.Abnormal = flags == NativeOperationExitFlags.Abnormal;
            invocation.Executing.ParentComplete(invocation.Returned);
            if (invocation.Returned && _backend is INativeTweenTypedEventGraphSource graphSource)
            {
                invocation.PostGraph = graphSource.ReadTypedEventGraph(this, invocation.Membership.EventRoot);
                invocation.PostWriterGeneration = graphSource.ReadTypedEventWriterGeneration(this, invocation.Membership.EventRoot);
                if (invocation.PostGraph.Event != invocation.Membership.Event)
                    throw new InvalidOperationException("Typed Single post event graph identity differs from its membership.");
            }
            if (invocation.Abnormal)
            {
                invocation.Failure = "The actual onTween original exited abnormally; listener effects remain unconfirmed.";
                Fault("Typed Tween event: " + invocation.Failure);
            }
        }
        catch (Exception error)
        {
            if (invocation is not null && _typedInvocations.Contains(invocation)) invocation.Failure ??= error.Message;
            Fault("Typed Tween event completion: " + error.Message); throw;
        }
    }

    private void DisposeTyped(TypedEventInvocation invocation)
    {
        try
        {
            _main.VerifyAccess();
            if (!_typedInvocations.Contains(invocation) || !ReferenceEquals(_typedEventTop, invocation)
                || !ReferenceEquals(_top, invocation.Parent) || invocation.Parent.Completed || invocation.Parent.Disposed
                || invocation.Disposed || invocation.Returned == invocation.Abnormal)
                throw new InvalidOperationException("Typed Tween event disposal has no unique completed outcome and exact active physical parent.");
            invocation.Disposed = true; _typedEventTop = invocation.Previous;
            invocation.Executing.ParentDispose();
            if (!invocation.Abnormal && _backend is INativeTweenTypedEventGraphSource graphSource)
            {
                invocation.DisposeGraph = graphSource.ReadTypedEventGraph(this, invocation.Membership.EventRoot);
                invocation.DisposeWriterGeneration = graphSource.ReadTypedEventWriterGeneration(this, invocation.Membership.EventRoot);
                if (invocation.DisposeGraph.Event != invocation.Membership.Event)
                    throw new InvalidOperationException("Typed Single dispose event graph identity differs from its membership.");
            }
        }
        catch (Exception error)
        { invocation.Failure ??= error.Message; Fault("Typed Tween event disposal: " + error.Message); throw; }
    }

    internal void ObserveTypedFault(NativeOperationFrame frame, string failure)
    {
        if (Volatile.Read(ref _typedEventIdentities).Contains(frame.Self.ToInt64())) Fault("Typed Tween event: " + failure);
    }

    internal IReadOnlyList<NativeTweenSingleEventSnapshot> ReadTypedEvents()
    {
        _main.VerifyAccess();
        return Array.AsReadOnly(_typedInvocations.Select(value => value.Snapshot()).ToArray());
    }
}
