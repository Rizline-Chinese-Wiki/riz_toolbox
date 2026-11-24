using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private static INativeSeekHoldGeometryAccess CreateSeekHoldGeometryAccess(NativeJudgementBackend owner,
        RunValue run, ISeekHoldGeometryProfile profile) => NativeSeekNoteFlagsAccess.CreateGeometrySource(owner, run, profile);
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        internal static INativeSeekHoldGeometryAccess CreateGeometrySource(NativeJudgementBackend owner, RunValue run, ISeekHoldGeometryProfile profile)
        {
            HoldAccess? source = null;
            try { source = new HoldAccess(owner, run, profile); return new HoldAccess.GeometryAccess(source, profile); }
            catch (Exception error)
            {
                if (source is not null)
                    try { source.Dispose(); }
                    catch (Exception cleanup) { error.Data["SeekHoldGeometryCleanupOwner"] = source; error.Data["SeekHoldGeometryCleanupFailure"] = cleanup; }
                if (!error.Data.Contains("SeekHoldGeometryCleanupFailure"))
                    foreach (var prefix in new[] { "SeekHoldState", "SeekNoteFlags" })
                        if (error.Data[prefix + "CleanupFailure"] is Exception cleanup)
                        {
                            error.Data["SeekHoldGeometryCleanupOwner"] = error.Data[prefix + "CleanupOwner"];
                            error.Data["SeekHoldGeometryCleanupFailure"] = cleanup; break;
                        }
                throw;
            }
        }
        private sealed partial class HoldAccess
        {
            internal sealed partial class GeometryAccess : INativeSeekHoldGeometryAccess
            {
                private readonly HoldAccess _source;
                private readonly ISeekHoldGeometryProfile _profile;
                private readonly NativeRuntime.RootHandle _ui, _uiRoot, _camera, _cameraTransform, _canvas;
                private SeekHoldGeometrySnapshot? _context;
                private readonly Dictionary<int, HashSet<long>> _selectedTransforms = new();
                private readonly HashSet<long> _allTransforms = new();
                private Action? _lifetime;
                private bool _withdrawn, _expired, _released;
                private readonly List<Exception> _cleanup = new();
                internal GeometryAccess(HoldAccess source, ISeekHoldGeometryProfile profile)
                {
                    _source = source; _profile = profile;
                    try
                    {
                        source.BindLifetimeGuard(OwnLifetime);
                        var c = source._core;
                        c._rootLimit = checked(NativeSeekHoldGeometryCommands.MaximumTransforms + 8 * NativeSeekHoldGeometryCommands.MaximumNotes + 512);
                        VerifyCurrent();
                        _ui = c.TargetReference(c._information, c._owner._info, "uIControl", "InLevel.UIControl", 0x68);
                        _uiRoot = TransformField(_ui, profile.UIClass, "gameSceneCanvas", 0x20)
                            ?? throw new InvalidDataException("The geometry UI root Transform is absent.");
                        _camera = c.TargetReference(c._information, c._owner._info, "cameraControl", "InLevel.CameraControl", 0x78);
                        _cameraTransform = InvokeReference(profile.GetTransformMethod, _camera, true)!;
                        _canvas = c.TargetReference(c._information, c._owner._info, "canvasControl", "InLevel.CanvasControl", 0x70);
                        if (c.Native(() => c._owner._api.ObjectClass(c.Target(_ui))) != profile.UIClass
                            || c.Native(() => c._owner._api.ObjectClass(c.Target(_camera))) != profile.CameraClass
                            || c.Native(() => c._owner._api.ObjectClass(c.Target(_canvas))) != profile.CanvasClass)
                            throw new UnsupportedSignatureException("The geometry basis has different exact UI/camera/canvas classes.");
                        VerifySource();
                    }
                    catch (Exception error)
                    {
                        try { Dispose(); }
                        catch (Exception cleanup) { error.Data["SeekHoldGeometryCleanupOwner"] = this; error.Data["SeekHoldGeometryCleanupFailure"] = cleanup; }
                        throw;
                    }
                }
                public void VerifyOwnerThread() => _source.VerifyOwnerThread();
                private void OwnLifetime()
                {
                    VerifyOwnerThread();
                    if (_withdrawn) throw new ObjectDisposedException(nameof(GeometryAccess));
                    if (_expired) throw new InvalidOperationException("The actual geometry source was previously observed expired.");
                    _lifetime?.Invoke();
                }
                public void VerifyLocal() { OwnLifetime(); _source.VerifyLocal(); }
                public void BindLifetimeGuard(Action guard)
                {
                    VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
                    if (_lifetime is not null) throw new InvalidOperationException("The geometry lifetime guard is once-attached.");
                    _lifetime = guard;
                }
                private InvalidOperationException Changed(string message) { _expired = true; return new InvalidOperationException(message); }
                private T Observe<T>(Func<T> action)
                {
                    try { return action(); }
                    catch (Exception error) when (error is InvalidDataException or UnsupportedSignatureException)
                    { _expired = true; throw; }
                }
                private void VerifyCurrent(bool fullGraph = false)
                {
                    VerifyLocal(); var c = _source._core;
                    c.Native(() => { _profile.VerifyCurrent(); return 0; });
                    if (fullGraph) _source.VerifySource(); else _source.VerifyCurrent();
                    VerifyLocal();
                }
                public void VerifySource()
                {
                    VerifyCurrent(fullGraph: true); var c = _source._core;
                    if (_ui is null) return; // Constructor has not published basis roots yet.
                    if (!c.Alive(_ui) || !c.Alive(_camera) || !c.Alive(_canvas)
                        || c.Native(() => c._owner._api.ObjectClass(c.Target(_ui))) != _profile.UIClass
                        || c.Native(() => c._owner._api.ObjectClass(c.Target(_camera))) != _profile.CameraClass
                        || c.Native(() => c._owner._api.ObjectClass(c.Target(_canvas))) != _profile.CanvasClass
                        || c.ReferenceToken(c._information, c._owner._info, "uIControl", "InLevel.UIControl", 0x68) != Address(_ui)
                        || c.ReferenceToken(c._information, c._owner._info, "cameraControl", "InLevel.CameraControl", 0x78) != Address(_camera)
                        || c.ReferenceToken(c._information, c._owner._info, "canvasControl", "InLevel.CanvasControl", 0x70) != Address(_canvas)
                        || c.ReferenceToken(_camera, _profile.CameraClass, "levelInformation", "InLevel.LevelInformation", 0x20) != c._informationAddress
                        || c.ReferenceToken(_canvas, _profile.CanvasClass, "levelInformation", "InLevel.LevelInformation", 0x20) != c._informationAddress
                        || c.ReferenceToken(_ui, _profile.UIClass, "gameSceneCanvas", "UnityEngine.Transform", 0x20) != Address(_uiRoot)
                        || Address(InvokeReference(_profile.GetTransformMethod, _camera, true)) != Address(_cameraTransform))
                        throw Changed("The actual geometry basis lost its live UI/camera/canvas associations.");
                    VerifyLocal();
                }
                public void RequestDispose() { VerifyOwnerThread(); _withdrawn = true; _source.RequestDispose(); }
                public void Dispose()
                {
                    VerifyOwnerThread(); _withdrawn = true;
                    if (!_released)
                    {
                        _released = true;
                        try { _source.Dispose(); } catch (Exception error) { _cleanup.Add(error); }
                    }
                    if (_cleanup.Count == 0) return;
                    var failure = new AggregateException("Geometry source retains unknown cleanup responsibility without retry.", _cleanup);
                    failure.Data["SeekHoldGeometryCleanupOwner"] = this; throw failure;
                }
            }
        }
    }
}
