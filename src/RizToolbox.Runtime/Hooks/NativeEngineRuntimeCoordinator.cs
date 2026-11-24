using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeEngineRuntimeStatus { CapabilityUnavailable, Ready, Unknown }

/// <summary>One permanent process owner of the complete published native family bundle.
/// Logical consumers subscribe to the existing dispatchers; detaching a consumer
/// never detaches a native callback or abandons a captured completion duty.</summary>
internal sealed class NativeEngineRuntimeCoordinator
{
    private static readonly object ProcessGate = new();
    private static readonly Dictionary<object, NativeEngineRuntimeCoordinator> Owners = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<object> Rejected = new(ReferenceEqualityComparer.Instance);
    private static readonly List<NativeEngineRuntimeCoordinator> PermanentOwners = new();
    private static int _productionUnknown;
    private readonly INativeEnginePublicationLifetime? _publicationOwner;
    private readonly NativeEngineNodeObserverDispatcher?[] _dispatchers = new NativeEngineNodeObserverDispatcher?[4];
    private INativeEngineInlineContextBridge? _inlineBridge;
    private NativeEngineInlineObserverDispatcher? _inline;
    private INativeEngineSchedulerContextBridge[]? _schedulerBridges;
    private NativeEngineSchedulerObserverDispatcher? _scheduler;
    private INativeEngineWaitContextBridge? _waitBridge;
    private NativeEngineWaitObserverDispatcher? _wait;
    private readonly object _gate = new();
    private int _unknown;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0 || _publicationOwner?.Unknown == true
        || _dispatchers.Any(dispatcher => dispatcher?.Unknown == true) || _inline?.Unknown == true
        || _scheduler?.Unknown == true || _wait?.Unknown == true;
    internal bool InlineAvailable => _inline is not null && !Unknown;

    internal static bool TryAcquire(ILogger log, out NativeEngineRuntimeCoordinator? coordinator,
        out NativeEngineRuntimeStatus status, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(log); coordinator = null;
        lock (ProcessGate)
        {
            if (Volatile.Read(ref _productionUnknown) != 0)
            { status = NativeEngineRuntimeStatus.Unknown; reason = "Engine production ownership is permanently Unknown."; return false; }
            // Candidate fingerprints prove identity only. No managed operation
            // may turn the still-unproven native startup window into authority.
            if (!NativeEngineTransportFirstInstall.ProductionStartupAdmissionKnown)
            { status = NativeEngineRuntimeStatus.CapabilityUnavailable; reason = "Engine native startup execution-window admission is unavailable."; return false; }
            try
            {
                var owner = NativeEngineTransportFirstInstall.RequireReady();
                coordinator = AcquirePublished(owner, log);
                if (!coordinator.ReadActivity().Known) throw new UnsupportedSignatureException("Engine production observation is Unknown.");
                status = NativeEngineRuntimeStatus.Ready; reason = null; return true;
            }
            catch (Exception error)
            {
                Interlocked.Exchange(ref _productionUnknown, 1);
                status = NativeEngineRuntimeStatus.Unknown; reason = error.Message; return false;
            }
        }
    }

    // Fixture access carries the actual sealed native owner, never a DTO or a
    // capability setter. Production admission still uses the independent gate.
    internal static NativeEngineRuntimeCoordinator AcquireOwnedPublished(NativeEngineTransportFirstInstall publication, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(publication); ArgumentNullException.ThrowIfNull(log);
        if (!publication.OwnedPublication)
            throw new UnsupportedSignatureException("The owned transport fixture requires an actual native profile-one owner.");
        lock (ProcessGate) return AcquirePublished(publication, log);
    }

    private static NativeEngineRuntimeCoordinator AcquirePublished(NativeEngineTransportFirstInstall publication, ILogger log)
        => publication.VerifyOperation(() => AcquirePublishedCore(publication, log));

