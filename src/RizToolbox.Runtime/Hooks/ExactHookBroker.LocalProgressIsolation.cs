using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    [StructLayout(LayoutKind.Sequential, Size = 8)]
    internal readonly struct TimelineEventHandle
    {
        internal readonly ulong Value;
        internal TimelineEventHandle(ulong value) => Value = value;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void StaticTimelineMode(int mode, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate TimelineEventHandle StaticTimelineInstantaneous(
        IntPtr title, IntPtr description, IntPtr icon, uint priority, float offset, int clipPriority, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void StaticTimelineEndRange(TimelineEventHandle handle, float offset, IntPtr method);

    /// <summary>All four authorities come from the one pinned image and exported
    /// metadata. No arbitrary native address or SDK mutation capability is exposed.</summary>
    internal sealed class LocalProgressIsolationTargets
    {
        private const string SupportedImage = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
        internal VerifiedHookTarget Progress { get; }
        internal VerifiedHookTarget Mode { get; }
        internal VerifiedHookTarget Instantaneous { get; }
        internal VerifiedHookTarget EndRange { get; }
        private LocalProgressIsolationTargets(VerifiedHookTarget progress, VerifiedHookTarget mode,
            VerifiedHookTarget instantaneous, VerifiedHookTarget endRange)
            => (Progress, Mode, Instantaneous, EndRange) = (progress, mode, instantaneous, endRange);
        internal static LocalProgressIsolationTargets Resolve(NativeRuntime api, IntPtr module, string imageSha256)
        {
            if (!string.Equals(imageSha256, SupportedImage, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("LOCAL progress isolation requires the pinned GameAssembly image.");
            ArgumentNullException.ThrowIfNull(api);
            if (module == IntPtr.Zero) throw new UnsupportedSignatureException("Verified GameAssembly module is required.");
            var progress = api.ResolveExactHook("local.progress.update", module,
                api.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl"), "Update", Array.Empty<string>(), 0x400FE0, ExactHookAbi.InstanceVoid);
            var timeline = api.ResolveLocalTimelineClass();
            return new(progress,
                api.ResolveLocalTimelineHook(module, timeline, ExactHookAbi.StaticTimelineModeVoid),
                api.ResolveLocalTimelineHook(module, timeline, ExactHookAbi.StaticTimelineInstantaneousHandle),
                api.ResolveLocalTimelineHook(module, timeline, ExactHookAbi.StaticTimelineEndRangeVoid));
        }
    }

    internal sealed record LocalProgressIdentity
    {
        internal string SessionId { get; }
        internal long RunGeneration { get; }
        private long _controller, _progress;
        internal long ControllerIdentity => Volatile.Read(ref _controller);
        internal long ProgressIdentity => Volatile.Read(ref _progress);
        internal LocalProgressIdentity(string sessionId, long runGeneration, long controllerIdentity, long progressIdentity)
            => (SessionId, RunGeneration, _controller, _progress) = (sessionId, runGeneration, controllerIdentity, progressIdentity);
        // No reader receives a reserved identity until both fields have been set.
        // Once published it is immutable, including while used as a dictionary key.
        internal void InitializeReservedGraph(long controller, long progress)
        {
            if (controller == 0 || progress == 0 || controller == progress
                || Volatile.Read(ref _progress) != 0 || Interlocked.CompareExchange(ref _controller, controller, 0) != 0)
                throw new InvalidOperationException("A progress identity can be completed only once before publication.");
            Volatile.Write(ref _progress, progress);
        }
    }
    internal enum LocalProgressTerminalMark { MarkedCurrentContext, NoCurrentContext, DifferentCurrentContext, UnknownCurrentContext, Detached }
    internal sealed record LocalProgressBoundaryState(long InFlight, long UnknownInFlight, bool Known);
    internal sealed record LocalProgressIsolationDiagnostics(long InFlight, long UnknownInFlight, long Entered, long Exited,
        long ClassifiedLocal, long Ordinary, long ClassificationFaults, long TerminalMarks, long SuppressedTimelineCalls, long UnknownObserved);

    internal sealed class LocalProgressPolicy
    {
        internal readonly LocalProgressIsolationTargets Targets;
        internal readonly Func<IntPtr, LocalProgressIdentity?> Classify;
        internal readonly IMainThreadDispatcher Main;
        internal readonly ILogger Log;
        internal readonly object Sync = new();
        internal readonly Dictionary<LocalProgressIdentity, long> InFlightByIdentity = new();
        internal Entry[] TimelineEntries = Array.Empty<Entry>();
        internal volatile bool Active = true;
        internal long InFlight, UnknownInFlight, Entered, Exited, ClassifiedLocal, Ordinary,
            ClassificationFaults, TerminalMarks, SuppressedTimelineCalls, UnknownObserved;
        internal LocalProgressPolicy(LocalProgressIsolationTargets targets, Func<IntPtr, LocalProgressIdentity?> classify,
            IMainThreadDispatcher main, ILogger log) => (Targets, Classify, Main, Log) = (targets, classify, main, log);
    }
    private sealed class LocalProgressFrame
    {
        internal readonly LocalProgressPolicy Policy;
        internal readonly IntPtr Receiver;
        internal readonly LocalProgressFrame? Parent;
        internal LocalProgressIdentity? Identity;
        internal long ClaimedController;
        internal bool Unknown = true, TerminalClaimed;
        internal long Sequence = NextLocalScopeSequence();
        internal LocalProgressFrame(LocalProgressPolicy policy, IntPtr receiver, LocalProgressFrame? parent)
            => (Policy, Receiver, Parent) = (policy, receiver, parent);
    }
    [ThreadStatic] private static LocalProgressFrame? CurrentLocalProgress;

    // Invoked solely by the sealed exact LevelOver policy. A positive LOCAL
    // controller claim made during the actual Progress original call is direct
    // evidence for this invocation's tail, even if its pure identity classifier
    // faulted. It does not relabel the frame/session or leak into child frames.
    private static void MarkLocalProgressTerminalForControllerClaim(IntPtr controller, bool classifiedLocal)
    {
        var current = CurrentLocalProgress;
        if (current is null || controller == IntPtr.Zero) return;
        var policy = current.Policy;
        lock (policy.Sync)
        {
            var exact = !current.Unknown && current.Identity is { } identity && identity.ControllerIdentity == controller.ToInt64();
            if (!exact && !classifiedLocal) return;
            current.TerminalClaimed = true; current.ClaimedController = controller.ToInt64(); ++policy.TerminalMarks;
            if (exact)
            {
                for (var ancestor = current.Parent; ancestor is not null; ancestor = ancestor.Parent)
                    if (ancestor.Policy == policy && ancestor.Identity == current.Identity)
                    { ancestor.TerminalClaimed = true; ancestor.ClaimedController = controller.ToInt64(); }
            }
            else
            {
                // The invocation is protected, but its unknown/mismatching pure
                // token cannot authorize capture or retirement for either run.
                ++policy.UnknownObserved;
            }
        }
    }

    internal sealed class LocalProgressIsolationBinding : IDisposable
    {
        private readonly LocalProgressPolicy _policy;
        internal LocalProgressPolicy Policy => _policy;
        private LocalProgressIsolationBinding(LocalProgressPolicy policy) => _policy = policy;
        internal bool IsActive { get { lock (_policy.Sync) return _policy.Active; } }
        internal LocalProgressTerminalMark MarkTerminal(LocalProgressIdentity identity)
        {
            ValidateProgressIdentity(identity);
            var current = CurrentLocalProgress;
            lock (_policy.Sync)
            {
                if (current is null) return _policy.Active ? LocalProgressTerminalMark.NoCurrentContext : LocalProgressTerminalMark.Detached;
                if (current.Policy != _policy) return LocalProgressTerminalMark.DifferentCurrentContext;
                if (current.Unknown) return LocalProgressTerminalMark.UnknownCurrentContext;
                if (current.Identity != identity) return LocalProgressTerminalMark.DifferentCurrentContext;
                // Mark before any host callback/queue work. Logical detach cannot
                // erase a frame's claim; each matching recursive frame owns it.
                for (var frame = current; frame is not null; frame = frame.Parent)
                    if (frame.Policy == _policy && frame.Identity == identity) frame.TerminalClaimed = true;
                ++_policy.TerminalMarks;
                return LocalProgressTerminalMark.MarkedCurrentContext;
            }
        }
        internal LocalProgressBoundaryState Read(LocalProgressIdentity identity)
        {
            ValidateProgressIdentity(identity);
            lock (_policy.Sync)
                // Static Timeline calls have no receiver identity. Conservatively
                // include all three real entry scopes for every session. Shared
                // Mode may also appear in initialization activity; zero is exact.
                return new(checked((_policy.InFlightByIdentity.TryGetValue(identity, out var count) ? count : 0) + ReadTimelineInFlight(_policy)),
                    _policy.UnknownInFlight, _policy.UnknownInFlight == 0);
        }
        internal LocalProgressIsolationDiagnostics Describe()
        {
            lock (_policy.Sync) return new(checked(_policy.InFlight + ReadTimelineInFlight(_policy)), _policy.UnknownInFlight, _policy.Entered, _policy.Exited,
                _policy.ClassifiedLocal, _policy.Ordinary, _policy.ClassificationFaults, _policy.TerminalMarks,
                _policy.SuppressedTimelineCalls, _policy.UnknownObserved);
        }
        public void Dispose() { _policy.Main.VerifyAccess(); lock (_policy.Sync) _policy.Active = false; }
        internal static LocalProgressIsolationBinding Create(LocalProgressPolicy policy) => new(policy);
    }

    /// <param name="classify">Pure, thread-safe lookup of a previously rooted,
    /// exact progress/controller/session mapping. It must never read native
    /// objects, metadata or GC-handle targets, including on a worker thread.</param>
    internal LocalProgressIsolationBinding BindLocalProgressIsolation(LocalProgressIsolationTargets targets,
        Func<IntPtr, LocalProgressIdentity?> classify, LocalProgressPlayReservation? playReservation = null)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(targets); ArgumentNullException.ThrowIfNull(classify);
        var policy = new LocalProgressPolicy(targets, classify, _main, _log);
        lock (Gate)
        {
            var all = new[] { targets.Progress, targets.Mode, targets.Instantaneous, targets.EndRange };
            if (all.Select(x => x.Address).Distinct().Count() != 4)
                throw new UnsupportedSignatureException("Progress and Timeline authorities require four distinct exact addresses.");
            foreach (var target in all)
                if (Entries.TryGetValue(target.Address, out var existing))
                {
                    RequireProgressTargetIdentity(existing, target);
                    if (existing.Engine != _engine || existing.LocalInitializationPolicy is { } initialization
                        && (initialization.Active || initialization.InstallationUnknown || initialization.Entries.Any(InitializationEntryBusy)))
                        throw new InvalidOperationException("A live initialization authority or another transport still owns this Progress/Timeline entry.");
                    if (existing.LocalProgressPolicy is { } owner)
                        lock (owner.Sync)
                            if (owner.Active || owner.InFlight != 0 || ReadTimelineInFlight(owner) != 0)
                                throw new InvalidOperationException("Conflict: the LOCAL progress/Timeline boundary already has an active or in-flight coordinator.");
                    if (existing.Sinks.Any(x => x.Active && x.LocalResultPolicy is not null))
                        throw new InvalidOperationException("A LOCAL void replacement policy cannot own Progress.Update.");
                }
            playReservation?.MarkNativeInstallationAttempt(this);
            try
            {
                // Timeline entries are inert pass-throughs until an exact around
                // frame exists. Install Progress last; partial failure never
                // publishes an active around context or a fake complete binding.
                foreach (var target in new[] { targets.Mode, targets.Instantaneous, targets.EndRange, targets.Progress })
                {
                    if (!Entries.TryGetValue(target.Address, out var entry))
                    {
                        entry = new() { Target = target, Engine = _engine, LocalProgressPolicy = policy };
                        entry.Replacement = target.Abi switch
                        {
                            ExactHookAbi.InstanceVoid => new InstanceVoid((self, method) => InvokeVoid(entry, self, method)),
                            ExactHookAbi.StaticTimelineModeVoid => new StaticTimelineMode((mode, method) => InvokeTimelineMode(entry, mode, method)),
                            ExactHookAbi.StaticTimelineInstantaneousHandle => new StaticTimelineInstantaneous((title, description, icon, priority, offset, clip, method)
                                => InvokeTimelineInstantaneous(entry, title, description, icon, priority, offset, clip, method)),
                            ExactHookAbi.StaticTimelineEndRangeVoid => new StaticTimelineEndRange((handle, offset, method) => InvokeTimelineEndRange(entry, handle, offset, method)),
                            _ => throw new UnsupportedSignatureException("Unexpected sealed progress/Timeline ABI.")
                        };
                        Entries.Add(target.Address, entry);
                        InstallLocalClrEntry(entry, includeReferenceIdentity: true);
                    }
                    entry.LocalProgressPolicy = policy;
                    if (target.Abi != ExactHookAbi.InstanceVoid)
                        policy.TimelineEntries = policy.TimelineEntries.Append(entry).ToArray();
                }
            }
            catch { lock (policy.Sync) policy.Active = false; throw; }
        }
        return LocalProgressIsolationBinding.Create(policy);
    }
    private void InstallLocalClrEntry(Entry entry, bool includeReferenceIdentity)
    {
        try
        {
            var code = includeReferenceIdentity && _engine is DobbyDetourEngine dobby
                ? ReferenceCodeIdentity.TryCapture(entry, dobby, _main, _log) : null;
            var original = _engine.Install(entry.Target, entry.Replacement);
            Volatile.Write(ref entry.Original, original);
            try { code?.Installed(original); }
            catch (UnsupportedSignatureException error) { code!.RefuseComposition(_log, error); }
        }
        catch (Exception error) { entry.NativeInstallFailure = error.GetType().Name; throw; }
    }
    private static void RequireProgressTargetIdentity(Entry entry, VerifiedHookTarget target)
    {
        if (entry.Target.Abi != target.Abi || entry.Target.MethodInfo != target.MethodInfo || entry.Target.DeclaringClass != target.DeclaringClass
            || entry.Target.ReturnClass != target.ReturnClass
            || entry.NativeInstallFailure is not null || entry.NativeJudgeBridge is not null
            || entry.Sinks.Any(x => x.Active && (x.MethodInfo != target.MethodInfo || x.DeclaringClass != target.DeclaringClass)))
            throw new UnsupportedSignatureException("The sealed progress/Timeline address cannot share a different method or transport.");
    }
    private static long ReadTimelineInFlight(LocalProgressPolicy policy)
    {
        long count = 0; foreach (var entry in policy.TimelineEntries) count = checked(count + Interlocked.Read(ref entry.InitializationInFlight)); return count;
    }
    private static void ValidateProgressIdentity(LocalProgressIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.SessionId) || identity.RunGeneration <= 0 || identity.ControllerIdentity == 0 || identity.ProgressIdentity == 0)
            throw new ArgumentException("Exact progress/controller/session identity is incomplete.", nameof(identity));
    }

    private static LocalProgressFrame? EnterLocalProgress(Entry entry, IntPtr receiver, IntPtr method)
    {
        var policy = entry.LocalProgressPolicy;
        if (policy is null || entry.Target.Abi != ExactHookAbi.InstanceVoid || entry.Target.Address != policy.Targets.Progress.Address) return null;
        var parent = CurrentLocalProgress;
        lock (policy.Sync)
        {
            // A detached but still-running same-receiver frame retains ownership.
            if (!policy.Active && (parent is null || parent.Policy != policy)) return null;
            ++policy.InFlight; ++policy.UnknownInFlight; ++policy.Entered;
        }
        var frame = new LocalProgressFrame(policy, receiver, parent); CurrentLocalProgress = frame;
        try
        {
            // Same-receiver recursion inherits an already verified token and its
            // sticky claim, even through an intervening ordinary progress frame.
            // The ordinary frame itself still masks the outer terminal context.
            LocalProgressFrame? inherited = null;
            for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
                if (ancestor.Policy == policy && ancestor.Receiver == receiver
                    && (!ancestor.Unknown || ancestor.TerminalClaimed && ancestor.ClaimedController != 0))
                { inherited = ancestor; break; }
            if (inherited is not null)
            { frame.Identity = inherited.Identity; frame.TerminalClaimed = inherited.TerminalClaimed; frame.ClaimedController = inherited.ClaimedController; }
            else if (parent is not null && parent.Policy == policy && parent.Receiver == receiver && parent.TerminalClaimed && parent.ClaimedController != 0)
            { frame.TerminalClaimed = true; frame.ClaimedController = parent.ClaimedController; }
            // An unexpected trailing MethodInfo must not revoke an existing
            // exact receiver claim and expose its recursive Timeline tail.
            if (method != IntPtr.Zero && method != policy.Targets.Progress.MethodInfo && !frame.TerminalClaimed)
                throw new UnsupportedSignatureException("Progress.Update trailing MethodInfo differs from its sealed identity.");
            // Retain both the protected controller claim and the unknown accounting
            // when the exact receiver recursively reenters an unknown claimed frame.
            if (inherited is { Unknown: true }) return frame;
            if (inherited is null && policy.Active)
                frame.Identity = policy.Classify(receiver);
            if (frame.Identity is not null)
            {
                ValidateProgressIdentity(frame.Identity);
                if (frame.Identity.ProgressIdentity != receiver.ToInt64())
                    throw new InvalidOperationException("Pure LOCAL progress classifier returned a different receiver token.");
            }
            lock (policy.Sync)
            {
                frame.Unknown = false; --policy.UnknownInFlight;
                if (frame.Identity is { } identity)
                {
                    ++policy.ClassifiedLocal;
                    policy.InFlightByIdentity[identity] = policy.InFlightByIdentity.TryGetValue(identity, out var count) ? count + 1 : 1;
                }
                else ++policy.Ordinary;
            }
        }
        catch (Exception error)
        {
            lock (policy.Sync) { ++policy.ClassificationFaults; ++policy.UnknownObserved; }
            try { policy.Log.Log(LogLevel.Error, "LOCAL Progress.Update classification is unknown; terminal completion must wait for this frame to leave.", error); } catch { }
        }
        return frame;
    }
    private static void ExitLocalProgress(LocalProgressFrame? frame)
    {
        if (frame is null) return;
        var policy = frame.Policy;
        try
        {
            lock (policy.Sync)
            {
                --policy.InFlight; ++policy.Exited;
                if (frame.Unknown) --policy.UnknownInFlight;
                else if (frame.Identity is { } identity)
                {
                    var count = policy.InFlightByIdentity[identity] - 1;
                    if (count == 0) policy.InFlightByIdentity.Remove(identity); else policy.InFlightByIdentity[identity] = count;
                }
            }
        }
        finally { CurrentLocalProgress = frame.Parent; }
    }

    private static bool SuppressLocalTimeline(Entry entry, IntPtr method)
    {
        var frame = CurrentLocalProgress;
        if (CurrentInitialization is { } initialization
            && (frame is null || initialization.Sequence > frame.Sequence)) return false;
        if (frame is null || !frame.TerminalClaimed || frame.Identity is null && frame.ClaimedController == 0) return false;
        var targets = frame.Policy.Targets;
        var target = entry.Target.Abi switch
        {
            ExactHookAbi.StaticTimelineModeVoid => targets.Mode,
            ExactHookAbi.StaticTimelineInstantaneousHandle => targets.Instantaneous,
            ExactHookAbi.StaticTimelineEndRangeVoid => targets.EndRange,
            _ => null
        };
        // Authority follows the retained exact frame, so an in-flight detach
        // cannot silently fall back to Steam. Other contexts keep full ABI pass-through.
        if (target is null || target.Address != entry.Target.Address) return false;
        // A mismatching non-null MethodInfo cannot revoke an already claimed
        // terminal frame. Ordinary contexts still pass that pointer unchanged.
        _ = method;
        lock (frame.Policy.Sync) ++frame.Policy.SuppressedTimelineCalls;
        return true;
    }
    private static void InvokeTimelineMode(Entry entry, int mode, IntPtr method)
    {
        Interlocked.Increment(ref entry.InitializationInFlight);
        Interlocked.Increment(ref entry.NativeCalls);
        if (entry.LocalInitializationPolicy is { } policy) lock (policy.Sync) ++policy.ModeCalls;
        try
        {
            if (SuppressInitializationTimeline(entry, false) || SuppressLocalTimeline(entry, method)) return;
            if (entry.NativeInstallFailure is not null) return;
            if (Original(entry) is not StaticTimelineMode original) return;
            Interlocked.Increment(ref entry.OriginalCalls); original(mode, method);
        }
        finally { Interlocked.Decrement(ref entry.InitializationInFlight); }
    }
    private static TimelineEventHandle InvokeTimelineInstantaneous(Entry entry, IntPtr title, IntPtr description, IntPtr icon,
        uint priority, float offset, int clip, IntPtr method)
    {
        Interlocked.Increment(ref entry.InitializationInFlight);
        Interlocked.Increment(ref entry.NativeCalls);
        try
        {
            if (SuppressLocalTimeline(entry, method)) return default; // Empty handle, never a fabricated SDK success event.
            if (entry.NativeInstallFailure is not null) return default;
            if (Original(entry) is not StaticTimelineInstantaneous original) return default;
            Interlocked.Increment(ref entry.OriginalCalls);
            return original(title, description, icon, priority, offset, clip, method);
        }
        finally { Interlocked.Decrement(ref entry.InitializationInFlight); }
    }
    private static void InvokeTimelineEndRange(Entry entry, TimelineEventHandle handle, float offset, IntPtr method)
    {
        Interlocked.Increment(ref entry.InitializationInFlight);
        Interlocked.Increment(ref entry.NativeCalls);
        try
        {
            if (SuppressLocalTimeline(entry, method)) return;
            if (entry.NativeInstallFailure is not null) return;
            if (Original(entry) is not StaticTimelineEndRange original) return;
            Interlocked.Increment(ref entry.OriginalCalls); original(handle, offset, method);
        }
        finally { Interlocked.Decrement(ref entry.InitializationInFlight); }
    }
}
