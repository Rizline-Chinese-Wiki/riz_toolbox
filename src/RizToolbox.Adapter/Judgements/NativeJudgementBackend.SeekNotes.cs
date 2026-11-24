using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    /// <summary>Independently retained read-only source for the actual RunValue.
    /// Complete chart traversal uses this source's guarded reads and root owner.
    /// Caller run/plan disposal leaves these independent responsibilities intact.</summary>
    private sealed class NativeSeekRuntimeNoteReader : IRetainedSeekRuntimeNoteReader
    {
        private const int MaximumRetainedReferences = 2 * (4 * NativeSeekRuntimeNoteView.MaximumNotes
            + NativeSeekRuntimeNoteView.MaximumLinePoints + 2 * NativeSeekRuntimeNoteView.MaximumLines) + 16;
        private readonly NativeJudgementBackend _owner;
        private readonly ObservedJudgementRun _run;
        private readonly ISeekChartNativeProfile _profile;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        private readonly Dictionary<long, NativeRuntime.RootHandle> _retained = new();
        private readonly Dictionary<NativeRuntime.RootHandle, long> _addresses = new();
        private readonly HashSet<NativeRuntime.RootHandle> _unknownFrees = new();
        private readonly List<Exception> _cleanupFailures = new();
        private readonly NativeRuntime.RootHandle _controllerRoot, _informationRoot, _chartRoot, _progressRoot, _controlRoot, _renderRoot, _allRoot;
        private readonly IntPtr _progressClass, _controlClass, _runtimeLineClass, _linePointClass, _groupClass;
        private readonly long _controllerAddress, _informationAddress, _chartAddress, _progressAddress, _controlAddress, _renderAddress, _allAddress;
        private bool _disposed, _cleaning, _faulted;
        private Action? _lifetime;
        private readonly Action? _canvasUseGuard;

        internal NativeSeekRuntimeNoteReader(NativeJudgementBackend owner, RunValue run, ISeekChartNativeProfile profile,
            Action? canvasUseGuard = null)
        {
            _owner = owner;
            _canvasUseGuard = canvasUseGuard;
            _run = run.Run;
            _controllerAddress = _run.Controller;
            _informationAddress = _run.Information;
            try
            {
                _profile = profile ?? throw new ArgumentNullException(nameof(profile));
                VerifyAccess();
                Native(() => { _profile.VerifyCurrent(); return 0; });
                _progressClass = Native(() => _profile.ProgressClass);
                if (Native(() => _profile.ChartClass) != owner._chart || _progressClass == IntPtr.Zero)
                    throw new UnsupportedSignatureException("The runtime-note profile belongs to another chart/progress metadata identity.");
                _controllerRoot = Exact(Native(() => run.Controller.Target), owner._controller);
                _informationRoot = Exact(Native(() => run.Information.Target), owner._info);
                _chartRoot = Exact(Native(() => run.Chart.Target), owner._chart);
                _chartAddress = Target(_chartRoot).ToInt64();
                _progressRoot = ReferenceField(_informationRoot, owner._info, "progressControl", "InLevel.ProgressControl", 0x28);
                if (Native(() => _owner._api.ObjectClass(Target(_progressRoot))) != _progressClass)
                    throw new UnsupportedSignatureException("The actual progress source differs from its exact fixed profile class.");
                _progressAddress = Target(_progressRoot).ToInt64();
                _controlClass = Class("NoteControl");
                _runtimeLineClass = Class("LineInformation");
                _linePointClass = Class("LinePoint");
                _groupClass = Class("LinesGroupInformation");
                _controlRoot = ReferenceField(_informationRoot, owner._info, "noteControl", "InLevel.NoteControl", 0x40);
                _controlAddress = Target(_controlRoot).ToInt64();
                _renderRoot = ReferenceField(_controlRoot, _controlClass, "notes", "InLevel.NoteInformation[]", 0x28);
                _allRoot = ReferenceField(_controlRoot, _controlClass, "allNotes", "InLevel.NoteInformation[]", 0x30);
                _renderAddress = Target(_renderRoot).ToInt64();
                _allAddress = Target(_allRoot).ToInt64();
                Layouts();
                var identity = ReadIdentity();
                VerifyLocal(identity);
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup)
                {
                    error.Data["SeekRuntimeNoteCleanupOwner"] = this;
                    error.Data["SeekRuntimeNoteCleanupFailure"] = cleanup;
                }
                throw;
            }
        }

        private IntPtr Class(string name)
            => Native(() => _owner._api.Class("Assembly-CSharp.dll", "InLevel", name));
        public void VerifyOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("The retained runtime-note source belongs to its owning main thread.");
        }
        public void VerifyAccess() => VerifyCurrentLocal();
        private void VerifyCurrentLocal()
        {
            VerifyOwnerThread();
            _canvasUseGuard?.Invoke();
            _lifetime?.Invoke();
            if (_disposed || _owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeSeekRuntimeNoteReader));
            if (_faulted) throw new InvalidOperationException("The retained runtime-note source was already observed expired.");
            if (_owner._state.CaptureJudgementRun(_informationAddress, _controllerAddress) != _run
                || !_owner._state.MatchesCurrentGameplayRun(_informationAddress, _controllerAddress, _run.RunId, _run.SceneGeneration))
            {
                _faulted = true;
                throw new InvalidOperationException("The retained runtime-note source lost its exact managed run revision.");
            }
        }
        public void BindLifetimeGuard(Action guard)
        {
            VerifyCurrentLocal();
            ArgumentNullException.ThrowIfNull(guard);
            if (_lifetime is not null) throw new InvalidOperationException("The runtime-note lifetime guard is once-attached.");
            _lifetime = guard;
        }
        public void VerifyLocal(SeekRuntimeNoteSourceIdentity expected)
        {
            VerifyCurrentLocal();
            // Each comparison uses saved addresses and managed state. Root.Target,
            // metadata/profile calls and native getters all precede this boundary.
            if (expected.Chart.Run != _run || expected.Chart.Chart != _chartAddress || expected.Chart.Progress != _progressAddress
                || expected.NoteControl != _controlAddress || expected.RenderNotes != _renderAddress || expected.AllNotes != _allAddress)
                throw new InvalidOperationException("The publication identity differs from this independently retained source.");
        }
        private T Native<T>(Func<T> call)
        {
            VerifyCurrentLocal();
            var value = call();
            VerifyCurrentLocal();
            return value;
        }
        // Keep the static invoke and value publication within one already-entered
        // Runtime operation. Disposal prevents the next high-level operation.
        private bool Alive(NativeRuntime.RootHandle root)
        {
            var value = Target(root);
            var method = Native(() => _owner._api.Method(_owner._object, "op_Implicit", 1, isStatic: true,
                parameterTypes: new[] { "UnityEngine.Object" }));
            return Native(() => _owner._api.InvokeValueWithPublication<bool>(method, IntPtr.Zero, A.Ref(value)));
        }
        private IntPtr Target(NativeRuntime.RootHandle root)
        {
            var value = Native(() => root.Target);
            if (!_addresses.TryGetValue(root, out var expected) || value.ToInt64() != expected)
                throw new InvalidOperationException("A runtime-note root changed its actual object identity.");
            return value;
        }
        private NativeRuntime.RootHandle Adopt(IntPtr value)
        {
            VerifyCurrentLocal();
            if (value == IntPtr.Zero) throw new InvalidDataException("The actual graph contains a missing native reference.");
            if (_retained.TryGetValue(value.ToInt64(), out var existing)) return existing;
            if (_retained.Count >= MaximumRetainedReferences)
                throw new InvalidOperationException("The independently retained runtime-note graph exceeds its reference bound.");
            var root = _owner._api.Root(value);
            // Register before any getter or post-call lifecycle check can throw.
            _roots.Add(root);
            _retained.Add(value.ToInt64(), root);
            _addresses.Add(root, value.ToInt64());
            VerifyCurrentLocal();
            return root;
        }
        private NativeRuntime.RootHandle Exact(IntPtr value, IntPtr type)
        {
            var root = Adopt(value);
            if (Native(() => _owner._api.ObjectClass(Target(root))) != type)
                throw new UnsupportedSignatureException("The actual runtime-note graph reference has another exact class.");
            return root;
        }
        private NativeRuntime.RootHandle ReferenceField(NativeRuntime.RootHandle from, IntPtr declaringClass,
            string name, string type, int offset)
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(declaringClass, name, type, false, offset));
            NativeRuntime.RootHandle? result = null;
            Native(() =>
            {
                _owner._api.PublishExactReferenceField(from, field, value => result = Adopt(value));
                return 0;
            });
            var root = result ?? throw new InvalidDataException("An actual graph field was not published to its independent root owner.");
            if (Native(() => _owner._api.ObjectClass(Target(root))) != field.FieldClass)
                throw new UnsupportedSignatureException("An actual graph field differs from its exact declared class.");
            return root;
        }
        private T ValueField<T>(NativeRuntime.RootHandle from, IntPtr declaringClass,
            string name, string type, int offset) where T : struct
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(declaringClass, name, type, false, offset));
            return Native(() => _owner._api.ReadExactField<T>(from, field));
        }
        private int Count(NativeRuntime.RootHandle root, string methodName, int maximum)
        {
            var type = Native(() => _owner._api.ObjectClass(Target(root)));
            var method = Native(() => _owner._api.Method(type, methodName, 0));
            var count = Native(() => _owner._api.InvokeValueWithPublication<int>(method, Target(root)));
            if (count < 0 || count > maximum) throw new InvalidDataException("An actual graph container exceeds its complete traversal bound.");
            return count;
        }
        private NativeRuntime.RootHandle ReferenceItem(NativeRuntime.RootHandle container, int ordinal,
            string methodName, IntPtr elementClass)
        {
            var type = Native(() => _owner._api.ObjectClass(Target(container)));
            var method = Native(() => _owner._api.Method(type, methodName, 1,
                parameterTypes: new[] { "System.Int32" }));
            NativeRuntime.RootHandle? result = null;
            Native(() => _owner._api.InvokeReferenceWithPublication(method, Target(container), value => result = Adopt(value), A.Value(ordinal)));
            var root = result ?? throw new InvalidDataException("An actual container item was not rooted at publication.");
            return Exact(Target(root), elementClass);
        }
        private T ValueItem<T>(NativeRuntime.RootHandle container, int ordinal) where T : struct
        {
            var type = Native(() => _owner._api.ObjectClass(Target(container)));
            var method = Native(() => _owner._api.Method(type, "get_Item", 1,
                parameterTypes: new[] { "System.Int32" }));
            return Native(() => _owner._api.InvokeValueWithPublication<T>(method, Target(container), A.Value(ordinal)));
        }
        private NativeRuntime.RootHandle ChartLines()
            => ReferenceField(_chartRoot, _owner._chart, "lines", "System.Collections.Generic.List<InLevel.Line>", 0x40);
        private void Layouts()
        {
            void Require(IntPtr cls, string name, string type, int offset)
                => Native(() => _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
            Native(() => _owner._api.InspectNormalControllerInformationField(_owner._controller, _owner._info, VerifyCurrentLocal));
            Require(_owner._info, "chart", "InLevel.Chart", 0x88);
            Require(_owner._info, "progressControl", "InLevel.ProgressControl", 0x28);
            Require(_progressClass, "levelInformation", "InLevel.LevelInformation", 0x20);
            Require(_owner._info, "noteControl", "InLevel.NoteControl", 0x40);
            Require(_controlClass, "levelInformation", "InLevel.LevelInformation", 0x20);
            Require(_controlClass, "notes", "InLevel.NoteInformation[]", 0x28);
            Require(_controlClass, "allNotes", "InLevel.NoteInformation[]", 0x30);
            Require(_owner._chart, "lines", "System.Collections.Generic.List<InLevel.Line>", 0x40);
            Require(_owner._line, "linePoints", "System.Collections.Generic.List<InLevel.LinePoint>", 0x10);
            Require(_owner._line, "notes", "System.Collections.Generic.List<InLevel.Note>", 0x18);
            Require(_owner._noteData, "type", "System.Int32", 0x10);
            Require(_owner._noteData, "time", "System.Single", 0x14);
            Require(_owner._noteData, "floorPosition", "System.Single", 0x18);
            Require(_owner._noteData, "otherInformations", "System.Collections.Generic.List<System.Single>", 0x20);
            Require(_owner._note, "lineInformation", "InLevel.LineInformation", 0x20);
            Require(_owner._note, "noteData", "InLevel.Note", 0x28);
            Require(_runtimeLineClass, "startLinePoint", "InLevel.LinePoint", 0x30);
            Require(_runtimeLineClass, "endLinePoint", "InLevel.LinePoint", 0x38);
            Require(_runtimeLineClass, "linesGroup", "InLevel.LinesGroupInformation", 0x60);
            Require(_runtimeLineClass, "lineIsStatic", "System.Boolean", 0x20);
            Require(_linePointClass, "time", "System.Single", 0x10);
        }

        public SeekRuntimeNoteSourceIdentity ReadIdentity()
        {
            VerifyCurrentLocal();
            Layouts();
            Native(() => { _profile.VerifyCurrent(); return 0; });
            if (Native(() => _profile.ChartClass) != _owner._chart || Native(() => _profile.ProgressClass) != _progressClass)
                throw new UnsupportedSignatureException("The fixed runtime-note profile changed its chart/progress metadata identity.");
            if (Target(_controllerRoot).ToInt64() != _controllerAddress
                || Target(_informationRoot).ToInt64() != _informationAddress
                || !Alive(_controllerRoot) || !Alive(_informationRoot)
                || !Alive(_controlRoot)
                || !Native(() => _owner._api.MatchesNormalControllerInformation(_controllerRoot, _informationRoot,
                    _owner._controller, _owner._info, VerifyCurrentLocal))
                || ReferenceField(_informationRoot, _owner._info, "chart", "InLevel.Chart", 0x88) != _chartRoot
                || ReferenceField(_informationRoot, _owner._info, "progressControl", "InLevel.ProgressControl", 0x28) != _progressRoot
                || ReferenceField(_progressRoot, _progressClass, "levelInformation", "InLevel.LevelInformation", 0x20) != _informationRoot
                || ReferenceField(_informationRoot, _owner._info, "noteControl", "InLevel.NoteControl", 0x40) != _controlRoot
                || ReferenceField(_controlRoot, _controlClass, "levelInformation", "InLevel.LevelInformation", 0x20) != _informationRoot
                || ReferenceField(_controlRoot, _controlClass, "notes", "InLevel.NoteInformation[]", 0x28) != _renderRoot
                || ReferenceField(_controlRoot, _controlClass, "allNotes", "InLevel.NoteInformation[]", 0x30) != _allRoot)
                throw new InvalidOperationException("The retained runtime-note run, chart, control or array association changed.");
            var lines = ChartLines();
            var chart = new SeekChartIdentity(_run, _chartAddress, _progressAddress, Target(lines).ToInt64());
            return new(chart, _controlAddress, _renderAddress, _allAddress);
        }
        public int ReadLineCount() => Count(ChartLines(), "get_Count", NativeSeekRuntimeNoteView.MaximumLines);
        public SeekRuntimeChartLine ReadChartLine(int ordinal)
        {
            // Each field, item and count uses this reader's lifetime/run guard.
            // Complete chart-only roots share its persistent unknown-free records.
            var line = ReferenceItem(ChartLines(), ordinal, "get_Item", _owner._line);
            var notes = ReferenceField(line, _owner._line, "notes", "System.Collections.Generic.List<InLevel.Note>", 0x18);
            var noteCount = Count(notes, "get_Count", NativeSeekRuntimeNoteView.MaximumNotes);
            var copiedNotes = new SeekChartRawNote[noteCount];
            for (var noteIndex = 0; noteIndex < noteCount; ++noteIndex)
            {
                var note = ReferenceItem(notes, noteIndex, "get_Item", _owner._noteData);
                var extra = ReferenceField(note, _owner._noteData, "otherInformations", "System.Collections.Generic.List<System.Single>", 0x20);
                var extraCount = Count(extra, "get_Count", 3);
                var values = new float[extraCount];
                for (var valueIndex = 0; valueIndex < extraCount; ++valueIndex)
                    values[valueIndex] = ValueItem<float>(extra, valueIndex);
                if (Count(extra, "get_Count", 3) != extraCount)
                    throw new InvalidOperationException("A complete chart Note information list changed during traversal.");
                copiedNotes[noteIndex] = new(Target(note).ToInt64(), Target(extra).ToInt64(),
                    ValueField<int>(note, _owner._noteData, "type", "System.Int32", 0x10),
                    ValueField<float>(note, _owner._noteData, "time", "System.Single", 0x14),
                    ValueField<float>(note, _owner._noteData, "floorPosition", "System.Single", 0x18), Array.AsReadOnly(values));
            }
            if (Count(notes, "get_Count", NativeSeekRuntimeNoteView.MaximumNotes) != noteCount)
                throw new InvalidOperationException("A complete chart Note list changed during traversal.");
            var data = new SeekChartRawLine(Target(line).ToInt64(), Target(notes).ToInt64(), noteCount, Array.AsReadOnly(copiedNotes));
            var points = ReferenceField(line, _owner._line, "linePoints", "System.Collections.Generic.List<InLevel.LinePoint>", 0x10);
            var count = Count(points, "get_Count", NativeSeekRuntimeNoteView.MaximumLinePoints);
            var copied = new SeekRuntimeChartPoint[count];
            for (var index = 0; index < count; ++index)
            {
                var point = ReferenceItem(points, index, "get_Item", _linePointClass);
                copied[index] = new(Target(point).ToInt64(), ValueField<float>(point, _linePointClass, "time", "System.Single", 0x10));
            }
            if (Count(points, "get_Count", NativeSeekRuntimeNoteView.MaximumLinePoints) != count)
                throw new InvalidOperationException("A complete chart LinePoint list changed during traversal.");
            return new(data, Target(points).ToInt64(), count, Array.AsReadOnly(copied));
        }
        public IReadOnlyList<SeekRuntimeRawNote> ReadRuntimeNotes(bool renderNotes)
        {
            VerifyCurrentLocal();
            var array = renderNotes ? _renderRoot : _allRoot;
            if (Count(array, "get_Rank", 1) != 1) throw new InvalidDataException("The actual note source is not a vector array.");
            var count = Count(array, "get_Length", NativeSeekRuntimeNoteView.MaximumNotes);
            var copied = new SeekRuntimeRawNote[count];
            for (var slot = 0; slot < count; ++slot)
            {
                var note = ReferenceItem(array, slot, "GetValue", _owner._note);
                var data = ReferenceField(note, _owner._note, "noteData", "InLevel.Note", 0x28);
                var line = ReferenceField(note, _owner._note, "lineInformation", "InLevel.LineInformation", 0x20);
                var start = ReferenceField(line, _runtimeLineClass, "startLinePoint", "InLevel.LinePoint", 0x30);
                var end = ReferenceField(line, _runtimeLineClass, "endLinePoint", "InLevel.LinePoint", 0x38);
                var group = ReferenceField(line, _runtimeLineClass, "linesGroup", "InLevel.LinesGroupInformation", 0x60);
                if (Native(() => _owner._api.ObjectClass(Target(group))) != _groupClass)
                    throw new UnsupportedSignatureException("The actual runtime line has another exact group class.");
                copied[slot] = new(Target(note).ToInt64(), Target(data).ToInt64(), new(Target(line).ToInt64(),
                    Target(start).ToInt64(), Target(end).ToInt64(), Target(group).ToInt64(),
                    ValueField<bool>(line, _runtimeLineClass, "lineIsStatic", "System.Boolean", 0x20)));
            }
            if (Count(array, "get_Length", NativeSeekRuntimeNoteView.MaximumNotes) != count)
                throw new InvalidOperationException("The actual runtime-note array changed length during traversal.");
            VerifyCurrentLocal();
            return Array.AsReadOnly(copied);
        }

        public void Dispose()
        {
            VerifyOwnerThread();
            if (_cleaning) return;
            _disposed = true;
            _cleaning = true;
            try
            {
                for (var index = _roots.Count - 1; index >= 0; --index)
                {
                    var root = _roots[index];
                    // RootHandle consumes its handle before entering native free.
                    // Retain each failed wrapper and record an unknown release.
                    if (_unknownFrees.Contains(root)) continue;
                    try { root.Dispose(); _roots.RemoveAt(index); }
                    catch (Exception error) { _unknownFrees.Add(root); _cleanupFailures.Add(error); }
                }
                if (_roots.Count == 0) { _retained.Clear(); _addresses.Clear(); }
                if (_cleanupFailures.Count != 0)
                    throw new AggregateException("Runtime-note cleanup retains unknown root-free responsibility.", _cleanupFailures);
            }
            finally { _cleaning = false; }
        }
    }
}