    private static NativeEngineRuntimeCoordinator AcquirePublishedCore(NativeEngineTransportFirstInstall publication, ILogger log)
    {
        // ProcessGate protects managed creation, not native execution. Counters
        // freeze the actual whole-family Attach interval and reject work crossing it.
        if (Rejected.Contains(publication)) throw new UnsupportedSignatureException("This V5 whole-family adoption is permanently Unknown.");
        if (Owners.TryGetValue(publication, out var existing))
        {
            TracePublished(log, "existing.verify.begin");
            publication.VerifyReady();
            if (existing.Unknown) throw new UnsupportedSignatureException("The retained V5 coordinator is Unknown.");
            TracePublished(log, "existing.verify.end");
            return existing;
        }
        TracePublished(log, "begin-full-adoption.begin");
        publication.BeginFullAdoption();
        TracePublished(log, "begin-full-adoption.end");
        try
        {
            var nodes = new NativeEngineNodeContextBridge[4];
            for (var index = 0; index < nodes.Length; ++index)
            {
                TracePublished(log, "node-bridge." + index + ".begin");
                nodes[index] = NativeEngineNodeContextBridge.AdoptPublished(publication, (NativeEngineNodeKind)index, log);
                TracePublished(log, "node-bridge." + index + ".end");
            }
            TracePublished(log, "inline-bridge.begin");
            var inline = NativeEngineInlineContextBridge.AdoptPublished(publication, log);
            TracePublished(log, "inline-bridge.end");
            var scheduler = new INativeEngineSchedulerContextBridge[6];
            for (var index = 0; index < scheduler.Length; ++index)
            {
                TracePublished(log, "scheduler-bridge." + index + ".begin");
                scheduler[index] = NativeEngineSchedulerContextBridge.AdoptPublished(publication, (NativeEngineSchedulerKind)index, log);
                TracePublished(log, "scheduler-bridge." + index + ".end");
            }
            TracePublished(log, "wait-bridge.begin");
            var wait = NativeEngineWaitContextBridge.AdoptPublished(publication, log);
            TracePublished(log, "wait-bridge.end");
            TracePublished(log, "share-all-families.begin");
            var owner = Share(publication, publication, kind => new NativeEngineNodeObserverDispatcher(nodes[(int)kind], log), inline, log, scheduler, wait);
            TracePublished(log, "share-all-families.end");
            TracePublished(log, "complete-full-adoption.begin");
            publication.CompleteFullAdoption();
            TracePublished(log, "complete-full-adoption.end");
            return owner;
        }
        catch { publication.MarkUnknown(); Rejected.Add(publication); throw; }
    }

    private static void TracePublished(ILogger? log, string stage)
    {
        // Diagnostics are never publication authority and may not alter an
        // adoption outcome merely because a consumer's logger throws.
        try { log?.Log(LogLevel.Debug, "V5 adoption " + stage); } catch { }
    }

