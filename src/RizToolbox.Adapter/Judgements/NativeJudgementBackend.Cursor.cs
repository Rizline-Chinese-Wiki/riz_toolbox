using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    /// <summary>Independent native roots for a real RunValue preparation. The existing
    /// complete-chart reader remains independently owned; disposing the caller's run or
    /// plan does not substitute another source or free this accessor's roots.</summary>
    private sealed class NativeNoteCursorAccess : INativeNoteCursorAccess
    {
        private const int MaximumNotes = NativeSeekChartPlan.MaximumNotes;
        private readonly NativeJudgementBackend _owner;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly ObservedJudgementRun _run;
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        private readonly Dictionary<long, NativeRuntime.RootHandle> _notes = new();
        private readonly NativeRuntime.RootHandle _controller, _information, _chart, _control, _progress, _pause, _renderNotes, _allNotes;
        private readonly IntPtr _controlClass, _progressClass, _pauseClass;
        private readonly long _controllerAddress, _informationAddress, _chartAddress, _controlAddress, _progressAddress, _pauseAddress;
        private readonly NativeSeekChartPlan _chartPlan;
        private readonly IntPtr[] _renderSlots, _allSlots;
        private bool _disposed, _cleaning, _faulted;
        private Action? _lifetime;
        private readonly List<string> _cleanupErrors = new();
        private readonly HashSet<NativeRuntime.RootHandle> _failedRootFrees = new();
        private bool _chartCleanupAttempted;

        internal NativeNoteCursorAccess(NativeJudgementBackend owner, RunValue run, NativeSeekChartPlan plan, ISeekChartNativeProfile profile)
        {
            _owner = owner;
            ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(profile);
            try
            {
                VerifyOwnerThread();
                if (owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeJudgementBackend));
                plan.VerifySource();
                if (owner._seekDisposed || run.Run != plan.Run)
                    throw new InvalidOperationException("The chart plan belongs to another retained run.");
                _controller = Add(owner._api.RetainExactObject(run.Controller.Target, owner._controller));
                _controllerAddress = Target(_controller).ToInt64();
                _information = Add(owner._api.RetainExactObject(run.Information.Target, owner._info));
                _informationAddress = Target(_information).ToInt64();
                _chart = Add(owner._api.RetainExactObject(run.Chart.Target, owner._chart));
                _chartAddress = Target(_chart).ToInt64();
                _run = owner._state.CaptureJudgementRun(_informationAddress, _controllerAddress)
                    ?? throw new InvalidOperationException("The captured cursor source has no actual run.");
                if (_run != plan.Run) throw new InvalidOperationException("The chart plan's exact run revision changed before cursor preparation.");
                _controlClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "InLevel", "NoteControl"));
                _progressClass = profile.ProgressClass;
                _pauseClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "PauseUIControl"));
                if (profile.ChartClass != owner._chart || _progressClass == IntPtr.Zero)
                    throw new UnsupportedSignatureException("The cursor profile belongs to another chart/progress identity.");
                _control = ExactField(_information, "noteControl", _controlClass);
                _controlAddress = Target(_control).ToInt64();
                _progress = ExactField(_information, "progressControl", _progressClass);
                _progressAddress = Target(_progress).ToInt64();
                _pause = ExactField(_controller, "pauseUIControl", _pauseClass);
                _pauseAddress = Target(_pause).ToInt64();
                Layouts();
                _renderNotes = ArrayField("notes", 0x28); _allNotes = ArrayField("allNotes", 0x30);
                _renderSlots = CopyArray(_renderNotes, retain: true); _allSlots = CopyArray(_allNotes, retain: true);
                // Own a fresh full-chart source rather than borrowing the caller's
                // plan lifetime. Both current plans are verified before publication.
                _chartPlan = Native(() => run.CaptureSeekPlan(plan.Target, profile));
                Native(() => { plan.VerifySource(); return 0; });
                VerifyCurrent(); VerifyLocal();
            }
            catch { Dispose(); throw; }
        }
        public void VerifyOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("Native cursor preparation belongs to its owning main thread.");
        }
        public void BindLifetimeGuard(Action guard)
        {
            VerifyOwnerThread(); ArgumentNullException.ThrowIfNull(guard);
            if (_lifetime is not null || _disposed) throw new InvalidOperationException("Cursor lifetime guard is once-attached.");
            _lifetime = guard;
        }
        public void VerifyLocal()
        {
            VerifyOwnerThread();
            _lifetime?.Invoke();
            if (_disposed || _owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeNoteCursorAccess));
            if (_faulted) throw new InvalidOperationException("This cursor source was already observed expired.");
            // Constructor setup precedes the first complete run identity. All
            // published operations require the exact managed lifecycle record.
            if (_run is not null && (_owner._state.CaptureJudgementRun(_informationAddress, _controllerAddress) != _run
                || !_owner._state.MatchesCurrentGameplayRun(_informationAddress, _controllerAddress, _run.RunId, _run.SceneGeneration)))
            { _faulted = true; throw new InvalidOperationException("The exact retained cursor run ended or changed."); }
        }
        private T Native<T>(Func<T> call)
        { VerifyLocal(); var value = call(); VerifyLocal(); return value; }
        private IntPtr Target(NativeRuntime.RootHandle root) => Native(() => root.Target);
        private NativeRuntime.RootHandle Add(NativeRuntime.RootHandle root) { _roots.Add(root); return root; }
        private NativeRuntime.RootHandle ExactField(NativeRuntime.RootHandle from, string name, IntPtr cls)
        {
            var value = Native(() => _owner._api.Read<IntPtr>(Target(from), name));
            var root = Add(_owner._api.RetainExactObject(value, cls)); VerifyLocal(); return root;
        }
        private NativeRuntime.RootHandle ArrayField(string name, int offset)
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(_controlClass, name, "InLevel.NoteInformation[]", false, offset));
            NativeRuntime.RootHandle? owned = null;
            Native(() =>
            {
                _owner._api.PublishExactReferenceField(_control, field, value => owned = Add(_owner._api.Root(value)));
                return 0;
            });
            var root = owned ?? throw new InvalidOperationException("The actual note array was not rooted.");
            if (Native(() => _owner._api.ObjectClass(Target(root))) != field.FieldClass)
                throw new UnsupportedSignatureException("The actual NoteControl array has another exact type.");
            return root;
        }
        private T Field<T>(NativeRuntime.RootHandle root, string name) where T : struct
            => Native(() => _owner._api.Read<T>(Target(root), name));
        private void Layouts()
        {
            var api = _owner._api;
            void Require(IntPtr cls, string name, string type, int offset)
                => Native(() => { api.RequireInstanceFieldLayout(cls, name, type, offset); return 0; });
            Require(_owner._info, "noteControl", "InLevel.NoteControl", 0x40);
            Require(_owner._info, "progressControl", "InLevel.ProgressControl", 0x28);
            Require(_owner._info, "chart", "InLevel.Chart", 0x88);
            Require(_owner._controller, "LevelInformation", "InLevel.LevelInformation", 0x28);
            Require(_owner._controller, "pauseUIControl", "PauseUIControl", 0xb0);
            Require(_controlClass, "levelInformation", "InLevel.LevelInformation", 0x20);
            Require(_progressClass, "levelInformation", "InLevel.LevelInformation", 0x20);
            foreach (var (name, offset) in new[] { ("_hasNote", 0x44), ("_activeHasNote", 0x4c) })
                Require(_controlClass, name, "System.Boolean", offset);
            foreach (var (name, offset) in new[] { ("_startIndex", 0x48), ("_activeStartIndex", 0x50) })
                Require(_controlClass, name, "System.Int32", offset);
            foreach (var (name, type, offset) in new[] { ("_hasNote", "System.Boolean", 0x44), ("_activeHasNote", "System.Boolean", 0x4c),
                ("_startIndex", "System.Int32", 0x48), ("_activeStartIndex", "System.Int32", 0x50) })
            {
                var field = Native(() => api.InspectExactDeclaredReadField(_controlClass, name, type, false, offset));
                if ((field.Flags & (0x20u | 0x40u)) != 0) throw new UnsupportedSignatureException("Cursor field is not mutable: " + name);
            }
            Require(_controlClass, "notes", "InLevel.NoteInformation[]", 0x28);
            Require(_controlClass, "allNotes", "InLevel.NoteInformation[]", 0x30);
            foreach (var (name, offset) in new[] { ("loadOver", 0xbe), ("levelOver", 0xc0), ("crashed", 0xc1) })
                Require(_owner._info, name, "System.Boolean", offset);
            foreach (var (name, offset) in new[] { ("prepareToPlay", 0x40), ("isPlaying", 0x42), ("timeReady", 0x65) })
                Require(_progressClass, name, "System.Boolean", offset);
            foreach (var (name, offset) in new[] { ("_pausing", 0xb4), ("_over", 0xc0), ("_terminated", 0xc1) })
                Require(_pauseClass, name, "System.Boolean", offset);
            Require(_pauseClass, "_pauseTime", "System.Single", 0xbc);
        }
        private IntPtr[] CopyArray(NativeRuntime.RootHandle root, bool retain)
        {
            var length = Native(() => _owner._api.Unbox<int>(_owner._api.Call(Target(root), "get_Length")));
            if (length is < 0 or > MaximumNotes) throw new InvalidOperationException("The actual note array exceeds its bound.");
            var copied = new List<IntPtr>(length);
            foreach (var value in _owner._api.ArrayItems(Target(root)))
            {
                VerifyLocal();
                if (copied.Count >= length || value == IntPtr.Zero) throw new InvalidOperationException("The actual note array is partial or contains a null slot.");
                if (retain && !_notes.ContainsKey(value.ToInt64()))
                {
                    if (_notes.Count >= 2 * MaximumNotes) throw new InvalidOperationException("Retained native notes exceed their bound.");
                    var note = Add(_owner._api.RetainExactObject(value, _owner._note));
                    _notes.Add(value.ToInt64(), note); VerifyLocal();
                }
                copied.Add(value);
            }
            VerifyLocal();
            if (copied.Count != length || Native(() => _owner._api.Unbox<int>(_owner._api.Call(Target(root), "get_Length"))) != length)
                throw new InvalidOperationException("The actual note array changed while being copied.");
            return copied.ToArray();
        }
        private void VerifyCurrent()
        {
            try
            {
                VerifyLocal();
                if (_chartPlan is not null) Native(() => { _chartPlan.VerifySource(); return 0; });
                Layouts();
                if (Target(_controller).ToInt64() != _controllerAddress || Target(_information).ToInt64() != _informationAddress
                    || Target(_chart).ToInt64() != _chartAddress || Target(_control).ToInt64() != _controlAddress
                    || Target(_progress).ToInt64() != _progressAddress || Target(_pause).ToInt64() != _pauseAddress
                    || Field<IntPtr>(_controller, "LevelInformation") != Target(_information)
                    || Field<IntPtr>(_information, "chart") != Target(_chart)
                    || Field<IntPtr>(_information, "noteControl") != Target(_control)
                    || Field<IntPtr>(_information, "progressControl") != Target(_progress)
                    || Field<IntPtr>(_controller, "pauseUIControl") != Target(_pause)
                    || Field<IntPtr>(_control, "levelInformation") != Target(_information)
                    || Field<IntPtr>(_progress, "levelInformation") != Target(_information)
                    || Field<IntPtr>(_control, "notes") != Target(_renderNotes)
                    || Field<IntPtr>(_control, "allNotes") != Target(_allNotes))
                    throw new InvalidOperationException("The retained cursor/array associations changed.");
                if (!Native(() => _owner.Alive(Target(_controller))) || !Native(() => _owner.Alive(Target(_information)))
                    || !Native(() => _owner.Alive(Target(_control))) || !Native(() => _owner.Alive(Target(_progress)))
                    || !Native(() => _owner.Alive(Target(_pause)))
                    || !Native(() => _owner._api.Unbox<bool>(_owner._api.Call(Target(_controller), "get_isActiveAndEnabled")))
                    || !Native(() => _owner._api.Unbox<bool>(_owner._api.Call(Target(_pause), "get_isActiveAndEnabled"))))
                    throw new InvalidOperationException("The retained cursor source is inactive or dead.");
                var pauseTime = Field<float>(_pause, "_pauseTime");
                if (!Field<bool>(_information, "loadOver") || Field<bool>(_information, "levelOver") || Field<bool>(_information, "crashed")
                    || !Field<bool>(_progress, "timeReady") || Field<bool>(_progress, "isPlaying") || Field<bool>(_progress, "prepareToPlay")
                    || !Field<bool>(_pause, "_pausing") || Field<bool>(_pause, "_over") || Field<bool>(_pause, "_terminated")
                    || !float.IsFinite(pauseTime) || pauseTime > 0
                    || !Native(() => _owner._api.Unbox<bool>(_owner._api.Call(Target(_pause), "get_CanDoOperation"))))
                    throw new InvalidOperationException("Cursor rewind requires its actual ready paused non-ending source.");
                if (!CopyArray(_renderNotes, false).SequenceEqual(_renderSlots) || !CopyArray(_allNotes, false).SequenceEqual(_allSlots))
                    throw new InvalidOperationException("The actual retained note array slots changed.");
                foreach (var item in _notes)
                    if (Target(item.Value).ToInt64() != item.Key) throw new InvalidOperationException("A retained note root changed its actual identity.");
                VerifyLocal();
            }
            catch { _faulted = true; throw; }
        }
        public NativeNoteCursorState Read()
        {
            VerifyCurrent();
            var value = new NativeNoteCursorState(Field<bool>(_control, "_hasNote"), Field<int>(_control, "_startIndex"),
                Field<bool>(_control, "_activeHasNote"), Field<int>(_control, "_activeStartIndex"));
            VerifyCurrent(); VerifyLocal(); return value;
        }
        public void ValidateBaseline(NativeNoteCursorState state)
        {
            VerifyLocal();
            if (state.StartIndex < 0 || state.StartIndex > _renderSlots.Length || state.ActiveStartIndex < 0 || state.ActiveStartIndex > _allSlots.Length)
                throw new InvalidOperationException("The original cursor is outside its actual native array.");
        }
        public void Write(int slot, int value)
        {
            VerifyCurrent();
            var (name, type) = slot switch { 0 => ("_hasNote", "System.Boolean"), 1 => ("_activeHasNote", "System.Boolean"),
                2 => ("_startIndex", "System.Int32"), 3 => ("_activeStartIndex", "System.Int32"),
                _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
            // The caller marks responsibility before entering this potentially
            // reentrant official setter. No renderer/scanner is invoked.
            using var argument = slot < 2 ? A.Value(value != 0) : A.Value(value);
            _owner._api.WriteExactGuarded(_control, _controlClass, name, type, argument, VerifyLocal);
            VerifyLocal();
        }
        public void Dispose()
        {
            VerifyOwnerThread();
            if (_cleaning) return;
            _disposed = true; _cleaning = true;
            try
            {
                if (_chartPlan is not null && !_chartCleanupAttempted)
                {
                    _chartCleanupAttempted = true;
                    try { _chartPlan.Dispose(); } catch (Exception error) { _cleanupErrors.Add("Full-chart root cleanup: " + error.Message); }
                }
                for (var index = _roots.Count - 1; index >= 0; --index)
                {
                    var root = _roots[index];
                    // RootHandle has already consumed its id before a native
                    // free can throw. Keep the failed wrapper and responsibility;
                    // never blindly repeat a free with unknown side effects.
                    if (_failedRootFrees.Contains(root)) continue;
                    try { root.Dispose(); _roots.RemoveAt(index); }
                    catch (Exception error) { _failedRootFrees.Add(root); _cleanupErrors.Add("Native root free: " + error.Message); }
                }
                if (_roots.Count == 0) _notes.Clear();
                if (_cleanupErrors.Count != 0) throw new InvalidOperationException("Cursor root cleanup has unknown failures: " + string.Join("; ", _cleanupErrors));
            }
            finally { _cleaning = false; }
        }
    }
}
