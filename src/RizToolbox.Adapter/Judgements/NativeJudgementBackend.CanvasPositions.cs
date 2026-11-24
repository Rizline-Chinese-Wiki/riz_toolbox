using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private static INativeSeekCanvasPositionAccess CreateSeekCanvasPositionAccess(NativeJudgementBackend owner,
        RunValue run, ISeekCanvasNativeProfile sourceProfile, ISeekCanvasPositionNativeProfile positionProfile,
        float chartTime, float speed)
        => NativeSeekNoteFlagsAccess.CreateCanvasPositions(owner, run, sourceProfile, positionProfile, chartTime, speed);

    private sealed partial class NativeSeekNoteFlagsAccess
    {
        internal static INativeSeekCanvasPositionAccess CreateCanvasPositions(NativeJudgementBackend owner,
            RunValue run, ISeekCanvasNativeProfile sourceProfile, ISeekCanvasPositionNativeProfile positionProfile,
            float chartTime, float speed)
            => CanvasSource.CreatePositions(owner, run, sourceProfile, positionProfile, chartTime, speed);

        private sealed partial class CanvasSource
        {
            internal static INativeSeekCanvasPositionAccess CreatePositions(NativeJudgementBackend owner,
                RunValue run, ISeekCanvasNativeProfile sourceProfile, ISeekCanvasPositionNativeProfile positionProfile,
                float chartTime, float speed)
            {
                SharedBasisSource? basis = null;
                try
                {
                    basis = SharedBasisSource.Capture(owner, run, sourceProfile, chartTime, speed);
                    var access = basis.CreatePositionAccess(positionProfile); basis.Dispose(); return access;
                }
                catch (Exception error)
                {
                    if (basis is not null)
                        try { basis.Dispose(); }
                        catch (Exception cleanup) { basis.ObserveCleanupFailure(cleanup); basis.AttachProvenance(error); }
                    SharedBasisSource.AttachStandaloneConstructionFailure(error, positions: true); throw;
                }
            }

            /// <summary>Holds a managed child lease on one complete actual source.
            /// Source equality excludes target localPosition, allowing explicit
            /// writes without invalidating their independently retained origin.</summary>
            private sealed partial class PositionAccess : INativeSeekCanvasPositionAccess
            {
                private readonly int _thread = Environment.CurrentManagedThreadId;
                private readonly RunValue _run;
                private readonly long _serial;
                private readonly SharedBasisSource _basis;
                private SharedBasisSource.ChildLease? _lease;
                private readonly ISeekCanvasPositionNativeProfile _profile;
                private CanvasSource _source = null!;
                private NativeSeekCanvasTargetCapture? _capture;
                private NativeMetadataMethod _get = null!, _set = null!;
                private readonly IReadOnlyList<SeekCanvasPositionTarget> _targets = Array.Empty<SeekCanvasPositionTarget>();
                private readonly long[] _transforms = Array.Empty<long>();
                private Action? _lifetime;
                private bool _withdrawn, _expired, _releaseAttempted, _released;
                private Exception? _cleanup;
                private object? _unknownSourceOwner;

                internal PositionAccess(SharedBasisSource basis, ISeekCanvasPositionNativeProfile profile)
                {
                    _basis = basis; _run = basis.Origin; _serial = basis.ConstructionSerial; _profile = profile;
                    try
                    {
                        _lease = basis.AcquireChild(positions: true);
                        _capture = basis.SourceCapture; _source = basis.PhysicalSource;
                        using var operation = EnterOperation();
                        _transforms = _capture.TransformReferences.ToArray();
                        var rows = _capture.Plan.Rows;
                        if (rows.Count != _transforms.Length || rows.Count > NativeSeekCanvasTargetPlan.MaxCanvases)
                            throw new InvalidDataException("The actual Canvas source and target plan differ in complete coverage.");
                        var targets = new SeekCanvasPositionTarget[rows.Count];
                        for (var i = 0; i < targets.Length; ++i)
                        {
                            if (rows[i].Ordinal != i || _transforms[i] == 0)
                                throw new InvalidDataException("The actual Canvas Transform occurrence mapping is incomplete.");
                            targets[i] = new(i, _transforms[i], rows[i].LocalPosition);
                        }
                        _targets = Array.AsReadOnly(targets);
                        N(() => { _profile.VerifyCurrent(); return 0; });
                        if (N(() => _profile.TransformClass) != _source._classes[6])
                            throw new UnsupportedSignatureException("The Canvas position profile has another exact Transform class.");
                        _get = Method("get_localPosition", N(() => _profile.GetLocalPositionMethod));
                        _set = Method("set_localPosition", N(() => _profile.SetLocalPositionMethod));
                        VerifySource();
                    }
                    catch (Exception error)
                    {
                        ObserveCleanupFailure(error); _basis.AttachProvenance(error);
                        try { Dispose(); }
                        catch (Exception cleanup) { Attach(error, _cleanup ?? cleanup); }
                        throw;
                    }
                }

                private void Attach(Exception error, Exception cleanup)
                {
                    error.Data["SeekCanvasPositionCleanupOwner"] = this;
                    error.Data["SeekCanvasPositionCleanupFailure"] = cleanup;
                    if (_unknownSourceOwner is not null) error.Data["SeekCanvasPositionSourceCleanupOwner"] = _unknownSourceOwner;
                    _basis.AttachProvenance(error);
                }
                public void VerifyOwnerThread()
                {
                    if (Environment.CurrentManagedThreadId != _thread)
                        throw new InvalidOperationException("Canvas positions belong to their owner thread.");
                }
                private void OwnLifetime()
                {
                    VerifyOwnerThread(); _run.VerifyCanvasSourceConstruction(_serial);
                    if (_withdrawn || _released) throw new ObjectDisposedException(nameof(PositionAccess));
                    if (_expired) throw new InvalidOperationException("The actual Canvas position source was observed expired.");
                    _lifetime?.Invoke();
                }
                public void VerifyLocal() { OwnLifetime(); _source.VerifyLocal(); }
                public NativeSeekCanvasTargetCapture SourceCapture
                { get { VerifyOwnerThread(); return _capture ?? _basis.SourceCapture; } }
                public IDisposable EnterOperation()
                {
                    VerifyOwnerThread();
                    try { return new AccessOperation(this, _basis.EnterOperation(OwnLifetime)); }
                    catch (Exception error) { ObserveCleanupFailure(error); throw; }
                }
                private sealed class AccessOperation : IDisposable
                {
                    private readonly PositionAccess _owner;
                    private readonly IDisposable _scope;
                    internal AccessOperation(PositionAccess owner, IDisposable scope) { _owner = owner; _scope = scope; }
                    public void Dispose()
                    {
                        try { _scope.Dispose(); }
                        catch (Exception error) { _owner.ObserveCleanupFailure(error); throw; }
                    }
                }
                private void ObserveCleanupFailure(Exception error)
                {
                    _basis.ObserveCleanupFailure(error);
                    if (_basis.CleanupFailure is not Exception cleanup) return;
                    _cleanup ??= cleanup; _unknownSourceOwner ??= _basis.SourceCleanupOwner;
                    Attach(error, cleanup);
                }
                private T N<T>(Func<T> action)
                {
                    try { VerifyLocal(); var value = action(); VerifyLocal(); return value; }
                    catch (Exception error)
                    {
                        ObserveCleanupFailure(error);
                        throw;
                    }
                }
                public void BindLifetimeGuard(Action guard)
                {
                    VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
                    if (_lifetime is not null) throw new InvalidOperationException("Canvas position lifetime attaches only once.");
                    _lifetime = guard;
                }
                public IReadOnlyList<SeekCanvasPositionTarget> Targets { get { VerifyLocal(); return _targets; } }
                private NativeMetadataMethod Method(string name, IntPtr selected)
                {
                    var method = N(() => _source._core._owner._api.InspectCanvasPositionMethod(_source._classes[6], name, IntPtr.Zero));
                    if (selected == IntPtr.Zero || method.MethodInfo != selected)
                        throw new UnsupportedSignatureException("The current Canvas position MethodInfo differs from its exact profile.");
                    return method;
                }
                public void VerifySource()
                {
                    try
                    {
                        VerifyLocal();
                        N(() => { _profile.VerifyCurrent(); return 0; });
                        if (N(() => _profile.TransformClass) != _source._classes[6]
                            || !SameMethod(_get, Method("get_localPosition", N(() => _profile.GetLocalPositionMethod)))
                            || !SameMethod(_set, Method("set_localPosition", N(() => _profile.SetLocalPositionMethod))))
                            throw new UnsupportedSignatureException("The actual Canvas position method metadata changed.");
                        N(() => { _basis.VerifySourceInOperation(); return 0; }); VerifyLocal();
                    }
                    catch { _expired = true; throw; }
                }
                private NativeRuntime.RootHandle Transform(int ordinal)
                {
                    VerifyLocal();
                    if ((uint)ordinal >= (uint)_transforms.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
                    var root = _source.Root(_transforms[ordinal]);
                    if (N(() => _source._core._owner._api.ObjectClass(_source.Target(root))) != _source._classes[6]
                        || !N(() => _source._core.Alive(root)))
                        throw new UnsupportedSignatureException("A retained Canvas Transform died or changed its exact class.");
                    return root;
                }
                private static bool SameBits(NativeVector3 a, NativeVector3 b)
                    => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
                    && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
                    && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

                public void RequestDispose()
                {
                    VerifyOwnerThread();
                    if (_withdrawn) return;
                    _withdrawn = true;
                }
                public void Dispose()
                {
                    VerifyOwnerThread(); _withdrawn = true;
                    if (!_releaseAttempted)
                    {
                        _releaseAttempted = true;
                        try { _lease?.Dispose(); _released = true; }
                        catch (Exception error)
                        {
                            ObserveCleanupFailure(error); _cleanup ??= error;
                            _unknownSourceOwner ??= _basis.SourceCleanupOwner ?? _capture;
                        }
                    }
                    if (_cleanup is null) return;
                    var failure = new AggregateException("Canvas position source cleanup remains unknown without retry.", _cleanup);
                    Attach(failure, _cleanup);
                    failure.Data["SeekCanvasPositionSourceCleanupOwner"] = _unknownSourceOwner;
                    throw failure;
                }
            }
        }
    }
}