    private static NativeEngineRuntimeCoordinator Share(object key, INativeEnginePublicationLifetime? publicationOwner,
        Func<NativeEngineNodeKind, NativeEngineNodeObserverDispatcher> create,
        INativeEngineInlineContextBridge? inlineBridge = null, ILogger? inlineLog = null,
        IReadOnlyList<INativeEngineSchedulerContextBridge>? schedulerBridges = null,
        INativeEngineWaitContextBridge? waitBridge = null)
    {
        lock (ProcessGate)
        {
            var traceLog = publicationOwner is NativeEngineTransportFirstInstall ? inlineLog : null;
            if (Rejected.Contains(key)) throw new UnsupportedSignatureException("This physical engine observation owner is permanently Unknown.");
            if (Owners.TryGetValue(key, out var existing))
            {
                if (existing.Unknown) throw new UnsupportedSignatureException("This physical engine observation owner is permanently Unknown.");
                if (inlineBridge is not null && !ReferenceEquals(existing._inlineBridge, inlineBridge))
                    throw new UnsupportedSignatureException("This engine owner cannot adopt another or late inline responsibility.");
                if (schedulerBridges is not null && (existing._schedulerBridges is null
                    || schedulerBridges.Count != existing._schedulerBridges.Length
                    || schedulerBridges.Where((bridge, index) => !ReferenceEquals(bridge, existing._schedulerBridges[index])).Any()))
                    throw new UnsupportedSignatureException("This engine owner cannot adopt another or late scheduler responsibility.");
                if (waitBridge is not null && !ReferenceEquals(existing._waitBridge, waitBridge))
                    throw new UnsupportedSignatureException("This engine owner cannot adopt another or late wait responsibility.");
                return existing;
            }
            var owner = new NativeEngineRuntimeCoordinator(publicationOwner);
            PermanentOwners.Add(owner); // Before the first possibly partial Attach.
            try
            {
                for (var index = 0; index < 4; ++index)
                {
                    TracePublished(traceLog, "attach.node." + index + ".begin");
                    publicationOwner?.VerifyReady();
                    var dispatcher = create((NativeEngineNodeKind)index);
                    if (dispatcher.Identity.Kind != (NativeEngineNodeKind)index || dispatcher.Unknown)
                        throw new UnsupportedSignatureException("Engine dispatcher has a different typed channel or is Unknown.");
                    owner._dispatchers[index] = dispatcher;
                    TracePublished(traceLog, "attach.node." + index + ".end");
                }
                if (inlineBridge is not null)
                {
                    TracePublished(traceLog, "attach.inline.begin");
                    owner._inlineBridge = inlineBridge;
                    owner._inline = new(inlineBridge, owner._dispatchers[(int)NativeEngineNodeKind.Start]!,
                        inlineLog ?? throw new ArgumentNullException(nameof(inlineLog)));
                    TracePublished(traceLog, "attach.inline.end");
                }
                if (schedulerBridges is not null)
                {
                    TracePublished(traceLog, "attach.scheduler-six.begin");
                    owner._schedulerBridges = schedulerBridges.ToArray();
                    owner._scheduler = new(owner._schedulerBridges,
                        inlineLog ?? throw new ArgumentNullException(nameof(inlineLog)));
                    TracePublished(traceLog, "attach.scheduler-six.end");
                }
                if (waitBridge is not null)
                {
                    TracePublished(traceLog, "attach.wait.begin");
                    if (owner._schedulerBridges is null)
                        throw new UnsupportedSignatureException("Wait observation requires the same six scheduler channels.");
                    for (var index = 0; index < 4; ++index)
                        if (waitBridge.Identity.Target((NativeEngineWaitKind)(index + 3)) != owner._dispatchers[index]!.Identity.Target)
                            throw new UnsupportedSignatureException("Wait boundary target differs from its actual node channel.");
                    for (var index = 0; index < 6; ++index)
                        if (waitBridge.Identity.Target((NativeEngineWaitKind)(index + 7)) != owner._schedulerBridges[index].Identity.Target
                            || waitBridge.Identity.MainThreadId != owner._schedulerBridges[index].Identity.MainThreadId)
                            throw new UnsupportedSignatureException("Wait boundary target or thread differs from its actual scheduler channel.");
                    owner._waitBridge = waitBridge;
                    owner._wait = new(waitBridge, inlineLog ?? throw new ArgumentNullException(nameof(inlineLog)));
                    TracePublished(traceLog, "attach.wait.end");
                }
                TracePublished(traceLog, "share.verify.begin");
                publicationOwner?.VerifyReady(); Owners.Add(key, owner); return owner;
            }
            catch { owner.MarkUnknown(); Rejected.Add(key); throw; }
        }
    }
    private NativeEngineRuntimeCoordinator(INativeEnginePublicationLifetime? owner) => _publicationOwner = owner;

