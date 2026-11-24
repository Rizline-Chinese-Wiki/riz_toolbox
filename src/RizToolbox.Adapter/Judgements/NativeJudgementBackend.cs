using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal interface IJudgementBackend
{
    JudgementRunCapture? CaptureRun();
    ObservedJudgementRun? CurrentRun(JudgementRunCapture run);
    JudgementNoteCapture CaptureNote(JudgementRunCapture run, JudgementCallSite site, NativeJudgeFrame frame);
    IDisposable RetainNote(long note);
    JudgementInnerCapture CaptureInner(JudgementRunCapture run, IntPtr receiver);
}
internal abstract class JudgementRunCapture : IDisposable
{
    internal abstract ObservedJudgementRun Run { get; }
    internal abstract IReadOnlyList<ChartNoteReference> Graph { get; }
    internal virtual NativeSeekChartPlan CaptureSeekPlan(GameplaySeekTargetSeconds target, ISeekChartNativeProfile profile)
        => throw new NotSupportedException("This retained chart has no full-graph seek-plan reader.");
    internal virtual NativeSeekRuntimeNoteView CaptureSeekRuntimeNoteView(ISeekChartNativeProfile profile)
        => throw new NotSupportedException("This retained run has no actual runtime-note view reader.");
    internal virtual NativeSeekJudgeStateView CaptureSeekJudgeStateView(ISeekChartNativeProfile profile)
        => throw new NotSupportedException("This retained run has no actual Judge-state view reader.");
    internal virtual NativeJudgementCounterResetParticipant PrepareJudgementCounterReset(ISeekChartNativeProfile profile)
        => throw new NotSupportedException("This retained run has no actual judgement-counter reset accessor.");
    internal virtual NativeSeekNoteFlagsParticipant PrepareSeekNoteFlags(ISeekChartNativeProfile profile,
        IReadOnlyList<SeekNoteFlagTarget> targets)
        => throw new NotSupportedException("This retained run has no actual explicit note-flags accessor.");
    internal virtual NativeSeekJudgeQueuesParticipant PrepareSeekJudgeQueues(ISeekChartNativeProfile profile,
        SeekJudgeQueueTargets targets)
        => throw new NotSupportedException("This retained run has no actual explicit Judge-queues accessor.");
    internal virtual NativeSeekFingerIdsParticipant PrepareSeekFingerIds(ISeekChartNativeProfile profile,
        IReadOnlyList<SeekFingerIdsTarget> targets)
        => throw new NotSupportedException("This retained run has no actual explicit nullable fingerIds accessor.");
    internal virtual NativeSeekRendererCommands PrepareSeekRendererCommands(ISeekRendererNativeProfile profile,
        IReadOnlyList<SeekRendererCommand> commands)
        => throw new NotSupportedException("This retained run has no actual explicit renderer command/effects accessor.");
    internal virtual NativeSeekTargetStatePlan CaptureSeekTargetStatePlan(ISeekTargetNativeProfile profile,
        GameplaySeekTargetSeconds target, SeekPracticePolicy policy)
        => throw new NotSupportedException("This retained run has no actual native target-state planner source.");
    internal virtual NativeSeekHoldStateParticipant PrepareSeekHoldState(ISeekHoldNativeProfile profile,
        SeekHoldStateTargets targets)
        => throw new NotSupportedException("This retained run has no actual explicit Hold-state accessor.");
    internal virtual NativeSeekNoteTimeParticipant PrepareSeekNoteTime(ISeekChartNativeProfile profile,
        IReadOnlyList<SeekRuntimeTimeTarget> targets)
        => throw new NotSupportedException("This retained run has no actual explicit note-time cache accessor.");
    internal virtual NativeSeekJudgeInitializationParticipant PrepareSeekJudgeInitialization(ISeekTargetNativeProfile profile,
        SeekJudgeInitializationTargets targets)
        => throw new NotSupportedException("This retained run has no actual explicit Judge initialization accessor.");
    internal virtual NativeSeekHoldGeometryCommands PrepareSeekHoldGeometry(ISeekHoldGeometryProfile profile,
        SeekHoldGeometryRequest request)
        => throw new NotSupportedException("This retained run has no actual Hold geometry command accessor.");
    internal virtual NativeSeekCanvasTargetCapture CaptureSeekCanvasTargetSource(ISeekCanvasNativeProfile profile,
        float chartTime, float speed)
        => throw new NotSupportedException("This retained run has no actual Canvas target source reader.");
    internal virtual NativeSeekCanvasPositionCommands PrepareSeekCanvasPositions(ISeekCanvasNativeProfile sourceProfile,
        ISeekCanvasPositionNativeProfile positionProfile, float chartTime, float speed)
        => throw new NotSupportedException("This retained run has no actual Canvas position command accessor.");
    internal virtual NativeSeekCanvasCacheParticipant PrepareSeekCanvasCaches(ISeekCanvasNativeProfile profile,
        float chartTime, float speed)
        => throw new NotSupportedException("This retained run has no actual Canvas cache storage accessor.");
    internal virtual NativeSeekCanvasBasisCommands PrepareSeekCanvasBasis(ISeekCanvasNativeProfile sourceProfile,
        ISeekCanvasPositionNativeProfile positionProfile, float chartTime, float speed)
        => throw new NotSupportedException("This retained run has no same-source Canvas basis command accessor.");
    internal virtual NativeNoteCursorRewindParticipant PrepareCursorRewind(NativeSeekChartPlan plan, ISeekChartNativeProfile profile)
        => throw new NotSupportedException("This retained run has no actual NoteControl cursor accessor.");
    public abstract void Dispose();
}
internal abstract class JudgementNoteCapture : IDisposable
{
    internal abstract long Note { get; }
    internal abstract long Data { get; }
    public abstract void Dispose();
}
internal abstract class JudgementInnerCapture : IDisposable
{
    internal abstract GameplayJudgementCounters ReadCounters();
    public abstract void Dispose();
}

