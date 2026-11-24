using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    /// <summary>Owns exact note receivers, six source roots and a separate NoteView. The view
    /// verifies the complete current chart and runtime reference graph without
    /// depending on cursor values, a borrowed plan or a caller RunValue lifetime.
    /// The default flags constructor permits three selected Boolean fields. An
    /// explicit fingerIds constructor in its partial supplies separate access.</summary>
    private sealed partial class NativeSeekNoteFlagsAccess : INativeSeekNoteFlagsAccess
    {
        private static readonly string[] FlagNames =
            { "isActive", "destroyed", "missed" };
        private static readonly int[] FlagOffsets = { 0x30, 0x31, 0x55 };
        private readonly NativeJudgementBackend _owner;
        private readonly ObservedJudgementRun _run;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        private int _rootLimit = 6 + NativeSeekRuntimeNoteView.MaximumNotes;
        private readonly Dictionary<long, NativeRuntime.RootHandle> _retained = new();
        private readonly Dictionary<NativeRuntime.RootHandle, long> _addresses = new();
        private readonly HashSet<NativeRuntime.RootHandle> _unknownFrees = new();
        private readonly List<Exception> _cleanupFailures = new();
        private readonly NativeExactReadField[] _flagFields = new NativeExactReadField[3];
        private readonly NativeRuntime.RootHandle _controller, _information, _chart, _progress, _pause, _control;
        private readonly IntPtr _progressClass, _pauseClass, _controlClass, _timeClass, _touchBlockerClass, _windowClass;
        private readonly long _controllerAddress, _informationAddress, _chartAddress, _progressAddress, _pauseAddress, _controlAddress;
        private NativeSeekRuntimeNoteView? _view;
        private readonly SeekRuntimeNoteSourceIdentity _noteViewIdentity;
        private readonly SeekRuntimeNote[] _capturedNotes;
        private readonly NativeRuntime.RootHandle[] _notes;
        private readonly long[] _noteReferences;
        private Action? _lifetime;
        private readonly Action? _canvasUseGuard;
        private bool _withdrawn, _faulted, _cleaning, _closingView, _viewCleanupFailed;

        internal NativeSeekNoteFlagsAccess(NativeJudgementBackend owner, RunValue run, ISeekChartNativeProfile profile,
            Action? canvasUseGuard = null)
        {
            _owner = owner; _run = run.Run; _canvasUseGuard = canvasUseGuard;
            _controllerAddress = _run.Controller; _informationAddress = _run.Information;
            try
            {
                ArgumentNullException.ThrowIfNull(profile);
                VerifyLocal();
                // The r4 reader owns each chart-only and actual runtime reference.
                // It binds its one lifetime guard to this view, not to a note-flags
                // or cursor participant. RequestDispose closes that view directly.
                _view = Native(() => NativeSeekRuntimeNoteView.Capture(new NativeSeekRuntimeNoteReader(owner, run, profile, canvasUseGuard)));
                if (_view.Run != _run || _view.Identity.Chart.Run != _run)
                    throw new InvalidOperationException("The independent note-flags graph belongs to another exact run.");
                _noteViewIdentity = _view.Identity;
                _controlAddress = _noteViewIdentity.NoteControl;
                _chartAddress = _view.Identity.Chart.Chart; _progressAddress = _view.Identity.Chart.Progress;
                _progressClass = Native(() => profile.ProgressClass);
                if (Native(() => profile.ChartClass) != owner._chart || _progressClass == IntPtr.Zero)
                    throw new UnsupportedSignatureException("The note-flags source profile has another chart/progress identity.");
                _pauseClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "PauseUIControl"));
                _controlClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "InLevel", "NoteControl"));
                _timeClass = Native(() => owner._api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time"));
                _touchBlockerClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "TouchBlocker"));
                _windowClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "WindowControl"));
                _controller = Exact(new(_controllerAddress), owner._controller);
                _information = Exact(new(_informationAddress), owner._info);
                _chart = Exact(new(_chartAddress), owner._chart);
                _progress = Exact(new(_progressAddress), _progressClass);
                _control = Exact(new(_controlAddress), _controlClass);
                _pause = ReferenceField(_controller, owner._controller, "pauseUIControl", "PauseUIControl", 0xb0);
                if (Native(() => owner._api.ObjectClass(Target(_pause))) != _pauseClass)
                    throw new UnsupportedSignatureException("The note-flags source has another exact pause class.");
                _pauseAddress = Target(_pause).ToInt64();
                CaptureFlagFields();
                var notes = Native(() => (_view ?? throw new InvalidOperationException("The owned note-flags view is absent.")).Notes);
                _capturedNotes = notes.ToArray();
                _notes = new NativeRuntime.RootHandle[notes.Count];
                _noteReferences = new long[notes.Count];
                for (var index = 0; index < notes.Count; ++index)
                {
                    _noteReferences[index] = notes[index].Reference;
                    _notes[index] = Exact(new(_noteReferences[index]), owner._note);
                }
                VerifyCurrent();
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup)
                {
                    error.Data["SeekNoteFlagsCleanupOwner"] = this;
                    error.Data["SeekNoteFlagsCleanupFailure"] = cleanup;
                }
                throw;
            }
        }
        public void VerifyOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("The native note-flags source belongs to its owner thread.");
        }
        public void BindLifetimeGuard(Action guard)
        {
            VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
            if (_lifetime is not null) throw new InvalidOperationException("The note-flags lifetime guard is once-attached.");
            _lifetime = guard;
        }
        public void VerifyLocal()
        {
            VerifyOwnerThread();
            _canvasUseGuard?.Invoke();
            _lifetime?.Invoke();
            if (_withdrawn || _owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeSeekNoteFlagsAccess));
            if (_faulted) throw new InvalidOperationException("The actual note-flags source was already observed expired.");
            if (_owner._state.CaptureJudgementRun(_informationAddress, _controllerAddress) != _run
                || !_owner._state.MatchesCurrentGameplayRun(_informationAddress, _controllerAddress, _run.RunId, _run.SceneGeneration))
            {
                _faulted = true;
                throw new InvalidOperationException("The note-flags source lost its exact managed run revision.");
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
                throw new InvalidOperationException("An independently rooted note-flags receiver changed identity.");
            return value;
        }
        private NativeRuntime.RootHandle Adopt(IntPtr value)
        {
            VerifyLocal();
            if (value == IntPtr.Zero) throw new InvalidDataException("A required note-flags source reference is absent.");
            if (_retained.TryGetValue(value.ToInt64(), out var existing)) return existing;
            if (_roots.Count >= _rootLimit) throw new InvalidOperationException("The independent note source exceeded its complete root bound.");
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
                throw new UnsupportedSignatureException("The note-flags graph reference has another exact class.");
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
            var root = result ?? throw new InvalidDataException("The note-flags source reference was not adopted at publication.");
            if (Native(() => _owner._api.ObjectClass(Target(root))) != field.FieldClass)
                throw new UnsupportedSignatureException("The note-flags source field has another exact reference type.");
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
        private void CaptureFlagFields()
        {
            var boolean = Native(() => _owner._api.Class("mscorlib.dll", "System", "Boolean"));
            for (var slot = 0; slot < FlagNames.Length; ++slot)
            {
                var field = Native(() => _owner._api.InspectExactDeclaredReadField(
                    _owner._note, FlagNames[slot], "System.Boolean", false, FlagOffsets[slot]));
                if (field.DeclaringClass != _owner._note || field.TypeName != "System.Boolean"
                    || field.FieldClass != boolean || field.Size != sizeof(bool)
                    || field.IsStatic || (field.Flags & (0x10u | 0x20u | 0x40u)) != 0)
                    throw new UnsupportedSignatureException("A selected note flag is not exact mutable instance Boolean storage.");
                for (var previous = 0; previous < slot; ++previous)
                    if (_flagFields[previous].Field == field.Field
                        || (long)_flagFields[previous].Offset < (long)field.Offset + field.Size
                            && (long)field.Offset < (long)_flagFields[previous].Offset + _flagFields[previous].Size)
                        throw new UnsupportedSignatureException("Selected note flags overlap or alias.");
                _flagFields[slot] = field;
            }
        }
        private NativeExactReadField FlagField(int slot)
        {
            if ((uint)slot >= 3) throw new ArgumentOutOfRangeException(nameof(slot));
            try
            {
                var baseline = _flagFields[slot];
                var current = Native(() => _owner._api.InspectExactDeclaredReadField(
                    _owner._note, FlagNames[slot], "System.Boolean", false, baseline.Offset));
                if (current != baseline) throw new UnsupportedSignatureException("A prepared note flag's official descriptor changed.");
                return current;
            }
            catch { _faulted = true; throw; }
        }
        private void VerifyCurrent(bool fullGraph = true)
        {
            try
            {
                VerifyLocal();
                var view = _view ?? throw new InvalidOperationException("The independent complete note-flags graph is absent.");
                if (fullGraph) view.VerifySource();
                VerifyLocal();
                foreach (var pair in new[] { (_controller, _owner._controller), (_information, _owner._info),
                    (_chart, _owner._chart), (_progress, _progressClass), (_pause, _pauseClass), (_control, _controlClass) })
                    if (Native(() => _owner._api.ObjectClass(Target(pair.Item1))) != pair.Item2)
                        throw new UnsupportedSignatureException("A retained note-flags source changed its exact class.");
                if (!Native(() => _owner._api.MatchesNormalControllerInformation(_controller, _information,
                    _owner._controller, _owner._info, VerifyLocal))
                    || ReferenceToken(_information, _owner._info, "chart", "InLevel.Chart", 0x88) != _chartAddress
                    || ReferenceToken(_information, _owner._info, "progressControl", "InLevel.ProgressControl", 0x28) != _progressAddress
                    || ReferenceToken(_progress, _progressClass, "levelInformation", "InLevel.LevelInformation", 0x20) != _informationAddress
                    || ReferenceToken(_controller, _owner._controller, "pauseUIControl", "PauseUIControl", 0xb0) != _pauseAddress
                    || ReferenceToken(_information, _owner._info, "noteControl", "InLevel.NoteControl", 0x40) != _controlAddress
                    || ReferenceToken(_control, _controlClass, "levelInformation", "InLevel.LevelInformation", 0x20) != _informationAddress
                    || ReferenceToken(_control, _controlClass, "notes", "InLevel.NoteInformation[]", 0x28) != _noteViewIdentity.RenderNotes
                    || ReferenceToken(_control, _controlClass, "allNotes", "InLevel.NoteInformation[]", 0x30) != _noteViewIdentity.AllNotes)
                    throw new InvalidOperationException("The actual note-flags run/chart/progress/pause association changed.");
                if (!Alive(_controller) || !Alive(_information) || !Alive(_progress) || !Alive(_pause) || !Alive(_control)
                    || !InvokeBoolean(_owner._controller, "get_isActiveAndEnabled", _controller)
                    || !InvokeBoolean(_pauseClass, "get_isActiveAndEnabled", _pause))
                    throw new InvalidOperationException("The retained note-flags source is inactive or dead.");
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
                    throw new InvalidOperationException("Note-flags writes require the actual ready paused non-ending source.");
                VerifyLocal();
            }
            catch (Exception error)
            {
                if (!SeekCanvasBasisCallerWithdrawal.Is(error)) _faulted = true;
                // A NoteView may defer a failed native free until its in-flight
                // verification exits. Keep its original cleanup exception/owner.
                if (error.Data["SeekRuntimeNoteCleanupFailure"] is Exception cleanup) RecordViewFailure(cleanup);
                throw;
            }
        }
        private SeekNoteFlagTarget[] ReadFlags()
        {
            var rows = new SeekNoteFlagTarget[_notes.Length];
            for (var index = 0; index < rows.Length; ++index)
                rows[index] = ReadOne(index);
            return rows;
        }
        private void VerifyNote(int ordinal)
        {
            var note = _notes[ordinal];
            if (Target(note).ToInt64() != _noteReferences[ordinal] || !Alive(note)
                || Native(() => _owner._api.ObjectClass(Target(note))) != _owner._note
                || ReferenceToken(note, _owner._note, "noteData", "InLevel.Note", 0x28) != _capturedNotes[ordinal].NoteData
                || ReferenceToken(note, _owner._note, "lineInformation", "InLevel.LineInformation", 0x20) != _capturedNotes[ordinal].Line.Reference)
            {
                _faulted = true;
                throw new InvalidOperationException("A selected runtime note died or lost its captured data/line identity.");
            }
        }
        private SeekNoteFlagTarget ReadOne(int ordinal)
        {
            VerifyNote(ordinal);
            var note = _notes[ordinal];
            bool Read(int slot)
            {
                var field = FlagField(slot);
                return Native(() => _owner._api.ReadExactField<bool>(note, field));
            }
            return new(_noteReferences[ordinal], Read(0), Read(1), Read(2));
        }
        public IReadOnlyList<SeekNoteFlagTarget> Read()
        {
            VerifyCurrent();
            var first = ReadFlags();
            VerifyCurrent();
            var second = ReadFlags();
            VerifyCurrent();
            if (!first.SequenceEqual(second))
                throw new InvalidOperationException("Actual note flags changed between complete snapshots.");
            return Array.AsReadOnly(second);
        }
        public SeekNoteFlagTarget ReadNote(int ordinal)
        {
            if ((uint)ordinal >= (uint)_notes.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
            VerifyCurrent(false);
            var first = ReadOne(ordinal);
            VerifyCurrent(false);
            var second = ReadOne(ordinal);
            VerifyCurrent(false);
            VerifyNote(ordinal);
            VerifyLocal();
            if (first != second) throw new InvalidOperationException("The selected note flags changed between snapshots.");
            return second;
        }
        public void Write(int noteOrdinal, int fieldSlot, bool value, bool expectedCurrent, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            if ((uint)noteOrdinal >= (uint)_notes.Length || (uint)fieldSlot >= 3)
                throw new ArgumentOutOfRangeException(nameof(noteOrdinal));
            VerifyCurrent(false);
            var note = _notes[noteOrdinal];
            VerifyNote(noteOrdinal);
            var field = FlagField(fieldSlot);
            if (Target(note).ToInt64() != _noteReferences[noteOrdinal]
                || Native(() => _owner._api.ReadExactField<bool>(note, field)) != expectedCurrent)
                throw new InvalidOperationException("The selected note flag changed before its conditional setter.");
            // This sample plus managed guard is not an atomic native CAS and
            // does not exclude an external writer or Boolean double ABA.
            Native(() =>
            {
                using var argument = A.Value(value);
                _owner._api.WriteExactGuarded(note, _owner._note, FlagNames[fieldSlot], "System.Boolean", argument, () =>
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
            var error = new AggregateException("Note-flags cleanup retains unknown native responsibility without retry.", _cleanupFailures);
            error.Data["SeekNoteFlagsCleanupOwner"] = this;
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
