using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Adapter.UiInput;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    /// <summary>Independent rooted renderer source with exact ready-paused admission.
    /// Logical withdrawal preserves its NoteView and roots until physical Dispose.
    /// Releasing GC roots never certifies Unity object or callback cleanup.</summary>
    private sealed partial class NativeSeekRendererAccess : INativeSeekRendererAccess, INativeSeekRendererRestoreAccess
    {
        private readonly NativeJudgementBackend _owner;
        private readonly ISeekRendererNativeProfile _profile;
        private readonly IntPtr _rendererClass, _poolClass, _childClass, _transformClass, _componentClass;
        private readonly ObservedJudgementRun _run;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        private const int MaximumRoots = 16 + 12 * NativeSeekRendererCommands.MaximumTotalCapacity;
        private readonly Dictionary<long, NativeRuntime.RootHandle> _retained = new();
        private readonly Dictionary<NativeRuntime.RootHandle, long> _addresses = new();
        private readonly HashSet<NativeRuntime.RootHandle> _unknownFrees = new();
        private readonly List<Exception> _cleanupFailures = new();
        private readonly NativeRuntime.RootHandle _controller, _information, _chart, _progress, _pause, _control;
        private readonly IntPtr _progressClass, _pauseClass, _controlClass, _timeClass, _touchBlockerClass, _windowClass;
        private readonly long _controllerAddress, _informationAddress, _chartAddress, _progressAddress, _pauseAddress, _controlAddress;
        private NativeSeekRuntimeNoteView? _view;
        private readonly SeekRuntimeNoteSourceIdentity _identity;
        private readonly SeekRuntimeNote[] _capturedNotes;
        private readonly NativeRuntime.RootHandle[] _notes;
        private readonly long[] _noteRefs;
        private Action? _lifetime;
        private bool _withdrawn, _faulted, _cleaning, _closingView, _viewCleanupFailed;

        internal NativeSeekRendererAccess(NativeJudgementBackend owner, RunValue run, ISeekRendererNativeProfile profile)
        {
            _owner = owner; _run = run.Run; _profile = profile;
            _controllerAddress = _run.Controller; _informationAddress = _run.Information;
            try
            {
                ArgumentNullException.ThrowIfNull(profile);
                VerifyLocal();
                Native(() => { profile.VerifyCurrent(); return 0; });
                _rendererClass = Native(() => owner._api.NestedClass(owner._note, "NoteRenderer"));
                _poolClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "InLevel", "EasyPool"));
                _childClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "InLevel", "EasyPoolChild"));
                _transformClass = Native(() => owner._api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform"));
                _componentClass = Native(() => owner._api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component"));
                if (Native(() => profile.NoteClass) != owner._note || Native(() => profile.RendererClass) != _rendererClass
                    || Native(() => profile.PoolClass) != _poolClass || Native(() => profile.ChildClass) != _childClass)
                    throw new UnsupportedSignatureException("The renderer profile has different actual class identities.");
                // Assign ownership before a post-call lifecycle check can fail.
                _view = NativeSeekRuntimeNoteView.Capture(new RendererNoteReader(this, new NativeSeekRuntimeNoteReader(owner, run, profile)));
                VerifyLocal();
                if (_view.Run != _run || _view.Identity.Chart.Run != _run)
                    throw new InvalidOperationException("The independent renderer graph belongs to another exact run.");
                _identity = _view.Identity;
                _controlAddress = _identity.NoteControl;
                _chartAddress = _view.Identity.Chart.Chart; _progressAddress = _view.Identity.Chart.Progress;
                _progressClass = Native(() => profile.ProgressClass);
                if (Native(() => profile.ChartClass) != owner._chart || _progressClass == IntPtr.Zero)
                    throw new UnsupportedSignatureException("The renderer source profile has another chart/progress identity.");
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
                _pause = Reference(_controller, owner._controller, "pauseUIControl", "PauseUIControl", 0xb0)!;
                if (Native(() => owner._api.ObjectClass(Target(_pause))) != _pauseClass)
                    throw new UnsupportedSignatureException("The renderer source has another exact pause class.");
                _pauseAddress = Target(_pause).ToInt64();
                var notes = Native(() => (_view ?? throw new InvalidOperationException("The owned renderer view is absent.")).Notes);
                _capturedNotes = notes.ToArray();
                _notes = new NativeRuntime.RootHandle[notes.Count];
                _noteRefs = new long[notes.Count];
                for (var index = 0; index < notes.Count; ++index)
                {
                    _noteRefs[index] = notes[index].Reference;
                    _notes[index] = Exact(new(_noteRefs[index]), owner._note);
                }
                VerifyCurrent();
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup)
                {
                    error.Data["SeekRendererEffectsOwner"] = this;
                    error.Data["SeekRendererCleanupFailure"] = cleanup;
                }
                throw;
            }
        }
        public void VerifyOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("The native renderer source belongs to its owner thread.");
        }
        public void BindLifetimeGuard(Action guard)
        {
            VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
            if (_lifetime is not null) throw new InvalidOperationException("The renderer lifetime guard is once-attached.");
            _lifetime = guard;
        }
        public void VerifyLocal()
        {
            VerifyOwnerThread();
            _lifetime?.Invoke();
            if (_withdrawn || _owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeSeekRendererAccess));
            if (_faulted) throw new InvalidOperationException("The actual renderer source was already observed expired.");
            if (_owner._state.CaptureJudgementRun(_informationAddress, _controllerAddress) != _run
                || !_owner._state.MatchesCurrentGameplayRun(_informationAddress, _controllerAddress, _run.RunId, _run.SceneGeneration))
            {
                _faulted = true;
                throw new InvalidOperationException("The renderer source lost its exact managed run revision.");
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
                throw new InvalidOperationException("An independently rooted renderer receiver changed identity.");
            return value;
        }
        private NativeRuntime.RootHandle Adopt(IntPtr value)
        {
            VerifyOwnerThread();
            if (value == IntPtr.Zero) throw new InvalidDataException("A required renderer reference is absent.");
            if (_retained.TryGetValue(value.ToInt64(), out var existing)) { VerifyLocal(); return existing; }
            if (_roots.Count >= MaximumRoots) throw new NotSupportedException("The complete renderer graph exceeds its root bound.");
            // Reserve bookkeeping before native handle creation. Publication
            // already in flight records ownership even after logical withdrawal.
            EnsureRootCapacity(_roots.Count + 1);
            var root = _owner._api.Root(value);
            _roots.Add(root); _retained.Add(value.ToInt64(), root); _addresses.Add(root, value.ToInt64());
            VerifyLocal(); return root;
        }
        private void EnsureRootCapacity(int count)
        {
            if (count < 0 || count > MaximumRoots) throw new NotSupportedException("Renderer root reservation exceeds its complete bound.");
            _roots.EnsureCapacity(count); _retained.EnsureCapacity(count); _addresses.EnsureCapacity(count);
        }
        public void ReserveEffects(int maximumNewChildren, int maximumNewBackings, int projectedCapacity)
        {
            VerifyLocal();
            if (maximumNewChildren < 0 || maximumNewBackings < 0 || projectedCapacity < 0
                || projectedCapacity > NativeSeekRendererCommands.MaximumTotalCapacity)
                throw new NotSupportedException("Renderer effect reservation exceeds its complete storage budget.");
            EnsureRootCapacity(checked(_roots.Count + 4 * maximumNewChildren + maximumNewBackings)); VerifyLocal();
        }
        private NativeRuntime.RootHandle Root(long reference)
        {
            VerifyLocal();
            if (!_retained.TryGetValue(reference, out var root) || Target(root).ToInt64() != reference)
                throw new InvalidOperationException("The actual renderer reference has no independent root.");
            return root;
        }
        private IntPtr ClassOf(NativeRuntime.RootHandle root) => Native(() => _owner._api.ObjectClass(Target(root)));
        private NativeRuntime.RootHandle Exact(IntPtr value, IntPtr expectedClass)
        {
            var root = Adopt(value);
            if (Native(() => _owner._api.ObjectClass(Target(root))) != expectedClass)
                throw new UnsupportedSignatureException("The renderer graph reference has another exact class.");
            return root;
        }
        private NativeRuntime.RootHandle? Reference(NativeRuntime.RootHandle receiver, IntPtr declaringClass,
            string name, string type, int offset, bool required = true)
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(declaringClass, name, type, false, offset));
            NativeRuntime.RootHandle? result = null; var published = false;
            Native(() =>
            {
                _owner._api.PublishExactReferenceField(receiver, field, value =>
                {
                    if (published) throw new InvalidOperationException("A renderer field published twice.");
                    published = true; if (value != IntPtr.Zero) result = Adopt(value); else VerifyLocal();
                }); return 0;
            });
            if (!published || required && result is null) throw new InvalidDataException("A required renderer field was not published.");
            if (result is not null) Native(() => { _owner._api.RequireAssignable(field.FieldClass, ClassOf(result)); return 0; });
            return result;
        }
        private T Value<T>(NativeRuntime.RootHandle owner, IntPtr cls, string name, string type, int offset) where T : struct
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
            return Native(() =>
            {
                using var argument = A.Ref(target);
                return _owner._api.InvokeValueWithPublication<bool>(method, IntPtr.Zero, argument);
            });
        }
        private float SceneTime()
        {
            var method = Native(() => _owner._api.Method(_timeClass, "get_timeSinceLevelLoad", 0, isStatic: true));
            return Native(() => _owner._api.InvokeValueWithPublication<float>(method, IntPtr.Zero));
        }
        private void VerifyCurrent(bool fullGraph = true)
        {
            try
            {
                VerifyLocal(); Native(() => { _profile.VerifyCurrent(); return 0; });
                var view = _view ?? throw new InvalidOperationException("The independent complete renderer graph is absent.");
                if (fullGraph) view.VerifySource();
                VerifyLocal();
                foreach (var pair in new[] { (_controller, _owner._controller), (_information, _owner._info),
                    (_chart, _owner._chart), (_progress, _progressClass), (_pause, _pauseClass), (_control, _controlClass) })
                    if (Native(() => _owner._api.ObjectClass(Target(pair.Item1))) != pair.Item2)
                        throw new UnsupportedSignatureException("A retained renderer source changed its exact class.");
                if (!Native(() => _owner._api.MatchesNormalControllerInformation(_controller, _information,
                    _owner._controller, _owner._info, VerifyLocal))
                    || ReferenceToken(_information, _owner._info, "chart", "InLevel.Chart", 0x88) != _chartAddress
                    || ReferenceToken(_information, _owner._info, "progressControl", "InLevel.ProgressControl", 0x28) != _progressAddress
                    || ReferenceToken(_progress, _progressClass, "levelInformation", "InLevel.LevelInformation", 0x20) != _informationAddress
                    || ReferenceToken(_controller, _owner._controller, "pauseUIControl", "PauseUIControl", 0xb0) != _pauseAddress
                    || ReferenceToken(_information, _owner._info, "noteControl", "InLevel.NoteControl", 0x40) != _controlAddress
                    || ReferenceToken(_control, _controlClass, "levelInformation", "InLevel.LevelInformation", 0x20) != _informationAddress
                    || ReferenceToken(_control, _controlClass, "notes", "InLevel.NoteInformation[]", 0x28) != _identity.RenderNotes
                    || ReferenceToken(_control, _controlClass, "allNotes", "InLevel.NoteInformation[]", 0x30) != _identity.AllNotes)
                    throw new InvalidOperationException("The actual renderer run/chart/progress/pause association changed.");
                if (!Alive(_controller) || !Alive(_information) || !Alive(_progress) || !Alive(_pause) || !Alive(_control)
                    || !InvokeBoolean(_owner._controller, "get_isActiveAndEnabled", _controller)
                    || !InvokeBoolean(_pauseClass, "get_isActiveAndEnabled", _pause))
                    throw new InvalidOperationException("The retained renderer source is inactive or dead.");
                var countdown = Value<float>(_pause, _pauseClass, "_pauseTime", "System.Single", 0xbc);
                var sceneTime = SceneTime();
                if (!Value<bool>(_information, _owner._info, "loadOver", "System.Boolean", 0xbe)
                    || Value<bool>(_information, _owner._info, "levelOver", "System.Boolean", 0xc0)
                    || Value<bool>(_information, _owner._info, "crashed", "System.Boolean", 0xc1)
                    || !Value<bool>(_progress, _progressClass, "timeReady", "System.Boolean", 0x65)
                    || Value<bool>(_progress, _progressClass, "isPlaying", "System.Boolean", 0x42)
                    || Value<bool>(_progress, _progressClass, "prepareToPlay", "System.Boolean", 0x40)
                    || !Value<bool>(_pause, _pauseClass, "_pausing", "System.Boolean", 0xb4)
                    || Value<bool>(_pause, _pauseClass, "_over", "System.Boolean", 0xc0)
                    || Value<bool>(_pause, _pauseClass, "_terminated", "System.Boolean", 0xc1)
                    || !float.IsFinite(countdown) || countdown > 0 || !float.IsFinite(sceneTime) || sceneTime < 1.8f
                    || !InvokeBoolean(_pauseClass, "get_CanDoOperation", _pause)
                    || InvokeBoolean(_touchBlockerClass, "get_Blocked") || InvokeBoolean(_windowClass, "get_HasActiveWindow"))
                    throw new InvalidOperationException("Renderer commands require the actual ready paused non-ending source.");
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
        public IReadOnlyList<SeekRuntimeNote> RuntimeNotes { get { VerifyLocal(); return Array.AsReadOnly(_capturedNotes); } }
        public void VerifySource() => VerifyCurrent(true);
        private void VerifyReady() => VerifyCurrent(false);
        private IntPtr Getter(IntPtr cls, string name, string returns)
        {
            var method = Native(() => _owner._api.InspectDeclaredMethod(cls, name, Array.Empty<string>(), returns, IntPtr.Zero));
            if (method.GenericMethod || method.InflatedMethod || (method.Flags & 0x10) != 0)
                throw new UnsupportedSignatureException("A renderer Transform getter has an unsupported signature.");
            return method.MethodInfo;
        }
        private NativeRuntime.RootHandle? ReferenceGetter(NativeRuntime.RootHandle receiver, IntPtr method, bool required)
        {
            NativeRuntime.RootHandle? result = null; var published = false; var target = Target(receiver);
            Native(() =>
            {
                _owner._api.InvokeReferenceWithPublication(method, target, token =>
                {
                    if (published) throw new InvalidOperationException("A renderer getter published twice.");
                    published = true; if (token != IntPtr.Zero) result = Adopt(token); else VerifyLocal();
                }); return 0;
            });
            if (!published || required && result is null) throw new InvalidDataException("A required renderer Transform getter returned null.");
            if (result is not null) Native(() => { _owner._api.RequireAssignable(_transformClass, ClassOf(result)); return 0; });
            return result;
        }
        private NativeRuntime.RootHandle ComponentTransform(NativeRuntime.RootHandle component)
        {
            Native(() => { _owner._api.RequireAssignable(_componentClass, ClassOf(component)); return 0; });
            if (!Alive(component)) throw new InvalidOperationException("The renderer Component is no longer alive.");
            return ReferenceGetter(component, Getter(_componentClass, "get_transform", "UnityEngine.Transform"), true)!;
        }
        private SeekRendererTransformState ReadTransform(NativeRuntime.RootHandle transform)
        {
            Native(() => { _owner._api.RequireAssignable(_transformClass, ClassOf(transform)); return 0; });
            if (!Alive(transform)) throw new InvalidOperationException("The renderer Transform is no longer alive.");
            var parent = ReferenceGetter(transform, Getter(_transformClass, "get_parent", "UnityEngine.Transform"), false);
            var position = Native(() => _owner._api.InvokeValueWithPublication<NativeVector3>(Getter(_transformClass, "get_localPosition", "UnityEngine.Vector3"), Target(transform)));
            var rotation = Native(() => _owner._api.InvokeValueWithPublication<NativeRendererQuaternion>(Getter(_transformClass, "get_localRotation", "UnityEngine.Quaternion"), Target(transform)));
            var scale = Native(() => _owner._api.InvokeValueWithPublication<NativeVector3>(Getter(_transformClass, "get_localScale", "UnityEngine.Vector3"), Target(transform)));
            return new(Target(transform).ToInt64(), parent is null ? 0 : Target(parent).ToInt64(), position, rotation, scale);
        }
        private sealed class RendererNoteReader : IRetainedSeekRuntimeNoteReader
        {
            private readonly NativeSeekRendererAccess _access;
            private readonly IRetainedSeekRuntimeNoteReader _inner;
            internal RendererNoteReader(NativeSeekRendererAccess access, IRetainedSeekRuntimeNoteReader inner) { _access = access; _inner = inner; }
            public void VerifyOwnerThread() { _access.VerifyOwnerThread(); _inner.VerifyOwnerThread(); }
            public void VerifyAccess() { _access.VerifyLocal(); _inner.VerifyAccess(); _access.VerifyLocal(); }
            public void BindLifetimeGuard(Action guard) => _inner.BindLifetimeGuard(() => { _access.VerifyLocal(); guard(); });
            public SeekRuntimeNoteSourceIdentity ReadIdentity() => _access.Native(_inner.ReadIdentity);
            public int ReadLineCount() => _access.Native(_inner.ReadLineCount);
            public SeekRuntimeChartLine ReadChartLine(int ordinal) => _access.Native(() => _inner.ReadChartLine(ordinal));
            public IReadOnlyList<SeekRuntimeRawNote> ReadRuntimeNotes(bool renderNotes) => _access.Native(() => _inner.ReadRuntimeNotes(renderNotes));
            public void VerifyLocal(SeekRuntimeNoteSourceIdentity expected) { _access.VerifyLocal(); _inner.VerifyLocal(expected); _access.VerifyLocal(); }
            public void Dispose() => _inner.Dispose();
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
            // Keep the independently rooted NoteView until physical disposal.
            if (_cleanupFailures.Count != 0) throw CleanupError();
        }
        private AggregateException CleanupError()
        {
            var error = new AggregateException("Note-flags cleanup retains unknown native responsibility without retry.", _cleanupFailures);
            error.Data["SeekRendererEffectsOwner"] = this;
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
