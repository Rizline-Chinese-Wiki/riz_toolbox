using System.Runtime.ExceptionServices;
using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private static INativeSeekCanvasBasisAccess CreateSeekCanvasBasisAccess(NativeJudgementBackend owner,
        RunValue run, ISeekCanvasNativeProfile profile, float chartTime, float speed)
        => NativeSeekNoteFlagsAccess.CreateCanvasBasis(owner, run, profile, chartTime, speed);

    private sealed partial class NativeSeekNoteFlagsAccess
    {
        internal static INativeSeekCanvasBasisAccess CreateCanvasBasis(NativeJudgementBackend owner,
            RunValue run, ISeekCanvasNativeProfile profile, float chartTime, float speed)
            => CanvasSource.SharedBasisSource.Capture(owner, run, profile, chartTime, speed);

        private sealed partial class CanvasSource
        {
            /// <summary>One actual Canvas capture, with managed bootstrap and
            /// child holds. The operation gate never enters the NoteView gate.</summary>
            internal sealed partial class SharedBasisSource : INativeSeekCanvasBasisAccess
            {
                private readonly int _thread = Environment.CurrentManagedThreadId;
                private readonly RunValue _run;
                private readonly long _serial;
                private CanvasSource? _source;
                private NativeSeekCanvasTargetCapture? _capture;
                private SharedOperation? _operation;
                private int _holds = 1;
                private bool _bootstrapHeld = true, _cacheAcquired, _positionAcquired, _lineRingAcquired;
                private bool _expired, _terminalAttempted, _released;
                private Exception? _cleanupFailure;
                private object? _cleanupOwner;
                private Action? _terminalObserver;
                private bool _terminalObserverInvoked;
                private Exception? _terminalObserverFailure;

                private SharedBasisSource(RunValue run)
                { _run = run; _serial = run.CanvasSourceConstructionSerial; }
                internal RunValue Origin => _run;
                internal long ConstructionSerial => _serial;
                internal CanvasSource PhysicalSource => _source
                    ?? throw new InvalidOperationException("The shared actual Canvas source did not finish acquisition.");

                internal static SharedBasisSource Capture(NativeJudgementBackend owner, RunValue run,
                    ISeekCanvasNativeProfile profile, float chartTime, float speed)
                {
                    var value = new SharedBasisSource(run);
                    try
                    {
                        using (value.EnterOperation(value.VerifyLocal))
                        {
                            value._capture = NativeSeekCanvasTargetCapture.Capture(() =>
                                value._source = new CanvasSource(owner, run, profile, value), chartTime, speed);
                            value.VerifySourceUse();
                        }
                        return value;
                    }
                    catch (Exception error)
                    {
                        // Capture may already own the failed source even though
                        // its factory never returned a value to this assignment.
                        value._capture ??= error.Data["SeekCanvasSourceCleanupOwner"] as NativeSeekCanvasTargetCapture;
                        value.ObserveCleanupFailure(error); value.AttachProvenance(error);
                        try { value.Dispose(); }
                        catch (Exception cleanup)
                        {
                            value.ObserveCleanupFailure(cleanup); value.AttachProvenance(error);
                            error.Data["SeekCanvasBasisSourceCleanupFailure"] = value._cleanupFailure ?? cleanup;
                        }
                        throw;
                    }
                }

                public void VerifyOwnerThread()
                {
                    if (Environment.CurrentManagedThreadId != _thread)
                        throw new InvalidOperationException("A shared Canvas basis source belongs to its original owner thread.");
                }

                public void VerifyLocal()
                {
                    VerifyPhysicalLocal();
                    if (!_bootstrapHeld) throw new ObjectDisposedException(nameof(SharedBasisSource));
                }

                internal void VerifyPhysicalLocal()
                {
                    VerifyOwnerThread(); _run.VerifyCanvasSourceConstruction(_serial);
                    if (_cleanupFailure is not null) throw CleanupError();
                    if (_released || _terminalAttempted) throw new ObjectDisposedException(nameof(SharedBasisSource));
                    if (_expired || _source is { _expired: true })
                        throw new InvalidOperationException("The common actual Canvas basis source was already observed expired.");
                    if (_source is { _withdrawn: true }) throw new ObjectDisposedException(nameof(CanvasSource));
                }

                internal void VerifySourceUse()
                {
                    VerifyPhysicalLocal();
                    var operation = _operation ?? throw new InvalidOperationException("A Canvas source use requires its managed shared operation scope.");
                    InvokeCaller(operation.VerifyCallerLocal);
                }

                private static void InvokeCaller(Action verifyCallerLocal)
                {
                    try { verifyCallerLocal(); }
                    catch (Exception error) { SeekCanvasBasisCallerWithdrawal.Mark(error); throw; }
                }

                internal IDisposable EnterOperation(Action verifyCallerLocal)
                {
                    VerifyOwnerThread(); ArgumentNullException.ThrowIfNull(verifyCallerLocal);
                    if (_operation is not null)
                        throw new InvalidOperationException("The common Canvas source cannot synchronously reenter another shared operation.");
                    VerifyPhysicalLocal(); InvokeCaller(verifyCallerLocal);
                    var operation = new SharedOperation(this, verifyCallerLocal); _operation = operation;
                    return operation;
                }

                private sealed class SharedOperation : IDisposable
                {
                    private readonly SharedBasisSource _owner;
                    internal readonly Action VerifyCallerLocal;
                    private bool _disposed;
                    private Exception? _failure;
                    internal SharedOperation(SharedBasisSource owner, Action verifyCallerLocal)
                    { _owner = owner; VerifyCallerLocal = verifyCallerLocal; }
                    public void Dispose()
                    {
                        _owner.VerifyOwnerThread();
                        if (_disposed)
                        {
                            if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
                            return;
                        }
                        if (!ReferenceEquals(_owner._operation, this))
                            throw new InvalidOperationException("The common Canvas operation scope lost its original owner.");
                        _disposed = true; _owner._operation = null;
                        try { _owner.TryTerminalCleanup(); }
                        catch (Exception error) { _failure = error; throw; }
                    }
                }

                internal ChildLease AcquireChild(bool positions)
                {
                    VerifyLocal();
                    if (_operation is not null)
                        throw new InvalidOperationException("A Canvas child cannot be acquired inside another shared operation.");
                    if (positions ? _positionAcquired : _cacheAcquired)
                        throw new InvalidOperationException("Each shared Canvas child access can be acquired only once.");
                    var lease = new ChildLease(this);
                    if (positions) _positionAcquired = true; else _cacheAcquired = true;
                    ++_holds; return lease;
                }

                internal ChildLease AcquireLineRingChild()
                {
                    VerifyLocal();
                    if (_operation is not null)
                        throw new InvalidOperationException("A line/ring child cannot be acquired inside another shared operation.");
                    if (_lineRingAcquired)
                        throw new InvalidOperationException("The shared Canvas line/ring reader can be acquired only once.");
                    var lease = new ChildLease(this); _lineRingAcquired = true;
                    ++_holds; return lease;
                }

                internal sealed class ChildLease : IDisposable
                {
                    private readonly SharedBasisSource _owner;
                    private bool _dropAttempted;
                    private Exception? _failure;
                    internal ChildLease(SharedBasisSource owner) => _owner = owner;
                    public void Dispose()
                    {
                        _owner.VerifyOwnerThread();
                        if (_dropAttempted)
                        {
                            if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
                            return;
                        }
                        _dropAttempted = true;
                        try { _owner.DropHold(); }
                        catch (Exception error) { _failure = error; throw; }
                    }
                }

                public NativeSeekCanvasTargetCapture SourceCapture
                {
                    get
                    {
                        VerifyOwnerThread();
                        return _capture ?? throw new InvalidOperationException("The shared Canvas capture did not finish acquisition.");
                    }
                }

                public INativeSeekCanvasCacheAccess CreateCacheAccess()
                {
                    VerifyLocal(); var access = new CacheAccess(this);
                    // The aggregate creates its original children first. Later
                    // independent children cannot replace that exact borrower.
                    _lineCacheAccess ??= access; return access;
                }
                public INativeSeekCanvasPositionAccess CreatePositionAccess(ISeekCanvasPositionNativeProfile profile)
                {
                    VerifyLocal(); ArgumentNullException.ThrowIfNull(profile);
                    var access = new PositionAccess(this, profile);
                    _linePositionAccess ??= access; return access;
                }
                public IRetainedSeekLineRingReader CreateLineRingReader(ISeekLineRingSourceProfile profile, Action? constructionGuard = null)
                { VerifyLocal(); ArgumentNullException.ThrowIfNull(profile); return new LineRingReader(this, profile, constructionGuard); }

                public void VerifySource(Action verifyCallerLocal)
                {
                    using var operation = EnterOperation(verifyCallerLocal);
                    VerifySourceInOperation();
                }

                internal void VerifySourceInOperation()
                {
                    VerifySourceUse();
                    try { SourceCapture.VerifySource(); VerifySourceUse(); }
                    catch (Exception error)
                    {
                        ObserveCleanupFailure(error);
                        if (!SeekCanvasBasisCallerWithdrawal.Is(error)) _expired = true;
                        AttachProvenance(error); throw;
                    }
                }

                internal void ObserveCleanupFailure(Exception error)
                {
                    Exception? cleanup = null;
                    foreach (var prefix in new[] { "NativePublication", "SeekCanvasSource", "SeekRuntimeNote", "SeekNoteFlags", "SeekChart", "SeekCanvasBasisSource" })
                        if (error.Data[prefix + "CleanupFailure"] is Exception found) { cleanup = found; break; }
                    if (cleanup is null) return;
                    var owner = FindCleanupOwner(error, 0);
                    RecordUnknownCleanup(cleanup, owner); AttachProvenance(error);
                }

                private static object? FindCleanupOwner(Exception error, int depth)
                {
                    if (error.Data["NativePublicationCleanupOwner"] is object publication) return publication;
                    if (error.Data["SeekCanvasSourceNativeCleanupOwner"] is object nativeSource) return nativeSource;
                    // Capture's outer cleanup wrapper names the public capture;
                    // prefer the retained publication/root owner inside it.
                    if (depth < 16)
                    {
                        if (error is AggregateException aggregate)
                        {
                            foreach (var aggregateFailure in aggregate.InnerExceptions)
                                if (FindCleanupOwner(aggregateFailure, depth + 1) is object aggregateOwner) return aggregateOwner;
                        }
                        else if (error.InnerException is Exception wrappedFailure
                            && FindCleanupOwner(wrappedFailure, depth + 1) is object wrappedOwner) return wrappedOwner;
                    }
                    return error.Data["SeekCanvasSourceCleanupOwner"] ?? error.Data["SeekRuntimeNoteCleanupOwner"]
                        ?? error.Data["SeekNoteFlagsCleanupOwner"] ?? error.Data["SeekChartCleanupOwner"]
                        ?? error.Data["SeekCanvasBasisSourceCleanupOwner"];
                }

                internal void RecordUnknownCleanup(Exception failure, object? originalOwner)
                {
                    VerifyOwnerThread(); ArgumentNullException.ThrowIfNull(failure);
                    _cleanupFailure ??= failure; _cleanupOwner ??= originalOwner ?? _capture ?? (object?)_source ?? this;
                }

                internal void AttachProvenance(Exception error)
                {
                    error.Data["SeekCanvasBasisSourceOwner"] = this;
                    if (_capture is not null) error.Data["SeekCanvasBasisSourceCapture"] = _capture;
                    if (_cleanupFailure is not null)
                    {
                        error.Data["SeekCanvasBasisSourceCleanupFailure"] = _cleanupFailure;
                        if (_cleanupOwner is not null) error.Data["SeekCanvasBasisSourceCleanupOwner"] = _cleanupOwner;
                    }
                    if (_terminalObserverFailure is not null)
                        error.Data["SeekCanvasBasisTerminalObserverFailure"] = _terminalObserverFailure;
                }

                public bool HasRetainedResponsibility { get { VerifyOwnerThread(); return _cleanupFailure is not null; } }
                public object? SourceCleanupOwner { get { VerifyOwnerThread(); return _cleanupOwner; } }
                public Exception? CleanupFailure { get { VerifyOwnerThread(); return _cleanupFailure; } }
                public bool IsOperationActive { get { VerifyOwnerThread(); return _operation is not null; } }

                public void BindTerminalObserver(Action observer)
                {
                    VerifyOwnerThread(); ArgumentNullException.ThrowIfNull(observer);
                    if (_terminalObserver is not null)
                        throw new InvalidOperationException("The Canvas source terminal observer attaches only once.");
                    _terminalObserver = observer; NotifyTerminalObserver();
                }

                private void NotifyTerminalObserver()
                {
                    if (_bootstrapHeld || !_terminalAttempted || _operation is not null
                        || _terminalObserver is null || _terminalObserverInvoked) return;
                    _terminalObserverInvoked = true;
                    try { _terminalObserver(); }
                    catch (Exception error) { _terminalObserverFailure = error; }
                }

                private Exception CleanupError()
                {
                    var error = new AggregateException("The common Canvas source retains its original unknown cleanup; source use and native cleanup are not retried.", _cleanupFailure!);
                    AttachProvenance(error); return error;
                }

                private void DropHold()
                {
                    if (_cleanupFailure is not null) throw CleanupError();
                    if (_holds <= 0) throw new InvalidOperationException("The common Canvas source has no remaining managed hold.");
                    --_holds; TryTerminalCleanup();
                }

                private void TryTerminalCleanup()
                {
                    if (_holds != 0 || _operation is not null || _released) return;
                    if (_cleanupFailure is not null) throw CleanupError();
                    if (_terminalAttempted) throw new InvalidOperationException("The common Canvas source terminal cleanup was already attempted.");
                    _terminalAttempted = true;
                    try { _capture?.Dispose(); _released = true; }
                    catch (Exception error)
                    {
                        ObserveCleanupFailure(error);
                        RecordUnknownCleanup(error, _cleanupOwner ?? _capture ?? (object?)_source);
                        AttachProvenance(error); throw;
                    }
                    finally { NotifyTerminalObserver(); }
                }

                public void Dispose()
                {
                    VerifyOwnerThread();
                    if (!_bootstrapHeld)
                    {
                        if (_cleanupFailure is not null) throw CleanupError();
                        return;
                    }
                    _bootstrapHeld = false; DropHold();
                }

                internal static void AttachStandaloneConstructionFailure(Exception error, bool positions)
                {
                    if (error.Data["SeekCanvasBasisSourceOwner"] is not SharedBasisSource basis || basis._cleanupFailure is null) return;
                    var prefix = positions ? "SeekCanvasPosition" : "SeekCanvasCache";
                    if (!error.Data.Contains(prefix + "CleanupOwner")) error.Data[prefix + "CleanupOwner"] = basis;
                    error.Data[prefix + "CleanupFailure"] = basis._cleanupFailure;
                    if (!error.Data.Contains(prefix + "SourceCleanupOwner") && basis._cleanupOwner is not null)
                        error.Data[prefix + "SourceCleanupOwner"] = basis._cleanupOwner;
                    basis.AttachProvenance(error);
                }
            }
        }
    }
}
