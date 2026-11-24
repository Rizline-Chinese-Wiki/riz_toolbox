using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    /// <summary>Owns five small source roots and a separate r4 NoteView. The view
    /// verifies the complete current chart and runtime reference graph without
    /// depending on cursor values, a borrowed plan or a caller RunValue lifetime.
    /// Only the seven named counter fields can reach WriteExactGuarded.</summary>
    private sealed class NativeJudgementCounterAccess : INativeJudgementCounterAccess
    {
        private static readonly string[] CounterNames =
            { "_bad", "_miss", "_perfect", "_early", "_late", "_maxPerfect", "_challengeTimeMiss" };
        private readonly NativeJudgementBackend _owner;
        private readonly ObservedJudgementRun _run;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        private readonly Dictionary<long, NativeRuntime.RootHandle> _retained = new();
        private readonly Dictionary<NativeRuntime.RootHandle, long> _addresses = new();
        private readonly HashSet<NativeRuntime.RootHandle> _unknownFrees = new();
        private readonly List<Exception> _cleanupFailures = new();
        private readonly NativeExactReadField[] _counterFields = new NativeExactReadField[7];
        private readonly NativeRuntime.RootHandle _controller, _information, _chart, _progress, _pause;
        private readonly IntPtr _progressClass, _pauseClass, _timeClass, _touchBlockerClass, _windowClass;
        private readonly long _controllerAddress, _informationAddress, _chartAddress, _progressAddress, _pauseAddress;
        private NativeSeekRuntimeNoteView? _view;
        private Action? _lifetime;
        private bool _withdrawn, _faulted, _cleaning, _closingView, _viewCleanupFailed;

        internal NativeJudgementCounterAccess(NativeJudgementBackend owner, RunValue run, ISeekChartNativeProfile profile)
        {
            _owner = owner; _run = run.Run;
            _controllerAddress = _run.Controller; _informationAddress = _run.Information;
            try
            {
                ArgumentNullException.ThrowIfNull(profile);
                VerifyLocal();
                // The r4 reader owns each chart-only and actual runtime reference.
                // It binds its one lifetime guard to this view, not to a counter
                // or cursor participant. RequestDispose closes that view directly.
                _view = Native(() => NativeSeekRuntimeNoteView.Capture(new NativeSeekRuntimeNoteReader(owner, run, profile)));
                if (_view.Run != _run || _view.Identity.Chart.Run != _run)
                    throw new InvalidOperationException("The independent counter graph belongs to another exact run.");
                _chartAddress = _view.Identity.Chart.Chart; _progressAddress = _view.Identity.Chart.Progress;
                _progressClass = Native(() => profile.ProgressClass);
                if (Native(() => profile.ChartClass) != owner._chart || _progressClass == IntPtr.Zero)
                    throw new UnsupportedSignatureException("The counter source profile has another chart/progress identity.");
                _pauseClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "PauseUIControl"));
                _timeClass = Native(() => owner._api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time"));
                _touchBlockerClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "TouchBlocker"));
                _windowClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "WindowControl"));
                _controller = Exact(new(_controllerAddress), owner._controller);
                _information = Exact(new(_informationAddress), owner._info);
                _chart = Exact(new(_chartAddress), owner._chart);
                _progress = Exact(new(_progressAddress), _progressClass);
                _pause = ReferenceField(_controller, owner._controller, "pauseUIControl", "PauseUIControl", 0xb0);
                if (Native(() => owner._api.ObjectClass(Target(_pause))) != _pauseClass)
                    throw new UnsupportedSignatureException("The counter source has another exact pause class.");
                _pauseAddress = Target(_pause).ToInt64();
                CaptureCounterFields();
                VerifyCurrent();
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup)
                {
                    error.Data["JudgementCounterCleanupOwner"] = this;
                    error.Data["JudgementCounterCleanupFailure"] = cleanup;
                }
                throw;
            }
        }
        public void VerifyOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("The native seven-counter source belongs to its owner thread.");
        }
        public void BindLifetimeGuard(Action guard)
        {
            VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
            if (_lifetime is not null) throw new InvalidOperationException("The counter lifetime guard is once-attached.");
            _lifetime = guard;
        }
        public void VerifyLocal()
        {
            VerifyOwnerThread();
            _lifetime?.Invoke();
            if (_withdrawn || _owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementCounterAccess));
            if (_faulted) throw new InvalidOperationException("The actual seven-counter source was already observed expired.");
            if (_owner._state.CaptureJudgementRun(_informationAddress, _controllerAddress) != _run
                || !_owner._state.MatchesCurrentGameplayRun(_informationAddress, _controllerAddress, _run.RunId, _run.SceneGeneration))
            {
                _faulted = true;
                throw new InvalidOperationException("The seven-counter source lost its exact managed run revision.");
            }
        }
        private T Native<T>(Func<T> operation)
        {
            VerifyLocal();
            var result = operation();
            VerifyLocal();
            return result;
        }
        private IntPtr Target(NativeRuntime.RootHandle root)
        {
            var value = Native(() => root.Target);
            if (!_addresses.TryGetValue(root, out var expected) || value.ToInt64() != expected)
                throw new InvalidOperationException("An independently rooted counter receiver changed identity.");
            return value;
        }
        private NativeRuntime.RootHandle Adopt(IntPtr value)
        {
            VerifyLocal();
            if (value == IntPtr.Zero) throw new InvalidDataException("A required counter source reference is absent.");
            if (_retained.TryGetValue(value.ToInt64(), out var existing)) return existing;
            if (_roots.Count >= 5) throw new InvalidOperationException("The fixed counter source exceeded its five-root bound.");
            // Record ownership before the post-call lifecycle guard can fail.
            var root = _owner._api.Root(value);
            _roots.Add(root); _retained.Add(value.ToInt64(), root); _addresses.Add(root, value.ToInt64());
            VerifyLocal();
            return root;
        }
        private NativeRuntime.RootHandle Exact(IntPtr value, IntPtr expectedClass)
        {
            var root = Adopt(value);
            if (Native(() => _owner._api.ObjectClass(Target(root))) != expectedClass)
                throw new UnsupportedSignatureException("The counter graph reference has another exact class.");
            return root;
        }
        private NativeRuntime.RootHandle ReferenceField(NativeRuntime.RootHandle owner, IntPtr declaringClass,
            string name, string type, int offset)
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(declaringClass, name, type, false, offset));
            NativeRuntime.RootHandle? result = null;
            Native(() =>
            {
                _owner._api.PublishExactReferenceField(owner, field, value => result = Adopt(value));
                return 0;
            });
            var root = result ?? throw new InvalidDataException("The counter source reference was not adopted at publication.");
            if (Native(() => _owner._api.ObjectClass(Target(root))) != field.FieldClass)
                throw new UnsupportedSignatureException("The counter source field has another exact reference type.");
            return root;
        }
        private T ValueField<T>(NativeRuntime.RootHandle owner, IntPtr cls, string name, string type, int offset) where T : struct
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
            return Native(() => _owner._api.ReadExactField<T>(owner, field));
        }
        private long ReferenceToken(NativeRuntime.RootHandle owner, IntPtr cls, string name, string type, int offset)
        {
            Native(() => _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
            var receiver = Target(owner);
            // The expected object already has its own root. Compare only the
            // returned address; no class/getter is dispatched on a foreign token.
            return Native(() => _owner._api.ReadWithPublication<IntPtr>(receiver, name)).ToInt64();
        }
        private bool InvokeBoolean(IntPtr cls, string name, NativeRuntime.RootHandle? receiver = null)
        {
            var method = Native(() => _owner._api.Method(cls, name, 0, isStatic: receiver is null));
            var target = receiver is null ? IntPtr.Zero : Target(receiver);
            return Native(() => _owner._api.InvokeValueWithPublication<bool>(method, target));
        }
        private bool Alive(NativeRuntime.RootHandle root)
        {
            var target = Target(root);
            var method = Native(() => _owner._api.Method(_owner._object, "op_Implicit", 1, isStatic: true,
                parameterTypes: new[] { "UnityEngine.Object" }));
            return Native(() => _owner._api.InvokeValueWithPublication<bool>(method, IntPtr.Zero, A.Ref(target)));
        }
        private float SceneTime()
        {
            var method = Native(() => _owner._api.Method(_timeClass, "get_timeSinceLevelLoad", 0, isStatic: true));
            return Native(() => _owner._api.InvokeValueWithPublication<float>(method, IntPtr.Zero));
        }
        private void CaptureCounterFields()
        {
            for (var slot = 0; slot < CounterNames.Length; ++slot)
            {
                // No counter offset is guessed. The official declared-field
                // descriptor supplies and bounds its actual offset and size.
                var field = Native(() => _owner._api.InspectExactDeclaredReadField(
                    _owner._controller, CounterNames[slot], "System.Int32", false));
                if (field.DeclaringClass != _owner._controller || field.TypeName != "System.Int32" || field.Size != sizeof(int)
                    || field.IsStatic || (field.Flags & (0x10u | 0x20u | 0x40u)) != 0)
                    throw new UnsupportedSignatureException("A selected counter is not exact mutable instance Int32 storage.");
                for (var previous = 0; previous < slot; ++previous)
                    if (_counterFields[previous].Field == field.Field
                        || (long)_counterFields[previous].Offset < (long)field.Offset + field.Size
                            && (long)field.Offset < (long)_counterFields[previous].Offset + _counterFields[previous].Size)
                        throw new UnsupportedSignatureException("Selected counter metadata aliases another selected storage slot.");
                _counterFields[slot] = field;
            }
        }
        private NativeExactReadField CounterField(int slot)
        {
            if ((uint)slot >= 7) throw new ArgumentOutOfRangeException(nameof(slot));
            try
            {
                var baseline = _counterFields[slot];
                var current = Native(() => _owner._api.InspectExactDeclaredReadField(
                    _owner._controller, CounterNames[slot], "System.Int32", false, baseline.Offset));
                if (current != baseline) throw new UnsupportedSignatureException("A prepared counter's official storage descriptor changed.");
                return current;
            }
            catch { _faulted = true; throw; }
        }
        private void VerifyCurrent()
        {
            try
            {
                VerifyLocal();
                (_view ?? throw new InvalidOperationException("The independent complete counter graph is absent.")).VerifySource();
                VerifyLocal();
                foreach (var pair in new[] { (_controller, _owner._controller), (_information, _owner._info),
                    (_chart, _owner._chart), (_progress, _progressClass), (_pause, _pauseClass) })
                    if (Native(() => _owner._api.ObjectClass(Target(pair.Item1))) != pair.Item2)
                        throw new UnsupportedSignatureException("A retained counter source changed its exact class.");
                if (!Native(() => _owner._api.MatchesNormalControllerInformation(_controller, _information,
                    _owner._controller, _owner._info, VerifyLocal))
                    || ReferenceToken(_information, _owner._info, "chart", "InLevel.Chart", 0x88) != _chartAddress
                    || ReferenceToken(_information, _owner._info, "progressControl", "InLevel.ProgressControl", 0x28) != _progressAddress
                    || ReferenceToken(_progress, _progressClass, "levelInformation", "InLevel.LevelInformation", 0x20) != _informationAddress
                    || ReferenceToken(_controller, _owner._controller, "pauseUIControl", "PauseUIControl", 0xb0) != _pauseAddress)
                    throw new InvalidOperationException("The actual counter run/chart/progress/pause association changed.");
                if (!Alive(_controller) || !Alive(_information) || !Alive(_progress) || !Alive(_pause)
                    || !InvokeBoolean(_owner._controller, "get_isActiveAndEnabled", _controller)
                    || !InvokeBoolean(_pauseClass, "get_isActiveAndEnabled", _pause))
                    throw new InvalidOperationException("The retained counter source is inactive or dead.");
                var countdown = ValueField<float>(_pause, _pauseClass, "_pauseTime", "System.Single", 0xbc);
                var sceneTime = SceneTime();
                if (!ValueField<bool>(_information, _owner._info, "loadOver", "System.Boolean", 0xbe)
                    || ValueField<bool>(_information, _owner._info, "levelOver", "System.Boolean", 0xc0)
                    || ValueField<bool>(_information, _owner._info, "crashed", "System.Boolean", 0xc1)
                    || !ValueField<bool>(_progress, _progressClass, "timeReady", "System.Boolean", 0x65)
                    || ValueField<bool>(_progress, _progressClass, "isPlaying", "System.Boolean", 0x42)
                    || ValueField<bool>(_progress, _progressClass, "prepareToPlay", "System.Boolean", 0x40)
                    || !ValueField<bool>(_pause, _pauseClass, "_pausing", "System.Boolean", 0xb4)
                    || ValueField<bool>(_pause, _pauseClass, "_over", "System.Boolean", 0xc0)
                    || ValueField<bool>(_pause, _pauseClass, "_terminated", "System.Boolean", 0xc1)
                    || !float.IsFinite(countdown) || countdown > 0 || !float.IsFinite(sceneTime) || sceneTime < 1.8f
                    || !InvokeBoolean(_pauseClass, "get_CanDoOperation", _pause)
                    || InvokeBoolean(_touchBlockerClass, "get_Blocked") || InvokeBoolean(_windowClass, "get_HasActiveWindow"))
                    throw new InvalidOperationException("Counter reset requires the actual ready paused non-ending source.");
                VerifyLocal();
            }
            catch (Exception error)
            {
                _faulted = true;
                // A NoteView may defer a failed native free until its in-flight
                // verification exits. Keep its original cleanup exception/owner.
                if (error.Data["SeekRuntimeNoteCleanupFailure"] is Exception cleanup) RecordViewFailure(cleanup);
                throw;
            }
        }
        private GameplayJudgementCounters ReadSeven()
        {
            int Read(int slot)
            {
                var field = CounterField(slot);
                return Native(() => _owner._api.ReadExactField<int>(_controller, field));
            }
            return new(Read(0), Read(1), Read(2), Read(3), Read(4), Read(5), Read(6));
        }
        public GameplayJudgementCounters Read()
        {
            VerifyCurrent();
            var first = ReadSeven();
            VerifyCurrent();
            var second = ReadSeven();
            VerifyLocal();
            if (first != second) throw new InvalidOperationException("The seven actual counter samples changed during observation.");
            return second;
        }
        public void Write(int slot, int value, int expectedCurrent, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            if ((uint)slot >= 7) throw new ArgumentOutOfRangeException(nameof(slot));
            VerifyCurrent();
            var field = CounterField(slot);
            // Source verification can call getters before this selected-slot
            // sample. Preserve any intervening external value before entering
            // the setter. The later managed guard is not an atomic native CAS.
            if (Native(() => _owner._api.ReadExactField<int>(_controller, field)) != expectedCurrent)
                throw new InvalidOperationException("The selected counter changed before its conditional setter.");
            Native(() =>
            {
                using var argument = A.Value(value);
                _owner._api.WriteExactGuarded(_controller, _owner._controller, CounterNames[slot], "System.Int32", argument, () =>
                {
                    VerifyLocal(); beforeDispatch(); VerifyLocal();
                });
                return 0;
            });
        }
        private void RecordViewFailure(Exception error)
        {
            if (_viewCleanupFailed) return;
            _viewCleanupFailed = true;
            _cleanupFailures.Add(error);
        }
        private void CloseView()
        {
            if (_closingView || _view is null || _viewCleanupFailed) return;
            _closingView = true;
            try { _view.Dispose(); }
            catch (Exception error) { RecordViewFailure(error); }
            finally { _closingView = false; }
        }
        public void RequestDispose()
        {
            VerifyOwnerThread();
            _withdrawn = true;
            CloseView(); // Sets NoteView._disposed even when its roots are in flight.
            if (_cleanupFailures.Count != 0) throw CleanupError();
        }
        private AggregateException CleanupError()
        {
            var error = new AggregateException("Seven-counter cleanup retains unknown native responsibility without retry.", _cleanupFailures);
            error.Data["JudgementCounterCleanupOwner"] = this;
            return error;
        }
        public void Dispose()
        {
            VerifyOwnerThread();
            if (_cleaning) return;
            _withdrawn = true; _cleaning = true;
            try
            {
                CloseView();
                for (var index = _roots.Count - 1; index >= 0; --index)
                {
                    var root = _roots[index];
                    if (_unknownFrees.Contains(root)) continue;
                    try { root.Dispose(); _roots.RemoveAt(index); }
                    catch (Exception error) { _unknownFrees.Add(root); _cleanupFailures.Add(error); }
                }
                if (_roots.Count == 0) { _retained.Clear(); _addresses.Clear(); }
                if (_cleanupFailures.Count != 0) throw CleanupError();
            }
            finally { _cleaning = false; }
        }
    }
}
