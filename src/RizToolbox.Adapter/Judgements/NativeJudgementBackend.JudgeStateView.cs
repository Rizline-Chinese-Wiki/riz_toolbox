using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    /// <summary>Actual Judge source with independent roots and its own complete
    /// NoteView. Default capture is read-only. The separate queue-writer
    /// constructor admits explicit guarded List operations and storage restore;
    /// neither mode controls coroutines, renderers, pools or clocks.</summary>
    private sealed partial class NativeSeekJudgeStateReader : IRetainedSeekJudgeStateReader
    {
        private const string ListType = "System.Collections.Generic.List<InLevel.NoteInformation>";
        // A writer additionally owns the complete actual-note map and every
        // bounded growth backing. Original inactive capacity refs remain rooted.
        private const int MaximumRetainedReferences = 4 * NativeSeekJudgeStateView.MaximumCapacity + 96;
        private static readonly string[] ListNames = { "notes", "tapNotesWaitingForJudge", "dragNotesWaitingForJudge" };
        private static readonly int[] ListOffsets = { 0x38, 0x40, 0x48 };
        private readonly NativeJudgementBackend _owner;
        private readonly ObservedJudgementRun _run;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        private readonly Dictionary<long, NativeRuntime.RootHandle> _retained = new();
        private readonly Dictionary<NativeRuntime.RootHandle, long> _addresses = new();
        private readonly HashSet<NativeRuntime.RootHandle> _unknownFrees = new();
        private readonly List<Exception> _cleanupFailures = new();
        private readonly NativeRuntime.RootHandle _information, _judge;
        private readonly NativeRuntime.RootHandle[] _lists = new NativeRuntime.RootHandle[3];
        private readonly NativeExactReadField[] _listFields = new NativeExactReadField[3];
        private readonly NativeNoteJudgeListLayout[] _layouts = new NativeNoteJudgeListLayout[3];
        private readonly long[] _listAddresses = new long[3];
        private readonly NativeExactReadField _holdField;
        private readonly long _informationAddress, _controllerAddress, _judgeAddress;
        private NativeSeekRuntimeNoteView? _view;
        private Action? _lifetime;
        private bool _withdrawn, _faulted, _cleaning, _closingView, _viewCleanupFailed;

        internal NativeSeekJudgeStateReader(NativeJudgementBackend owner, RunValue run, ISeekChartNativeProfile profile)
        {
            _owner = owner; _run = run.Run;
            _informationAddress = _run.Information; _controllerAddress = _run.Controller;
            try
            {
                ArgumentNullException.ThrowIfNull(profile);
                VerifyAccess();
                _view = Native(() => NativeSeekRuntimeNoteView.Capture(new NativeSeekRuntimeNoteReader(owner, run, profile)));
                if (_view.Run != _run || _view.Identity.Chart.Run != _run)
                    throw new InvalidOperationException("The independent Judge NoteView belongs to another actual run.");
                _information = Exact(new(_informationAddress), owner._info);
                _judge = ReferenceField(_information, owner._info, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58);
                if (Native(() => owner._api.ObjectClass(Target(_judge))) != owner._owner)
                    throw new UnsupportedSignatureException("The actual Judge owner has another exact class.");
                _judgeAddress = Target(_judge).ToInt64();
                for (var slot = 0; slot < 3; ++slot)
                {
                    var field = Native(() => owner._api.InspectExactDeclaredReadField(owner._owner,
                        ListNames[slot], ListType, false, ListOffsets[slot]));
                    _listFields[slot] = field;
                    _lists[slot] = ReferenceField(_judge, field);
                    _listAddresses[slot] = Target(_lists[slot]).ToInt64();
                    _layouts[slot] = Native(() => owner._api.InspectNoteJudgeListClass(field.FieldClass));
                    if (_layouts[slot].ElementClass != owner._note)
                        throw new UnsupportedSignatureException("The actual Judge list has another exact NoteInformation element.");
                }
                _holdField = Native(() => owner._api.InspectExactDeclaredReadField(owner._owner,
                    "holdOnJudgeCount", "System.Int32", false, 0x50));
                if (_holdField.Size != sizeof(int))
                    throw new UnsupportedSignatureException("The actual Hold counter has another official Int32 size.");
                ReadIdentity();
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup)
                {
                    error.Data["SeekJudgeStateCleanupOwner"] = this;
                    error.Data["SeekJudgeStateCleanupFailure"] = cleanup;
                }
                throw;
            }
        }

        public void VerifyOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("The independently retained Judge source belongs to its owner thread.");
        }
        public void VerifyAccess() => VerifyLocal();
        private void VerifyLocal()
        {
            VerifyOwnerThread();
            _lifetime?.Invoke();
            if (_withdrawn || _owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeSeekJudgeStateReader));
            if (_faulted) throw new InvalidOperationException("The actual Judge source was already observed expired.");
            if (_owner._state.CaptureJudgementRun(_informationAddress, _controllerAddress) != _run
                || !_owner._state.MatchesCurrentGameplayRun(_informationAddress, _controllerAddress, _run.RunId, _run.SceneGeneration))
            {
                _faulted = true;
                throw new InvalidOperationException("The independent Judge source lost its exact managed run revision.");
            }
        }
        public void BindLifetimeGuard(Action guard)
        {
            VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
            if (_lifetime is not null) throw new InvalidOperationException("The Judge-state lifetime guard is once-attached.");
            _lifetime = guard;
        }
        public void VerifyLocal(SeekJudgeStateSourceIdentity expected)
        {
            VerifyLocal();
            // Saved addresses and immutable value identities only. No root,
            // metadata, method, scalar or reference getter follows this guard.
            if (expected.Notes != (_view ?? throw new InvalidOperationException("The owned Judge NoteView is absent.")).Identity
                || expected.Notes.Chart.Run != _run || expected.NoteJudgeControl != _judgeAddress)
                throw new InvalidOperationException("The Judge snapshot belongs to another independently retained source.");
        }
        public IReadOnlyList<SeekRuntimeNote> RuntimeNotes
        {
            get { VerifyLocal(); return (_view ?? throw new InvalidOperationException("The owned Judge NoteView is absent.")).Notes; }
        }
        public SeekRuntimeNoteCoverageReport Coverage
        {
            get { VerifyLocal(); return (_view ?? throw new InvalidOperationException("The owned Judge NoteView is absent.")).Coverage; }
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
                throw new InvalidOperationException("An independently rooted Judge reference changed its actual identity.");
            return value;
        }
        private NativeRuntime.RootHandle Adopt(IntPtr value)
        {
            VerifyLocal();
            if (value == IntPtr.Zero) throw new InvalidDataException("A required actual Judge source reference is absent.");
            if (_retained.TryGetValue(value.ToInt64(), out var existing)) return existing;
            if (_roots.Count >= MaximumRetainedReferences)
                throw new InvalidOperationException("The complete Judge source exceeded its independent root bound.");
            var root = _owner._api.Root(value);
            // This owner records native responsibility before any getter,
            // metadata inspection or post-call lifecycle check can fail.
            _roots.Add(root); _retained.Add(value.ToInt64(), root); _addresses.Add(root, value.ToInt64());
            VerifyLocal();
            return root;
        }
        private NativeRuntime.RootHandle Exact(IntPtr value, IntPtr expectedClass)
        {
            var root = Adopt(value);
            if (Native(() => _owner._api.ObjectClass(Target(root))) != expectedClass)
                throw new UnsupportedSignatureException("An actual Judge source object has another exact class.");
            return root;
        }
        private NativeRuntime.RootHandle ReferenceField(NativeRuntime.RootHandle owner, IntPtr cls, string name, string type, int offset)
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
            return ReferenceField(owner, field);
        }
        private NativeRuntime.RootHandle ReferenceField(NativeRuntime.RootHandle owner, NativeExactReadField field)
        {
            NativeRuntime.RootHandle? result = null;
            Native(() =>
            {
                _owner._api.PublishExactReferenceField(owner, field, value => result = Adopt(value));
                return 0;
            });
            var root = result ?? throw new InvalidDataException("An actual Judge field was not adopted at publication.");
            if (Native(() => _owner._api.ObjectClass(Target(root))) != field.FieldClass)
                throw new UnsupportedSignatureException("An actual Judge field has another exact reference class.");
            return root;
        }
        private int Value(NativeRuntime.RootHandle owner, NativeExactReadField field)
            => Native(() => _owner._api.ReadExactField<int>(owner, field));
        private int ArrayCount(NativeRuntime.RootHandle array, string name, int maximum)
        {
            var type = Native(() => _owner._api.ObjectClass(Target(array)));
            var method = Native(() => _owner._api.Method(type, name, 0));
            var count = Native(() => _owner._api.InvokeValueWithPublication<int>(method, Target(array)));
            if (count < 0 || count > maximum) throw new InvalidDataException("An actual Judge backing array exceeds its complete bound.");
            return count;
        }
        private long ArrayItem(NativeRuntime.RootHandle array, IntPtr arrayClass, int slot)
        {
            var method = Native(() => _owner._api.Method(arrayClass, "GetValue", 1,
                parameterTypes: new[] { "System.Int32" }));
            var published = false;
            NativeRuntime.RootHandle? item = null;
            Native(() =>
            {
                _owner._api.InvokeReferenceWithPublication(method, Target(array), value =>
                {
                    VerifyLocal();
                    if (published) throw new InvalidOperationException("An actual Judge array item was published more than once.");
                    published = true;
                    if (value != IntPtr.Zero) item = Adopt(value);
                    VerifyLocal();
                }, A.Value(slot));
                return 0;
            });
            if (!published) throw new InvalidDataException("An actual Judge backing slot had no reference publication.");
            if (item is null) return 0;
            var retainedItem = item;
            if (Native(() => _owner._api.ObjectClass(Target(retainedItem))) != _owner._note)
                throw new UnsupportedSignatureException("An actual Judge backing slot has another exact NoteInformation class.");
            return Target(retainedItem).ToInt64();
        }
        private bool Alive(NativeRuntime.RootHandle root)
        {
            var target = Target(root);
            var method = Native(() => _owner._api.Method(_owner._object, "op_Implicit", 1, isStatic: true,
                parameterTypes: new[] { "UnityEngine.Object" }));
            return Native(() => _owner._api.InvokeValueWithPublication<bool>(method, IntPtr.Zero, A.Ref(target)));
        }
        private SeekJudgeStateSourceIdentity ReadIdentity(bool fullGraph = true)
        {
            if (fullGraph)
                Native(() => { (_view ?? throw new InvalidOperationException("The owned Judge NoteView is absent.")).VerifySource(); return 0; });
            if (Native(() => _owner._api.ObjectClass(Target(_information))) != _owner._info
                || Native(() => _owner._api.ObjectClass(Target(_judge))) != _owner._owner
                || !Alive(_judge)
                || ReferenceField(_information, _owner._info, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58) != _judge
                || ReferenceField(_judge, _owner._owner, "levelInformation", "InLevel.LevelInformation", 0x20) != _information)
                throw new InvalidOperationException("The actual Judge owner and retained information lost their mutual association.");
            var identity = new SeekJudgeStateSourceIdentity(_view!.Identity, _judgeAddress);
            VerifyLocal(identity);
            return identity;
        }
        private NativeNoteJudgeListLayout CurrentLayout(int slot)
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(_owner._owner,
                ListNames[slot], ListType, false, ListOffsets[slot]));
            if (field != _listFields[slot] || ReferenceField(_judge, field) != _lists[slot]
                || Target(_lists[slot]).ToInt64() != _listAddresses[slot])
                throw new InvalidOperationException("An actual Judge list field changed its official storage or retained object.");
            var layout = Native(() => _owner._api.InspectNoteJudgeListClass(field.FieldClass));
            if (layout != _layouts[slot]) throw new UnsupportedSignatureException("An actual Judge list changed its closed class or official descriptors.");
            return layout;
        }
        private int HoldCount()
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(_owner._owner,
                "holdOnJudgeCount", "System.Int32", false, 0x50));
            if (field != _holdField) throw new UnsupportedSignatureException("The actual Hold count changed its official storage descriptor.");
            return Value(_judge, field);
        }
        private SeekJudgeListStorage ReadList(int slot)
        {
            var layout = CurrentLayout(slot);
            var list = _lists[slot];
            var size = Value(list, layout.SizeField);
            var version = Value(list, layout.VersionField); // Any signed Int32 is preserved.
            if (size < 0 || size > NativeSeekJudgeStateView.MaximumCapacity)
                throw new InvalidDataException("An actual Judge list exceeds its complete logical bound.");
            var backing = ReferenceField(list, layout.BackingField);
            if (ArrayCount(backing, "get_Rank", 1) != 1)
                throw new InvalidDataException("An actual Judge backing array is not a vector.");
            var capacity = ArrayCount(backing, "get_Length", NativeSeekJudgeStateView.MaximumCapacity);
            if (size > capacity) throw new InvalidDataException("An actual Judge list size exceeds its complete backing array.");
            var slots = new long[capacity];
            for (var index = 0; index < slots.Length; ++index) slots[index] = ArrayItem(backing, layout.ArrayClass, index);
            if (ArrayCount(backing, "get_Length", NativeSeekJudgeStateView.MaximumCapacity) != capacity
                || Value(list, layout.SizeField) != size || Value(list, layout.VersionField) != version
                || ReferenceField(list, layout.BackingField) != backing || CurrentLayout(slot) != layout)
                throw new InvalidOperationException("Actual Judge size/version/backing changed during its complete capacity observation.");
            return new(Target(list).ToInt64(), layout.Class.ToInt64(), Target(backing).ToInt64(), layout.ArrayClass.ToInt64(),
                size, version, Array.AsReadOnly(slots));
        }
        public SeekJudgeRawState ReadSnapshot() => ReadSnapshot(fullGraph: true);
        private SeekJudgeRawState ReadSnapshot(bool fullGraph)
        {
            try
            {
                var identity = ReadIdentity(fullGraph);
                var hold = HoldCount();
                var notes = ReadList(0); var tap = ReadList(1); var drag = ReadList(2);
                if (HoldCount() != hold || ReadIdentity(fullGraph) != identity)
                    throw new InvalidOperationException("The actual Judge source or Hold count changed during its complete snapshot.");
                VerifyLocal(identity);
                return new(identity, notes, tap, drag, hold);
            }
            catch (Exception error)
            {
                _faulted = true;
                if (error.Data["SeekRuntimeNoteCleanupFailure"] is Exception cleanup) RecordViewFailure(cleanup);
                throw;
            }
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
            CloseView();
            if (_cleanupFailures.Count != 0) throw CleanupError();
        }
        private AggregateException CleanupError()
        {
            var error = new AggregateException("Judge-state source cleanup retains unknown native responsibility without retry.", _cleanupFailures);
            error.Data["SeekJudgeStateCleanupOwner"] = this;
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
