using System.Reflection;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] InitializationCases = {
        ("Actual Start/getter adopts five roots and cannot initialize until the complete native Start returns", ExactInitialization),
        ("Independent exact Play scopes contribute to shared precommit result and retirement drain", InitializationPlayDrain),
        ("Wrong Start information aborts before original and retains the claimed Steam tail", InitializationWrongStart),
        ("Wrong getter return retains its independent root and cannot publish shared readiness", InitializationWrongReturn),
        ("A replaced owned parameter closure is rejected before original initialization even with unchanged metadata", InitializationWrongInput),
        ("Worker initialization first claims the tail and atomically invalidates launch readiness without native reads", InitializationWorker),
        ("Initialization worker faults cannot leave a prepared or already published shared ready token usable", InitializationFaultRace)
        ,("Production initialization Start validates its actual information field through bounded native publication", NativeInitializationStartPublication)
    };
    private static void Initialize(PendingFixture f)
    { f.AttachController(); f.Launch.NoteActivationAttempt(); f.Backend.Start(f.Controller + 9); }
    private static void ExactInitialization()
    {
        using var f = new PendingFixture(initialization: true);
        f.Backend.OnInitializationInput = () => Throws<InvalidOperationException>(() => f.Bridge.Read(f.Session, f.Controller));
        f.Backend.OnStart = _ => {
            Equal(f.Controller + 4, f.Backend.Getter(f.Controller, f.Controller + 1).ToInt64());
            Equal(LocalRunLaunchCommitPhase.Initializing, f.Launch.Read().Phase);
            Check(!f.Bridge.VerifyInitializedGraph(f.Launch));
            Throws<InvalidOperationException>(() => f.Launch.PrepareCommit());
            Check(f.Backend.InitializationBinding!.Read().InFlight > 0);
            f.Backend.Mode(); f.Backend.Tooltip();
        };
        Initialize(f); Equal(5, f.Backend.Roots); Equal(1, f.Backend.GetterOriginals);
        Equal(0, f.Backend.ModeOriginals); Equal(0, f.Backend.TooltipOriginals);
        Check(!f.Launch.IsCommittedReady); Check(f.Bridge.VerifyInitializedGraph(f.Launch));
        f.Launch.PrepareCommit(); f.Launch.CommitPrepared(); Check(f.Launch.IsCommittedReady);
        Check(f.Backend.InitializationBinding!.Read() is { InFlight: 0, UnknownInFlight: 0, Known: true });
    }
    private static void InitializationPlayDrain()
    {
        using var f = new PendingFixture(initialization: true); Initialize(f);
        Check(f.Bridge.VerifyInitializedGraph(f.Launch)); f.Launch.PrepareCommit();
        f.Backend.OnPlay = (receiver, play) => {
            Equal(f.Controller + 2, receiver); Check(play);
            var read = f.Bridge.Read(f.Session, f.Controller);
            Check(read.NativeBoundaryStateKnown && read.InFlightNativeBoundaries > 0);
            Throws<InvalidOperationException>(() => f.Launch.CommitPrepared());
            f.Backend.Mode(); f.Backend.Tooltip(); // Play has Mode authority only.
        };
        f.Backend.Play(f.Controller + 2, true); Equal(0, f.Backend.ModeOriginals); Equal(1, f.Backend.TooltipOriginals);
        // Failed in-flight commit discards its prepared publication. A later main
        // transaction can prepare again only after this real native Play exits.
        f.Launch.PrepareCommit(); f.Launch.CommitPrepared(); Check(f.Launch.IsCommittedReady);
    }
    private static void InitializationWrongStart()
    {
        using var f = new PendingFixture(initialization: true); f.AttachController(); f.Launch.NoteActivationAttempt();
        f.Backend.OnStart = _ => {
            Equal(IntPtr.Zero, f.Backend.Getter(f.Controller, f.Controller + 66));
            f.Backend.Mode(); f.Backend.Tooltip();
        };
        f.Backend.Start(f.Controller + 9); Equal(0, f.Backend.GetterOriginals); Equal(1, f.Backend.Roots);
        Equal(0, f.Backend.ModeOriginals); Equal(0, f.Backend.TooltipOriginals);
        Check(!f.Launch.IsCommittedReady); Equal(LocalRunLaunchCommitPhase.Unknown, f.Launch.Read().Phase);
    }
    private static void InitializationWrongReturn()
    {
        using var f = new PendingFixture(initialization: true); f.Backend.ReturnedParameters = f.Controller + 66;
        Initialize(f); Equal(1, f.Backend.GetterOriginals); Equal(5, f.Backend.Roots); Equal(0, f.Backend.Frees);
        Equal(0, f.Backend.ModeOriginals); Equal(0, f.Backend.TooltipOriginals);
        Check(!f.Launch.IsCommittedReady); Equal(LocalRunLaunchCommitPhase.Unknown, f.Launch.Read().Phase);
    }
    private static void InitializationWrongInput()
    {
        using var f = new PendingFixture(initialization: true); f.Backend.InitializationInputMatches = false;
        Initialize(f); Equal(0, f.Backend.GetterOriginals); Equal(4, f.Backend.Roots);
        Equal(0, f.Backend.ModeOriginals); Equal(0, f.Backend.TooltipOriginals);
        Equal(LocalRunLaunchCommitPhase.Unknown, f.Launch.Read().Phase);
        var validationFailure = f.Bridge.Describe().Failure;
        Check(validationFailure?.Contains("Controlled owned parameter identity/closure differs", StringComparison.Ordinal) == true);
        Equal(validationFailure, f.Launch.Read().Failure);
    }
    private static void InitializationWorker()
    {
        using var f = new PendingFixture(initialization: true); f.AttachController(); f.Launch.NoteActivationAttempt();
        var roots = f.Backend.Roots; var reads = f.Backend.TargetReads;
        Exception? error = null; var worker = new Thread(() => { try { f.Backend.Start(f.Controller + 9); } catch (Exception fail) { error = fail; } });
        worker.Start(); worker.Join(); Check(error is null);
        Equal(0, f.Backend.GetterOriginals); Equal(roots, f.Backend.Roots); Equal(reads, f.Backend.TargetReads);
        Equal(0, f.Backend.ModeOriginals); Equal(0, f.Backend.TooltipOriginals);
        Equal(LocalRunLaunchCommitPhase.Unknown, f.Launch.Read().Phase);
        const string firstFailure = "LOCAL getter aborted without fallback: InvalidOperationException";
        Equal(firstFailure, f.Launch.Read().Failure);
        f.Launch.MarkUnknown("Later main-thread activity verification failed.");
        Equal(firstFailure, f.Launch.Read().Failure);
        Check(!f.Launch.IsCommittedReady);
    }
    private static void InitializationFaultRace()
    {
        using var f = new PendingFixture(initialization: true); Initialize(f); Check(f.Bridge.VerifyInitializedGraph(f.Launch));
        f.Launch.PrepareCommit(); f.Launch.CommitPrepared(); Check(f.Launch.IsCommittedReady);
        var failure = "Controlled late native observation fault. " + new string('x', 1024);
        Task.Run(() => f.Launch.ObserveNativeFaultFromAnyThread(failure)).GetAwaiter().GetResult();
        Check(!f.Launch.IsCommittedReady); Equal(LocalRunLaunchCommitPhase.Unknown, f.Launch.Read().Phase);
        Task.Run(() => f.Launch.ObserveNativeFaultFromAnyThread("Subsequent worker fault.")).GetAwaiter().GetResult();
        Equal(failure[..768], f.Launch.Read().Failure);
        Throws<InvalidOperationException>(() => f.Launch.PrepareCommit());
    }
    private static void NativeInitializationStartPublication()
    {
        using var main = new RizToolbox.Core.MainThreadDispatcher(); using var native = new FakeNative(); var backend = Production(native, main);
        Field(backend, "_levelControl", native.Api.ObjectClass(native.Instance));
        native.FieldType = native.Object; native.FieldBytes = BitConverter.GetBytes(77L);
        Throws<InvalidOperationException>(() => backend.RetainInitializationStart(native.Instance, native.OtherObject));
        Equal(1, native.FieldReads); Equal(native.NewRoots, native.FreedRoots); Equal(0, native.ActiveRoots);
        Check(backend.CleanupUnknown); var reads = native.FieldReads;
        Throws<InvalidOperationException>(() => backend.RetainInitializationStart(native.Instance, native.OtherObject));
        Equal(reads, native.FieldReads);
    }
    private sealed partial class Backend
    {
        public ExactHookBroker.LocalInitializationIsolationBinding? BindInitialization(
            ExactHookBroker.LocalProgressIsolationBinding progress, Func<IntPtr, ExactHookBroker.LocalInitializationIdentity?> classify,
            ExactHookBroker.LocalInitializationCallbacks callbacks)
        {
            if (!InitializationEnabled) return null;
            var address = Interlocked.Add(ref NextAddress, 0x1000);
            VerifiedHookTarget Target(int offset, ExactHookAbi abi, bool returns = false)
                => _ownedTargets == IntPtr.Zero ? new("initialization.fixture." + offset, new(address + offset), new(0x777), new(0x888),
                        abi, returns ? new(0x999) : IntPtr.Zero)
                    : OwnedTarget(offset switch { 0 => "OwnedStart", 0x100 => "OwnedGetter", _ => "OwnedTooltip" }, abi, returns);
            InitializationTargets = (ExactHookBroker.LocalInitializationIsolationTargets)typeof(ExactHookBroker.LocalInitializationIsolationTargets)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Enumerable.Repeat(typeof(VerifiedHookTarget), 5).ToArray(), null)!
                .Invoke(new object[] { Target(0, ExactHookAbi.InstanceVoid), Target(0x100, ExactHookAbi.LocalInitializationReference1, true),
                    SharedPlayTarget ?? Target(0x200, ExactHookAbi.LocalProgressPlayBooleanVoid), _targets.Mode, Target(0x300, ExactHookAbi.StaticTimelineTooltipVoid) });
            _engine.Originals[InitializationTargets.Start.Address] = new ExactHookBroker.InstanceVoid((self, _) => {
                if (OnStart is not null) OnStart(self.ToInt64());
                else { Getter(InitializationController, InitializationController + 1); Mode(); Tooltip(); }
            });
            _engine.Originals[InitializationTargets.Getter.Address] = new ExactHookBroker.LocalInitializationReference((self, info, method) => {
                GetterOriginals++; PendingControllerInitialized = true; return new(ReturnedParameters);
            });
            _engine.Originals[InitializationTargets.Play.Address] = new ExactHookBroker.LocalProgressPlay((self, play, _) => OnPlay?.Invoke(self.ToInt64(), play));
            _engine.Originals[InitializationTargets.Tooltip.Address] = new ExactHookBroker.StaticTimelineTooltip((_, _, _) => TooltipOriginals++);
            // Mode was installed by the Progress owner already: its original delegate
            // is the same exact object, so count via the engine's returned trampoline.
            _engine.ReplaceModeCounter = () => ModeOriginals++;
            // The owned image has synthetic Start/Getter RVAs. Its explicit
            // transport still uses the real typed native bridge and Dobby.
            Func<NativeInitializationKind, INativeInitializationContextBridge>? bridge = _ownedTargets == IntPtr.Zero ? null : kind =>
                new OwnedInitializationBridge(kind);
            return InitializationBinding = _broker.BindLocalInitializationIsolation(progress, InitializationTargets, classify, callbacks,
                createNativeBridge: bridge, playReservation: SharedPlayReservation);
        }
        internal void Start(long receiver) => ((ExactHookBroker.InstanceVoid)_engine.Replacements[InitializationTargets!.Start.Address])(new(receiver), InitializationTargets.Start.MethodInfo);
        internal IntPtr Getter(long controller, long information) => ((ExactHookBroker.LocalInitializationReference)_engine.Replacements[InitializationTargets!.Getter.Address])(new(controller), new(information), InitializationTargets.Getter.MethodInfo);
        internal void Play(long receiver, bool play) => ((ExactHookBroker.LocalProgressPlay)_engine.Replacements[InitializationTargets!.Play.Address])(new(receiver), play, InitializationTargets.Play.MethodInfo);
        internal void Mode() => ((ExactHookBroker.StaticTimelineMode)_engine.Replacements[_targets.Mode.Address])(42, _targets.Mode.MethodInfo);
        internal void Tooltip() => ((ExactHookBroker.StaticTimelineTooltip)_engine.Replacements[InitializationTargets!.Tooltip.Address])(new(42), -0f, InitializationTargets.Tooltip.MethodInfo);
        public NativeRuntime.RootHandle RetainInitializationStart(IntPtr start, IntPtr information)
        {
            Equal(InitializationController + 9, start.ToInt64()); Equal(InitializationController + 1, information.ToInt64()); return Root(start.ToInt64());
        }
        public NativeRuntime.RootHandle RetainInitializationInformation(IntPtr information) => Root(information.ToInt64());
        public NativeRuntime.RootHandle RetainInitializationReturn(IntPtr parameters) => Root(parameters.ToInt64());
        public void VerifyInitializationReturn(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
            NativeRuntime.RootHandle progress, NativeRuntime.RootHandle start, NativeRuntime.RootHandle parameters, IDisposable bundle)
        {
            Equal(InitializationController + 4, parameters.Target.ToInt64()); Equal(InitializationController + 9, start.Target.ToInt64());
            VerifyGraph(controller, information, progress, InitializationController + 3);
        }
        public void VerifyInitializationInput(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
            NativeRuntime.RootHandle progress, NativeRuntime.RootHandle start, IDisposable bundle)
        {
            Equal(InitializationController + 9, start.Target.ToInt64());
            OnInitializationInput?.Invoke();
            if (!InitializationInputMatches) throw new InvalidOperationException("Controlled owned parameter identity/closure differs with the same metadata.");
            VerifyInitializingGraph(controller, information, progress, InitializationController + 3);
        }
    }
}