internal sealed partial class NativeJudgementBackend : IJudgementBackend, IDisposable
{
    private readonly NativeRuntime _api;
    private readonly GameEventState _state;
    private readonly HoldCoroutineContext _holdContext = new(() => ExactHookBroker.BooleanInvocationDepth);
    private readonly IntPtr _controller, _info, _chart, _line, _note, _noteData, _owner, _outer, _coroutine, _resources, _object, _scene;
    internal NativeJudgementBackend(NativeRuntime api, GameEventState state)
    {
        _api = api; _state = state;
        IntPtr Type(string name, string ns = "InLevel") => api.Class("Assembly-CSharp.dll", ns, name);
        _controller = Type("NormalModeInLevelController", "");
        _info = Type("LevelInformation"); _chart = Type("Chart"); _line = Type("Line");
        _note = Type("NoteInformation"); _noteData = Type("Note"); _owner = Type("NoteJudgeControl"); _outer = Type("GetJudgeControl");
        _coroutine = api.NestedClass(_owner, "<CheckHold>d__32");
        _resources = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Resources");
        _object = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _scene = api.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene");
        foreach (var cls in new[] { _owner, _outer }) api.RequireInstanceFieldLayout(cls,"levelInformation","InLevel.LevelInformation",0x20);
        api.RequireInstanceFieldLayout(_controller,"LevelInformation","InLevel.LevelInformation",0x28);
        api.RequireInstanceFieldLayout(_info,"chart","InLevel.Chart",0x88);
        api.RequireInstanceFieldLayout(_info,"noteJudgeControl","InLevel.NoteJudgeControl",0x58);
        api.RequireInstanceFieldLayout(_info,"getJudge","InLevel.GetJudgeControl",0x60);
        api.RequireInstanceFieldLayout(_note,"noteData","InLevel.Note",0x28);
        api.RequireInstanceFieldLayout(_coroutine,"<>4__this","InLevel.NoteJudgeControl",0x20);
        api.RequireInstanceFieldLayout(_coroutine,"holdNoteInformation","InLevel.NoteInformation",0x28);
        foreach (var field in new[] { "_bad","_miss","_perfect","_early","_late","_maxPerfect","_challengeTimeMiss" })
            if (api.DescribeFieldType(_controller,field) != "System.Int32") throw new UnsupportedSignatureException("Judgement counter type changed: " + field);
    }
    private bool Alive(IntPtr value) => value != IntPtr.Zero && _api.Unbox<bool>(_api.CallStatic(_object,"op_Implicit",A.Ref(value)));
    internal HoldCoroutineContext.Scope CaptureHold(IntPtr receiver)
    {
        var root = _api.RetainExactObject(receiver, _coroutine);
        return _holdContext.Enter(root.Target.ToInt64(), root);
    }
    public void Dispose()
    {
        _holdMain?.VerifyAccess();
        _seekDisposed = true;
        try { _holdLifecycle?.Dispose(); }
        finally { _holdContext.Dispose(); }
    }
    public JudgementRunCapture? CaptureRun()
    {
        if (_state.RunId is null) return null;
        var roots = new Roots(); NativeRuntime.RootHandle? selected = null;
        try
        {
            var array = roots.Add(_api.Root(_api.CallStatic(_resources,"FindObjectsOfTypeAll",A.Ref(_api.Type(_controller)))));
            var count = 0;
            foreach (var candidate in _api.ArrayItems(array.Target))
            {
                if (++count > 1024) throw new InvalidOperationException("Judgement controller scan exceeded its bound.");
                if (candidate == IntPtr.Zero) continue;
                using var root = _api.RetainExactObject(candidate,_controller);
                if (!Alive(root.Target) || !_api.Unbox<bool>(_api.Call(root.Target,"get_isActiveAndEnabled"))) continue;
                using var go = _api.Root(_api.Call(root.Target,"get_gameObject"));
                var scene = _api.Unbox<int>(_api.Call(go.Target,"get_scene"));
                if (!_api.Unbox<bool>(_api.CallStatic(_scene,"IsValidInternal",A.Value(scene))) ||
                    !_api.Unbox<bool>(_api.CallStatic(_scene,"GetIsLoadedInternal",A.Value(scene)))) continue;
                if (selected is not null) throw new InvalidOperationException("Multiple live normal controllers matched judgement capture.");
                selected = roots.Add(_api.RetainExactObject(candidate,_controller));
            }
            if (selected is null) { roots.Dispose(); return null; }
            var info = roots.Add(_api.RetainExactObject(_api.Read<IntPtr>(selected.Target,"LevelInformation"),_info));
            var run = _state.CaptureJudgementRun(info.Target.ToInt64(),selected.Target.ToInt64());
            if (run is null) { roots.Dispose(); return null; }
            var chart = roots.Add(_api.RetainExactObject(_api.Read<IntPtr>(info.Target,"chart"),_chart));
            var graph = new List<ChartNoteReference>();
            using var lines = _api.Root(_api.Read<IntPtr>(chart.Target,"lines"));
            var lineCount = Count(lines.Target, NativeArrayScanCursor.MaximumArrayLength, "Chart.lines");
            using var expanded = lineCount > MaximumNoteBearingLines ? new ExpandedChartLineList(this, chart, lines, lineCount) : null;
            var noteBearingLines = 0;
            for (var lineIndex = 0; lineIndex < lineCount; ++lineIndex)
            {
                using var line = _api.RetainExactObject(Item(lines.Target,lineIndex),_line);
                using var notes = expanded is null ? _api.Root(_api.Read<IntPtr>(line.Target,"notes")) : expanded.ReadNotes(line);
                var noteCount = Count(notes.Target, RuntimeNoteRegistry.MaximumNotes, "Line.notes", lineIndex);
                if (noteCount == 0) continue;
                if (++noteBearingLines > MaximumNoteBearingLines)
                    throw new InvalidOperationException("Judgement chart exceeds its note-bearing line bound.");
                if (graph.Count + noteCount > RuntimeNoteRegistry.MaximumNotes) throw new InvalidOperationException("Judgement chart exceeds its retained reference bound.");
                for (var noteIndex = 0; noteIndex < noteCount; ++noteIndex)
                {
                    // Independent graph roots keep the initial ordinal association
                    // valid even if a third party edits the chart's lists later.
                    var note = roots.Add(_api.RetainExactObject(Item(notes.Target,noteIndex),_noteData));
                    graph.Add(new(note.Target.ToInt64(),lineIndex,noteIndex));
                }
            }
            if (expanded is not null)
            {
                expanded.VerifyCurrent();
                var current = _state.CaptureJudgementRun(info.Target.ToInt64(), selected.Target.ToInt64());
                if (current is null || !run.SameGeneration(current) || _api.Read<IntPtr>(selected.Target,"LevelInformation") != info.Target
                    || _api.Read<IntPtr>(info.Target,"chart") != chart.Target)
                    throw new InvalidOperationException("The current run changed during judgement chart observation.");
            }
            return new RunValue(this,run,graph.AsReadOnly(),roots,selected,info,chart);
        }
        catch { roots.Dispose(); throw; }
    }
    public ObservedJudgementRun? CurrentRun(JudgementRunCapture capture)
    {
        var run = (RunValue)capture;
        if (!Alive(run.Controller.Target) || !Alive(run.Information.Target) ||
            _api.Read<IntPtr>(run.Controller.Target,"LevelInformation") != run.Information.Target ||
            _api.Read<IntPtr>(run.Information.Target,"chart") != run.Chart.Target) return null;
        return _state.CaptureJudgementRun(run.Information.Target.ToInt64(),run.Controller.Target.ToInt64());
    }
    public JudgementNoteCapture CaptureNote(JudgementRunCapture run, JudgementCallSite site, NativeJudgeFrame frame)
        => new NoteValue(this,(RunValue)run,site,frame);
    public IDisposable RetainNote(long note) => _api.RetainExactObject(new(note),_note);
    public JudgementInnerCapture CaptureInner(JudgementRunCapture capture, IntPtr receiver)
    {
        var run = (RunValue)capture;
        if (receiver != run.Controller.Target || _api.Read<IntPtr>(receiver,"LevelInformation") != run.Information.Target)
            throw new InvalidOperationException("The inner judge receiver does not match the retained caller's observed run.");
        return new InnerValue(_api,_api.RetainExactObject(receiver,_controller));
    }
    private int Count(IntPtr list, int maximum, string? member = null, int? lineOrdinal = null)
    {
        var count = _api.Unbox<int>(_api.Call(list,"get_Count"));
        if (count < 0 || count > maximum)
        {
            if (member is null) throw new InvalidOperationException("Judgement list exceeds its validated traversal bound.");
            var location = lineOrdinal is { } ordinal ? $"{member} at line {ordinal}" : member;
            throw new InvalidOperationException($"Judgement {location} count {count} is outside its validated range 0–{maximum}.");
        }
        return count;
    }
    private IntPtr Item(IntPtr list,int index) => _api.CallTyped(list,"get_Item",new[]{"System.Int32"},A.Value(index));

