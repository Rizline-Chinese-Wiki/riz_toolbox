using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private IMainThreadDispatcher? _holdMain;
    private HoldCoroutineLifecycle? _holdLifecycle;
    private ObservedJudgementRun? _holdObservedRun;
    private IntPtr _holdHandleClass;
    private bool _holdSourceCaptureAdmissionReserved;
    internal HoldCoroutineLifecycle HoldLifecycle => _holdLifecycle
        ?? throw new InvalidOperationException("Hold lifetime observation has not been initialized.");
    internal void InitializeHoldLifecycle(IMainThreadDispatcher main)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        if (_seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
        if (_holdLifecycle is not null)
        {
            if (!ReferenceEquals(main, _holdMain)) throw new InvalidOperationException("The hold dispatcher is once-attached.");
            return;
        }
        _holdMain = main; _holdLifecycle = new HoldCoroutineLifecycle(main);
        _holdHandleClass = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Coroutine");
        HoldLocal();
    }
    private void HoldLocal()
    {
        (_holdMain ?? throw new InvalidOperationException("The hold dispatcher is absent.")).VerifyAccess();
        if (_seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
    }
    private void HoldAdmission()
    {
        HoldLocal();
        if (!HoldLifecycle.Accepting) throw new InvalidOperationException("Hold lifecycle admission is closed.");
    }
    private void ReserveHoldSourceCapture()
    {
        HoldLocal();
        if (_holdSourceCaptureAdmissionReserved)
            throw new InvalidOperationException("A Hold source capture reentered its admission before the original capture completed.");
        _holdSourceCaptureAdmissionReserved = true;
    }
    // Worker faults can only set managed state; no native reads or root frees.
    internal void ObserveHoldLifecycleFault(NativeCoroutineFrame frame, string reason)
        => _holdLifecycle?.Fault("Native hold transport: " + reason);
    internal void TickHoldLifecycle()
    {
        HoldLocal();
        if (_holdObservedRun is not { } prior) { PruneHoldCancellationMonitors(); return; }
        var now = _state.CaptureJudgementRun(prior.Information, prior.Controller);
        if (now is null || !prior.SameGeneration(now)
            || !_state.MatchesCurrentGameplayRun(prior.Information, prior.Controller, prior.RunId, prior.SceneGeneration))
        {
            _holdObservedRun = null; HoldLifecycle.ObserveRun(null); PruneHoldCancellationMonitors(); return;
        }
        HoldLifecycle.ObserveRun(now); _holdObservedRun = now; PruneHoldCancellationMonitors();
    }
    internal HoldStartObservation? CaptureHoldStart(NativeCoroutineFrame frame)
    {
        ReserveHoldSourceCapture();
        try
        {
        HoldAdmission();
        if (!frame.IsValid || frame.Kind != NativeCoroutineKind.Start)
            throw new InvalidOperationException("Invalid physical hold Start frame.");
        if (frame.Enumerator == IntPtr.Zero) return null;
        var capture = HoldSourceCapture.TryCapture(this, frame.Enumerator, frame.Self);
        if (capture is null) return null;
        var transferred = false;
        try
        {
            var identity = ObserveHoldRun(capture); _holdMain!.VerifyAccess();
            transferred = true; // Begin adopts after its own owner-thread check.
            var scope = HoldLifecycle.BeginStart(identity, capture);
            try
            {
                EnsureHoldCancellationMonitor(scope.Identity);
                var observation = new HoldStartObservation(this, frame, capture, scope);
                _seekHoldEpoch?.ObserveStartCapture(observation);
                return observation;
            }
            catch { scope.Dispose(); throw; }
        }
        catch (Exception error)
        {
            if (!transferred) capture.CleanupAfterFailure(error);
            throw;
        }
        }
        finally { _holdSourceCaptureAdmissionReserved = false; }
    }
    internal void CompleteHoldStart(HoldStartObservation observation, NativeCoroutineFrame frame, nuint handle)
    {
        _holdMain!.VerifyAccess(); observation.Verify(this, frame);
        if (handle != 0) observation.Capture.RetainHandle(unchecked((IntPtr)(nint)handle));
        HoldLifecycle.CompleteStart(observation.Scope, unchecked((long)handle));
    }
    internal HoldMoveObservation? CaptureHoldMove(NativeCoroutineFrame frame)
    {
        ReserveHoldSourceCapture();
        try
        {
        HoldLocal();
        if (!frame.IsValid || frame.Kind != NativeCoroutineKind.MoveNext)
            throw new InvalidOperationException("Invalid physical hold MoveNext frame.");
        var context = CaptureHoldSynchronousContext(frame.Self);
        HoldSourceCapture? capture = null;
        HoldCoroutineLifecycle.MoveScope? scope = null;
        var transferred = false;
        try
        {
            if (HoldLifecycle.Accepting)
            {
                capture = HoldSourceCapture.TryCapture(this, frame.Self, IntPtr.Zero)
                    ?? throw new UnsupportedSignatureException("The hold MoveNext receiver has another exact iterator class.");
                var identity = ObserveHoldRun(capture); _holdMain!.VerifyAccess();
                transferred = true;
                scope = HoldLifecycle.BeginMove(identity, capture);
                EnsureHoldCancellationMonitor(scope.Identity);
            }
        }
        catch (Exception error)
        {
            if (!transferred) capture?.CleanupAfterFailure(error);
            HoldLifecycle.Fault("Hold lifecycle capture failed; synchronous attribution remains independent: " + error.Message);
            capture = null;
        }
        try { return new HoldMoveObservation(this, frame, capture, scope, context); }
        catch { try { scope?.Dispose(); } finally { context.Dispose(); } throw; }
        }
        finally { _holdSourceCaptureAdmissionReserved = false; }
    }
    internal void CompleteHoldMove(HoldMoveObservation observation, NativeCoroutineFrame frame, nuint result)
    {
        _holdMain!.VerifyAccess(); observation.Verify(this, frame);
        // AL is the result; upper RAX bits are outside the Boolean ABI.
        if (observation.Scope is { } scope) HoldLifecycle.CompleteMove(scope, unchecked((byte)result));
    }
    private HoldCoroutineContext.Scope CaptureHoldSynchronousContext(IntPtr receiver)
    {
        var roots = new HoldRoots(_holdMain!, 1);
        var transferred = false;
        void Guard() { HoldLocal(); roots.VerifyOpen(); }
        try
        {
            Guard();
            var root = roots.Adopt(_api, receiver);
            Guard();
            var retained = roots.Target(root); Guard();
            var actualClass = _api.ObjectClass(retained); Guard();
            if (actualClass != _coroutine)
                throw new UnsupportedSignatureException("The synchronous Hold receiver is not the exact iterator class.");
            var coroutine = roots.Target(root).ToInt64(); Guard();
            transferred = true;
            return _holdContext.Enter(coroutine, roots);
        }
        catch { if (!transferred) roots.Dispose(); throw; }
    }
    internal sealed class HoldStartObservation : IDisposable
    {
        private readonly NativeJudgementBackend _owner;
        private readonly NativeCoroutineFrame _frame;
        internal HoldSourceCapture Capture { get; }
        internal HoldCoroutineLifecycle.StartScope Scope { get; }
        private bool _disposed;
        internal HoldStartObservation(NativeJudgementBackend owner, NativeCoroutineFrame frame,
            HoldSourceCapture capture, HoldCoroutineLifecycle.StartScope scope)
            => (_owner, _frame, Capture, Scope) = (owner, frame, capture, scope);
        internal void Verify(NativeJudgementBackend owner, NativeCoroutineFrame frame)
        {
            if (_disposed || !ReferenceEquals(owner, _owner) || !_frame.Equals(frame))
                throw new InvalidOperationException("Hold Start after observation has a foreign or closed invocation.");
        }
        public void Dispose()
        {
            _owner._holdMain!.VerifyAccess();
            if (_disposed) return;
            _disposed = true; Scope.Dispose();
        }
    }
    internal sealed class HoldMoveObservation : IDisposable
    {
        private readonly NativeJudgementBackend _owner;
        private readonly NativeCoroutineFrame _frame;
        private readonly HoldCoroutineContext.Scope _context;
        internal HoldSourceCapture? Capture { get; }
        internal HoldCoroutineLifecycle.MoveScope? Scope { get; }
        private bool _disposed;
        internal HoldMoveObservation(NativeJudgementBackend owner, NativeCoroutineFrame frame,
            HoldSourceCapture? capture, HoldCoroutineLifecycle.MoveScope? scope, HoldCoroutineContext.Scope context)
            => (_owner, _frame, Capture, Scope, _context) = (owner, frame, capture, scope, context);
        internal void Verify(NativeJudgementBackend owner, NativeCoroutineFrame frame)
        {
            if (_disposed || !ReferenceEquals(owner, _owner) || !_frame.Equals(frame))
                throw new InvalidOperationException("Hold MoveNext after observation has a foreign or closed invocation.");
        }
        public void Dispose()
        {
            _owner._holdMain!.VerifyAccess();
            if (_disposed) return;
            _disposed = true;
            try { _context.Dispose(); }
            catch { _owner.HoldLifecycle.Fault("Synchronous Hold root cleanup is unknown."); throw; }
            finally { Scope?.Dispose(); }
        }
    }
    internal sealed class HoldSourceCapture : IDisposable
    {
        private static readonly object UnknownGate = new();
        private static readonly HashSet<HoldSourceCapture> UnknownCaptureOwners = new();
        private readonly NativeJudgementBackend _owner;
        private readonly HoldRoots _roots;
        private string? _session, _runId;
        private long _sceneGeneration, _lastRevision;
        private NativeRuntime.RootHandle? _enumerator, _judge, _note, _information, _controller;
        private HoldCoroutineIdentity? _identity;
        private NativeUnityCoroutineOwnerObservation? _nativeOwnerObservation;
        private HoldNativeOwnerPair? _nativeOwnerPair;
        internal HoldCoroutineIdentity Identity => _identity
            ?? throw new InvalidOperationException("The physical hold identity is not complete.");
        internal NativeUnityCoroutineOwnerObservation NativeOwnerObservation => _nativeOwnerObservation
            ?? throw new InvalidOperationException("The physical Hold native owner has not been observed.");
        internal HoldNativeOwnerPair? NativeOwnerPair => _nativeOwnerPair;
        private HoldSourceCapture(NativeJudgementBackend owner)
        {
            _owner = owner; _roots = new HoldRoots(owner._holdMain!, 12);
        }
        internal void CleanupAfterFailure(Exception error)
        {
            try { Dispose(); }
            catch (Exception cleanup)
            {
                lock (UnknownGate) UnknownCaptureOwners.Add(this);
                error.Data["HoldCaptureCleanupOwner"] = this;
                error.Data["HoldCaptureCleanupFailure"] = cleanup;
            }
        }
        public void Dispose() => _roots.Dispose();
        private void Guard()
        {
            _owner.HoldLocal(); _roots.VerifyOpen();
            if (_runId is null) return;
            if (_owner._state.SessionId != _session || _owner._state.RunId != _runId
                || _owner._state.SceneGeneration != _sceneGeneration || _owner._state.Revision < _lastRevision)
                throw new InvalidOperationException("The hold capture lost its observed run generation.");
            _lastRevision = _owner._state.Revision;
            if (_identity is { } identity)
            {
                var current = _owner._state.CaptureJudgementRun(identity.Run.Information, identity.Run.Controller);
                if (current is null || !identity.Run.SameGeneration(current)
                    || !_owner._state.MatchesCurrentGameplayRun(identity.Run.Information, identity.Run.Controller,
                        identity.Run.RunId, identity.Run.SceneGeneration))
                    throw new InvalidOperationException("The hold source is no longer the actual observed run.");
            }
        }
        private T Native<T>(Func<T> operation) { Guard(); var value = operation(); Guard(); return value; }
        private IntPtr Target(NativeRuntime.RootHandle root) => Native(() => _roots.Target(root));
        private NativeRuntime.RootHandle Adopt(IntPtr value)
        {
            Guard(); var root = _roots.Adopt(_owner._api, value); Guard(); return root;
        }
        private void RequireClass(NativeRuntime.RootHandle root, IntPtr cls)
        {
            var retained = Target(root);
            if (Native(() => _owner._api.ObjectClass(retained)) != cls)
                throw new UnsupportedSignatureException("A hold graph receiver has another exact class.");
        }
        private NativeRuntime.RootHandle Reference(NativeRuntime.RootHandle receiver, IntPtr cls, string name, string type, int offset)
        {
            var expectedClass = type switch
            {
                "InLevel.NoteJudgeControl" => _owner._owner,
                "InLevel.NoteInformation" => _owner._note,
                "InLevel.LevelInformation" => _owner._info,
                _ => throw new UnsupportedSignatureException("The physical Hold reference type is outside its exact graph.")
            };
            var field = _owner._api.InspectHoldCaptureReferenceField(cls, name, type, expectedClass, offset, Guard);
            NativeRuntime.RootHandle? result = null;
            _owner._api.PublishHoldCaptureReferenceField(receiver, field, (token, temporary) =>
            {
                result = _roots.AdoptPublished(token, temporary, out var transferred);
                return transferred;
            }, Guard);
            var root = result ?? throw new InvalidDataException("A required hold graph reference is absent.");
            RequireClass(root, field.FieldClass); return root;
        }
        private long ReferenceAddress(NativeRuntime.RootHandle receiver, IntPtr cls, string name, string type, int offset)
            => Target(Reference(receiver, cls, name, type, offset)).ToInt64();
        private bool IsAlive(NativeRuntime.RootHandle root)
            => _owner._api.ReadHoldCaptureAlive(root, _owner._object, Guard);
        internal void RetainHandle(IntPtr handle)
        {
            // A paired old invocation may finish after retirement. Its roots
            // remain invocation-owned; do not reassociate it with a new run.
            // Completion must not require a still-current run or a live backend:
            // the original native Start may return after run/module retirement.
            void CompletionGuard() { _owner._holdMain!.VerifyAccess(); _roots.VerifyOpen(); }
            CompletionGuard();
            var root = _roots.Adopt(_owner._api, handle);
            CompletionGuard();
            var retained = _roots.Target(root); CompletionGuard();
            var actualClass = _owner._api.ObjectClass(retained); CompletionGuard();
            if (actualClass != _owner._holdHandleClass)
                throw new UnsupportedSignatureException("Hold Start returned a foreign Coroutine wrapper.");
        }
        internal void CaptureNativeOwner()
        {
            if (_nativeOwnerObservation is not null)
                throw new InvalidOperationException("The Hold source native owner was already observed.");
            VerifySource();
            var observation = _owner._api.ObserveUnityCoroutineOwner(_judge!, Identity.Owner, Guard);
            if (observation.ManagedReceiver != Identity.Owner || observation.ManagedClass != _owner._owner)
                throw new UnsupportedSignatureException("The Hold native owner observation has another exact managed receiver class.");
            VerifySource();
            _nativeOwnerObservation = observation;
            _nativeOwnerPair = observation.CachedNativeOwner == 0 ? null
                : new HoldNativeOwnerPair(observation.ManagedReceiver, observation.CachedNativeOwner, observation.ManagedClass);
        }
        internal static HoldSourceCapture? TryCapture(NativeJudgementBackend owner, IntPtr enumerator, IntPtr startOwner)
        {
            var value = new HoldSourceCapture(owner);
            try
            {
                value._enumerator = value.Adopt(enumerator);
                var retainedEnumerator = value.Target(value._enumerator);
                if (value.Native(() => owner._api.ObjectClass(retainedEnumerator)) != owner._coroutine)
                {
                    value.Dispose(); return null;
                }
                value._session = owner._state.SessionId; value._runId = owner._state.RunId;
                value._sceneGeneration = owner._state.SceneGeneration; value._lastRevision = owner._state.Revision;
                if (value._runId is null) throw new InvalidOperationException("The actual hold iterator has no observed run.");
                value.CaptureAssociation(startOwner);
                value._controller = value.FindController();
                var run = owner._state.CaptureJudgementRun(value.Target(value._information!).ToInt64(), value.Target(value._controller).ToInt64())
                    ?? throw new InvalidOperationException("The hold graph has no observed controller/run association.");
                value._identity = new(run, value.Target(value._judge!).ToInt64(), value.Target(value._enumerator).ToInt64(), value.Target(value._note!).ToInt64());
                value.VerifySource(); return value;
            }
            catch (Exception error) { value.CleanupAfterFailure(error); throw; }
        }
        private void CaptureAssociation(IntPtr startOwner)
        {
            Guard();
            _judge = Reference(_enumerator!, _owner._coroutine, "<>4__this", "InLevel.NoteJudgeControl", 0x20);
            _note = Reference(_enumerator!, _owner._coroutine, "holdNoteInformation", "InLevel.NoteInformation", 0x28);
            RequireClass(_judge, _owner._owner); RequireClass(_note, _owner._note);
            if (startOwner != IntPtr.Zero && Target(_judge) != startOwner)
                throw new InvalidOperationException("The hold Start receiver is not its iterator's judge owner.");
            _information = Reference(_judge, _owner._owner, "levelInformation", "InLevel.LevelInformation", 0x20);
            RequireClass(_information, _owner._info);
            if (ReferenceAddress(_information, _owner._info, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58) != Target(_judge).ToInt64())
                throw new InvalidOperationException("The hold judge/information association is not bidirectional.");
        }
        private NativeRuntime.RootHandle FindController()
        {
            // Bounded controller discovery, without chart/note/counter scans.
            var scan = new HoldRoots(_owner._holdMain!, 1026); Exception? failure = null;
            try
            {
                NativeRuntime.RootHandle? type = null, array = null;
                _owner._api.PublishHoldControllerType(_owner._controller, (token, temporary) =>
                {
                    type = scan.AdoptPublished(token, temporary, out var transferred); return transferred;
                }, Guard);
                _owner._api.PublishHoldControllerCandidates(type ?? throw new InvalidDataException("Controller discovery has no retained Type."),
                    _owner._resources, _owner._object, (token, temporary) =>
                    {
                        array = scan.AdoptPublished(token, temporary, out var transferred); return transferred;
                    }, Guard);
                var arrayRoot = array ?? throw new InvalidDataException("Controller discovery did not publish an array.");
                var arrayTarget = Native(() => scan.Target(arrayRoot));
                var arrayClass = Native(() => _owner._api.ObjectClass(arrayTarget));
                var vector = _owner._api.ReadHoldControllerCandidates(arrayRoot, arrayClass, _owner._object,
                    (_, token, temporary) =>
                    {
                        scan.AdoptPublished(token, temporary, out var transferred); return transferred;
                    }, Guard);
                long selected = 0;
                foreach (var candidate in vector.Items)
                {
                    if (candidate == IntPtr.Zero) continue;
                    Guard(); var root = scan.Find(candidate);
                    var retained = Native(() => scan.Target(root));
                    if (Native(() => _owner._api.ObjectClass(retained)) != _owner._controller) continue;
                    var information = Target(_information!).ToInt64();
                    var now = _owner._state.CaptureJudgementRun(information, candidate.ToInt64());
                    if (now is null || !_owner._state.MatchesCurrentGameplayRun(information, candidate.ToInt64(), now.RunId, now.SceneGeneration)) continue;
                    if (selected != 0) throw new InvalidOperationException("Controller discovery returned multiple matches for the hold run.");
                    selected = candidate.ToInt64();
                }
                if (selected == 0) throw new InvalidOperationException("No observed controller matches the physical hold source.");
                var controller = Adopt(new(selected)); RequireClass(controller, _owner._controller);
                if (!IsAlive(controller) || !IsAlive(_information!) || !IsAlive(_note!)
                    || ReferenceAddress(controller, _owner._controller, "LevelInformation", "InLevel.LevelInformation", 0x28) != Target(_information!).ToInt64())
                    throw new InvalidOperationException("The physical hold source has a dead or foreign controller association.");
                return controller;
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                try { scan.Dispose(); }
                catch (Exception cleanup)
                {
                    var error = failure ?? cleanup;
                    error.Data["HoldDiscoveryCleanupOwner"] = scan;
                    error.Data["HoldDiscoveryCleanupFailure"] = cleanup;
                    if (failure is null) throw;
                }
            }
        }
        internal void VerifySource()
        {
            Guard(); var identity = Identity;
            RequireClass(_enumerator!, _owner._coroutine); RequireClass(_judge!, _owner._owner);
            RequireClass(_note!, _owner._note); RequireClass(_information!, _owner._info);
            RequireClass(_controller!, _owner._controller);
            if (Target(_enumerator!).ToInt64() != identity.Enumerator || Target(_judge!).ToInt64() != identity.Owner
                || Target(_note!).ToInt64() != identity.Note || Target(_information!).ToInt64() != identity.Run.Information
                || Target(_controller!).ToInt64() != identity.Run.Controller
                || ReferenceAddress(_enumerator!, _owner._coroutine, "<>4__this", "InLevel.NoteJudgeControl", 0x20) != identity.Owner
                || ReferenceAddress(_enumerator!, _owner._coroutine, "holdNoteInformation", "InLevel.NoteInformation", 0x28) != identity.Note
                || ReferenceAddress(_judge!, _owner._owner, "levelInformation", "InLevel.LevelInformation", 0x20) != identity.Run.Information
                || ReferenceAddress(_information!, _owner._info, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58) != identity.Owner
                || ReferenceAddress(_controller!, _owner._controller, "LevelInformation", "InLevel.LevelInformation", 0x28) != identity.Run.Information
                || !IsAlive(_controller!) || !IsAlive(_information!) || !IsAlive(_note!))
                throw new InvalidOperationException("The retained hold graph changed identity or association.");
            Guard();
        }
    }
    private sealed class HoldRoots : IDisposable
    {
        private static readonly object Gate = new();
        private static readonly HashSet<HoldRoots> UnknownOwners = new();
        private readonly IMainThreadDispatcher _main;
        private readonly NativeRuntime.RootHandle?[] _roots;
        private readonly Dictionary<long, NativeRuntime.RootHandle> _byAddress = new();
        private readonly Dictionary<NativeRuntime.RootHandle, long> _addresses = new();
        private readonly List<NativeRuntime.RootHandle> _lateUnknownRoots = new();
        private int _count;
        private bool _closing;
        private Exception? _cleanupFailure;
        internal HoldRoots(IMainThreadDispatcher main, int maximum)
        {
            _main = main; main.VerifyAccess();
            lock (Gate) if (UnknownOwners.Count >= 4096)
                throw new InvalidOperationException("Hold captures exhausted their unknown-free owner bound.");
            _roots = new NativeRuntime.RootHandle[maximum];
        }
        internal void VerifyOpen()
        {
            _main.VerifyAccess();
            if (_closing) throw new ObjectDisposedException(nameof(HoldRoots));
        }
        internal NativeRuntime.RootHandle Adopt(NativeRuntime api, IntPtr token)
        {
            VerifyOpen();
            if (token == IntPtr.Zero) throw new InvalidDataException("A required physical hold reference is absent.");
            if (_byAddress.TryGetValue(token.ToInt64(), out var existing)) return existing;
            if (_count >= _roots.Length) throw new InvalidOperationException("The physical hold capture exceeded its root bound.");
            // newHandle may call back into Dispose. A root returned after that
            // close must never be installed into a container whose one cleanup
            // pass has already run.
            var root = api.Root(token);
            var transferred = false; var releaseAttempted = false;
            try
            {
                if (_closing) throw new ObjectDisposedException(nameof(HoldRoots));
                var retained = AdoptPublished(token, root, out transferred);
                if (!transferred) { releaseAttempted = true; ReleaseUnacceptedRoot(root); }
                return retained;
            }
            catch (Exception error)
            {
                if (!transferred && !releaseAttempted)
                {
                    try { ReleaseUnacceptedRoot(root); }
                    catch (Exception cleanup) { cleanup.Data["HoldLateRootOriginalFailure"] = error; throw; }
                }
                throw;
            }
        }
        // The publication call already owns this newly rooted reference. Its
        // caller transfers responsibility only after this method returns true.
        internal NativeRuntime.RootHandle AdoptPublished(IntPtr token, NativeRuntime.RootHandle root, out bool transferred)
        {
            transferred = false; VerifyOpen(); ArgumentNullException.ThrowIfNull(root);
            if (token == IntPtr.Zero) throw new InvalidDataException("A required physical hold reference is absent.");
            if (_byAddress.TryGetValue(token.ToInt64(), out var existing)) return existing;
            if (_count >= _roots.Length) throw new InvalidOperationException("The physical hold capture exceeded its root bound.");
            _byAddress.Add(token.ToInt64(), root);
            try { _addresses.Add(root, token.ToInt64()); }
            catch { _byAddress.Remove(token.ToInt64()); throw; }
            _roots[_count++] = root; transferred = true;
            return root;
        }
        private void ReleaseUnacceptedRoot(NativeRuntime.RootHandle root)
        {
            try { root.Dispose(); }
            catch (Exception error)
            {
                _lateUnknownRoots.Add(root);
                _closing = true;
                Exception[] failures = _cleanupFailure is { } previous
                    ? new[] { previous, error } : new[] { error };
                _cleanupFailure = new AggregateException(
                    "A late Hold root free is unknown; its original owner is retained without retry.",
                    failures);
                _cleanupFailure.Data["HoldLateRootCleanupOwner"] = this;
                _cleanupFailure.Data["HoldLateRootCleanupFailure"] = error;
                lock (Gate) UnknownOwners.Add(this);
                throw _cleanupFailure;
            }
        }
        internal NativeRuntime.RootHandle Find(IntPtr token)
        {
            VerifyOpen();
            return _byAddress.TryGetValue(token.ToInt64(), out var root) ? root
                : throw new InvalidOperationException("A discovery token was not independently retained.");
        }
        internal IntPtr Target(NativeRuntime.RootHandle root)
        {
            VerifyOpen(); var value = root.Target; VerifyOpen();
            if (!_addresses.TryGetValue(root, out var expected) || value.ToInt64() != expected)
                throw new InvalidOperationException("A physical hold root changed its retained reference.");
            return value;
        }
        public void Dispose()
        {
            _main.VerifyAccess();
            if (_closing)
            {
                if (_cleanupFailure is not null) throw _cleanupFailure;
                return;
            }
            _closing = true;
            List<Exception>? failures = null;
            for (var index = _count - 1; index >= 0; --index)
            {
                var root = _roots[index]; if (root is null) continue;
                try { root.Dispose(); _roots[index] = null; }
                catch (Exception error) { (failures ??= new()).Add(error); }
            }
            if (failures is null) return;
            _cleanupFailure = new AggregateException("Hold root cleanup is unknown; failed roots remain owned without retry.", failures);
            lock (Gate) UnknownOwners.Add(this);
            throw _cleanupFailure;
        }
    }
    private HoldCoroutineIdentity ObserveHoldRun(HoldSourceCapture capture)
    {
        capture.VerifySource();
        var identity = capture.Identity;
        var now = _state.CaptureJudgementRun(identity.Run.Information, identity.Run.Controller)
            ?? throw new InvalidOperationException("The hold source has no current observed run.");
        if (!identity.Run.SameGeneration(now) || now.Revision < identity.Run.Revision)
            throw new InvalidOperationException("The hold source changed generation before admission.");
        HoldLifecycle.ObserveRun(now);
        // Previous-run frees can reenter and alter this new graph. Its own
        // independent roots remain retained; validate them after that cleanup.
        capture.VerifySource();
        var latest = _state.CaptureJudgementRun(identity.Run.Information, identity.Run.Controller);
        if (latest is null || !identity.Run.SameGeneration(latest) || latest.Revision < now.Revision
            || !_state.MatchesCurrentGameplayRun(latest.Information, latest.Controller, latest.RunId, latest.SceneGeneration))
            throw new InvalidOperationException("The hold source changed during previous-run retirement.");
        capture.CaptureNativeOwner();
        var afterOwner = _state.CaptureJudgementRun(identity.Run.Information, identity.Run.Controller);
        if (afterOwner is null || !identity.Run.SameGeneration(afterOwner) || afterOwner.Revision < latest.Revision
            || !_state.MatchesCurrentGameplayRun(afterOwner.Information, afterOwner.Controller,
                afterOwner.RunId, afterOwner.SceneGeneration))
            throw new InvalidOperationException("The hold source changed during native owner observation.");
        HoldLifecycle.ObserveRun(afterOwner);
        _holdObservedRun = afterOwner;
        return identity with { Run = afterOwner };
    }
}
