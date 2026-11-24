using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private static IRetainedSeekCanvasTargetReader CreateSeekCanvasTargetReader(NativeJudgementBackend owner,
        RunValue run, ISeekCanvasNativeProfile profile) => NativeSeekNoteFlagsAccess.CreateCanvasSource(owner, run, profile);
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        internal static IRetainedSeekCanvasTargetReader CreateCanvasSource(NativeJudgementBackend owner, RunValue run, ISeekCanvasNativeProfile profile)
        {
            try { return new CanvasSource(owner, run, profile); }
            catch (Exception error)
            {
                if (!error.Data.Contains("SeekCanvasSourceCleanupFailure") && error.Data["SeekNoteFlagsCleanupFailure"] is Exception cleanup)
                {
                    error.Data["SeekCanvasSourceCleanupOwner"] = error.Data["SeekNoteFlagsCleanupOwner"];
                    error.Data["SeekCanvasSourceCleanupFailure"] = cleanup;
                }
                throw;
            }
        }
        private sealed partial class CanvasSource : IRetainedSeekCanvasTargetReader
        {
            private readonly NativeSeekNoteFlagsAccess _core;
            private readonly RunValue _origin;
            private readonly long _constructionSerial;
            private readonly SharedBasisSource? _basis;
            private readonly ISeekCanvasNativeProfile _profile;
            private readonly IntPtr[] _classes;
            private readonly NativeRuntime.RootHandle _canvas;
            private readonly long _canvasAddress;
            private readonly NativeMetadataMethod _range;
            private readonly Dictionary<(IntPtr Class, string Name, string Type, bool Static, int Offset), NativeExactReadField> _fields = new();
            private readonly Dictionary<(IntPtr Class, string Name), NativeMetadataMethod> _methods = new();
            private IDisposable? _viewLease;
            private Action? _lifetime;
            private bool _withdrawn, _expired, _released;
            private Exception? _cleanup;
            private Exception? _publicationAdmissionFailure;
            private sealed class ConstructionProfile : ISeekChartNativeProfile
            {
                private readonly ISeekCanvasNativeProfile _profile; private readonly RunValue _run; private readonly long _serial;
                internal ConstructionProfile(ISeekCanvasNativeProfile profile, RunValue run, long serial) { _profile = profile; _run = run; _serial = serial; }
                private T Guard<T>(Func<T> get) { _run.VerifyCanvasSourceConstruction(_serial); var v = get(); _run.VerifyCanvasSourceConstruction(_serial); return v; }
                public IntPtr ChartClass => Guard(() => _profile.ChartClass);
                public IntPtr ProgressClass => Guard(() => _profile.ProgressClass);
                public void VerifyCurrent() => Guard(() => { _profile.VerifyCurrent(); return 0; });
            }
            internal CanvasSource(NativeJudgementBackend owner, RunValue run, ISeekCanvasNativeProfile profile,
                SharedBasisSource? basis = null)
            {
                ArgumentNullException.ThrowIfNull(profile); _origin = run; _profile = profile; _basis = basis;
                _constructionSerial = run.CanvasSourceConstructionSerial;
                _core = new(owner, run, new ConstructionProfile(profile, run, _constructionSerial),
                    basis is null ? null : basis.VerifySourceUse);
                try
                {
                    if (basis is not null) _core._view!.BindCanvasUseGuard(basis.VerifySourceUse);
                    _viewLease = _core._view!.RetainPhysicalSource();
                    _core._rootLimit = checked(32 + 4 * NativeSeekRuntimeNoteView.MaximumNotes + 2 * NativeSeekCanvasTargetPlan.MaxTotalKeypoints);
                    _core.BindLifetimeGuard(OwnLifetime);
                    _classes = ReadClasses();
                    var specifications = new[]
                    {
                        ("Assembly-CSharp.dll", "InLevel", "Chart"), ("Assembly-CSharp.dll", "InLevel", "ProgressControl"),
                        ("Assembly-CSharp.dll", "InLevel", "LevelInformation"), ("Assembly-CSharp.dll", "InLevel", "CanvasControl"),
                        ("Assembly-CSharp.dll", "InLevel", "CanvasMove"), ("Assembly-CSharp.dll", "InLevel", "KeyPoint"),
                        ("UnityEngine.CoreModule.dll", "UnityEngine", "Transform"), ("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3"),
                        ("Assembly-CSharp.dll", "InLevel", "GetEase"), ("Assembly-CSharp.dll", "InLevel", "EaseInfor")
                    };
                    for (var i = 0; i < specifications.Length; ++i)
                    {
                        var s = specifications[i];
                        if (N(() => owner._api.Class(s.Item1, s.Item2, s.Item3)) != _classes[i])
                            throw new UnsupportedSignatureException("The Canvas source profile has another exact declared class.");
                    }
                    _canvas = Reference(_core._information, _classes[2], "canvasControl", "InLevel.CanvasControl", 0x70);
                    if (N(() => owner._api.ObjectClass(Target(_canvas))) != _classes[3])
                        throw new UnsupportedSignatureException("The actual CanvasControl has another exact class.");
                    _canvasAddress = Target(_canvas).ToInt64();
                    _range = InspectMethod(_classes[1], "ChartTimeRageToRealTime", new[] { "System.Double", "System.Double" }, "System.Single");
                    if (_range.MethodInfo != N(() => profile.ChartTimeRangeMethod))
                        throw new UnsupportedSignatureException("The Canvas range declaration differs from the exact source profile.");
                    VerifyAccess();
                }
                catch (Exception error)
                {
                    try { Dispose(); }
                    catch (Exception cleanup) { error.Data["SeekCanvasSourceCleanupOwner"] = this; error.Data["SeekCanvasSourceCleanupFailure"] = cleanup; }
                    throw;
                }
            }
            public void VerifyOwnerThread() => _core.VerifyOwnerThread();
            private void OwnLifetime()
            {
                VerifyOwnerThread(); _origin.VerifyCanvasSourceConstruction(_constructionSerial);
                if (_publicationAdmissionFailure is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_publicationAdmissionFailure).Throw();
                if (_withdrawn) throw new ObjectDisposedException(nameof(CanvasSource));
                if (_expired) throw new InvalidOperationException("The actual Canvas source was already observed expired.");
                _basis?.VerifySourceUse();
                _lifetime?.Invoke();
            }
            public void VerifyLocal() { OwnLifetime(); _core.VerifyLocal(); }
            private T N<T>(Func<T> action)
            {
                try { VerifyLocal(); var v = action(); VerifyLocal(); return v; }
                catch (Exception error) { _basis?.ObserveCleanupFailure(error); throw; }
            }
            private InvalidOperationException Changed(string reason) { _expired = true; return new InvalidOperationException(reason); }
            public void BindLifetimeGuard(Action guard)
            {
                VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
                if (_lifetime is not null) throw new InvalidOperationException("Canvas source lifetime is once-attached.");
                _lifetime = guard;
            }
            private IntPtr[] ReadClasses() => new[]
            {
                N(() => _profile.ChartClass), N(() => _profile.ProgressClass), N(() => _profile.InformationClass),
                N(() => _profile.CanvasClass), N(() => _profile.CanvasMoveClass), N(() => _profile.KeyPointClass),
                N(() => _profile.TransformClass), N(() => _profile.VectorClass), N(() => _profile.EaseClass), N(() => _profile.EaseInfoClass)
            };
            public void VerifyAccess()
            {
                try
                {
                    VerifyLocal(); N(() => { _profile.VerifyCurrent(); return 0; });
                    if (!ReadClasses().SequenceEqual(_classes)) throw Changed("The exact Canvas profile class identities changed.");
                    _core.VerifyCurrent();
                    if (!N(() => _core.Alive(_canvas)) || N(() => _core._owner._api.ObjectClass(Target(_canvas))) != _classes[3]
                        || _core.ReferenceToken(_core._information, _classes[2], "canvasControl", "InLevel.CanvasControl", 0x70) != _canvasAddress
                        || _core.ReferenceToken(_canvas, _classes[3], "levelInformation", "InLevel.LevelInformation", 0x20) != _core._informationAddress)
                        throw Changed("The actual CanvasControl lost its live source associations.");
                    if (N(() => _profile.ChartTimeRangeMethod) != _range.MethodInfo
                        || !SameMethod(_range, InspectMethod(_classes[1], "ChartTimeRageToRealTime", new[] { "System.Double", "System.Double" }, "System.Single")))
                        throw Changed("The saved/current Canvas range declaration changed.");
                    VerifyLocal();
                }
                catch (Exception error) { if (!SeekCanvasBasisCallerWithdrawal.Is(error)) _expired = true; throw; }
            }
            public void RequestDispose() { VerifyOwnerThread(); _withdrawn = true; _core.RequestDispose(); }
            public void Dispose()
            {
                VerifyOwnerThread(); _withdrawn = true;
                if (!_released)
                {
                    _released = true; var lease = _viewLease; _viewLease = null;
                    try { lease?.Dispose(); } catch (Exception error) { _core.RecordViewFailure(error); _cleanup = error; }
                    try { _core.Dispose(); } catch (Exception error) { _cleanup = _cleanup is null ? error : new AggregateException(_cleanup, error); }
                }
                if (_cleanup is null) return;
                var failure = new AggregateException("Canvas source retains unknown cleanup responsibility without retry.", _cleanup);
                failure.Data["SeekCanvasSourceCleanupOwner"] = this; failure.Data["SeekCanvasSourceCleanupFailure"] = _cleanup; throw failure;
            }
        }
    }
}
