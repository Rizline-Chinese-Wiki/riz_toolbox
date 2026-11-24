using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr LocalInitializationReference(IntPtr controller, IntPtr information, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void LocalProgressPlay(IntPtr progress, [MarshalAs(UnmanagedType.I1)] bool play, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void StaticTimelineTooltip(IntPtr text, float offset, IntPtr method);
    internal sealed record LocalInitializationIdentity
    {
        internal string SessionId { get; }
        internal long RunGeneration { get; }
        private long _controller;
        internal long ControllerIdentity => Volatile.Read(ref _controller);
        internal LocalInitializationIdentity(string sessionId, long runGeneration, long controllerIdentity)
            => (SessionId, RunGeneration, _controller) = (sessionId, runGeneration, controllerIdentity);
        // A reservation may prepare storage before a controller exists. Complete it
        // once, before publishing the token; published identities never change.
        internal void InitializeReservedController(long controller)
        {
            if (controller == 0 || Interlocked.CompareExchange(ref _controller, controller, 0) != 0)
                throw new InvalidOperationException("An initialization identity can be completed only once before publication.");
        }
    }
    internal sealed record LocalInitializationArguments(LocalInitializationIdentity Identity, IntPtr StartReceiver, IntPtr StartMethod,
        IntPtr Controller, IntPtr Information, IntPtr GetterMethod);
    internal sealed record LocalInitializationCallbacks(Func<LocalInitializationArguments, bool> Before,
        Func<LocalInitializationArguments, IntPtr, bool> After, Action<LocalInitializationArguments?, string> Fault);
    internal sealed record LocalInitializationBoundaryState(long InFlight, long UnknownInFlight, bool Known);
    internal sealed record LocalInitializationDiagnostics(long StartCalls, long GetterCalls, long PlayCalls, long ModeCalls,
        long TooltipCalls, long ProvisionalClaims, long GetterOriginals, long AbortedGetters, long SuppressedMode,
        long SuppressedTooltip, long UnknownObserved, long InFlight, long UnknownInFlight);
    private static readonly Dictionary<IntPtr, LocalProgressPlayReservation> LocalProgressPlayReservations = new();
    internal sealed partial class Entry
    {
        internal bool RetainedStockProgressPlay;
    }
    internal sealed class LocalProgressPlayConflictException : InvalidOperationException
    {
        internal LocalProgressPlayConflictException(string message) : base(message) { }
    }
    internal sealed class LocalProgressPlayReservation : IDisposable
    {
        private readonly ExactHookBroker _broker;
        private readonly VerifiedHookTarget _play;
        private bool _attempted, _disposed, _sharedBindingCompleted, _dormantPlayAttempted;
        internal bool SharesOfficialPlay { get; private set; }
        internal LocalProgressPlayReservation(ExactHookBroker broker, VerifiedHookTarget play, bool sharesOfficialPlay)
            => (_broker, _play, SharesOfficialPlay) = (broker, play, sharesOfficialPlay);

        internal void PublishDormantSharedPlay(PlaybackSourceGroupTargets targets, string sourceHelper, string sourceHelperHash,
            NativeFirstInstall.PlaybackMembership? foundation)
        {
            _broker._main.VerifyAccess();
            lock (Gate)
            {
                if (!_attempted && !_dormantPlayAttempted && IsCurrent(_broker, _play) && !SharesOfficialPlay)
                {
                    _dormantPlayAttempted = true;
                    _broker.InstallDormantLocalPlay(_play, targets, sourceHelper, sourceHelperHash, foundation);
                    SharesOfficialPlay = true;
                }
                else throw new InvalidOperationException("Dormant LOCAL Play requires the current unused physical reservation.");
            }
        }

        internal void MarkNativeInstallationAttempt(ExactHookBroker broker)
        {
            _broker._main.VerifyAccess();
            lock (Gate)
            {
                if (_attempted || !IsCurrent(broker, _play))
                    throw new InvalidOperationException("LOCAL ProgressControl.Play requires its current unused installation reservation.");
                _attempted = true;
            }
        }
        internal bool AuthorizesBinding(ExactHookBroker broker, VerifiedHookTarget play)
            => _attempted && IsCurrent(broker, play);
        internal void CompleteSharedBinding(ExactHookBroker broker, VerifiedHookTarget play)
        {
            if (!SharesOfficialPlay || _sharedBindingCompleted || !AuthorizesBinding(broker, play))
                throw new InvalidOperationException("The shared LOCAL Play initialization no longer owns its exact reservation.");
            _sharedBindingCompleted = true;
        }
        private bool IsCurrent(ExactHookBroker broker, VerifiedHookTarget play)
            => !_disposed && ReferenceEquals(_broker, broker) && _play == play
                && LocalProgressPlayReservations.TryGetValue(play.Address, out var saved) && ReferenceEquals(this, saved);

        public void Dispose()
        {
            _broker._main.VerifyAccess();
            lock (Gate)
            {
                if (_disposed) return;
                if (!IsCurrent(_broker, _play))
                    throw new InvalidOperationException("LOCAL ProgressControl.Play reservation identity changed.");
                _disposed = true;
                // An attempted reservation survives a partial LOCAL startup.
                // Shared Play has no LOCAL physical entry of its own, so it
                // releases this reservation only after the full policy bound.
                if (!_attempted && !_dormantPlayAttempted || _sharedBindingCompleted
                    || Entries.TryGetValue(_play.Address, out var entry) && !entry.RetainedStockProgressPlay)
                    LocalProgressPlayReservations.Remove(_play.Address);
            }
        }
    }
    internal LocalProgressPlayReservation ReserveLocalProgressPlay(VerifiedHookTarget play)
    {
        _main.VerifyAccess();
        if (play.Abi != ExactHookAbi.LocalProgressPlayBooleanVoid || play.Address == IntPtr.Zero
            || play.MethodInfo == IntPtr.Zero || play.DeclaringClass == IntPtr.Zero || play.ReturnClass != IntPtr.Zero)
            throw new UnsupportedSignatureException("LOCAL ProgressControl.Play requires its exact verified target.");
        lock (Gate)
        {
            if (LocalProgressPlayReservations.ContainsKey(play.Address))
                throw new LocalProgressPlayConflictException("A prior LOCAL installation retains its ProgressControl.Play reservation.");
            var sharesOfficialPlay = Entries.TryGetValue(play.Address, out var entry) && entry.RetainedStockProgressPlay;
            if (sharesOfficialPlay)
            {
                var owner = FindOfficialProgressPlay(play, allowDormant: true)
                    ?? throw new LocalProgressPlayConflictException("Official playback retains ProgressControl.Play for this process. LOCAL playback could not acquire its progress observers.");
                // Publish the fixed native dispatcher before any LOCAL Update or
                // Timeline write. It has no subscriber until initialization binds.
                owner.PrepareLocalPlayObserver(dormant: !owner.Group.IsActive);
            }
            var reservation = new LocalProgressPlayReservation(this, play, sharesOfficialPlay);
            LocalProgressPlayReservations.Add(play.Address, reservation);
            return reservation;
        }
    }
    internal sealed class LocalInitializationIsolationTargets
    {
        internal VerifiedHookTarget Start { get; } internal VerifiedHookTarget Getter { get; }
        internal VerifiedHookTarget Play { get; } internal VerifiedHookTarget Mode { get; } internal VerifiedHookTarget Tooltip { get; }
        private LocalInitializationIsolationTargets(VerifiedHookTarget start, VerifiedHookTarget getter, VerifiedHookTarget play,
            VerifiedHookTarget mode, VerifiedHookTarget tooltip) => (Start, Getter, Play, Mode, Tooltip) = (start, getter, play, mode, tooltip);
        internal static LocalInitializationIsolationTargets Resolve(NativeRuntime api, IntPtr module, string imageSha256)
        {
            if (!string.Equals(imageSha256, "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79", StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("LOCAL initialization requires its pinned GameAssembly image.");
            ArgumentNullException.ThrowIfNull(api); if (module == IntPtr.Zero) throw new UnsupportedSignatureException("No verified GameAssembly module.");
            var start = api.ResolveExactHook("local.initialization.start", module, api.Class("Assembly-CSharp.dll", "InLevel", "LevelControl"),
                "Start", Array.Empty<string>(), 0x3EA580, ExactHookAbi.InstanceVoid);
            var getter = api.ResolveLocalInitializationHook(module, true); var play = api.ResolveLocalInitializationHook(module, false);
            var timeline = api.ResolveLocalTimelineClass();
            return new(start, getter, play, api.ResolveLocalTimelineHook(module, timeline, ExactHookAbi.StaticTimelineModeVoid),
                api.ResolveLocalTimelineHook(module, timeline, ExactHookAbi.StaticTimelineTooltipVoid));
        }
    }
    internal sealed class LocalInitializationPolicy
    {
        internal LocalInitializationIsolationTargets Targets = null!; internal LocalProgressPolicy Progress = null!;
        internal Func<IntPtr, LocalInitializationIdentity?> Classify = null!; internal LocalInitializationCallbacks Callbacks = null!;
        internal IMainThreadDispatcher Main = null!; internal readonly object Sync = new(); internal Entry[] Entries = Array.Empty<Entry>();
        internal Action? FrameCreationProbe;
        internal ILocalProgressPlayObservation? SharedPlay;
        internal volatile bool Active; internal bool InstallationUnknown;
        internal long StartCalls, GetterCalls, PlayCalls, ModeCalls, TooltipCalls, Claims, GetterOriginals, Aborted, SuppressedMode, SuppressedTooltip, UnknownObserved, UnknownInFlight;
    }
    private enum InitializationScope { Start, Getter, Play }
    private sealed class LocalInitializationFrame
    {
        internal LocalInitializationPolicy Policy = null!; internal LocalInitializationFrame? Parent;
        internal InitializationScope Scope; internal IntPtr Receiver, Method;
        internal LocalInitializationIdentity? Identity; internal bool Claimed, GetterSeen, Unknown; internal long Sequence;
        internal nuint NativeToken; internal Entry? NativeEntry;
        internal LocalInitializationArguments? Arguments;
        internal Sink[]? NativeSinks;
        internal List<(Sink Sink, IDisposable Context)>? NativeRetained;
        internal bool OriginalAllowed;
        internal bool NativeOriginalWindow;
        internal LocalProgressPlayBoundary? SharedPlay;
    }
    [ThreadStatic] private static LocalInitializationFrame? CurrentInitialization;
    [ThreadStatic] private static int InitializationDepth;
    [ThreadStatic] private static long LocalScopeSequence;
    // This is only an additional provenance check. Callers still need the actual
    // controller, physical return caller and rooted IEnumerator identities.
    internal static bool HasCurrentNativeGetter(string sessionId, long generation, long controller, long information)
    {
        var frame = CurrentInitialization;
        return frame is { Scope: InitializationScope.Getter, Claimed: true, NativeOriginalWindow: true, OriginalAllowed: true,
                Identity: { } identity, Arguments: { } arguments, NativeEntry.NativeInitializationBridge.Kind: NativeInitializationKind.Getter }
            && frame.NativeToken != 0 && frame.Policy.Active && frame.Policy.Progress.Active && frame.Policy.Main.IsMainThread
            && !frame.Policy.InstallationUnknown && Interlocked.Read(ref frame.Policy.UnknownObserved) == 0
            && identity.SessionId == sessionId && identity.RunGeneration == generation && identity.ControllerIdentity == controller
            && frame.Receiver.ToInt64() == controller && arguments.Controller.ToInt64() == controller && arguments.Information.ToInt64() == information
            && (CurrentLocalProgress is null || CurrentLocalProgress.Sequence < frame.Sequence);
    }
    private static long NextLocalScopeSequence() => ++LocalScopeSequence;
    internal sealed class LocalInitializationIsolationCreationException : InvalidOperationException
    {
        internal LocalInitializationIsolationBinding Responsibility { get; }
        internal LocalInitializationIsolationCreationException(LocalInitializationIsolationBinding owner, Exception error)
            : base("Sealed initialization installation is unknown; process native responsibility is retained.", error) => Responsibility = owner;
    }
    internal sealed class LocalInitializationIsolationBinding : IDisposable
    {
        private readonly LocalInitializationPolicy _policy;
        private LocalInitializationIsolationBinding(LocalInitializationPolicy policy) => _policy = policy;
        internal bool IsActive => _policy.Active && _policy.Progress.Active && !_policy.InstallationUnknown;
        // Tests can inject allocation/entry failure before a new TLS frame exists;
        // this does not alter native classification or grant any scope authority.
        internal void SetFrameCreationProbe(Action? probe) { _policy.Main.VerifyAccess(); Volatile.Write(ref _policy.FrameCreationProbe, probe); }
        internal LocalInitializationBoundaryState Read(LocalInitializationIdentity? identity = null)
        {
            // Full owner-wide counts deliberately include ordinary and retained old-policy
            // entries. No identity query may omit a still-unclassified native Start.
            long count = 0; var known = true;
            foreach (var entry in _policy.Entries)
            {
                var managed = entry.Target.Abi == ExactHookAbi.InstanceVoid ? Interlocked.Read(ref entry.VoidInFlight) : Interlocked.Read(ref entry.InitializationInFlight);
                if (entry.NativeInitializationBridge is { } bridge)
                {
                    var native = ReadInitializationActivity(entry, bridge); known &= native.Known;
                    // Native counts include the intervals before reverse P/Invoke
                    // Enter and after Exit, including failed/skipped callbacks.
                    managed = Math.Max(managed, native.InFlight);
                }
                count = checked(count + managed);
            }
            if (_policy.SharedPlay is { } shared)
            {
                var activity = shared.ReadActivity();
                count = checked(count + activity.InFlight); known &= activity.Known;
            }
            lock (_policy.Sync) return new(count, _policy.UnknownInFlight, known && IsActive && !_policy.InstallationUnknown && _policy.UnknownObserved == 0);
        }
        internal LocalInitializationDiagnostics Describe()
        {
            var state = Read(); lock (_policy.Sync) return new(_policy.StartCalls, _policy.GetterCalls, _policy.PlayCalls, _policy.ModeCalls,
                _policy.TooltipCalls, _policy.Claims, _policy.GetterOriginals, _policy.Aborted, _policy.SuppressedMode, _policy.SuppressedTooltip,
                _policy.UnknownObserved, state.InFlight, state.UnknownInFlight);
        }
        public void Dispose()
        {
            _policy.Main.VerifyAccess();
            _policy.Active = false;
            _policy.SharedPlay?.Dispose();
        }
        internal static LocalInitializationIsolationBinding Create(LocalInitializationPolicy policy) => new(policy);
    }
    internal LocalInitializationIsolationBinding BindLocalInitializationIsolation(LocalProgressIsolationBinding progress,
        LocalInitializationIsolationTargets targets, Func<IntPtr, LocalInitializationIdentity?> classify, LocalInitializationCallbacks callbacks,
        Func<NativeInitializationKind, INativeInitializationContextBridge>? createNativeBridge = null,
        LocalProgressPlayReservation? playReservation = null)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(progress); ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(classify); ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(callbacks.Before); ArgumentNullException.ThrowIfNull(callbacks.After); ArgumentNullException.ThrowIfNull(callbacks.Fault);
        // Only non-native controlled engines may use the managed fixture path.
        // Production Dobby must call both originals entirely on the native stack.
        var defaultNativeTransport = _engine is DobbyDetourEngine && createNativeBridge is null;
        var nativeTransport = _engine is DobbyDetourEngine || createNativeBridge is not null;
        if (nativeTransport && _engine is not IPreparedNativeDetourEngine) throw new UnsupportedSignatureException("Typed initialization requires Prepare/Configure/Commit publication.");
        createNativeBridge ??= kind => new NativeInitializationContextBridge(Path.Combine(Path.GetDirectoryName(typeof(ExactHookBroker).Assembly.Location)!, "RizToolbox.JudgeContext.dll"), kind, _log);
        if (!progress.IsActive) throw new InvalidOperationException("The one existing Progress/Timeline authority must be active.");
        var authority = progress.Policy;
        if (targets.Mode != authority.Targets.Mode) throw new UnsupportedSignatureException("Initialization must share the exact existing Mode authority.");
        var all = new[] { targets.Start, targets.Getter, targets.Play, targets.Mode, targets.Tooltip };
        var abis = new[] { ExactHookAbi.InstanceVoid, ExactHookAbi.LocalInitializationReference1, ExactHookAbi.LocalProgressPlayBooleanVoid,
            ExactHookAbi.StaticTimelineModeVoid, ExactHookAbi.StaticTimelineTooltipVoid };
        for (var index = 0; index < all.Length; index++) if (all[index].Address == IntPtr.Zero || all[index].MethodInfo == IntPtr.Zero
            || all[index].Abi != abis[index]) throw new UnsupportedSignatureException("Sealed initialization target ABI/identity differs.");
        if (targets.Getter.ReturnClass == IntPtr.Zero) throw new UnsupportedSignatureException("The initialization getter requires its verified reference return class.");
        if (all.Select(target => target.Address).Distinct().Count() != all.Length
            || all.Where(target => target != targets.Mode).Any(target => new[] { authority.Targets.Progress, authority.Targets.Instantaneous, authority.Targets.EndRange }
                .Any(other => other.Address == target.Address))) throw new UnsupportedSignatureException("Initialization targets must have distinct sealed authority addresses.");
        var policy = new LocalInitializationPolicy { Targets = targets, Progress = authority, Classify = classify, Callbacks = callbacks, Main = _main };
        var owner = LocalInitializationIsolationBinding.Create(policy);
        lock (Gate)
        {
            if (playReservation is not null && !playReservation.AuthorizesBinding(this, targets.Play)
                || playReservation is null && LocalProgressPlayReservations.ContainsKey(targets.Play.Address))
                throw new InvalidOperationException("The sealed LOCAL Play binding does not own the current progress reservation.");
            var sharesOfficialPlay = playReservation?.SharesOfficialPlay == true;
            foreach (var target in all) if (Entries.TryGetValue(target.Address, out var existing))
            {
                if (sharesOfficialPlay && target == targets.Play)
                {
                    if (FindOfficialProgressPlay(target, allowDormant: true) is null)
                        throw new LocalProgressPlayConflictException("The official ProgressControl.Play observer lost its exact original.");
                    continue;
                }
                RequireProgressTargetIdentity(existing, target);
                if (existing.Engine != _engine) throw new UnsupportedSignatureException("Initialization cannot share another native transport.");
                if (nativeTransport && (target == targets.Start || target == targets.Getter) && existing.NativeInitializationBridge is null)
                    throw new InvalidOperationException("Typed native initialization must own the first patch; an installed managed entry cannot be upgraded.");
                if (!nativeTransport && existing.NativeInitializationBridge is not null)
                    throw new InvalidOperationException("A native initialization entry cannot be replaced by a managed fixture transport.");
                if (existing.NativeInstallFailure is not null) throw new InvalidOperationException("An unconfirmed trampoline still owns this initialization address.");
                if (existing.LocalInitializationPolicy is { } previous && (previous.Active || previous.InstallationUnknown
                    || previous.UnknownObserved != 0 || previous.Entries.Any(InitializationEntryBusy)))
                    throw new InvalidOperationException("The sealed initialization owner is still active, in-flight or unknown.");
                if (existing.Sinks.Any(sink => sink.Active && sink.LocalResultPolicy is not null))
                    throw new InvalidOperationException("A skip policy cannot own the initialization Start entry.");
            }
            try
            {
                // Establish that both previously unbound typed sources still
                // have their exact preimages before either can publish a slot.
                var startBridge = PrepareDefaultBridge(targets.Start, NativeInitializationKind.Start);
                var getterBridge = PrepareDefaultBridge(targets.Getter, NativeInitializationKind.Getter);
                if (sharesOfficialPlay)
                {
                    var boundary = new LocalProgressPlayBoundary(policy);
                    var sharedOwner = FindOfficialProgressPlay(targets.Play, allowDormant: true)
                        ?? throw new LocalProgressPlayConflictException("The retained Play entry no longer hosts its LOCAL observation.");
                    policy.SharedPlay = sharedOwner.BindLocalPlayObserver(boundary.Enter, boundary.Exit, boundary.Fault);
                }
                var entries = new List<Entry>(all.Length);
                // Install pass-through Tooltip/getter/Play first and Start last. Mode is
                // already installed once by Progress; there is never a second Mode patch.
                foreach (var target in new[] { targets.Mode, targets.Tooltip, targets.Getter, targets.Play, targets.Start })
                {
                    if (sharesOfficialPlay && target == targets.Play) continue;
                    if (!Entries.TryGetValue(target.Address, out var entry))
                    {
                        entry = new() { Target = target, Engine = _engine, LocalInitializationPolicy = policy };
                        if (nativeTransport && (target == targets.Start || target == targets.Getter))
                        {
                            var kind = target == targets.Start ? NativeInitializationKind.Start : NativeInitializationKind.Getter;
                            var preflightBridge = kind == NativeInitializationKind.Start ? startBridge : getterBridge;
                            entry.NativeInitializationBridge = preflightBridge ?? createNativeBridge(kind)
                                ?? throw new InvalidOperationException("Typed native initialization bridge is null.");
                            if (entry.NativeInitializationBridge.Kind != kind || entry.NativeInitializationBridge.EntryAddress == IntPtr.Zero)
                                throw new UnsupportedSignatureException("The typed native initialization bridge kind/entry differs.");
                            Entries.Add(target.Address, entry); entries.Add(entry); policy.Entries = entries.ToArray();
                            try
                            {
                                var prepared = (IPreparedNativeDetourEngine)_engine;
                                var code = prepared is DobbyDetourEngine engine
                                    && entry.NativeInitializationBridge is NativeInitializationContextBridge { SupportsPublicationReadback: true } bridge
                                    ? new NativeInitializationCodeIdentity(entry, engine, bridge, _main) : null;
                                var original = prepared.Prepare(target, entry.NativeInitializationBridge.EntryAddress);
                                if (original == IntPtr.Zero) throw new InvalidOperationException("Typed native initialization preparation returned a null original.");
                                code?.Prepared(original);
                                entry.NativeInitializationBridge.Configure(original,
                                    (IntPtr self, IntPtr info, IntPtr method, out nuint token) => EnterNativeInitialization(entry, self, info, method, out token),
                                    (self, info, method, returned, token, flags) => ExitNativeInitialization(entry, self, info, method, returned, token, flags),
                                    failure => InitializationFault(entry.LocalInitializationPolicy!, null, failure));
                                code?.VerifyBeforeCommit();
                                prepared.Commit(target);
                                code?.Installed();
                            }
                            catch (Exception error) { entry.NativeInstallFailure = error.GetType().Name; throw; }
                            continue;
                        }
                        entry.Replacement = target.Abi switch {
                            ExactHookAbi.InstanceVoid => new InstanceVoid((self, method) => InvokeVoid(entry, self, method)),
                            ExactHookAbi.LocalInitializationReference1 => new LocalInitializationReference((self, info, method) => InvokeInitializationGetter(entry, self, info, method)),
                            ExactHookAbi.LocalProgressPlayBooleanVoid => new LocalProgressPlay((self, play, method) => InvokeInitializationPlay(entry, self, play, method)),
                            ExactHookAbi.StaticTimelineTooltipVoid => new StaticTimelineTooltip((text, offset, method) => InvokeInitializationTooltip(entry, text, offset, method)),
                            _ => throw new UnsupportedSignatureException("Unexpected initialization transport ABI.") };
                        Entries.Add(target.Address, entry);
                        entries.Add(entry); policy.Entries = entries.ToArray();
                        InstallLocalClrEntry(entry, includeReferenceIdentity: target == targets.Tooltip);
                    }
                    entry.LocalInitializationPolicy = policy;
                    if (!entries.Contains(entry)) { entries.Add(entry); policy.Entries = entries.ToArray(); }
                }
                if (sharesOfficialPlay) playReservation!.CompleteSharedBinding(this, targets.Play);
                policy.Active = true;
            }
            catch (Exception error)
            {
                policy.InstallationUnknown = true; policy.Active = false;
                try { policy.SharedPlay?.Dispose(); }
                catch (Exception cleanup) { InitializationFault(policy, null, "Shared Play cleanup failed: " + cleanup.GetType().Name); }
                InitializationFault(policy, null, "Partial native initialization installation is unconfirmed.");
                throw new LocalInitializationIsolationCreationException(owner, error);
            }
        }
        return owner;

        INativeInitializationContextBridge? PrepareDefaultBridge(VerifiedHookTarget target, NativeInitializationKind kind)
        {
            if (!defaultNativeTransport || Entries.ContainsKey(target.Address)) return null;
            var bridge = createNativeBridge(kind);
            if (bridge is NativeInitializationContextBridge { SupportsPublicationReadback: true })
                NativeInitializationCodeIdentity.Preflight(target, kind);
            return bridge;
        }
    }
    private static bool InitializationEntryBusy(Entry entry)
    {
        if (Interlocked.Read(ref entry.InitializationInFlight) != 0 || Interlocked.Read(ref entry.VoidInFlight) != 0) return true;
        if (entry.NativeInitializationBridge is not { } bridge) return false;
        var activity = ReadInitializationActivity(entry, bridge); return !activity.Known || activity.InFlight != 0;
    }
    private static NativeInitializationActivity ReadInitializationActivity(Entry entry, INativeInitializationContextBridge bridge)
    {
        try
        {
            var activity = bridge.ReadActivity();
            if (activity.InFlight < 0) throw new InvalidOperationException("Native initialization reported negative activity.");
            if (!activity.Known && entry.LocalInitializationPolicy is { } owner)
                InitializationFault(owner, null, "Native initialization activity is unknown; responsibility is retained.");
            return activity;
        }
        catch (Exception error)
        {
            if (entry.LocalInitializationPolicy is { } owner) InitializationFault(owner, null, "Native initialization activity failed: " + error.GetType().Name);
            return new(0, false);
        }
    }
    private static void InitializationFault(LocalInitializationPolicy policy, LocalInitializationArguments? args, string failure)
    {
        lock (policy.Sync) ++policy.UnknownObserved;
        try { policy.Callbacks.Fault(args, failure); } catch { }
    }
    private static bool ValidInitializationIdentity(LocalInitializationIdentity identity, IntPtr controller)
        => !string.IsNullOrWhiteSpace(identity.SessionId) && identity.RunGeneration > 0 && identity.ControllerIdentity != 0 && identity.ControllerIdentity == controller.ToInt64();
    private static LocalInitializationFrame CreateInitializationFrame(LocalInitializationPolicy policy, InitializationScope scope, IntPtr receiver, IntPtr method)
    {
        Volatile.Read(ref policy.FrameCreationProbe)?.Invoke();
        return new() { Policy = policy, Parent = CurrentInitialization, Scope = scope, Receiver = receiver, Method = method,
            Sequence = NextLocalScopeSequence() };
    }
    private static LocalInitializationFrame? EnterInitializationStart(Entry entry, IntPtr receiver, IntPtr method, out bool skip)
    {
        skip = false; var policy = entry.LocalInitializationPolicy;
        if (policy is null || entry.Target.Address != policy.Targets.Start.Address) return null;
        lock (policy.Sync) ++policy.StartCalls;
        if (InitializationDepth >= 32)
        {
            InitializationFault(policy, null, "The bounded initialization frame stack overflowed; Start original is aborted."); skip = true; return null;
        }
        LocalInitializationFrame frame;
        try { frame = CreateInitializationFrame(policy, InitializationScope.Start, receiver, method); }
        catch (Exception error)
        { InitializationFault(policy, null, "Start frame creation failed: " + error.GetType().Name); skip = true; return null; }
        frame.Unknown = true;
        // A direct same-receiver recursive Start may retain the already claimed token;
        // an ordinary or different-receiver child always starts with no permission.
        if (frame.Parent is { Scope: InitializationScope.Start, Claimed: true } parent && parent.Policy == policy && parent.Receiver == receiver)
        { frame.Claimed = true; frame.Identity = parent.Identity; frame.Unknown = false; }
        CurrentInitialization = frame; ++InitializationDepth; if (frame.Unknown) lock (policy.Sync) ++policy.UnknownInFlight;
        return frame;
    }
    private static void ExitInitializationFrame(LocalInitializationFrame? frame)
    {
        if (frame is null) return;
        if (frame.Unknown) lock (frame.Policy.Sync) --frame.Policy.UnknownInFlight;
        CurrentInitialization = frame.Parent; --InitializationDepth;
    }
    private static uint EnterNativeInitialization(Entry entry, IntPtr self, IntPtr information, IntPtr method, out nuint token)
    {
        var start = entry.NativeInitializationBridge!.Kind == NativeInitializationKind.Start;
        if (start) Interlocked.Increment(ref entry.VoidInFlight); else Interlocked.Increment(ref entry.InitializationInFlight);
        Interlocked.Increment(ref entry.NativeCalls); token = 0;
        var policy = entry.LocalInitializationPolicy!; LocalInitializationArguments? args = null;
        try
        {
            if (entry.NativeInstallFailure is not null) throw new InvalidOperationException("Native initialization publication is unknown.");
            LocalInitializationFrame? frame;
            if (start)
            {
                if (information != IntPtr.Zero) throw new InvalidOperationException("Start native transport supplied an unexpected information reference.");
                frame = EnterInitializationStart(entry, self, method, out var skip);
                if (skip || frame is null) return 0;
            }
            else
            {
                lock (policy.Sync) ++policy.GetterCalls;
                LocalInitializationIdentity? identity = null;
                if (policy.Active) identity = policy.Classify(self);
                else if (CurrentInitialization is { Claimed: true, Identity: { } retained } current && current.Policy == policy
                    && retained.ControllerIdentity == self.ToInt64()) identity = retained;
                if (identity is not null)
                {
                    if (!ValidInitializationIdentity(identity, self)) throw new InvalidOperationException("Controller classifier returned an invalid exact token.");
                    var parent = CurrentInitialization;
                    if (parent is null || parent.Policy != policy || parent.Scope != InitializationScope.Start)
                        throw new InvalidOperationException("An exact LOCAL getter has no direct Start parent.");
                    parent.Claimed = true; lock (policy.Sync) ++policy.Claims;
                    var previous = parent.Identity; parent.Identity ??= identity;
                    args = new(identity, parent.Receiver, parent.Method, self, information, method);
                    if (parent.GetterSeen || previous is not null && previous != identity) throw new InvalidOperationException("Start cannot initialize a second or different LOCAL run.");
                    parent.GetterSeen = true;
                    if (parent.Unknown) { parent.Unknown = false; lock (policy.Sync) --policy.UnknownInFlight; }
                    if (!policy.Active || !policy.Progress.Active || !policy.Main.IsMainThread
                        || method != IntPtr.Zero && method != policy.Targets.Getter.MethodInfo
                        || parent.Method != IntPtr.Zero && parent.Method != policy.Targets.Start.MethodInfo)
                        throw new InvalidOperationException("LOCAL getter thread, identity or logical initialization authority is unavailable.");
                }
                if (InitializationDepth >= 32) throw new InvalidOperationException("The bounded getter scope overflowed.");
                frame = CreateInitializationFrame(policy, InitializationScope.Getter, self, method);
                frame.Claimed = identity is not null; frame.Identity = identity; frame.Arguments = args;
                CurrentInitialization = frame; ++InitializationDepth;
            }
            frame.NativeEntry = entry;
            frame.NativeToken = unchecked((nuint)Interlocked.Increment(ref entry.Invocation)); token = frame.NativeToken;
            if (token == 0) throw new InvalidOperationException("Native initialization invocation identity exhausted.");
            if (start)
            {
                // The around frame precedes all ordinary observers, including
                // worker and nested calls. Their own delivery rules are unchanged.
                frame.NativeSinks = Before(entry, self, (long)token, 0, 0);
                frame.NativeRetained = RetainObservations(frame.NativeSinks, self);
            }
            else if (args is not null && !policy.Callbacks.Before(args)) throw new InvalidOperationException("LOCAL initialization validation rejected original entry.");
            frame.OriginalAllowed = true; frame.NativeOriginalWindow = true; Interlocked.Increment(ref entry.OriginalCalls);
            if (!start && args is not null) lock (policy.Sync) ++policy.GetterOriginals;
            return 1;
        }
        catch (Exception error)
        {
            if (!start) lock (policy.Sync) ++policy.Aborted;
            InitializationFault(policy, args, "Native initialization entry aborted without fallback: " + error.GetType().Name); return 0;
        }
    }
    private static IntPtr ExitNativeInitialization(Entry entry, IntPtr self, IntPtr information, IntPtr method, IntPtr returned,
        nuint token, NativeInitializationExitFlags flags)
    {
        var start = entry.NativeInitializationBridge!.Kind == NativeInitializationKind.Start;
        var policy = entry.LocalInitializationPolicy!; LocalInitializationFrame? frame = null;
        try
        {
            if (token == 0) return IntPtr.Zero; // Failed Enter owns no frame; native finally still pairs activity.
            frame = CurrentInitialization;
            if (frame is null || frame.NativeEntry != entry || frame.NativeToken != token || frame.Receiver != self || frame.Method != method)
            { frame = null; InitializationFault(policy, null, "Native initialization exit does not match its exact TLS invocation."); return IntPtr.Zero; }
            policy = frame.Policy; // An admitted invocation retains its original logical owner through detach.
            frame.NativeOriginalWindow = false; // Before all successful After observers and validation.
            var completed = frame.OriginalAllowed && (flags & NativeInitializationExitFlags.Returned) != 0
                && (flags & (NativeInitializationExitFlags.Abnormal | NativeInitializationExitFlags.EnterFaulted | NativeInitializationExitFlags.Skipped)) == 0;
            if (start)
            {
                if ((flags & NativeInitializationExitFlags.Abnormal) != 0 && frame.Claimed)
                    InitializationFault(policy, null, "Claimed native Start original unwound abnormally.");
                if (completed && frame.NativeRetained is not null)
                    foreach (var item in frame.NativeRetained)
                        if (item.Sink.Active)
                            try { item.Sink.AfterRetained!(item.Context); }
                            catch (Exception error) { SafeLog(item.Sink, "Retained Start observation failed.", error); }
                return IntPtr.Zero;
            }
            if (frame.Arguments is not { } args) return completed ? returned : IntPtr.Zero;
            if (!completed)
            {
                if (frame.OriginalAllowed) { lock (policy.Sync) ++policy.Aborted; InitializationFault(policy, args, "LOCAL native getter original did not return normally."); }
                return IntPtr.Zero;
            }
            try
            {
                if (information != args.Information || !policy.Callbacks.After(args, returned) || returned == IntPtr.Zero)
                    throw new InvalidOperationException("Actual LOCAL native getter result was not verified.");
                return returned;
            }
            catch (Exception error)
            { lock (policy.Sync) ++policy.Aborted; InitializationFault(policy, args, "LOCAL native getter result was rejected: " + error.GetType().Name); return IntPtr.Zero; }
        }
        catch (Exception error) { InitializationFault(policy, frame?.Arguments, "Native initialization exit failed: " + error.GetType().Name); return IntPtr.Zero; }
        finally
        {
            try
            {
                if (frame is not null && start)
                {
                    var completed = frame.OriginalAllowed && (flags & NativeInitializationExitFlags.Returned) != 0
                        && (flags & (NativeInitializationExitFlags.Abnormal | NativeInitializationExitFlags.EnterFaulted | NativeInitializationExitFlags.Skipped)) == 0;
                    try { Finish(entry, frame.NativeSinks, completed, (long)token, 0, 0); }
                    finally
                    {
                        if (frame.NativeRetained is not null)
                            for (var index = frame.NativeRetained.Count - 1; index >= 0; --index)
                                try { frame.NativeRetained[index].Context.Dispose(); }
                                catch (Exception error) { SafeLog(frame.NativeRetained[index].Sink, "Retained Start context release failed.", error); }
                    }
                }
            }
            finally
            {
                try { ExitInitializationFrame(frame); }
                finally { if (start) Interlocked.Decrement(ref entry.VoidInFlight); else Interlocked.Decrement(ref entry.InitializationInFlight); }
            }
        }
    }
    private static IntPtr InvokeInitializationGetter(Entry entry, IntPtr controller, IntPtr information, IntPtr method)
    {
        Interlocked.Increment(ref entry.InitializationInFlight); Interlocked.Increment(ref entry.NativeCalls);
        var policy = entry.LocalInitializationPolicy!; LocalInitializationArguments? args = null; LocalInitializationFrame? getterFrame = null; var ordinaryOriginal = false;
        lock (policy.Sync) ++policy.GetterCalls;
        try
        {
            if (entry.NativeInstallFailure is not null) throw new InvalidOperationException("Getter trampoline installation is unknown.");
            LocalInitializationIdentity? identity = null;
            if (policy.Active) identity = policy.Classify(controller);
            else if (CurrentInitialization is { Claimed: true, Identity: { } retained } current && current.Policy == policy
                && retained.ControllerIdentity == controller.ToInt64()) identity = retained;
            if (identity is null)
            {
                if (InitializationDepth >= 32) throw new InvalidOperationException("The bounded getter scope overflowed.");
                getterFrame = CreateInitializationFrame(policy, InitializationScope.Getter, controller, method);
                CurrentInitialization = getterFrame; ++InitializationDepth;
                if (Original(entry) is not LocalInitializationReference ordinary) throw new InvalidOperationException("Getter original installation is unconfirmed.");
                Interlocked.Increment(ref entry.OriginalCalls);
                ordinaryOriginal = true;
                return ordinary(controller, information, method);
            }
            if (!ValidInitializationIdentity(identity, controller)) throw new InvalidOperationException("Controller classifier returned an invalid exact token.");
            var parent = CurrentInitialization;
            if (parent is null || parent.Policy != policy || parent.Scope != InitializationScope.Start)
                throw new InvalidOperationException("An exact LOCAL getter has no direct Start parent.");
            // Provisional authority is irrevocable before main-thread, MethodInfo, root
            // or callback checks. A failed LOCAL getter always returns native null.
            parent.Claimed = true; lock (policy.Sync) ++policy.Claims;
            var previous = parent.Identity; parent.Identity ??= identity;
            args = new(identity, parent.Receiver, parent.Method, controller, information, method);
            if (parent.GetterSeen || previous is not null && previous != identity) throw new InvalidOperationException("Start cannot initialize a second or different LOCAL run.");
            parent.GetterSeen = true;
            if (parent.Unknown) { parent.Unknown = false; lock (policy.Sync) --policy.UnknownInFlight; }
            if (!policy.Active || !policy.Progress.Active || !policy.Main.IsMainThread
                || method != IntPtr.Zero && method != policy.Targets.Getter.MethodInfo
                || parent.Method != IntPtr.Zero && parent.Method != policy.Targets.Start.MethodInfo)
                throw new InvalidOperationException("LOCAL getter thread, identity or logical initialization authority is unavailable.");
            if (InitializationDepth >= 32) throw new InvalidOperationException("The bounded LOCAL getter scope overflowed.");
            getterFrame = CreateInitializationFrame(policy, InitializationScope.Getter, controller, method);
            getterFrame.Claimed = true; getterFrame.Identity = identity;
            CurrentInitialization = getterFrame; ++InitializationDepth;
            if (!policy.Callbacks.Before(args)) throw new InvalidOperationException("LOCAL initialization validation rejected original entry.");
            if (Original(entry) is not LocalInitializationReference original) throw new InvalidOperationException("Getter original installation is unconfirmed.");
            Interlocked.Increment(ref entry.OriginalCalls); lock (policy.Sync) ++policy.GetterOriginals;
            var result = original(controller, information, method);
            if (!policy.Callbacks.After(args, result) || result == IntPtr.Zero) throw new InvalidOperationException("Actual LOCAL getter result was not verified.");
            return result;
        }
        catch (Exception error) when (!ordinaryOriginal)
        {
            lock (policy.Sync) ++policy.Aborted;
            InitializationFault(policy, args, "LOCAL getter aborted without fallback: " + error.GetType().Name); return IntPtr.Zero;
        }
        finally { ExitInitializationFrame(getterFrame); Interlocked.Decrement(ref entry.InitializationInFlight); }
    }
    private static LocalInitializationFrame? CaptureInitializationPlay(LocalInitializationPolicy policy, IntPtr receiver, IntPtr method,
        LocalProgressPlayBoundary? sharedPlay = null)
    {
        lock (policy.Sync) ++policy.PlayCalls;
        LocalInitializationFrame frame;
        if (InitializationDepth >= 32) { InitializationFault(policy, null, "The bounded Play scope overflowed."); return null; }
        try { frame = CreateInitializationFrame(policy, InitializationScope.Play, receiver, method); }
        catch (Exception error) { InitializationFault(policy, null, "Play frame creation failed: " + error.GetType().Name); return null; }
        frame.SharedPlay = sharedPlay;
        if (frame.Parent is { Scope: InitializationScope.Play, Claimed: true } claimedParent
            && claimedParent.Policy == policy && claimedParent.Receiver == receiver && claimedParent.SharedPlay?.IsFaulted != true)
        { frame.Identity = claimedParent.Identity; frame.Claimed = true; }
        CurrentInitialization = frame; ++InitializationDepth;
        try
        {
            var token = policy.Active && policy.Progress.Active ? policy.Progress.Classify(receiver) : null;
            // Only the immediately matching exact Play scope may inherit on detach.
            if (token is null && frame.Parent is { Scope: InitializationScope.Play, Claimed: true } parent
                && parent.Policy == policy && parent.Receiver == receiver && parent.SharedPlay?.IsFaulted != true) frame.Identity = parent.Identity;
            if (token is not null) { ValidateProgressIdentity(token); if (token.ProgressIdentity != receiver.ToInt64()) throw new InvalidOperationException("Play token receiver differs.");
                var identity = new LocalInitializationIdentity(token.SessionId, token.RunGeneration, token.ControllerIdentity);
                if (frame.Identity is not null && frame.Identity != identity) throw new InvalidOperationException("Recursive Play generation differs from its retained exact token.");
                frame.Identity = identity; }
            frame.Claimed |= frame.Identity is not null;
            if (frame.Claimed && method != IntPtr.Zero && method != policy.Targets.Play.MethodInfo)
                InitializationFault(policy, null, "LOCAL Play MethodInfo differs; retained Mode scope remains protected.");
        }
        catch { frame.Unknown = true; lock (policy.Sync) ++policy.UnknownInFlight; InitializationFault(policy, null, "Play classification is unknown."); }
        return frame;
    }
    private static void InvokeInitializationPlay(Entry entry, IntPtr receiver, bool play, IntPtr method)
    {
        Interlocked.Increment(ref entry.InitializationInFlight); Interlocked.Increment(ref entry.NativeCalls);
        var policy = entry.LocalInitializationPolicy!; LocalInitializationFrame? frame = null;
        try
        {
            frame = CaptureInitializationPlay(policy, receiver, method);
            if (frame is null) return;
            if (entry.NativeInstallFailure is not null) { InitializationFault(policy, null, "Play trampoline installation is unknown."); return; }
            if (Original(entry) is not LocalProgressPlay original) { InitializationFault(policy, null, "Play trampoline installation is unknown."); return; }
            Interlocked.Increment(ref entry.OriginalCalls); original(receiver, play, method);
        }
        finally { ExitInitializationFrame(frame); Interlocked.Decrement(ref entry.InitializationInFlight); }
    }
    // The latest scope determines authority: ordinary Start/Play/Progress child frames
    // mask outer permission. Play grants Mode only; initialization grants Mode/Tooltip.
    private static bool SuppressInitializationTimeline(Entry entry, bool tooltip)
    {
        var frame = CurrentInitialization;
        if (frame is null || !frame.Claimed || frame.Identity is null || frame.SharedPlay?.IsFaulted == true
            || CurrentLocalProgress is { } progress && progress.Sequence > frame.Sequence) return false;
        var target = tooltip ? frame.Policy.Targets.Tooltip : frame.Policy.Targets.Mode;
        if (target.Address != entry.Target.Address || tooltip && frame.Scope == InitializationScope.Play) return false;
        lock (frame.Policy.Sync) { if (tooltip) ++frame.Policy.SuppressedTooltip; else ++frame.Policy.SuppressedMode; } return true;
    }
    private static void InvokeInitializationTooltip(Entry entry, IntPtr text, float offset, IntPtr method)
    {
        Interlocked.Increment(ref entry.InitializationInFlight); Interlocked.Increment(ref entry.NativeCalls);
        var policy = entry.LocalInitializationPolicy!; lock (policy.Sync) ++policy.TooltipCalls;
        try
        {
            if (SuppressInitializationTimeline(entry, true)) return;
            if (entry.NativeInstallFailure is not null) { InitializationFault(policy, null, "Tooltip trampoline installation is unknown."); return; }
            if (Original(entry) is not StaticTimelineTooltip original) { InitializationFault(policy, null, "Tooltip trampoline installation is unknown."); return; }
            Interlocked.Increment(ref entry.OriginalCalls); original(text, offset, method);
        }
        finally { Interlocked.Decrement(ref entry.InitializationInFlight); }
    }
}