    // Controlled transports exercise the same sharing and lifetime code without
    // issuing a publication owner, checking game admission or changing production.
    internal static NativeEngineRuntimeCoordinator ShareControlled(object responsibility,
        IReadOnlyList<INativeEngineNodeContextBridge> bridges, ILogger log,
        INativeEngineInlineContextBridge? inlineBridge = null,
        IReadOnlyList<INativeEngineSchedulerContextBridge>? schedulerBridges = null,
        INativeEngineWaitContextBridge? waitBridge = null)
    {
        ArgumentNullException.ThrowIfNull(responsibility); ArgumentNullException.ThrowIfNull(bridges); ArgumentNullException.ThrowIfNull(log);
        if (bridges.Count != 4 || bridges.Where((bridge, index) => bridge is null || bridge.Identity.Kind != (NativeEngineNodeKind)index).Any())
            throw new ArgumentException("Four ordered exact controlled channels are required.", nameof(bridges));
        if (schedulerBridges is not null && (schedulerBridges.Count != 6
            || schedulerBridges.Where((bridge, index) => bridge is null || bridge.Identity.Kind != (NativeEngineSchedulerKind)index).Any()))
            throw new ArgumentException("Six ordered exact controlled scheduler channels are required.", nameof(schedulerBridges));
        return Share(responsibility, null, kind => new NativeEngineNodeObserverDispatcher(bridges[(int)kind], log), inlineBridge, log, schedulerBridges, waitBridge);
    }

