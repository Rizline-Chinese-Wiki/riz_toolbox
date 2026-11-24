using System.Runtime.ExceptionServices;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal enum SeekLineEndpointDirection { Apply, Restore }
internal enum SeekLineEndpointCommandKind { Parent, Local }
internal sealed record SeekLineEndpointParentEdge(long Transform, long Parent);

internal sealed record SeekLineEndpointCommandEffect(int Sequence, SeekLineEndpointDirection Direction,
    long Transform, long Parent, NativeVector3 Before, NativeVector3 Target,
    bool NativeEntered, bool NativeReturned, NativeVector3? Readback,
    bool TargetObserved, bool OriginalObserved, bool ExternalEffectsUnclosed,
    bool SideEffectsUnknown, string? Failure)
{
    // Before is the last admitted observation, not an atomic compare/exchange
    // pre-state. Native getters/profile checks can themselves call back into
    // the engine; no finite polling sequence closes that engine boundary.
    internal bool BeforeIsAtomicPreState => false;
    internal SeekLineEndpointCommandKind Kind { get; init; } = SeekLineEndpointCommandKind.Local;
    internal long BeforeParent { get; init; }
    internal long TargetParent { get; init; }
    internal long? ReadbackParent { get; init; }
}

internal sealed record SeekLineEndpointJournal(bool Prepared, bool ExecutionEntered, bool ExecutionFinished,
    bool AppliedValuesVerified, bool RestorationEntered, bool RestorationFinished,
    bool RestoredValuesVerified, bool Disposed, int AttemptedWrites, int AttemptedRestores,
    bool UnrecognizedState, bool SideEffectsUnknown, bool ExternalEffectsUnclosed,
    bool HasRetainedResponsibility, string? Failure, string? CleanupFailure,
    IReadOnlyList<SeekLineEndpointLocalWrite> Targets,
    IReadOnlyList<SeekLineEndpointLocalWrite> UniqueTargets,
    IReadOnlyList<SeekLineEndpointObserved> Baseline,
    IReadOnlyList<SeekLineEndpointObserved> Expected,
    IReadOnlyList<SeekLineEndpointCommandEffect> Commands,
    NativeSeekLineRingSourceCapture? OriginalSource, object? EffectsOwner,
    object? SourceCleanupOwner)
{
    internal bool CompleteSeekPermitted => false;
    internal bool AtomicConditionalWrites => false;
    internal int AttemptedParentWrites { get; init; }
    internal int AttemptedLocalWrites { get; init; }
    internal int AttemptedParentRestores { get; init; }
    internal int AttemptedLocalRestores { get; init; }
    internal IReadOnlyList<SeekLineEndpointParentEdge> BaselineParentGraph { get; init; } = Array.Empty<SeekLineEndpointParentEdge>();
    internal IReadOnlyList<SeekLineEndpointParentEdge> ExpectedParentGraph { get; init; } = Array.Empty<SeekLineEndpointParentEdge>();
    internal SeekLineBufferJournal? OriginalLineBuffer { get; init; }
    internal SeekLineRendererJournal? OriginalRenderer { get; init; }
}

/// <summary>An actual Transform parent/localPosition writer over the
/// original retained line reader. This owns its line capture once; it never
/// owns the caller's Canvas aggregate/capture or claims Unity effects closed.
/// Whole-vector/selected observations reject detected conflicts; they do not
/// make the actual Unity setter atomic. Every entered call retains unknown
/// engine effects even after successful readback and selected restoration.</summary>
internal sealed partial class NativeSeekLineEndpointCommands : IDisposable
{
    internal const int MaximumTargets = NativeSeekLineRingSourceCapture.MaximumLines * 2;
    internal const int MaximumUnclosedOwners = 256;
    private static readonly object OwnerGate = new();
    private static readonly HashSet<NativeSeekLineEndpointCommands> Owners = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly NativeSeekLineEndpointCommands _registered;
    private NativeSeekLineRingSourceCapture? _source;
    private SeekLineRingLocalTargetProjection? _projection;
    private SeekLineEndpointLocalWrite[] _targets = Array.Empty<SeekLineEndpointLocalWrite>();
    private SeekLineEndpointLocalWrite[] _unique = Array.Empty<SeekLineEndpointLocalWrite>();
    private SeekLineEndpointObserved[] _baseline = Array.Empty<SeekLineEndpointObserved>();
    private SeekLineEndpointObserved[] _expected = Array.Empty<SeekLineEndpointObserved>();
    private SeekLineEndpointAppliedWorldObservation? _lastIssuedWorldObservation;
    private readonly List<SeekLineEndpointCommandEffect> _commands = new();
    private readonly List<int> _entered = new();
    private Dictionary<long, long> _graphBaseline = new(), _graphExpected = new();
    private int[] _parentOrder = Array.Empty<int>();
    private int _pending = -1, _attemptedWrites, _attemptedRestores;
    private int _parentWrites, _localWrites, _parentRestores, _localRestores;
    private bool _prepared, _executionEntered, _executionFinished, _appliedVerified;
    private bool _restorationEntered, _restorationFinished, _restoredVerified;
    private bool _busy, _disposed, _released, _releaseAttempted, _ownerReserved, _adopted;
    private bool _unrecognized, _unknown, _externalEffects, _retained;
    private string? _failure;
    private Exception? _cleanupFailure;
    private object? _sourceCleanupOwner;

