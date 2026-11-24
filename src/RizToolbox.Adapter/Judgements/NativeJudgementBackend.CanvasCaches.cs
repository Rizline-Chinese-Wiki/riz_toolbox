using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private static INativeSeekCanvasCacheAccess CreateSeekCanvasCacheAccess(NativeJudgementBackend owner,
        RunValue run, ISeekCanvasNativeProfile profile, float chartTime, float speed)
        => NativeSeekNoteFlagsAccess.CreateCanvasCaches(owner, run, profile, chartTime, speed);

    private sealed partial class NativeSeekNoteFlagsAccess
    {
        internal static INativeSeekCanvasCacheAccess CreateCanvasCaches(NativeJudgementBackend owner,
            RunValue run, ISeekCanvasNativeProfile profile, float chartTime, float speed)
            => CanvasSource.CreateCaches(owner, run, profile, chartTime, speed);

        private sealed partial class CanvasSource
        {
            internal static INativeSeekCanvasCacheAccess CreateCaches(NativeJudgementBackend owner,
                RunValue run, ISeekCanvasNativeProfile profile, float chartTime, float speed)
            {
                SharedBasisSource? basis = null;
                try
                {
                    basis = SharedBasisSource.Capture(owner, run, profile, chartTime, speed);
                    var access = basis.CreateCacheAccess(); basis.Dispose(); return access;
                }
                catch (Exception error)
                {
                    if (basis is not null)
                        try { basis.Dispose(); }
                        catch (Exception cleanup) { basis.ObserveCleanupFailure(cleanup); basis.AttachProvenance(error); }
                    SharedBasisSource.AttachStandaloneConstructionFailure(error, positions: false); throw;
                }
            }

            /// <summary>Same complete source plan as the position writer, with
            /// independently rooted actual cache vectors and exact mutable time
            /// fields. Cache contents are targets, not source-plan inputs.</summary>
            private sealed partial class CacheAccess : INativeSeekCanvasCacheAccess
            {
                private static readonly string[] Names = { "floorPositions", "floorSpeeds", "xPositions", "nowTime", "speed" };
                private readonly int _thread = Environment.CurrentManagedThreadId;
                private readonly RunValue _run;
                private readonly long _serial;
                private readonly SharedBasisSource _basis;
                private SharedBasisSource.ChildLease? _lease;
                private CanvasSource _source = null!;
                private NativeSeekCanvasTargetCapture? _capture;
                private readonly NativeExactReadField[] _fields = new NativeExactReadField[5];
                private readonly NativeRuntime.RootHandle[] _arrays = new NativeRuntime.RootHandle[3];
                private readonly long[] _arrayAddresses = new long[3];
                private readonly IReadOnlyList<SeekCanvasCacheSlot> _slots = Array.Empty<SeekCanvasCacheSlot>();
                private readonly int _length;
                private Action? _lifetime;
                private bool _withdrawn, _expired, _releaseAttempted, _released;
                private Exception? _cleanup;
                private object? _cleanupOwner;

                internal CacheAccess(SharedBasisSource basis)
                {
                    _basis = basis; _run = basis.Origin; _serial = basis.ConstructionSerial;
                    try
                    {
                        _lease = basis.AcquireChild(positions: false);
                        _capture = basis.SourceCapture; _source = basis.PhysicalSource;
                        using var operation = EnterOperation();
                        var plan = _capture.Plan; _length = plan.Rows.Count;
                        var slots = new List<SeekCanvasCacheSlot>(checked(3 * _length + 2));
                        for (var i = 0; i < Names.Length; ++i)
                        {
                            var name = Names[i]; _fields[i] = N(() => _source._core._owner._api.InspectCanvasCacheField(_source._classes[3], name));
                            if (i >= 3) continue;
                            var field = _fields[i];
                            _arrays[i] = N(() => _source.Reference(_source._canvas, _source._classes[3], name, "System.Single[]", field.Offset));
                            _arrayAddresses[i] = _source.Target(_arrays[i]).ToInt64();
                            var raw = ReadArray(i);
                            if (raw.Length != _length) throw new InvalidDataException("Each complete actual Canvas cache array must cover every Transform slot exactly.");
                            for (var k = 0; k < _length; ++k)
                            {
                                if (plan.Rows[k].Ordinal != k) throw new InvalidDataException("Canvas cache target rows have another occurrence mapping.");
                                var target = i == 0 ? plan.Rows[k].FloorPosition : i == 1 ? plan.Rows[k].FloorSpeed : plan.Rows[k].XPosition;
                                slots.Add(new(slots.Count, (SeekCanvasCacheKind)i, _arrayAddresses[i], k, BitConverter.SingleToInt32Bits(target)));
                            }
                        }
                        slots.Add(new(slots.Count, SeekCanvasCacheKind.NowTime, _source._canvasAddress, -1, BitConverter.SingleToInt32Bits(plan.Request.ChartTime)));
                        slots.Add(new(slots.Count, SeekCanvasCacheKind.Speed, _source._canvasAddress, -1, BitConverter.SingleToInt32Bits(plan.Request.Speed)));
                        _slots = Array.AsReadOnly(slots.ToArray()); VerifySource();
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
                    error.Data["SeekCanvasCacheCleanupOwner"] = this;
                    error.Data["SeekCanvasCacheCleanupFailure"] = cleanup;
                    if (_cleanupOwner is not null) error.Data["SeekCanvasCacheSourceCleanupOwner"] = _cleanupOwner;
                    _basis.AttachProvenance(error);
                }
                public void VerifyOwnerThread()
                {
                    if (Environment.CurrentManagedThreadId != _thread)
                        throw new InvalidOperationException("Canvas cache access belongs to its original owner thread.");
                }
                private void OwnLifetime()
                {
                    VerifyOwnerThread(); _run.VerifyCanvasSourceConstruction(_serial);
                    if (_withdrawn || _released) throw new ObjectDisposedException(nameof(CacheAccess));
                    if (_expired) throw new InvalidOperationException("The original Canvas cache source was observed expired.");
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
                    private readonly CacheAccess _owner;
                    private readonly IDisposable _scope;
                    internal AccessOperation(CacheAccess owner, IDisposable scope) { _owner = owner; _scope = scope; }
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
                    _cleanup ??= cleanup; _cleanupOwner ??= _basis.SourceCleanupOwner;
                    Attach(error, cleanup);
                }
                private T N<T>(Func<T> operation)
                {
                    try { VerifyLocal(); var value = operation(); VerifyLocal(); return value; }
                    catch (Exception error)
                    {
                        ObserveCleanupFailure(error);
                        throw;
                    }
                }
                public void BindLifetimeGuard(Action guard)
                {
                    VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
                    if (_lifetime is not null) throw new InvalidOperationException("Canvas cache lifetime attaches only once.");
                    _lifetime = guard;
                }
                public IReadOnlyList<SeekCanvasCacheSlot> Slots { get { VerifyLocal(); return _slots; } }
                private NativeExactReadField CurrentField(int kind)
                {
                    var current = N(() => _source._core._owner._api.InspectCanvasCacheField(_source._classes[3], Names[kind]));
                    if (current != _fields[kind]) throw new UnsupportedSignatureException("A saved/current exact Canvas cache descriptor changed.");
                    return current;
                }
                public void VerifySource()
                {
                    try
                    {
                        N(() => { _basis.VerifySourceInOperation(); return 0; });
                        for (var i = 0; i < Names.Length; ++i)
                        {
                            CurrentField(i);
                            if (i >= 3) continue;
                            var current = N(() => _source.Reference(_source._canvas, _source._classes[3], Names[i], "System.Single[]", _fields[i].Offset));
                            if (_source.Target(current).ToInt64() != _arrayAddresses[i]
                                || _source.Target(_arrays[i]).ToInt64() != _arrayAddresses[i])
                                throw new InvalidOperationException("An independently retained Canvas cache vector was replaced.");
                        }
                        VerifyLocal();
                    }
                    catch { _expired = true; throw; }
                }
                public void RequestDispose()
                {
                    VerifyOwnerThread(); if (_withdrawn) return;
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
                            _cleanupOwner ??= _basis.SourceCleanupOwner ?? _capture;
                        }
                    }
                    if (_cleanup is null) return;
                    var failure = new AggregateException("Canvas cache source cleanup remains unknown without retry.", _cleanup);
                    Attach(failure, _cleanup); throw failure;
                }
            }
        }
    }
}