    internal sealed class Subscription : IDisposable
    {
        internal const int MaximumRetainedContexts = 4096;
        private readonly NativeEngineRuntimeCoordinator _owner;
        private readonly object _lifetimeGate = new();
        private readonly NativeEngineNodeObserverDispatcher.Binding?[] _bindings = new NativeEngineNodeObserverDispatcher.Binding?[4];
        private readonly HashSet<object> _contexts = new(ReferenceEqualityComparer.Instance);
        private NativeEngineInlineObserverDispatcher.Binding? _inlineBinding;
        private int _active, _disposed, _bindingsRetired, _starts;
        private bool _untracked;
        internal bool Active => Volatile.Read(ref _active) != 0;
        internal Subscription(NativeEngineRuntimeCoordinator owner) => _owner = owner;
        internal void Set(int index, NativeEngineNodeObserverDispatcher.Binding binding) => _bindings[index] = binding;
        internal NativeEngineNodeObserverDispatcher.Binding StartBinding => _bindings[(int)NativeEngineNodeKind.Start]!;
        internal void SetInline(NativeEngineInlineObserverDispatcher.Binding binding) => _inlineBinding = binding;
        internal void Activate() => Volatile.Write(ref _active, 1);
        internal ExactHookBroker.ManagedBoundaryActivity ReadActivity() => _owner.ReadActivity();
        internal bool Admit(NativeEngineNodeKind kind)
        {
            if (Active) return true;
            if (kind == NativeEngineNodeKind.Start || Volatile.Read(ref _bindingsRetired) != 0) return false;
            if (StartBinding.IsCapturedByNearestOpenStart) return true;
            lock (_lifetimeGate)
                return _contexts.OfType<IStartResponsibility>().Any(context => context.Kind == NativeEngineNodeKind.Start
                    && StartBinding.MatchesNearestCapturedStart(context.Sequence));
        }
        private interface IStartResponsibility
        {
            NativeEngineNodeKind Kind { get; }
            ulong Sequence { get; }
        }
        internal Context<T> Capture<T>(NativeEngineNodeFrame frame, Func<NativeEngineNodeFrame, T?> capture,
            Action<T, NativeEngineNodeFrame, NativeEngineNodeCompletion> complete,
            Action<NativeEngineNodeFrame, string> fault) where T : class, IDisposable
        {
            var context = new Context<T>(this, frame, complete, fault);
            lock (_lifetimeGate)
            {
                if (_contexts.Count == MaximumRetainedContexts)
                    _untracked = true;
                else
                {
                    _contexts.Add(context); context.Registered = true;
                    if (frame.Kind == NativeEngineNodeKind.Start) ++_starts;
                }
            }
            if (!context.Registered)
            { context.ReportFault("Engine subscription context capacity exhausted; physical responsibility remains untracked."); return context; }
            try { context.Value = capture(frame); }
            catch { context.ReportFault("Engine subscription consumer capture failed; its actual physical scope remains retained."); }
            return context;
        }
        internal sealed class Context<T> : IDisposable, IStartResponsibility where T : class, IDisposable
        {
            private readonly Subscription _owner;
            private readonly NativeEngineNodeFrame _frame;
            private readonly Action<T, NativeEngineNodeFrame, NativeEngineNodeCompletion> _complete;
            private readonly Action<NativeEngineNodeFrame, string> _fault;
            private readonly int _thread = Environment.CurrentManagedThreadId;
            internal T? Value;
            internal bool Registered;
            private bool _completed, _disposed;
            NativeEngineNodeKind IStartResponsibility.Kind => _frame.Kind;
            ulong IStartResponsibility.Sequence => _frame.Sequence;
            internal Context(Subscription owner, NativeEngineNodeFrame frame,
                Action<T, NativeEngineNodeFrame, NativeEngineNodeCompletion> complete, Action<NativeEngineNodeFrame, string> fault)
                => (_owner, _frame, _complete, _fault) = (owner, frame, complete, fault);
            internal void ReportFault(string reason)
            {
                _owner._owner.MarkUnknown();
                try { _fault(_frame, reason); } catch { }
            }
            internal void Complete(NativeEngineNodeFrame frame, NativeEngineNodeCompletion completion)
            {
                if (_completed || _disposed || _thread != Environment.CurrentManagedThreadId
                    || !NativeEngineNodeObserverDispatcher.SameFrame(_frame, frame))
                { ReportFault("Engine subscription completion crossed its exact frame, thread or already consumed scope."); return; }
                _completed = true;
                if (Value is not null)
                    try { _complete(Value, frame, completion); }
                    catch { ReportFault("Engine subscription consumer completion failed; Dispose responsibility remains."); }
            }
            public void Dispose()
            {
                if (_disposed || !_completed || _thread != Environment.CurrentManagedThreadId)
                { ReportFault("Engine subscription cleanup has no exact completed physical scope; responsibility remains retained."); return; }
                _disposed = true;
                try { Value?.Dispose(); }
                catch { ReportFault("Engine subscription consumer Dispose failed."); }
                finally
                {
                    if (Registered)
                    {
                        lock (_owner._lifetimeGate)
                        {
                            _owner._contexts.Remove(this);
                            if (_frame.Kind == NativeEngineNodeKind.Start) --_owner._starts;
                        }
                        _owner.TryRetireBindings();
                    }
                }
            }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Volatile.Write(ref _active, 0);
            // The side dispatcher retains the exact typed Start's frozen sink.
            // Withdraw future Starts now, but its original may still produce
            // Execute/Release/Detach physical calls before Complete/Dispose.
            _inlineBinding?.Dispose();
            TryRetireBindings();
        }
        private void TryRetireBindings()
        {
            lock (_lifetimeGate)
            {
                if (Volatile.Read(ref _disposed) == 0 || _untracked || _starts != 0 || _bindingsRetired != 0
                    || _bindings[(int)NativeEngineNodeKind.Start]?.MayHaveCapturedStart == true) return;
                Volatile.Write(ref _bindingsRetired, 1);
            }
            for (var index = _bindings.Length - 1; index >= 0; --index) _bindings[index]?.Dispose();
            _inlineBinding?.DrainRetired();
        }
    }

    internal Subscription Bind<T>(Func<NativeEngineNodeFrame, T?> capture,
        Action<T, NativeEngineNodeFrame, NativeEngineNodeCompletion> complete,
        Action<NativeEngineNodeFrame, string> fault) where T : class, IDisposable
        => BindCore(capture, complete, fault, null);