    private NativeSeekLineEndpointCommands() { _registered = this; }
    internal bool CompleteSeekPermitted => false;
    internal bool HasRetainedResponsibility { get { Diagnostic(); return _retained; } }
    internal NativeSeekLineRingSourceCapture OriginalSource
    { get { Diagnostic(); return _source ?? throw new InvalidOperationException("The original line source was not attached."); } }

    internal static NativeSeekLineEndpointCommands Prepare(Func<NativeSeekLineRingSourceCapture> acquire,
        ISeekCanvasPositionNativeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(acquire); ArgumentNullException.ThrowIfNull(profile);
        var owner = new NativeSeekLineEndpointCommands();
        lock (OwnerGate)
        {
            if (Owners.Count >= MaximumUnclosedOwners)
                throw new NotSupportedException("The bounded endpoint writer owner capacity is exhausted before source acquisition.");
            Owners.Add(owner); owner._ownerReserved = true;
        }
        try
        {
            owner._source = acquire() ?? throw new InvalidOperationException("An original retained line source is required.");
            owner._source.AdoptByEndpointWriter(owner); owner._adopted = true;
            owner._projection = owner._source.ReadLocalTargetProjection();
            owner.CopyTargets();
            owner._source.WithVerifiedEndpointOperation(owner, (_, access) =>
            {
                access.BindWriterProfile(profile);
                var first = owner.Read(access);
                var second = owner.Read(access);
                if (!Same(first, second)) throw new InvalidOperationException("Endpoint parents/local baselines changed during preparation.");
                owner._baseline = second; owner._expected = second.ToArray();
                var graph = owner.ReadGraph(access);
                owner.CheckEndpointGraph(second, graph);
                owner._parentOrder = owner.PlanParentOrder(graph);
                owner._graphBaseline = graph;
                owner._graphExpected = new(graph);
                return 0;
            });
            owner._prepared = true; return owner;
        }
        catch (Exception error)
        {
            owner._failure ??= error.Message; owner.AdoptCleanup(error);
            if (owner._source is null && error.Data["SeekLineRingSourceCleanupOwner"] is NativeSeekLineRingSourceCapture attached)
                owner._source = attached;
            if (owner._adopted)
                try { owner.Dispose(); } catch (Exception cleanup) { owner.Attach(error); error.Data["SeekLineEndpointConstructionCleanup"] = cleanup; }
            else if (owner._cleanupFailure is not null)
            {
                // A failed acquisition can still transfer an exact unknown
                // source cleanup owner through the exception. Retain this
                // reserved facade and the original responsibility, no retry.
                owner._disposed = true; owner._retained = true; owner.Attach(error);
            }
            else owner.ReleaseUnadopted();
            throw;
        }
    }

    private void CopyTargets()
    {
        var projection = _projection ?? throw new InvalidOperationException("The actual endpoint projection is absent.");
        var rows = projection.Endpoints.LocalTargets;
        if (rows.Count > MaximumTargets) throw new NotSupportedException("The complete endpoint target count exceeds its retained bound.");
        _targets = rows.Select(row => row with { }).ToArray();
        var byTransform = new Dictionary<long, SeekLineEndpointLocalWrite>();
        var unique = new List<SeekLineEndpointLocalWrite>();
        foreach (var row in _targets)
        {
            if (row.Transform == 0 || row.ParentTransform == 0
                || row.ParentTransform != projection.Endpoints.CanvasBasis.TransformAt(row.CanvasSlot)
                || !Finite(row.LocalPosition))
                throw new InvalidDataException("An endpoint target lacks exact parent/Transform/finite local bits.");
            if (byTransform.TryGetValue(row.Transform, out var prior))
            {
                if (prior.ParentTransform != row.ParentTransform || !Same(prior.LocalPosition, row.LocalPosition))
                    throw new InvalidDataException("Aliases of one endpoint Transform have conflicting parent/local targets.");
            }
            else { byTransform.Add(row.Transform, row); unique.Add(row); }
        }
        _unique = unique.ToArray();
        _commands.Capacity = checked(_unique.Length * 4);
        _entered.Capacity = checked(_unique.Length * 2);
    }