    private sealed class Roots : IDisposable
    {
        private readonly List<IDisposable> _items = new();
        internal T Add<T>(T root) where T : IDisposable { try { _items.Add(root); return root; } catch { root.Dispose(); throw; } }
        public void Dispose()
        {
            Exception? failure = null;
            while (_items.Count != 0) { var index = _items.Count-1; var root = _items[index]; _items.RemoveAt(index); try { root.Dispose(); } catch (Exception error) { failure ??= error; } }
            if (failure is not null) throw failure;
        }
    }
    private sealed partial class RunValue : JudgementRunCapture
    {
        private readonly NativeJudgementBackend _owner;
        private readonly Roots _roots;
        private bool _disposed;
        private bool _capturingSeekJudgeStateView;
        private bool _preparingSeekNoteFlags;
        private bool _preparingSeekJudgeQueues;
        private bool _preparingSeekFingerIds;
        private bool _preparingSeekRendererCommands;
        private bool _capturingSeekTargetStatePlan;
        private bool _preparingSeekHoldState;
        private bool _preparingSeekHoldGeometry;
        private bool _preparingSeekJudgeInitialization;
        private bool _preparingSeekNoteTime;
        private bool _capturingSeekCanvasTargetSource;
        private long _canvasSourceConstructionSerial;
        internal NativeRuntime.RootHandle Controller { get; }
        internal NativeRuntime.RootHandle Information { get; }
        internal NativeRuntime.RootHandle Chart { get; }
        internal override ObservedJudgementRun Run { get; }
        internal override IReadOnlyList<ChartNoteReference> Graph { get; }
        internal RunValue(NativeJudgementBackend owner,ObservedJudgementRun run,IReadOnlyList<ChartNoteReference> graph,Roots roots,
            NativeRuntime.RootHandle controller,NativeRuntime.RootHandle info,NativeRuntime.RootHandle chart)
            => (_owner,Run,Graph,_roots,Controller,Information,Chart) = (owner,run,graph,roots,controller,info,chart);
        internal override NativeSeekChartPlan CaptureSeekPlan(GameplaySeekTargetSeconds target, ISeekChartNativeProfile profile)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            return NativeSeekChartPlan.Capture(new NativeSeekChartReader(_owner,this,profile),target);
        }
        internal override NativeSeekRuntimeNoteView CaptureSeekRuntimeNoteView(ISeekChartNativeProfile profile)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            return NativeSeekRuntimeNoteView.Capture(new NativeSeekRuntimeNoteReader(_owner, this, profile));
        }
        internal override NativeSeekJudgeStateView CaptureSeekJudgeStateView(ISeekChartNativeProfile profile)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            if (_capturingSeekJudgeStateView)
                throw new InvalidOperationException("Judge-state capture cannot reenter the same retained run.");
            // Include reader construction: its native callbacks can reenter
            // before a view exists and has its own verification guard.
            _capturingSeekJudgeStateView = true;
            try
            {
                return NativeSeekJudgeStateView.Capture(new NativeSeekJudgeStateReader(_owner, this, profile));
            }
            finally { _capturingSeekJudgeStateView = false; }
        }
        internal override NativeJudgementCounterResetParticipant PrepareJudgementCounterReset(ISeekChartNativeProfile profile)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            return NativeJudgementCounterResetParticipant.Prepare(new NativeJudgementCounterAccess(_owner, this, profile));
        }
        internal override NativeSeekNoteFlagsParticipant PrepareSeekNoteFlags(ISeekChartNativeProfile profile,
            IReadOnlyList<SeekNoteFlagTarget> targets)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(targets);
            if (_preparingSeekNoteFlags)
                throw new InvalidOperationException("Note-flags preparation cannot reenter the same retained run.");
            _preparingSeekNoteFlags = true;
            try
            {
                return NativeSeekNoteFlagsParticipant.Prepare(new NativeSeekNoteFlagsAccess(_owner, this, profile), targets);
            }
            finally { _preparingSeekNoteFlags = false; }
        }
        internal override NativeSeekJudgeQueuesParticipant PrepareSeekJudgeQueues(ISeekChartNativeProfile profile,
            SeekJudgeQueueTargets targets)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(targets);
            if (_preparingSeekJudgeQueues)
                throw new InvalidOperationException("Judge-queues preparation cannot reenter the same retained run.");
            _preparingSeekJudgeQueues = true;
            try
            {
                return NativeSeekJudgeQueuesParticipant.Prepare(new NativeSeekJudgeStateReader(_owner, this, profile, queueWriter: true), targets);
            }
            finally { _preparingSeekJudgeQueues = false; }
        }
        internal override NativeSeekFingerIdsParticipant PrepareSeekFingerIds(ISeekChartNativeProfile profile,
            IReadOnlyList<SeekFingerIdsTarget> targets)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(targets);
            if (_preparingSeekFingerIds)
                throw new InvalidOperationException("FingerIds preparation cannot reenter the same retained run.");
            _preparingSeekFingerIds = true;
            try
            {
                return NativeSeekFingerIdsParticipant.Prepare(new NativeSeekNoteFlagsAccess(_owner, this, profile, fingerIds: true), targets);
            }
            finally { _preparingSeekFingerIds = false; }
        }
        internal override NativeSeekRendererCommands PrepareSeekRendererCommands(ISeekRendererNativeProfile profile,
            IReadOnlyList<SeekRendererCommand> commands)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(commands); ArgumentNullException.ThrowIfNull(profile);
            if (_preparingSeekRendererCommands)
                throw new InvalidOperationException("Renderer command preparation cannot reenter the same retained run.");
            _preparingSeekRendererCommands = true;
            try
            {
                return NativeSeekRendererCommands.Prepare(() => new NativeSeekRendererAccess(_owner, this, profile), commands);
            }
            finally { _preparingSeekRendererCommands = false; }
        }
        internal override NativeSeekTargetStatePlan CaptureSeekTargetStatePlan(ISeekTargetNativeProfile profile,
            GameplaySeekTargetSeconds target, SeekPracticePolicy policy)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(policy);
            if (_capturingSeekTargetStatePlan) throw new InvalidOperationException("Target-state capture cannot reenter the same retained run.");
            _capturingSeekTargetStatePlan = true;
            try { return NativeSeekTargetStatePlan.Capture(CreateSeekTargetStateReader(_owner, this, profile, target), policy); }
            finally { _capturingSeekTargetStatePlan = false; }
        }
        internal override NativeSeekHoldStateParticipant PrepareSeekHoldState(ISeekHoldNativeProfile profile,
            SeekHoldStateTargets targets)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(targets);
            if (_preparingSeekHoldState)
                throw new InvalidOperationException("Hold-state preparation cannot reenter the same retained run.");
            _preparingSeekHoldState = true;
            try
            {
                return NativeSeekHoldStateParticipant.Prepare(() => CreateSeekHoldStateAccess(_owner, this, profile), targets);
            }
            finally { _preparingSeekHoldState = false; }
        }
        internal override NativeSeekNoteTimeParticipant PrepareSeekNoteTime(ISeekChartNativeProfile profile,
            IReadOnlyList<SeekRuntimeTimeTarget> targets)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(targets);
            if (_preparingSeekNoteTime) throw new InvalidOperationException("Note-time preparation cannot reenter the same retained run.");
            _preparingSeekNoteTime = true;
            try { return NativeSeekNoteTimeParticipant.Prepare(() => CreateNoteTimeAccess(_owner, this, profile), targets); }
            finally { _preparingSeekNoteTime = false; }
        }
        internal override NativeSeekJudgeInitializationParticipant PrepareSeekJudgeInitialization(ISeekTargetNativeProfile profile,
            SeekJudgeInitializationTargets targets)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(targets);
            if (_preparingSeekJudgeInitialization)
                throw new InvalidOperationException("Judge initialization preparation cannot reenter the same retained run.");
            _preparingSeekJudgeInitialization = true;
            try { return NativeSeekJudgeInitializationParticipant.Prepare(() => CreateJudgeInitializationAccess(_owner, this, profile), targets); }
            finally { _preparingSeekJudgeInitialization = false; }
        }
        internal override NativeSeekHoldGeometryCommands PrepareSeekHoldGeometry(ISeekHoldGeometryProfile profile,
            SeekHoldGeometryRequest request)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(request);
            if (_preparingSeekHoldGeometry)
                throw new InvalidOperationException("Hold geometry preparation cannot reenter the same retained run.");
            _preparingSeekHoldGeometry = true;
            try { return NativeSeekHoldGeometryCommands.Prepare(() => CreateSeekHoldGeometryAccess(_owner, this, profile), request); }
            finally { _preparingSeekHoldGeometry = false; }
        }
        internal override NativeNoteCursorRewindParticipant PrepareCursorRewind(NativeSeekChartPlan plan, ISeekChartNativeProfile profile)
        {
            Controller.VerifyOwnerThread();
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            return NativeNoteCursorRewindParticipant.Prepare(new NativeNoteCursorAccess(_owner, this, plan, profile));
        }
        internal long CanvasSourceConstructionSerial
        {
            get { Controller.VerifyOwnerThread(); return _canvasSourceConstructionSerial; }
        }
        internal void VerifyCanvasSourceConstruction(long serial)
        {
            Controller.VerifyOwnerThread();
            if (_capturingSeekCanvasTargetSource && serial == _canvasSourceConstructionSerial && _disposed)
                throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
        }
        internal override NativeSeekCanvasTargetCapture CaptureSeekCanvasTargetSource(ISeekCanvasNativeProfile profile,
            float chartTime, float speed)
        {
            Controller.VerifyOwnerThread();
            // This admission covers construction itself, before argument/profile
            // access or the first independent source/native publication exists.
            if (_capturingSeekCanvasTargetSource)
                throw new InvalidOperationException("Canvas source capture cannot reenter the same retained run.");
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(profile);
            if (!float.IsFinite(chartTime) || !float.IsFinite(speed))
                throw new ArgumentOutOfRangeException(nameof(chartTime), "Canvas targets require finite chart-time and speed inputs.");
            _canvasSourceConstructionSerial = checked(_canvasSourceConstructionSerial + 1);
            _capturingSeekCanvasTargetSource = true;
            try { return NativeSeekCanvasTargetCapture.Capture(() => CreateSeekCanvasTargetReader(_owner, this, profile), chartTime, speed); }
            finally { _capturingSeekCanvasTargetSource = false; }
        }
        internal override NativeSeekCanvasPositionCommands PrepareSeekCanvasPositions(ISeekCanvasNativeProfile sourceProfile,
            ISeekCanvasPositionNativeProfile positionProfile, float chartTime, float speed)
        {
            Controller.VerifyOwnerThread();
            if (_capturingSeekCanvasTargetSource)
                throw new InvalidOperationException("Canvas source/position preparation cannot reenter the same retained run.");
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(sourceProfile); ArgumentNullException.ThrowIfNull(positionProfile);
            if (!float.IsFinite(chartTime) || !float.IsFinite(speed)) throw new ArgumentOutOfRangeException(nameof(chartTime));
            _canvasSourceConstructionSerial = checked(_canvasSourceConstructionSerial + 1);
            _capturingSeekCanvasTargetSource = true;
            try
            {
                return NativeSeekCanvasPositionCommands.Prepare(() =>
                    CreateSeekCanvasPositionAccess(_owner, this, sourceProfile, positionProfile, chartTime, speed));
            }
            finally { _capturingSeekCanvasTargetSource = false; }
        }
        internal override NativeSeekCanvasCacheParticipant PrepareSeekCanvasCaches(ISeekCanvasNativeProfile profile,
            float chartTime, float speed)
        {
            Controller.VerifyOwnerThread();
            if (_capturingSeekCanvasTargetSource)
                throw new InvalidOperationException("Canvas cache/source preparation cannot reenter the same retained run.");
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(profile);
            if (!float.IsFinite(chartTime) || !float.IsFinite(speed)) throw new ArgumentOutOfRangeException(nameof(chartTime));
            _canvasSourceConstructionSerial = checked(_canvasSourceConstructionSerial + 1);
            _capturingSeekCanvasTargetSource = true;
            try { return NativeSeekCanvasCacheParticipant.Prepare(() => CreateSeekCanvasCacheAccess(_owner, this, profile, chartTime, speed)); }
            finally { _capturingSeekCanvasTargetSource = false; }
        }
        internal override NativeSeekCanvasBasisCommands PrepareSeekCanvasBasis(ISeekCanvasNativeProfile sourceProfile,
            ISeekCanvasPositionNativeProfile positionProfile, float chartTime, float speed)
        {
            Controller.VerifyOwnerThread();
            if (_capturingSeekCanvasTargetSource)
                throw new InvalidOperationException("Canvas basis/source preparation cannot reenter the same retained run.");
            if (_disposed) throw new ObjectDisposedException(nameof(JudgementRunCapture));
            if (_owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
            ArgumentNullException.ThrowIfNull(sourceProfile); ArgumentNullException.ThrowIfNull(positionProfile);
            if (!float.IsFinite(chartTime) || !float.IsFinite(speed)) throw new ArgumentOutOfRangeException(nameof(chartTime));
            _canvasSourceConstructionSerial = checked(_canvasSourceConstructionSerial + 1);
            _capturingSeekCanvasTargetSource = true;
            try
            {
                return NativeSeekCanvasBasisCommands.Prepare(() =>
                    CreateSeekCanvasBasisAccess(_owner, this, sourceProfile, chartTime, speed), positionProfile);
            }
            finally { _capturingSeekCanvasTargetSource = false; }
        }
        public override void Dispose()
        {
            Controller.VerifyOwnerThread();
            if (_disposed) return;
            _disposed = true; _roots.Dispose();
        }
    }
    private sealed class NoteValue : JudgementNoteCapture, IJudgementNoteSourceReader
    {
        private readonly NativeJudgementBackend _owner; private readonly RunValue _run; private readonly Roots _roots = new();
        internal override long Note { get; }
        internal override long Data { get; }
        internal NoteValue(NativeJudgementBackend owner,RunValue run,JudgementCallSite site,NativeJudgeFrame frame)
        {
            _owner = owner; _run = run;
            try
            {
                var outer = _roots.Add(owner._api.RetainExactObject(frame.Receiver,owner._outer));
                if (owner._api.Read<IntPtr>(outer.Target,"levelInformation") != run.Information.Target ||
                    owner._api.Read<IntPtr>(run.Information.Target,"getJudge") != outer.Target)
                    throw new InvalidOperationException("Outer judgement owner differs from the retained run.");
                var source = JudgementSourceResolver.Resolve(site,frame,this); Note = source.Note;
                Data = _roots.Add(owner._api.RetainExactObject(owner._api.Read<IntPtr>(new(Note),"noteData"),owner._noteData)).Target.ToInt64();
            }
            catch { _roots.Dispose(); throw; }
        }
        public void RequireOwner(long value)
        {
            var root = _roots.Add(_owner._api.RetainExactObject(new(value),_owner._owner));
            if (_owner._api.Read<IntPtr>(root.Target,"levelInformation") != _run.Information.Target ||
                _owner._api.Read<IntPtr>(_run.Information.Target,"noteJudgeControl") != root.Target)
                throw new InvalidOperationException("Caller note-judge owner differs from the retained run.");
        }
        public void RequireNote(long value) => _roots.Add(_owner._api.RetainExactObject(new(value),_owner._note));
        public long ReadWaitingNote(long owner,bool drag,int index)
        {
            var list = _roots.Add(_owner._api.Root(_owner._api.Read<IntPtr>(new(owner),drag ? "dragNotesWaitingForJudge" : "tapNotesWaitingForJudge")));
            var count = _owner.Count(list.Target,RuntimeNoteRegistry.MaximumNotes);
            if (index < 0 || index >= count) throw new InvalidOperationException("Caller waiting-note index is outside the actual list.");
            return _owner.Item(list.Target,index).ToInt64();
        }
        public long ReadHoldNote(long expectedOwner)
        {
            var coroutine = _owner._holdContext.Current();
            var root = _roots.Add(_owner._api.RetainExactObject(new(coroutine),_owner._coroutine));
            if (_owner._api.Read<IntPtr>(root.Target,"<>4__this").ToInt64() != expectedOwner)
                throw new InvalidOperationException("Hold coroutine belongs to another caller.");
            return _owner._api.Read<IntPtr>(root.Target,"holdNoteInformation").ToInt64();
        }
        public override void Dispose() => _roots.Dispose();
    }
    private sealed class InnerValue : JudgementInnerCapture
    {
        private readonly NativeRuntime _api; private readonly NativeRuntime.RootHandle _controller;
        internal InnerValue(NativeRuntime api,NativeRuntime.RootHandle controller) => (_api,_controller) = (api,controller);
        internal override GameplayJudgementCounters ReadCounters() => new(
            _api.Read<int>(_controller.Target,"_bad"),_api.Read<int>(_controller.Target,"_miss"),_api.Read<int>(_controller.Target,"_perfect"),
            _api.Read<int>(_controller.Target,"_early"),_api.Read<int>(_controller.Target,"_late"),_api.Read<int>(_controller.Target,"_maxPerfect"),
            _api.Read<int>(_controller.Target,"_challengeTimeMiss"));
        public override void Dispose() => _controller.Dispose();
    }
}
