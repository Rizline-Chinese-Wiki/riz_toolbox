using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Core;
using RizToolbox.Core.Input;
using RizToolbox.Sdk;
using static Assert;

internal static partial class NativeLocalRunResultSinkRegression
{
    internal static readonly (string Name, Action Test)[] CommandCompositionCases = {
        ("Typed host result Return queues the exact cache without a fabricated native ResultScene", TypedReturnWithoutResultScene),
        ("Typed host Retry preserves the exact result session and duplicate callbacks cannot replace its intent", TypedRetryOnce),
        ("Result command composition rejects copied results, unknown actions and returning generations before owner dispatch", TypedCommandAuthority),
        ("Reentrant and worker typed result commands cannot dispatch a second owner request", TypedCommandReentry),
        ("Typed owner exceptions retain UI responsibility and never fall back to another result action", TypedOwnerFailure),
        ("Checked host result disposal drains the actual current callback before a scene boundary", CheckedCloseBeforeScene),
        ("Formal LOCAL composition refuses production initialization before shared UI or native calls when admission is absent", CompositionAdmission),
        ("Internal host UI reuses the existing module backend and expires its previous service view on release", SharedHostUi)
    };
    private static void TypedReturnWithoutResultScene()
    {
        using var f = new CommandFixture();
        Equal(0L, f.Base.Registry.Read(f.Base.Session).ResultSceneIdentity);
        f.Present(); f.Base.Ui.Click("return");
        Equal(1, f.Target.Calls); Equal(LocalRunHostResultAction.Return, f.Target.Action);
        Check(ReferenceEquals(f.Target.Session, f.Base.Session) && ReferenceEquals(f.Target.Result, f.Base.Result));
        Equal(LocalRunSessionPhase.ResultReady, f.Base.Registry.Read(f.Base.Session).Phase);
        Check(!f.Presentation!.ConsumersEnded && !f.Base.Registry.Read(f.Base.Session).ProtectionRetired);
    }
    private static void TypedRetryOnce()
    {
        using var f = new CommandFixture(); f.Present();
        var late = f.Base.Ui.Window!.Controls.Single(row => row.Id == "retry").Click!;
        f.Base.Ui.Click("retry"); f.Base.Ui.Click("return"); late();
        Equal(1, f.Target.Calls); Equal(LocalRunHostResultAction.Retry, f.Target.Action);
        Check(ReferenceEquals(f.Target.Result, f.Base.Result));
        Equal(f.Base.Result.Session.RunGeneration, f.Target.Result!.Session.RunGeneration);
        // Request acceptance cannot create a new registry run or a completion receipt.
        Equal(LocalRunSessionPhase.ResultReady, f.Base.Registry.Read(f.Base.Session).Phase);
    }
    private static void TypedCommandAuthority()
    {
        using var f = new CommandFixture();
        Equal(ErrorCode.TargetExpired, f.Commands.Execute(f.Base.Session, f.Base.Result with { }, LocalRunHostResultAction.Return).Error!.Code);
        Equal(ErrorCode.InvalidArgument, f.Commands.Execute(f.Base.Session, f.Base.Result, (LocalRunHostResultAction)77).Error!.Code);
        f.Base.Registry.BeginReturn(f.Base.Session);
        Equal(ErrorCode.TargetExpired, f.Commands.Execute(f.Base.Session, f.Base.Result, LocalRunHostResultAction.Retry).Error!.Code);
        Equal(0, f.Target.Calls);
    }
    private static void TypedCommandReentry()
    {
        using var f = new CommandFixture();
        f.Target.OnQueue = () => Equal(ErrorCode.Conflict,
            f.Commands.Execute(f.Base.Session, f.Base.Result, LocalRunHostResultAction.Retry).Error!.Code);
        Need(f.Commands.Execute(f.Base.Session, f.Base.Result, LocalRunHostResultAction.Return)); Equal(1, f.Target.Calls);
        Task.Run(() => Throws<InvalidOperationException>(() => f.Commands.Execute(f.Base.Session, f.Base.Result,
            LocalRunHostResultAction.Return))).GetAwaiter().GetResult();
        Equal(1, f.Target.Calls);
    }
    private static void TypedOwnerFailure()
    {
        using var f = new CommandFixture(); f.Target.OnQueue = () => throw new IOException("Owned command outcome Unknown.");
        f.Present(); f.Base.Ui.Click("return"); f.Base.Ui.Click("retry");
        Equal(1, f.Target.Calls); Check(f.Sink.HasUnknownCleanup && !f.Presentation!.ConsumersEnded);
        Check(!f.Sink.Close(f.Base.Session).Success);
        Check(!f.Base.Registry.Read(f.Base.Session).ProtectionRetired);
    }
    private static void CheckedCloseBeforeScene()
    {
        using var f = new CommandFixture();
        f.Target.OnQueue = () => {
            Need(f.Sink.Close(f.Base.Session));
            Check(!f.Presentation!.ConsumersEnded); // Actual action is still on stack.
        };
        f.Present(); f.Base.Ui.Click("return"); Check(f.Presentation!.ConsumersEnded);
        f.Base.Ui.Window!.Alive = false; f.Sink.Tick();
        Check(!f.Sink.HasUnknownCleanup); Equal(1, f.Base.Ui.Window.DisposeCalls);
        Check(f.Base.Ui.Window.Controls.All(row => row.DisposeCalls == 1));
        // This checks the real sink/command callback behavior, not native return/Close.
        Equal(LocalRunSessionPhase.ResultReady, f.Base.Registry.Read(f.Base.Session).Phase);
    }
    private static void CompositionAdmission()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        var transport = new IdleCompositionTransport();
        var host = LocalRunProtectionHost.Create(main, new Logger(), transport); var uiCalls = 0;
        var profile = new AdapterBuildProfile("controlled.composition", "unsupported", "fixture", "fixture", "fixture", "fixture",
            new Dictionary<string, TypeBinding>());
        var runtime = new NativeLocalAudioContentRuntime(native.Api, main, new Logger(), profile, IntPtr.Zero,
            "unused.dll", host, () => { uiCalls++; throw new InvalidOperationException("UI must not be resolved."); });
        Check(!runtime.TryInstall() && !runtime.TryInstall()); Check(!runtime.Installed && !runtime.Unknown);
        Check(runtime.Failure!.Contains("production engine admission")); Equal(0, uiCalls); Equal(0, native.Invocations);
        Check(!host.Read().AcceptingNewRuns);
        Throws<InvalidOperationException>(() => new NativeLocalAudioContentRuntime(native.Api, main, new Logger(), profile,
            IntPtr.Zero, "unused.dll", host, null));
        runtime.RequestStop(); Check(host.Read().Stopped); Equal(5, transport.Disposals);
    }
    private static void SharedHostUi()
    {
        using var main = new MainThreadDispatcher(); var backend = new HostUiBackend(); var created = 0;
        var module = new UiInputModule(() => { created++; return backend; }, main, new Logger(), new EventHub(new Logger()));
        Throws<InvalidOperationException>(() => module.GetHostUi()); Check(module.Probe().Ready);
        var first = module.GetHostUi(); Check(ReferenceEquals(first, module.GetHostUi())); Equal(1, created);
        Equal(1, first.GetMounts().Count); module.Release(); Equal(1, backend.Disposals);
        Equal(0, first.GetMounts().Count); Throws<InvalidOperationException>(() => module.GetHostUi());
    }
    private sealed class CommandFixture : IDisposable
    {
        internal readonly Fixture Base = new();
        internal readonly CommandTarget Target = new();
        internal readonly NativeLocalRunResultCommands Commands;
        internal readonly NativeLocalRunResultSink Sink;
        internal ILocalRunTerminationResultPresentation? Presentation;
        internal CommandFixture()
        {
            Commands = new(Base.Main, Base.Registry, Target);
            Sink = new(Base.Main, Base.Registry, () => Base.Ui, Commands);
        }
        internal void Present() => Sink.Present(Base.Session, Base.Result, value => Presentation = value);
        public void Dispose() { try { Sink.Dispose(); } catch { } Base.Dispose(); }
    }
    private sealed class CommandTarget : ILocalRunOwnedResultCommandTarget
    {
        internal int Calls;
        internal LocalRunHostResultAction Action;
        internal LocalRunSessionRegistry.SessionHandle? Session;
        internal LocalRunCapturedResult? Result;
        internal Action? OnQueue;
        public Result QueueOwnedResult(LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result, LocalRunHostResultAction action)
        { Calls++; (Session, Result, Action) = (session, result, action); OnQueue?.Invoke(); return RizToolbox.Sdk.Result.Ok(); }
    }
    internal sealed class IdleCompositionTransport : ILocalRunBoundaryTransport
    {
        internal int Disposals;
        public ILocalRunBoundaryBinding Bind(LocalBoundaryKind boundary, Func<long, bool> classify, Action<long> handle) => new Binding(this, boundary);
        private sealed class Binding : ILocalRunBoundaryBinding
        {
            private readonly IdleCompositionTransport _owner; private readonly LocalBoundaryKind _boundary;
            internal Binding(IdleCompositionTransport owner, LocalBoundaryKind boundary) => (_owner, _boundary) = (owner, boundary);
            public LocalRunBoundaryCounters Read() => new(_boundary, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            public LocalRunBoundaryActivity ReadActivity() => new(0, true);
            public void Dispose() => _owner.Disposals++;
        }
    }
    private sealed class HostUiBackend : IUiInputBackend
    {
        internal int Disposals;
        public string SceneIdentity => "controlled-host-ui";
        public IReadOnlyList<UiMountInfo> GetMounts() => new[] { new UiMountInfo(UiMountIds.Overlay, true, "controlled", null, 1, "host") };
        public IReadOnlyList<InputDeviceInfo> GetDevices() => Array.Empty<InputDeviceInfo>();
        public IUiNativeWindow Create(string id, UiWindowDefinition definition) => throw new NotSupportedException();
        public UiInputFrame Capture(IReadOnlyList<InputBindingOwner> bindings) => new(Array.Empty<InputBinding>(), null, false, false, false, InputActionContext.Menu);
        public IReadOnlyList<GameNavigationTarget> GetNavigationTargets() => Array.Empty<GameNavigationTarget>();
        public Result Navigate(string id) => Result.Fail(new(ErrorCode.NavigationUnavailable, "Controlled host UI only."));
        public void Dispose() => Disposals++;
    }
}
