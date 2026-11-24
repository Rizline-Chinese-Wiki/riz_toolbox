using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            /// <summary>One read-only child of the original physical Canvas
            /// source. All permanent roots stay in its shared deduplicated table;
            /// this reader owns only its independently revocable managed hold.</summary>
            private sealed partial class LineRingReader : IRetainedSeekLineRingReader, IRetainedSeekLineRingSceneReader
            {
                private readonly int _thread = Environment.CurrentManagedThreadId;
                private readonly SharedBasisSource _basis;
                private readonly ISeekLineRingSourceProfile _profile;
                private SharedBasisSource.ChildLease? _lease;
                private CanvasSource _source = null!;
                private NativeSeekCanvasTargetCapture? _capture;
                private readonly IntPtr[] _classes = Array.Empty<IntPtr>();
                private readonly IntPtr _component;
                private NativeRuntime.RootHandle _control = null!;
                private long _controlAddress;
                private NativeRuntime.RootHandle? _linePositions;
                private long _linePositionsAddress;
                private readonly Dictionary<string, NativeMetadataMethod> _getters = new();
                private readonly Dictionary<(IntPtr Class, string Name, string Type, int Offset), NativeExactReadField> _descriptors = new();
                private Action? _lifetime;
                private Action? _constructionGuard;
                private bool _withdrawn, _expired, _releaseAttempted, _released, _captureCompleted;
                private Exception? _cleanup;
                private LineRingObservationRefusedException? _publicationRefusal;
                private object? _unknownSourceOwner;

                // This is an additional, explicit complete-graph allowance. It
                // never replaces or reduces the existing Canvas root allowance.
                private static int AdditionalRootCapacity => checked(7 // includes original LineControl.linePositions
                    + 2 * NativeSeekLineRingSourceCapture.MaximumGroups
                    + 9 * NativeSeekLineRingSourceCapture.MaximumLines // Line plus saved array/plan/pool/child/consumer roots
                    + NativeSeekLineRingSourceCapture.MaximumPoints
                    + NativeSeekLineRingSourceCapture.MaximumTransforms);

                internal LineRingReader(SharedBasisSource basis, ISeekLineRingSourceProfile profile, Action? constructionGuard)
                {
                    _basis = basis; _profile = profile; _constructionGuard = constructionGuard;
                    try
                    {
                        _lease = basis.AcquireLineRingChild();
                        _capture = basis.SourceCapture; _source = basis.PhysicalSource;
                        using var operation = EnterOperation();
                        _source._core._rootLimit = checked(_source._core._rootLimit + AdditionalRootCapacity);
                        N(() => { _profile.VerifyCurrent(VerifyLocal); return 0; });
                        _classes = ReadClasses(); VerifyClasses();
                        _component = N(() => _source._core._owner._api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component"));
                        Layouts(); VerifyGetters(initial: true);
                        _control = RequiredReference(_source._core._information, _classes[2],
                            "lineControl", "InLevel.LineControl", 0x38);
                        _controlAddress = Address(_control);
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

                public NativeSeekCanvasTargetCapture SourceCapture
                {
                    get
                    {
                        VerifyOwnerThread();
                        return _capture ?? _basis.SourceCapture;
                    }
                }
                public void VerifyOwnerThread()
                {
                    if (Environment.CurrentManagedThreadId != _thread)
                        throw new InvalidOperationException("The actual line/ring reader belongs to its original owner thread.");
                }
                private void OwnLifetime()
                {
                    VerifyOwnerThread();
                    if (_withdrawn || _released) throw new ObjectDisposedException(nameof(LineRingReader));
                    if (_expired) throw new InvalidOperationException("The actual line/ring source was previously observed expired.");
                    InvokeCallerGuard(_constructionGuard); InvokeCallerGuard(_lifetime);
                }
                private static void InvokeCallerGuard(Action? guard)
                {
                    if (guard is null) return;
                    try { guard(); }
                    catch (Exception error) { SeekCanvasBasisCallerWithdrawal.Mark(error); throw; }
                }
                public void VerifyLocal()
                {
                    OwnLifetime();
                    if (_publicationRefusal is not null) throw _publicationRefusal;
                    _source.VerifyLocal();
                }
                public IDisposable EnterOperation()
                {
                    VerifyOwnerThread();
                    try { return new ReaderOperation(this, _basis.EnterOperation(OwnLifetime)); }
                    catch (Exception error) { ObserveCleanupFailure(error); throw; }
                }
                private sealed class ReaderOperation : IDisposable
                {
                    private readonly LineRingReader _owner;
                    private readonly IDisposable _scope;
                    internal ReaderOperation(LineRingReader owner, IDisposable scope) { _owner = owner; _scope = scope; }
                    public void Dispose()
                    {
                        // A known managed publication refusal belongs to this
                        // observation only. Its GC token was acknowledged before
                        // the post-publication guard rejected the operation.
                        _owner._publicationRefusal = null;
                        try { _scope.Dispose(); }
                        catch (Exception error) { _owner.ObserveCleanupFailure(error); throw; }
                    }
                }
                public void BindLifetimeGuard(Action guard)
                {
                    VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
                    if (_lifetime is not null) throw new InvalidOperationException("The line/ring reader lifetime attaches only once.");
                    _lifetime = guard;
                }
                public void CompleteCapture()
                {
                    VerifyLocal();
                    if (_captureCompleted) throw new InvalidOperationException("The original line/ring reader capture completes only once.");
                    _captureCompleted = true; _constructionGuard = null;
                    VerifyLocal();
                }
                private T N<T>(Func<T> action)
                {
                    try { VerifyLocal(); var value = action(); VerifyLocal(); return value; }
                    catch (Exception error) { ObserveCleanupFailure(error); throw; }
                }
                private InvalidOperationException Changed(string reason)
                { _expired = true; return new InvalidOperationException(reason); }
                private void Attach(Exception error, Exception cleanup)
                {
                    error.Data["SeekLineRingSourceCleanupOwner"] = this;
                    error.Data["SeekLineRingSourceCleanupFailure"] = cleanup;
                    error.Data["SeekLineRingSourceNativeCleanupOwner"] = _unknownSourceOwner ?? _basis.SourceCleanupOwner ?? (object?)_capture ?? _basis;
                    _basis.AttachProvenance(error);
                }
                private void ObserveCleanupFailure(Exception error)
                {
                    _basis.ObserveCleanupFailure(error);
                    if (_basis.CleanupFailure is not Exception cleanup) return;
                    _cleanup ??= cleanup; _unknownSourceOwner ??= _basis.SourceCleanupOwner;
                    Attach(error, cleanup);
                }
                public void VerifySource()
                {
                    try
                    {
                        VerifyLocal(); N(() => { _profile.VerifyCurrent(VerifyLocal); return 0; });
                        if (!ReadClasses().SequenceEqual(_classes))
                            throw Changed("The exact line/ring source profile class identities changed.");
                        VerifyClasses(); Layouts(); VerifyGetters(initial: false);
                        N(() => { _basis.VerifySourceInOperation(); return 0; });
                        if (Address(RequiredReference(_source._core._information, _classes[2], "lineControl", "InLevel.LineControl", 0x38)) != _controlAddress
                            || Address(RequiredReference(_control, _classes[3], "levelInformation", "InLevel.LevelInformation", 0x20)) != _source._core._informationAddress
                            || Address(RequiredReference(_control, _classes[3], "_canvasControl", "InLevel.CanvasControl", 0x38)) != _source._canvasAddress
                            || !Alive(_control))
                            throw Changed("The actual LineControl lost its original information/CanvasControl source associations.");
                        foreach (var pair in _descriptors.ToArray())
                            if (Field(pair.Key.Class, pair.Key.Name, pair.Key.Type, pair.Key.Offset) != pair.Value)
                                throw Changed("A saved/current line/ring source field descriptor changed: " + pair.Key.Name);
                        if (_linePositions is not null) VerifyLinePositionsAssociation();
                        VerifyLocal();
                    }
                    catch (Exception error)
                    {
                        if (!SeekCanvasBasisCallerWithdrawal.Is(error) && error is not LineRingObservationRefusedException) _expired = true;
                        if (_expired) error.Data["SeekLineRingSourceExpired"] = true;
                        ObserveCleanupFailure(error); _basis.AttachProvenance(error); throw;
                    }
                }
                public void RequestDispose() { VerifyOwnerThread(); _withdrawn = true; }
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
                            _unknownSourceOwner ??= _basis.SourceCleanupOwner ?? (object?)_capture ?? _basis;
                        }
                    }
                    if (_cleanup is null) return;
                    var failure = new AggregateException("The actual line/ring reader retains unknown source cleanup without native retry.", _cleanup);
                    Attach(failure, _cleanup); throw failure;
                }
            }
        }
    }
}