    private SeekLineEndpointObserved[] ReadOnce(IRetainedSeekLineEndpointAccess access)
    {
        Local(); var result = new SeekLineEndpointObserved[_unique.Length];
        for (var i = 0; i < result.Length; ++i)
        {
            Local(); var target = _unique[i];
            result[i] = access.ReadEndpoint(target.Transform); Local();
            if (result[i].Transform != target.Transform || !Finite(result[i].Local))
            {
                if (_prepared) MarkUnrecognized();
                throw new NotSupportedException("An actual endpoint lacks its exact Transform or finite localPosition.");
            }
        }
        return result;
    }

    private SeekLineEndpointObserved[] Read(IRetainedSeekLineEndpointAccess access)
    {
        try
        {
            var first = ReadOnce(access); var second = ReadOnce(access);
            if (!Same(first, second))
                throw new SeekLineEndpointObservationConflict("The complete endpoint vector changed between its two full observations.");
            return second;
        }
        catch (SeekLineEndpointObservationConflict)
        {
            if (_prepared) MarkUnrecognized();
            throw;
        }
    }

    private Dictionary<long, long> ReadGraphOnce(IRetainedSeekLineEndpointAccess access)
    {
        Local();
        var pending = new SortedSet<long>(_unique.Select(row => row.Transform)
            .Concat(_unique.Select(row => row.ParentTransform)).Concat(_graphBaseline.Keys));
        var graph = new Dictionary<long, long>();
        while (pending.Count != 0)
        {
            Local(); var node = pending.Min; pending.Remove(node);
            if (node == 0 || graph.ContainsKey(node)) continue;
            if (graph.Count >= NativeSeekLineRingSourceCapture.MaximumTransforms)
                throw new NotSupportedException("The fresh parent graph exceeds its retained Transform bound.");
            var parent = access.ReadParent(node);
            graph.Add(node, parent);
            if (parent != 0 && !graph.ContainsKey(parent)) pending.Add(parent);
        }
        CheckGraphCycles(graph);
        return graph;
    }

    private Dictionary<long, long> ReadGraph(IRetainedSeekLineEndpointAccess access)
    {
        try
        {
            var first = ReadGraphOnce(access); var second = ReadGraphOnce(access);
            if (!SameGraph(first, second))
                throw new SeekLineEndpointObservationConflict("The complete actual parent graph changed between observations.");
            return second;
        }
        catch (SeekLineEndpointObservationConflict)
        {
            if (_prepared) MarkUnrecognized();
            throw;
        }
        catch (NotSupportedException)
        {
            if (_prepared) MarkUnrecognized();
            throw;
        }
    }

    private void CheckEndpointGraph(IReadOnlyList<SeekLineEndpointObserved> endpoints,
        IReadOnlyDictionary<long, long> graph)
    {
        Local();
        foreach (var endpoint in endpoints)
        {
            Local();
            if (!graph.TryGetValue(endpoint.Transform, out var parent) || parent != endpoint.Parent)
            {
                if (_prepared) MarkUnrecognized();
                throw new SeekLineEndpointObservationConflict(
                    "A selected endpoint parent differs from the complete observed parent graph.");
            }
        }
        Local();
    }

