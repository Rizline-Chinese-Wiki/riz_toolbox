using System.Globalization;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Real registry/cached-result identity with a controlled SDK UI. No
/// native capture, game scene, command backend or consumer-end proof is supplied.</summary>
internal static partial class NativeLocalRunResultSinkRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Host result sink publishes before UI work and displays only the exact cached counters", PublishAndIdentity),
        ("LOCAL result log contains only the focused label read back from its exact capture", DisplayReadback),
        ("A changed result label or failed reader cannot attest values or disable Return", UnconfirmedDisplay),
        ("Published result owners cannot be retired synchronously during publication or native UI construction", ConstructionReentry),
        ("Host result buttons carry exact session and typed actions with visible structured failures", TypedActions),
        ("Result UI checked close keeps current callback live until exit and disables saved late callbacks", CallbackDrain),
        ("Partial result UI creation retains its published owner and refuses a second presentation", PartialCreation),
        ("Result UI cleanup attempts independent controls once and retains unknown window responsibility", CleanupFailure),
        ("Unexpected external result window retirement cannot certify checked UI consumer end", ExternalRetirement),
        ("A returning session cannot execute an old result callback or fabricate another result", StaleResult),
        ("Result action exceptions stay visible in the owned page without escaping the UI callback", ActionFailure),
        ("Host result sink rejects worker UI operations before creating windows or invoking commands", WrongThread)
    };
    private static void PublishAndIdentity()
    {
        using var f = new Fixture();
        Throws<InvalidOperationException>(() => f.Sink.Present(f.Session, f.Result with { }, _ => { })); Equal(0, f.Ui.Creates);
        f.Ui.OnCreate = () => Check(f.Presentation is not null && !f.Presentation.ConsumersEnded);
        f.Present();
        Equal(1, f.Ui.Creates); Check(f.Sink.HasPendingPresentations && !f.Sink.HasUnknownCleanup);
        var text = f.Ui.Window!.Controls[0].Text;
        Check(text.Contains("Score: 123456789") && text.Contains("Raw completion: " + f.Result.Copy.RawCompletion.ToString("R", CultureInfo.InvariantCulture)));
        Check(text.Contains("Perfect: 12") && text.Contains("MaxPerfect: 34") && text.Contains("Bad: 5") && text.Contains("Miss: 6"));
        Check(text.Contains("Crashed: True") && text.Contains("Skipped: False"));
        Check(f.Result.Copy.RunId is null && f.Result.Copy.ChartId is null && !f.Result.Copy.AssociatedWithObservedStart);
        Check(ReferenceEquals(f.Result, f.Registry.Read(f.Session).Result));
        Throws<InvalidOperationException>(() => f.Present()); Equal(1, f.Ui.Creates);
    }
    private static void DisplayReadback()
    {
        using var f = new Fixture();
        f.Present(); f.Sink.Tick(); f.Sink.Tick();
        var row = f.Log.Rows.Single();
        const string prefix = "LocalResultDisplayed ";
        Check(row.Level == LogLevel.Info && row.Message.StartsWith(prefix, StringComparison.Ordinal));
        using var body = JsonDocument.Parse(row.Message[prefix.Length..]);
        var data = body.RootElement;
        var session = f.Result.Session;
        var copy = f.Result.Copy;
        Equal(f.Ui.Window!.Id, data.GetProperty("windowId").GetString());
        Equal(session.SessionId, data.GetProperty("localSessionId").GetString());
        Equal(session.RunGeneration, data.GetProperty("RunGeneration").GetInt64());
        Equal(session.Song, data.GetProperty("Song").Deserialize<LocalContentId>());
        Equal(session.SongRevision, data.GetProperty("SongRevision").GetInt64());
        Equal(session.ChartId, data.GetProperty("ChartId").GetString());
        Equal(copy.Score, data.GetProperty("Score").GetInt64());
        Equal(copy.RawCompletion, data.GetProperty("RawCompletion").GetDouble());
        Equal(copy.Perfect, data.GetProperty("Perfect").GetInt32());
        Equal(copy.MaxPerfect, data.GetProperty("MaxPerfect").GetInt32());
        Equal(copy.Bad, data.GetProperty("Bad").GetInt32());
        Equal(copy.Miss, data.GetProperty("Miss").GetInt32());
        Equal(copy.Crashed, data.GetProperty("Crashed").GetBoolean());
        Equal(copy.Skipped, data.GetProperty("Skipped").GetBoolean());
        Equal(copy.EndReason.ToString(), data.GetProperty("endReason").GetString());
    }
    private static void UnconfirmedDisplay()
    {
        foreach (var readerThrows in new[] { false, true })
        {
            using var f = new Fixture();
            f.Ui.OnFocus = () => {
                var label = f.Ui.Window!.Controls[0];
                if (readerThrows) label.ThrowRead = true;
                else label.Text = label.Text.Replace("123456789", "0");
            };
            f.Present();
            Check(f.Log.Rows.Single().Level == LogLevel.Warning && !f.Sink.HasUnknownCleanup);
            var calls = 0;
            f.Execute = (_, action) => { Equal(LocalRunHostResultAction.Return, action); calls++; return Result.Ok(); };
            f.Ui.Click("return");
            Equal(1, calls);
        }
    }
    private static void ConstructionReentry()
    {
        using (var f = new Fixture())
        {
            // A publish callback can observe the owner before any native UI
            // allocation, but it cannot certify its end while Create is pending.
            Throws<InvalidOperationException>(() => f.Sink.Present(f.Session, f.Result, value => {
                f.Presentation = value; value.Dispose();
            }));
            Equal(0, f.Ui.Creates); Check(f.Sink.HasUnknownCleanup);
            Check(f.Presentation is not null && !f.Presentation.ConsumersEnded);
            Check(!f.Sink.Close(f.Session).Success);
        }
        using (var f = new Fixture())
        {
            // A backend can synchronously call managed code before returning
            // its newly created window. Reject retirement without changing its
            // state so that the subsequently returned handle is still adopted.
            f.Ui.OnCreate = () => {
                Throws<InvalidOperationException>(() => f.Presentation!.Dispose());
                Check(!f.Presentation!.ConsumersEnded);
            };
            f.Present(); Equal(1, f.Ui.Creates);
            Check(!f.Sink.HasUnknownCleanup && !f.Presentation!.ConsumersEnded);
            Check(f.Sink.Close(f.Session).Success && f.Presentation.ConsumersEnded);
            Equal(1, f.Ui.Window!.DisposeCalls);
            Check(f.Ui.Window.Controls.All(item => item.DisposeCalls == 1));
        }
    }
    private static void TypedActions()
    {
        using var f = new Fixture(); var calls = new List<LocalRunHostResultAction>();
        f.Execute = (session, action) => {
            Check(ReferenceEquals(session, f.Session)); calls.Add(action);
            return action == LocalRunHostResultAction.Retry ? Result.Fail(new(ErrorCode.OperationUnsupported, "Retry backend is not ready.")) : Result.Ok();
        };
        f.Present(); f.Ui.Click("retry");
        Equal(1, calls.Count); Check(f.Ui.Status.Contains("OperationUnsupported") && f.Ui.Status.Contains("Retry backend is not ready."));
        Equal(ErrorCode.OperationUnsupported, f.Sink.ReadFailure(f.Session)!.Code);
        Check(!f.Sink.HasUnknownCleanup && !f.Presentation!.ConsumersEnded);
        f.Ui.Click("return"); f.Ui.Click("return"); f.Ui.Click("retry");
        Equal("Retry,Return", string.Join(",", calls)); Check(f.Ui.Status.Contains("request accepted"));
        Equal(LocalRunSessionPhase.ResultReady, f.Registry.Read(f.Session).Phase);
    }
    private static void CallbackDrain()
    {
        using var f = new Fixture(); var calls = 0;
        f.Execute = (session, _) => {
            calls++; Check(!f.Presentation!.ConsumersEnded);
            Check(f.Sink.Close(session).Success); Check(!f.Presentation.ConsumersEnded);
            return Result.Ok();
        };
        f.Present(); var late = f.Ui.Window!.Controls.Single(item => item.Id == "retry").Click!;
        f.Ui.Click("retry"); Check(f.Presentation!.ConsumersEnded); Check(!f.Sink.HasPendingPresentations);
        late(); late(); Equal(1, calls); Equal(1, f.Ui.Window.DisposeCalls);
        Check(f.Ui.Window.Controls.All(item => item.DisposeCalls == 1));
        // UI end does not close the registry or issue native consumer evidence.
        Equal(LocalRunSessionPhase.ResultReady, f.Registry.Read(f.Session).Phase);
        Check(!f.Registry.Read(f.Session).ProtectionRetired);
    }
    private static void PartialCreation()
    {
        using var f = new Fixture(); f.Ui.FailAdd = 3;
        Throws<InvalidOperationException>(() => f.Present());
        Check(f.Presentation is not null && !f.Presentation.ConsumersEnded && f.Sink.HasUnknownCleanup);
        Equal(2, f.Ui.Window!.Controls.Count);
        Throws<InvalidOperationException>(() => f.Present()); Equal(1, f.Ui.Creates);
        Check(!f.Sink.Close(f.Session).Success); Check(!f.Presentation!.ConsumersEnded);
        Equal(1, f.Ui.Window.DisposeCalls); Check(f.Ui.Window.Controls.All(item => item.DisposeCalls == 1));
        Check(!f.Sink.Close(f.Session).Success); Equal(1, f.Ui.Window.DisposeCalls);
    }
    private static void CleanupFailure()
    {
        using var f = new Fixture(); f.Present();
        var failed = f.Ui.Window!.Controls[2]; failed.ThrowDispose = true;
        Check(!f.Sink.Close(f.Session).Success);
        Check(f.Sink.HasUnknownCleanup && !f.Presentation!.ConsumersEnded);
        Check(f.Ui.Window.Controls.All(item => item.DisposeCalls == 1)); Equal(1, f.Ui.Window.DisposeCalls);
        failed.ThrowDispose = false; Check(!f.Sink.Close(f.Session).Success);
        Check(f.Ui.Window.Controls.All(item => item.DisposeCalls == 1)); Equal(1, f.Ui.Window.DisposeCalls);
    }
    private static void ExternalRetirement()
    {
        using var f = new Fixture(); var calls = 0; f.Execute = (_, _) => { calls++; return Result.Ok(); };
        f.Present(); var late = f.Ui.Window!.Controls[2].Click!;
        f.Ui.Window.Alive = false; f.Sink.Tick(); late();
        Check(f.Sink.HasUnknownCleanup && !f.Presentation!.ConsumersEnded); Equal(0, calls);
        Equal(1, f.Ui.Window.DisposeCalls); Check(!f.Sink.Close(f.Session).Success);
    }
    private static void StaleResult()
    {
        using var f = new Fixture(); var calls = 0; f.Execute = (_, _) => { calls++; return Result.Ok(); };
        f.Present(); f.Registry.BeginReturn(f.Session); f.Ui.Click("return");
        Equal(0, calls); Check(f.Sink.HasUnknownCleanup && f.Ui.Status.Contains("outcome unknown"));
        Check(ReferenceEquals(f.Result, f.Registry.Read(f.Session).Result));
    }
    private static void ActionFailure()
    {
        using var f = new Fixture(); var calls = 0;
        f.Execute = (_, _) => { calls++; throw new IOException("Controlled command outcome unknown."); };
        f.Present(); f.Ui.Click("retry"); f.Ui.Click("return");
        Equal(1, calls); Check(f.Sink.HasUnknownCleanup && f.Ui.Window!.Alive);
        Check(f.Ui.Status.Contains("Controlled command outcome unknown."));
        Check(!f.Presentation!.ConsumersEnded); Equal(ErrorCode.InternalError, f.Sink.ReadFailure(f.Session)!.Code);
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); Task.Run(() => {
            Throws<InvalidOperationException>(() => f.Present());
            Throws<InvalidOperationException>(() => f.Sink.Close(f.Session));
            Throws<InvalidOperationException>(() => f.Sink.Tick());
            Throws<InvalidOperationException>(() => f.Sink.Dispose());
        }).GetAwaiter().GetResult();
        Equal(0, f.Ui.Creates); f.Present();
    }
    private sealed class Fixture : IDisposable
    {
        private static long Next;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunCapturedResult Result;
        internal readonly FakeUi Ui = new();
        internal readonly ResultLog Log = new();
        internal readonly NativeLocalRunResultSink Sink;
        internal ILocalRunTerminationResultPresentation? Presentation;
        internal Func<LocalRunSessionRegistry.SessionHandle, LocalRunHostResultAction, Result> Execute = (_, _) => RizToolbox.Sdk.Result.Ok();
        internal Fixture()
        {
            Registry = new(Main); var id = Interlocked.Add(ref Next, 10) + 500_000_000;
            Session = Registry.RegisterPrepared(new(new("result.sink.test", "song"), 4, "chart"), id + 1, new Lease());
            var commit = Registry.BeginCommit(Session); Registry.BindPendingController(commit, id + 2, new Lease()); Registry.Commit(commit);
            var claim = Registry.TryClaimResult(Session).Claim!;
            Result = Registry.CompleteResult(claim, id + 3, id + 1,
                new(null, null, 123456789, .12345678901234567, 12, 34, 5, 6, true, false) { EndReason = RunEndReason.Failed }, new Lease());
            Sink = new(Main, Registry, Ui, (session, action) => Execute(session, action), Log);
        }
        internal void Present() => Sink.Present(Session, Result, value => Presentation = value);
        public void Dispose()
        {
            try
            {
                try { Sink.Dispose(); } catch { /* Unknown fake UI deliberately cannot certify end. */ }
                if (Registry.Read(Session).Phase != LocalRunSessionPhase.Closed)
                {
                    Registry.BeginReturn(Session); var token = Registry.VerifyConsumersEnded(Session, new Verifier())!;
                    Check(Registry.Close(Session, token));
                }
            }
            finally { Main.Dispose(); }
        }
    }
    private sealed class Lease : IDisposable { public void Dispose() { } }
    private sealed class ResultLog : ILogger
    {
        internal readonly List<(LogLevel Level, string Message)> Rows = new();
        public void Log(LogLevel level, string message, Exception? exception = null) => Rows.Add((level, message));
    }
    private sealed class Verifier : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Controlled SDK UI fixture with no native consumers; not a production retirement proof."; return true; } }
    private sealed class FakeUi : IOwnedUiService
    {
        internal int Creates, FailAdd;
        internal Action? OnCreate, OnFocus;
        internal FakeWindow? Window;
        internal string Status => Window!.Controls.Single(item => item.Id == "status").Text;
        public IReadOnlyList<UiMountInfo> GetMounts() => new[] { new UiMountInfo(UiMountIds.Overlay, true, "test", null, 1, "host") };
        public Result<IUiWindowHandle> CreateWindow(UiWindowDefinition definition)
        { OnCreate?.Invoke(); Creates++; Window = new(definition.Id, FailAdd) { OnFocus = OnFocus }; return Result<IUiWindowHandle>.Ok(Window); }
        internal void Click(string id) => Window!.Controls.Single(item => item.Id == id).Click?.Invoke();
    }
    private sealed class FakeWindow : IUiWindowHandle
    {
        private readonly int _failAdd;
        internal readonly List<FakeControl> Controls = new();
        internal bool Alive = true;
        internal Action? OnFocus;
        internal int DisposeCalls;
        public string Id { get; }
        public bool IsAlive => Alive;
        internal FakeWindow(string id, int failAdd) => (Id, _failAdd) = (id, failAdd);
        public Result<IUiControlHandle> AddLabel(UiControlDefinition definition) => Add(definition, null);
        public Result<IUiControlHandle> AddButton(UiControlDefinition definition, Action clicked) => Add(definition, clicked);
        public Result<IUiControlHandle> AddTextField(UiControlDefinition definition, Action<string>? changed = null, int characterLimit = 1024) => Add(definition, null);
        private Result<IUiControlHandle> Add(UiControlDefinition definition, Action? callback)
        {
            if (_failAdd == Controls.Count + 1) return Result<IUiControlHandle>.Fail(new(ErrorCode.ResourceLoadFailed, "Controlled partial UI creation."));
            var item = new FakeControl(definition.Id, definition.Text, callback); Controls.Add(item); return Result<IUiControlHandle>.Ok(item);
        }
        public Result Focus() { OnFocus?.Invoke(); return Result.Ok(); }
        public Result SetLayout(UiLayout layout) => Result.Ok();
        public Result<UiViewport> GetViewport() => Result<UiViewport>.Fail(new(ErrorCode.OperationUnsupported, "No native viewport in the controlled result UI."));
        public Result<UiPoint> ScreenToUi(UiPoint screenPixels) => Result<UiPoint>.Fail(new(ErrorCode.OperationUnsupported, "No projection."));
        public Result<UiPoint> UiToScreen(UiPoint referencePixels) => Result<UiPoint>.Fail(new(ErrorCode.OperationUnsupported, "No projection."));
        public void Dispose() { DisposeCalls++; Alive = false; }
    }
    private sealed class FakeControl : IUiControlHandle
    {
        internal string Text;
        internal readonly Action? Click;
        internal bool ThrowDispose, ThrowRead;
        internal int DisposeCalls;
        public string Id { get; }
        internal FakeControl(string id, string text, Action? click) => (Id, Text, Click) = (id, text, click);
        public Result SetText(string text) { Text = text; return Result.Ok(); }
        public Result<string> GetText() => ThrowRead ? throw new IOException("Controlled read failure.") : Result<string>.Ok(Text);
        public Result Focus() => Result.Fail(new(ErrorCode.OperationUnsupported, "Not a text field."));
        public void Dispose() { DisposeCalls++; if (ThrowDispose) throw new IOException("Controlled control disposal failure."); }
    }
}