    internal Subscription BindWithInline<T, TInline>(Func<NativeEngineNodeFrame, T?> capture,
        Action<T, NativeEngineNodeFrame, NativeEngineNodeCompletion> complete,
        Action<NativeEngineNodeFrame, string> fault,
        Func<NativeEngineInlineObserverDispatcher.Observation, TInline?> captureInline,
        Action<TInline, NativeEngineInlineObserverDispatcher.Observation> observeInline,
        Action<NativeEngineInlineFrame, string> inlineFault)
        where T : class, IDisposable where TInline : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(captureInline); ArgumentNullException.ThrowIfNull(observeInline);
        ArgumentNullException.ThrowIfNull(inlineFault);
        RequireInlineObservation();
        return BindCore(capture, complete, fault, subscription => _inline!.Bind(captureInline, observeInline,
            (frame, reason) => { MarkUnknown(); inlineFault(frame, reason); }, subscription.StartBinding));
    }

    internal void RequireInlineObservation()
    {
        if (!InlineAvailable || !ReadActivity().Known)
            throw new UnsupportedSignatureException("Exact native inline completion observation is unavailable or Unknown.");
    }

    internal NativeEngineSchedulerObserverDispatcher RequireSchedulerObservation()
    {
        if (_scheduler is null || !ReadActivity().Known)
            throw new UnsupportedSignatureException("Exact native scheduler observation is unavailable or Unknown.");
        return _scheduler;
    }

    internal NativeEngineWaitObserverDispatcher RequireWaitObservation()
    {
        if (_wait is null || !ReadActivity().Known)
            throw new UnsupportedSignatureException("Exact native wait observation is unavailable or Unknown.");
        return _wait;
    }

    internal IntPtr RequirePublishedWaitImage()
    {
        _ = RequireWaitObservation();
        var imagePointer = RequirePublishedSchedulerImage();
        var image = checked((ulong)imagePointer.ToInt64());
        var identity = _waitBridge!.Identity;
        var targets = new uint[] { 0x78fc50, 0x5a5260, 0x5da510, 0x792240, 0x78f840, 0x78f680, 0x793840,
            0x44d630, 0x447ad0, 0x447c30, 0x447d60, 0x447750, 0x78f5e0 };
        var sites = new uint[] { 0x790069, 0x79008d, 0x790098, 0x7900a0, 0x7938b3, 0x793983,
            0x78f6f5, 0x78f704, 0x78f711, 0x792527, 0x792536, 0x792543,
            0x5a527c, 0x5a5281, 0x5a528f, 0x5a529a, 0x5da63b, 0x5da660 };
        for (var index = 0; index < targets.Length; ++index)
            if (unchecked((ulong)identity.Target((NativeEngineWaitKind)index).ToInt64()) != checked(image + targets[index]))
                throw new UnsupportedSignatureException("Wait channel target differs from the published fixed image.");
        for (var index = 0; index < sites.Length; ++index)
            if (unchecked((ulong)identity.Site((NativeEngineWaitPhase)index).SitePc.ToInt64()) != checked(image + sites[index]))
                throw new UnsupportedSignatureException("Wait phase site differs from the published fixed image.");
        _publicationOwner!.VerifyReady();
        return imagePointer;
    }

    // A controlled transport cannot supply the image base used by the adapter's
    // fixed-game call sites. Production still requires its native publication.
    internal IntPtr RequirePublishedSchedulerImage()
    {
        _ = RequireSchedulerObservation();
        if (_publicationOwner is null || _schedulerBridges is null)
            throw new UnsupportedSignatureException("Fixed scheduler call sites require their actual native publication owner.");
        _publicationOwner.VerifyReady();
        var image = _publicationOwner.TargetModule;
        var rvas = new uint[] { 0x44d630, 0x447ad0, 0x447c30, 0x447d60, 0x447750, 0x78f5e0 };
        for (var index = 0; index < rvas.Length; ++index)
            if (unchecked((ulong)_schedulerBridges[index].Identity.Target.ToInt64()) != checked(image + rvas[index]))
                throw new UnsupportedSignatureException("Scheduler channel target differs from the published fixed image.");
        var drain = _schedulerBridges[(int)NativeEngineSchedulerKind.Drain].Identity;
        if (unchecked((ulong)drain.Begin.SitePc.ToInt64()) != checked(image + 0x447787)
            || unchecked((ulong)drain.End.SitePc.ToInt64()) != checked(image + 0x447866)
            || unchecked((ulong)_dispatchers[(int)NativeEngineNodeKind.Release]!.Identity.Target.ToInt64()) != checked(image + 0x78f680))
            throw new UnsupportedSignatureException("Scheduler row sites or cleanup target differs from the published fixed image.");
        _publicationOwner.VerifyReady();
        return new IntPtr(checked((long)image));
    }

    private Subscription BindCore<T>(Func<NativeEngineNodeFrame, T?> capture,
        Action<T, NativeEngineNodeFrame, NativeEngineNodeCompletion> complete,
        Action<NativeEngineNodeFrame, string> fault,
        Func<Subscription, NativeEngineInlineObserverDispatcher.Binding>? bindInline) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(complete); ArgumentNullException.ThrowIfNull(fault);
        lock (_gate)
        {
            if (Unknown) throw new UnsupportedSignatureException("Engine runtime observation is permanently Unknown.");
            var subscription = new Subscription(this);
            try
            {
                _publicationOwner?.VerifyReady();
                for (var index = 0; index < 4; ++index)
                {
                    var kind = (NativeEngineNodeKind)index;
                    subscription.Set(index, _dispatchers[index]!.Bind(
                        frame => subscription.Capture(frame, capture, complete, fault),
                        (context, frame, completion) => context.Complete(frame, completion),
                        (frame, reason) => { MarkUnknown(); fault(frame, reason); }, () => subscription.Admit(kind)));
                }
                if (bindInline is not null) subscription.SetInline(bindInline(subscription));
                if (!ReadActivity().Known) throw new UnsupportedSignatureException("Engine native activity changed during subscription.");
                _publicationOwner?.VerifyReady(); subscription.Activate(); return subscription;
            }
            catch { subscription.Dispose(); MarkUnknown(); throw; }
        }
    }

    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        try
        {
            return _publicationOwner is NativeEngineTransportFirstInstall publication
                ? publication.ReadVerifiedActivity(this) : ReadActivityCore();
        }
        catch { MarkUnknown(); return new(0, false); }
    }

    // Called only by the concrete V5 owner's synchronous activity reader. It
    // performs actual dispatcher/GetInfo reads, never a caller-supplied action.
    internal ExactHookBroker.ManagedBoundaryActivity ReadPublishedActivityCore(NativeEngineTransportFirstInstall actualOwner)
    {
        if (!ReferenceEquals(_publicationOwner, actualOwner) || Unknown)
        {
            MarkUnknown();
            throw new UnsupportedSignatureException("V5 activity coordinator does not own this exact retained publication.");
        }
        actualOwner.RequireActivityReader(this);
        return ReadActivityCore();
    }

    private ExactHookBroker.ManagedBoundaryActivity ReadActivityCore()
    {
        long count = 0; var known = !Unknown;
        try
        {
            _publicationOwner?.VerifyReady();
            foreach (var dispatcher in _dispatchers)
            {
                if (dispatcher is null) { known = false; continue; }
                var activity = dispatcher.ReadActivity();
                if (!activity.Known || activity.InFlight < 0 || count > long.MaxValue - activity.InFlight)
                    known = false;
                else count += activity.InFlight;
            }
            if (_inline is not null)
            {
                var activity = _inline.ReadActivity();
                if (!activity.Known || activity.InFlight < 0 || count > long.MaxValue - activity.InFlight)
                    known = false;
                else count += activity.InFlight;
            }
            if (_scheduler is not null)
            {
                var activity = _scheduler.ReadActivity();
                if (!activity.Known || activity.InFlight < 0 || count > long.MaxValue - activity.InFlight)
                    known = false;
                else count += activity.InFlight;
            }
            if (_wait is not null)
            {
                var activity = _wait.ReadActivity();
                if (!activity.Known || activity.InFlight < 0 || count > long.MaxValue - activity.InFlight)
                    known = false;
                else count += activity.InFlight;
            }
        }
        catch { known = false; }
        if (!known) MarkUnknown();
        return new(count, known && !Unknown);
    }
    private void MarkUnknown() { Interlocked.Exchange(ref _unknown, 1); _publicationOwner?.MarkUnknown(); }
}