    private static bool SameGraph(IReadOnlyDictionary<long, long> a, IReadOnlyDictionary<long, long> b)
        => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var parent) && parent == pair.Value);

    private static void CheckGraphCycles(IReadOnlyDictionary<long, long> graph)
    {
        foreach (var seed in graph.Keys)
        {
            var seen = new HashSet<long>(); var cursor = seed; var depth = 0;
            while (cursor != 0)
            {
                if (++depth > NativeSeekLineRingSourceCapture.MaximumParentDepth || !seen.Add(cursor)
                    || !graph.TryGetValue(cursor, out var parent))
                    throw new NotSupportedException("The complete actual/planned parent graph is cyclic, unrooted or too deep.");
                cursor = parent;
            }
        }
    }

    private int[] PlanParentOrder(IReadOnlyDictionary<long, long> actual)
    {
        var planned = new Dictionary<long, long>(actual);
        foreach (var row in _unique)
        {
            if (!planned.ContainsKey(row.Transform) || !planned.ContainsKey(row.ParentTransform))
                throw new InvalidDataException("A planned endpoint parent is outside the complete retained graph.");
            planned[row.Transform] = row.ParentTransform;
        }
        CheckGraphCycles(planned);
        var cursor = new Dictionary<long, long>(actual);
        var remaining = new HashSet<int>(Enumerable.Range(0, _unique.Length)
            .Where(index => cursor[_unique[index].Transform] != _unique[index].ParentTransform));
        var order = new List<int>(remaining.Count);
        while (remaining.Count != 0)
        {
            var selected = -1;
            foreach (var index in remaining.OrderBy(index => index))
            {
                var child = _unique[index].Transform;
                var parent = _unique[index].ParentTransform;
                // Validate the whole simulated intermediate closure. Moving a
                // deep subtree first can exceed the bound until another target
                // descendant moves out, even with an acyclic final graph.
                var previous = cursor[child]; cursor[child] = parent;
                var safe = true;
                try { CheckGraphCycles(cursor); }
                catch (NotSupportedException) { safe = false; }
                finally { cursor[child] = previous; }
                if (safe) { selected = index; break; }
            }
            if (selected < 0)
                throw new NotSupportedException("The final parent graph has no supported cycle/depth-safe one-setter order.");
            cursor[_unique[selected].Transform] = _unique[selected].ParentTransform;
            order.Add(selected); remaining.Remove(selected);
        }
        return order.ToArray();
    }

    private static bool Finite(NativeVector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Same(NativeVector3 a, NativeVector3 b)
        => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
        && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
        && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);
    private static bool Same(SeekLineEndpointObserved a, SeekLineEndpointObserved b)
        => a.Transform == b.Transform && a.Parent == b.Parent && Same(a.Local, b.Local);
    private static bool Same(SeekLineEndpointWorldObserved a, SeekLineEndpointWorldObserved b)
        => a.Transform == b.Transform && a.Parent == b.Parent
        && Same(a.Local, b.Local) && Same(a.World, b.World);
    private static bool Same(IReadOnlyList<SeekLineEndpointObserved> a, IReadOnlyList<SeekLineEndpointObserved> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; ++i) if (!Same(a[i], b[i])) return false;
        return true;
    }
    private bool MatchesTargets(IReadOnlyList<SeekLineEndpointObserved> actual)
    {
        if (actual.Count != _unique.Length) return false;
        for (var i = 0; i < actual.Count; ++i)
            if (actual[i].Parent != _unique[i].ParentTransform
                || !Same(actual[i].Local, _unique[i].LocalPosition)) return false;
        return true;
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _thread || !ReferenceEquals(_registered, this))
            throw new InvalidOperationException("The endpoint writer belongs to its original owner thread.");
    }
    private void Diagnostic()
    {
        CheckThread();
        if (_busy) throw new InvalidOperationException("Endpoint writer diagnostics cannot reenter a native operation.");
    }
    private void Local()
    {
        CheckThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekLineEndpointCommands));
        if (_cleanupFailure is not null)
            throw Responsibility("Unknown original endpoint source cleanup forbids another native operation.");
    }
    // Appended to the adopted reader's existing lifetime guard. All deepest
    // metadata/publication/getter checks observe callback withdrawal at once.
    internal void VerifySourceLifetime() => Local();
    // Only the exact object returned by this writer's latest completed world
    // read can authorize a later, separate scene observation. Record value
    // equality and with-copies do not prove that this owner issued a receipt.
    internal void RequireIssuedWorldObservation(SeekLineEndpointAppliedWorldObservation observation)
    {
        Diagnostic(); Local(); ArgumentNullException.ThrowIfNull(observation);
        if (!_executionFinished || !_appliedVerified || _restorationEntered || _unrecognized
            || !ReferenceEquals(_lastIssuedWorldObservation, observation)
            || !ReferenceEquals(observation.OriginalSource, _source)
            || !ReferenceEquals(observation.CommandOwner, this)
            || !ReferenceEquals(observation.LocalProjection, _projection)
            || observation.SourceIdentity != _projection?.Source.Identity)
            throw new InvalidOperationException("The applied world observation was not issued by this writer in its current applied state.");
    }
    private T Operation<T>(Func<IRetainedSeekLineEndpointAccess,T> action)
    {
        CheckThread();
        if (_busy) throw new InvalidOperationException("An endpoint writer operation cannot synchronously reenter.");
        _appliedVerified = false; _restoredVerified = false; _lastIssuedWorldObservation = null;
        _busy = true; Exception? failure = null;
        try
        {
            Local(); var source = _source ?? throw new InvalidOperationException("Prepare the endpoint writer first.");
            return source.WithVerifiedEndpointOperation(this, (_, access) =>
            {
                Local(); var result = action(access); Local(); return result;
            });
        }
        catch (Exception error)
        {
            _targetOwnershipAcknowledged = false;
            failure = error; _failure ??= error.Message; AdoptCleanup(error);
            if (_externalEffects) { _unknown = true; _retained = true; Attach(error); }
            throw;
        }
        finally
        {
            _busy = false;
            if (_disposed)
            {
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (failure is null) ExceptionDispatchInfo.Capture(cleanup).Throw();
                    failure.Data["SeekLineEndpointOperationCleanup"] = cleanup;
                }
            }
        }
    }

    private void MarkUnrecognized()
    {
        _unrecognized = true; _unknown = true; _appliedVerified = false; _restoredVerified = false;
        _lastIssuedWorldObservation = null;
        _failure ??= "An actual endpoint parent/local vector differs from the complete expected state.";
        if (_externalEffects) _retained = true;
    }
    private void ResolvePending(IReadOnlyList<SeekLineEndpointObserved> actual,
        IReadOnlyDictionary<long, long> graph)
    {
        if (_pending < 0) return;
        var index = _pending; var command = _commands[index];
        var ordinal = Array.FindIndex(_unique, row => row.Transform == command.Transform);
        if (ordinal < 0) { MarkUnrecognized(); _pending = -1; return; }
        var observed = actual[ordinal];
        var before = new SeekLineEndpointObserved(command.Transform, command.BeforeParent, command.Before);
        var target = new SeekLineEndpointObserved(command.Transform, command.TargetParent, command.Target);
        _commands[index] = command with { Readback = observed.Local, ReadbackParent = observed.Parent,
            TargetObserved = Same(observed, target), OriginalObserved = Same(observed, _baseline[ordinal]) };
        if (!graph.TryGetValue(command.Transform, out var observedParent) || observedParent != observed.Parent)
            MarkUnrecognized();
        else if (Same(observed, before) || Same(observed, target))
        {
            _expected[ordinal] = observed;
            _graphExpected[command.Transform] = observed.Parent;
        }
        else MarkUnrecognized();
        _pending = -1;
    }
    private SeekLineEndpointObserved[] Observe(IRetainedSeekLineEndpointAccess access)
    {
        var actual = Read(access); var graph = ReadGraph(access);
        CheckEndpointGraph(actual, graph); ResolvePending(actual, graph);
        if (!Same(actual, _expected) || !SameGraph(graph, _graphExpected)) MarkUnrecognized();
        return actual;
    }

    private SeekLineEndpointWorldObserved[] ReadAppliedWorldOnce(IRetainedSeekLineEndpointAccess access)
    {
        Local(); var result = new SeekLineEndpointWorldObserved[_unique.Length];
        for (var i = 0; i < result.Length; ++i)
        {
            Local(); var target = _unique[i];
            SeekLineEndpointObserved endpoint;
            try { endpoint = access.ReadEndpoint(target.Transform); }
            catch (SeekLineEndpointObservationConflict)
            { MarkUnrecognized(); throw; }
            Local();
            if (endpoint.Transform != target.Transform || endpoint.Parent != target.ParentTransform
                || !Finite(endpoint.Local) || !Same(endpoint.Local, target.LocalPosition))
            {
                MarkUnrecognized();
                throw new SeekLineEndpointObservationConflict(
                    "A selected endpoint differs from its applied parent/local target during world observation.");
            }
            NativeVector3 world;
            try { world = access.ReadWorld(target.Transform); }
            catch (SeekLineEndpointObservationConflict)
            { MarkUnrecognized(); throw; }
            Local();
            if (!Finite(world))
            {
                _failure ??= "An actual endpoint world getter returned a nonfinite component.";
                MarkUnrecognized();
                throw new NotSupportedException("An actual endpoint world getter returned a nonfinite component.");
            }
            result[i] = new(target.Transform, endpoint.Parent, endpoint.Local, world);
        }
        Local(); return result;
    }

    /// <summary>Owns two fresh actual parent/local/world tuple observations
    /// after this same writer has applied all selected endpoint values. The
    /// retained source and command owner remain explicit; finite getter samples
    /// do not make the Unity effects or the snapshots atomic.</summary>
    internal SeekLineEndpointAppliedWorldObservation ReadAppliedWorldObservation()
    {
        var observed = Operation<SeekLineEndpointAppliedWorldObservation>(access =>
        {
            if (!_executionFinished || _restorationEntered)
                throw new InvalidOperationException("Apply endpoint parent/local targets before observing their actual world positions.");
            var before = Observe(access); RequireKnown();
            if (!MatchesTargets(before))
            { MarkUnrecognized(); RequireKnown(); }
            var first = ReadAppliedWorldOnce(access);
            var second = ReadAppliedWorldOnce(access);
            for (var i = 0; i < first.Length; ++i)
                if (!Same(first[i], second[i]))
                {
                    _failure ??= "An actual endpoint parent/local/world tuple changed between complete observations.";
                    MarkUnrecognized();
                    throw new SeekLineEndpointObservationConflict(
                        "An actual endpoint parent/local/world tuple changed between complete observations.");
                }
            var after = Observe(access); RequireKnown();
            if (!Same(before, after) || !MatchesTargets(after))
            { MarkUnrecognized(); RequireKnown(); }
            for (var i = 0; i < second.Length; ++i)
                if (second[i].Transform != after[i].Transform || second[i].Parent != after[i].Parent
                    || !Same(second[i].Local, after[i].Local))
                { MarkUnrecognized(); RequireKnown(); }
            var source = _source ?? throw new InvalidOperationException("The original endpoint source was not retained.");
            var identity = _projection?.Source.Identity
                ?? throw new InvalidOperationException("The original endpoint source identity was not captured.");
            return new(source, this, identity with { }, Array.AsReadOnly(second))
            { LocalProjection = _projection };
        });
        _appliedVerified = true;
        _lastIssuedWorldObservation = observed;
        return observed;
    }

    private void RequireKnown()
    {
        if (_unrecognized) throw new InvalidOperationException("An unrecognized endpoint state permanently forbids another setter or inverse.");
    }

    internal void VerifyPrepared() => Operation(access =>
    {
        if (!_prepared || _executionEntered || _restorationEntered)
            throw new InvalidOperationException("Endpoint preparation must precede once-only execution/restoration.");
        if (!Same(Observe(access), _baseline) || _unrecognized)
            throw new InvalidOperationException("The complete prepared actual endpoint baseline changed.");
        return 0;
    });

    internal void Execute()
    {
        CheckThread();
        if (_rendererPreparationEntered)
            throw new InvalidOperationException("Renderer preparation cannot interleave endpoint execution.");
        Operation(access =>
    {
        if (!_prepared || _executionEntered || _restorationEntered)
            throw new InvalidOperationException("Prepared endpoint commands execute once only.");
        _executionEntered = true;
        // One complete preflight precedes the first native setter.
        if (!Same(Observe(access), _baseline) || _unrecognized)
            throw new InvalidOperationException("The complete actual endpoint baseline changed before execution.");
        foreach (var i in _parentOrder)
            if (_expected[i].Parent != _unique[i].ParentTransform)
                Store(access, i, SeekLineEndpointCommandKind.Parent,
                    _unique[i].ParentTransform, _expected[i].Local, SeekLineEndpointDirection.Apply);
        for (var i = 0; i < _unique.Length; ++i)
        {
            if (!Same(_expected[i].Local, _unique[i].LocalPosition))
                Store(access, i, SeekLineEndpointCommandKind.Local,
                    _expected[i].Parent, _unique[i].LocalPosition, SeekLineEndpointDirection.Apply);
        }
        var final = Observe(access); RequireKnown();
        if (!MatchesTargets(final)) throw new InvalidOperationException("The complete actual endpoint targets were not observed.");
            return 0;
        });
        _executionFinished = true;
    }

    internal bool VerifyAppliedValues()
    {
        var verified = Operation(access =>
    {
        if (!_executionFinished || _restorationEntered)
            throw new InvalidOperationException("Complete endpoint execution before applied verification.");
            var actual = Observe(access); return !_unrecognized && MatchesTargets(actual);
        });
        _appliedVerified = verified; return verified;
    }

    internal void RestoreSelectedValues()
    {
        RevokeTargetOwnershipAcknowledgement();
        if (_rendererStarting || _rendererAdmitted && !_rendererRestoredVerified)
            throw new InvalidOperationException("Restore and verify selected renderer storage before endpoint restoration.");
        var restored = Operation(access =>
    {
        if (!_prepared || _restorationEntered)
            throw new InvalidOperationException("Selected endpoint restoration is once-only after preparation.");
        _restorationEntered = true; Observe(access); RequireKnown();
        for (var index = _entered.Count - 1; index >= 0; --index)
        {
            Observe(access); RequireKnown();
            var command = _commands[_entered[index]];
            var ordinal = Array.FindIndex(_unique, row => row.Transform == command.Transform);
            if (ordinal < 0) { MarkUnrecognized(); RequireKnown(); }
            var before = new SeekLineEndpointObserved(command.Transform, command.BeforeParent, command.Before);
            if (Same(_expected[ordinal], before)) continue;
            var target = new SeekLineEndpointObserved(command.Transform, command.TargetParent, command.Target);
            if (!Same(_expected[ordinal], target)) { MarkUnrecognized(); RequireKnown(); }
            try { Store(access, ordinal, command.Kind, command.BeforeParent,
                command.Before, SeekLineEndpointDirection.Restore); }
            catch
            {
                Observe(access); RequireKnown();
            }
        }
        var final = Observe(access); RequireKnown();
            return Same(final, _baseline);
        });
        _restorationFinished = restored;
    }

    internal bool VerifyRestoredValues()
    {
        var verified = Operation(access =>
    {
        if (!_restorationEntered) throw new InvalidOperationException("Restore selected endpoint values before verifying baseline.");
            var actual = Observe(access); return !_unrecognized && Same(actual, _baseline);
        });
        _restoredVerified = verified; return verified;
    }

    private void Store(IRetainedSeekLineEndpointAccess access, int ordinal,
        SeekLineEndpointCommandKind kind, long parent, NativeVector3 value,
        SeekLineEndpointDirection direction)
    {
        Observe(access); RequireKnown();
        var before = _expected[ordinal]; var target = _unique[ordinal];
        if (parent == 0 && (kind != SeekLineEndpointCommandKind.Parent || direction != SeekLineEndpointDirection.Restore)
            || (kind == SeekLineEndpointCommandKind.Parent && !Same(before.Local, value))
            || kind == SeekLineEndpointCommandKind.Local && before.Parent != parent)
            throw new InvalidOperationException("The parent/local command has another expected selected vector.");
        var index = _commands.Count;
        _commands.Add(new SeekLineEndpointCommandEffect(index, direction, target.Transform, parent,
            before.Local, value, false, false, null, false, false, false, false, null)
        { Kind = kind, BeforeParent = before.Parent, TargetParent = parent });
        var entered = false; Exception? failure = null;
        try
        {
            void VerifyVector()
            {
                // This final vector check runs after setter metadata and
                // temporary rooting, inside the original retained dispatch.
                Observe(access); RequireKnown(); Local();
            }
            void BeforeDispatch()
            {
                Local();
                if (entered || _pending >= 0) { MarkUnrecognized(); throw new InvalidOperationException("The final endpoint setter guard entered twice."); }
                _targetOwnershipAcknowledged = false;
                entered = true; _externalEffects = true; _unknown = true; _retained = true; _pending = index;
                if (direction == SeekLineEndpointDirection.Apply)
                {
                    ++_attemptedWrites; _entered.Add(index);
                    if (kind == SeekLineEndpointCommandKind.Parent) ++_parentWrites; else ++_localWrites;
                }
                else
                {
                    ++_attemptedRestores;
                    if (kind == SeekLineEndpointCommandKind.Parent) ++_parentRestores; else ++_localRestores;
                }
                _commands[index] = _commands[index] with
                { NativeEntered = true, ExternalEffectsUnclosed = true, SideEffectsUnknown = true };
            }
            if (kind == SeekLineEndpointCommandKind.Parent)
                access.WriteEndpointParent(target.Transform, before.Parent, before.Local, parent,
                    VerifyVector, BeforeDispatch);
            else
                access.WriteEndpointLocal(target.Transform, before.Parent, before.Local, value,
                    VerifyVector, BeforeDispatch);
            if (entered) _commands[index] = _commands[index] with { NativeReturned = true };
            if (!entered) throw new InvalidOperationException("The exact endpoint setter did not record native entry.");
        }
        catch (Exception error)
        {
            failure = error; AdoptCleanup(error);
            if (error is SeekLineEndpointObservationConflict) MarkUnrecognized();
        }
        if (entered && _cleanupFailure is null)
        {
            try
            {
                var actual = Observe(access); RequireKnown();
                if (failure is null && (actual[ordinal].Parent != parent || !Same(actual[ordinal].Local, value)))
                    failure = new InvalidOperationException("The endpoint setter returned without its commanded parent/local bits.");
            }
            catch (Exception observation)
            {
                if (failure is not null && !ReferenceEquals(failure, observation)) failure.Data["SeekLineEndpointObservationFailure"] = observation;
                failure ??= observation;
            }
        }
        if (failure is null) return;
        _commands[index] = _commands[index] with { Failure = failure.Message };
        _failure ??= failure.Message;
        if (entered) Attach(failure);
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal SeekLineEndpointJournal GetJournal()
    {
        Diagnostic();
        return new SeekLineEndpointJournal(_prepared, _executionEntered, _executionFinished, _appliedVerified,
            _restorationEntered, _restorationFinished, _restoredVerified, _disposed,
            _attemptedWrites, _attemptedRestores, _unrecognized, _unknown, _externalEffects,
            _retained, _failure, _cleanupFailure?.Message,
            Array.AsReadOnly(_targets.ToArray()), Array.AsReadOnly(_unique.ToArray()),
            Array.AsReadOnly(_baseline.ToArray()), Array.AsReadOnly(_expected.ToArray()),
            Array.AsReadOnly(_commands.ToArray()), _source, _retained ? this : null, _sourceCleanupOwner)
        {
            AttemptedParentWrites = _parentWrites, AttemptedLocalWrites = _localWrites,
            AttemptedParentRestores = _parentRestores, AttemptedLocalRestores = _localRestores,
            BaselineParentGraph = Array.AsReadOnly(_graphBaseline.OrderBy(row => row.Key)
                .Select(row => new SeekLineEndpointParentEdge(row.Key, row.Value)).ToArray()),
            ExpectedParentGraph = Array.AsReadOnly(_graphExpected.OrderBy(row => row.Key)
                .Select(row => new SeekLineEndpointParentEdge(row.Key, row.Value)).ToArray()),
            OriginalLineBuffer = LineBufferJournalCore(), OriginalRenderer = RendererJournalCore()
        };
    }
    private void Attach(Exception error)
    {
        error.Data["SeekLineEndpointCleanupOwner"] = this;
        if (_externalEffects) error.Data["SeekLineEndpointEffectsOwner"] = this;
        if (_source is not null) error.Data["SeekLineEndpointOriginalSource"] = _source;
        if (_sourceCleanupOwner is not null) error.Data["SeekLineEndpointSourceCleanupOwner"] = _sourceCleanupOwner;
        if (_cleanupFailure is not null) error.Data["SeekLineEndpointCleanupFailure"] = _cleanupFailure;
    }
    private void AdoptCleanup(Exception error)
    {
        var sourceCleanup = error.Data["SeekLineRingSourceCleanupFailure"] as Exception
            ?? error.Data["SeekCanvasBasisSourceCleanupFailure"] as Exception;
        if (sourceCleanup is not null)
        {
            _cleanupFailure ??= sourceCleanup;
            _sourceCleanupOwner ??= error.Data["SeekLineRingSourceNativeCleanupOwner"]
                ?? error.Data["SeekCanvasBasisSourceCleanupOwner"]
                ?? error.Data["SeekLineRingSourceCleanupOwner"];
            _unknown = true; _retained = true; Attach(error);
        }
    }
    private Exception Responsibility(string reason)
    { _retained = true; var error = new InvalidOperationException(reason); Attach(error); AttachLineBuffer(error); AttachRenderer(error); return error; }
    private void ReleaseUnadopted()
    {
        _disposed = true; _released = true; _lastIssuedWorldObservation = null;
        lock (OwnerGate) { if (_ownerReserved) { Owners.Remove(this); _ownerReserved = false; } }
    }
    public void Dispose()
    {
        CheckThread(); _appliedVerified = false; _restoredVerified = false; _lastIssuedWorldObservation = null;
        if (_released) return;
        _disposed = true;
        if (_busy || _rendererStarting) return;
        Release();
    }
    private void Release()
    {
        if (_released) return;
        if (_externalEffects && !_targetOwnershipAcknowledged)
            throw Responsibility("Entered Unity line endpoint setters retain exact effects owner and original source roots after raw value restoration.");
        if (_lineBufferStoreAdmitted && !_targetOwnershipAcknowledged)
            throw Responsibility("Entered original LineControl scratch stores retain the exact effects owner and original source roots after selected value restoration.");
        if (_rendererAdmitted && !_targetOwnershipAcknowledged)
            throw Responsibility("Admitted original renderer commands retain the exact effects owner and source roots after selected storage restoration.");
        if (_cleanupFailure is not null || _releaseAttempted)
            throw Responsibility("Unknown endpoint source cleanup is not retried.");
        _releaseAttempted = true;
        try { if (_adopted) _source?.DisposeFromEndpointWriter(this); _released = true; }
        catch (Exception error) { _cleanupFailure ??= error; AdoptCleanup(error); Attach(error); throw; }
        lock (OwnerGate) { if (_ownerReserved) { Owners.Remove(this); _ownerReserved = false; } }
        _retained = false;
    }
}
