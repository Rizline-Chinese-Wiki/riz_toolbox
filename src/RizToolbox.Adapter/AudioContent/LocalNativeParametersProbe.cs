using System.Text.Json;
using RizToolbox.Adapter.Visual;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Explicit scratch-object probe. No controller, scene commit, playable chart or result is created.</summary>
internal sealed class LocalNativeParametersProbe : IDisposable
{
    private static readonly HashSet<LocalNativeParametersProbe> ProcessOwners = new();
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private OwnedNativeLocalMetadata? _metadata;
    private NativeResourceReferenceHandle? _cover, _music;
    private BorrowedNativeLocalParameters? _first, _second;
    private NativeRuntime.RootHandle? _texture, _sprite, _audio, _chart, _judge;
    private IntPtr _unityObject, _time;
    private IntPtr _unrootedAllocation;
    private bool _attempted, _cleanupAttempted, _cleanupUnknown;
    private bool _disposedReadRejected, _borrowWithdrawalRejected;
    private int _phase, _destroyFrame, _polls;

    internal LocalNativeParametersProbe(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, IntPtr module)
        => (_api, _main, _log, _profile, _module) = (api, main, log, profile, module);

    internal void TryStart()
    {
        _main.VerifyAccess(); if (_attempted) return; _attempted = true;
        try
        {
            if (ProcessOwners.Count >= 4) throw new InvalidOperationException("Parameter probe responsibility capacity is exhausted.");
            ProcessOwners.Add(this);
            var descriptor = new LocalNativeMetadataDescriptor(new("probe.parameters", "scratch-song"), 1, "scratch-chart",
                "LOCAL Parameter Sentinel", "Scratch Artist", "SCRATCH", "0", "Scratch Designer",
                new(.2f, .4f, .6f, 1), 0, 0);
            // The first factory enforces build/image/thread admission before any scratch allocation.
            _metadata = new NativeLocalMetadataFactory(_api, _main, _profile, _module).Create(descriptor);
            _unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            _time = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time");
            _texture = _api.NewExact(_api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Texture2D"),
                new[] { "System.Int32", "System.Int32" }, A.Value(2), A.Value(2));
            if (!Alive(_texture.Target)) throw new InvalidDataException("Scratch Texture2D was not live.");
            var spriteClass = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Sprite");
            var spriteCreate = _api.Method(spriteClass, "Create", 4, true,
                new[] { "UnityEngine.Rect", "UnityEngine.Vector2", "System.Single", "UnityEngine.Texture2D" });
            _unrootedAllocation = _api.Invoke(spriteCreate, IntPtr.Zero, A.Value(new NativeRect(0, 0, 2, 2)),
                A.Value(new Vec2(.5f, .5f)), A.Value(100f), A.Ref(_texture.Target));
            _sprite = RootPendingAllocation();
            var clipClass = _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
            var audioCreate = _api.Method(clipClass, "Create", 5, true,
                new[] { "System.String", "System.Int32", "System.Int32", "System.Int32", "System.Boolean" });
            _unrootedAllocation = _api.Invoke(audioCreate, IntPtr.Zero, _api.Str("RizToolbox.Parameters.Silent"),
                A.Value(80), A.Value(1), A.Value(8000), A.Value(false));
            _audio = RootPendingAllocation();
            if (_api.ObjectClass(_sprite.Target) != spriteClass || !Alive(_sprite.Target)
                || _api.ObjectClass(_audio.Target) != clipClass || !Alive(_audio.Target)
                || _api.Unbox<int>(_api.Call(_audio.Target, "get_samples")) != 80
                || _api.Unbox<int>(_api.Call(_audio.Target, "get_channels")) != 1
                || _api.Unbox<int>(_api.Call(_audio.Target, "get_frequency")) != 8000)
                throw new InvalidDataException("Scratch Sprite/AudioClip identity or audio geometry differs.");
            // These ordinary objects are constructor inputs only. An empty Chart is never sent to gameplay.
            var chartClass = _api.Class("Assembly-CSharp.dll", "InLevel", "Chart");
            var judgeClass = _api.Class("Assembly-CSharp.dll", "", "JudgeData");
            RequireConstructor(chartClass, 0x3E5190); RequireConstructor(judgeClass, 0x36C310);
            _chart = _api.NewExact(chartClass, Array.Empty<string>());
            _judge = _api.NewExact(judgeClass, Array.Empty<string>());
            var references = new NativeResourceReferenceFactory(_api, _main, _profile);
            _cover = references.CreateSprite(_sprite.Target, canDestroy: false);
            _music = references.CreateAudio(_audio.Target, canDestroy: false);
            var factory = new NativeLocalParametersFactory(_api, _main, _profile, _module);
            _first = factory.CreateBorrowing(_metadata, descriptor, _cover, _music, _chart, _judge);
            _second = factory.CreateBorrowing(_metadata, descriptor, _cover, _music, _chart, _judge);
            var firstId = _first.Pointer; var secondId = _second.Pointer;
            if (firstId == secondId || _first.RootCount != 1 || _second.RootCount != 1)
                throw new InvalidDataException("Parameter owners were not separate one-root objects.");
            Collect();
            var first = _first.Read(); var second = _second.Read();
            if (first != second || first.Metadata != _metadata.NativeIdentity || first.Chart != _chart.Target.ToInt64()
                || first.JudgeData != _judge.Target.ToInt64() || first.CoverReference != _cover.Pointer.ToInt64()
                || first.MusicReference != _music.Pointer.ToInt64())
                throw new InvalidDataException("Parameter graph changed after native collection.");
            Log("constructed", new { owners = 2, distinctIdentities = true, rootRecordsPerOwner = 1,
                nativeGcCollectReturned = true, first, speedBits = first.SpeedBits, offsetBits = first.OffsetBits,
                scratchChartOnly = true, controllerCreated = false, canLaunch = false });
            _first.Dispose();
            try { _first.Read(); } catch (ObjectDisposedException) { _disposedReadRejected = true; }
            if (!_disposedReadRejected || !_first.IsDisposed || _first.CleanupUnknown || _first.RootCount != 0)
                throw new InvalidDataException("Disposed parameter owner did not reject access.");
            Collect(); _second.Read();
            // Explicit negative borrow test, with no native consumer: withdrawing one borrowed
            // wrapper must invalidate reads even though its own non-destroyed resource stays rooted.
            _cover.Dispose();
            try { _second.Read(); } catch (ObjectDisposedException) { _borrowWithdrawalRejected = true; }
            if (!_borrowWithdrawalRejected || !Alive(_sprite.Target) || !Alive(_audio.Target))
                throw new InvalidDataException("Withdrawn borrow was accepted or non-destroying wrapper destroyed a scratch asset.");
            _second.Dispose(); _music.Dispose();
            if (_first.RootCount != 0 || _second.RootCount != 0 || _second.CleanupUnknown
                || !_cover.IsReleased || !_music.IsReleased || _cover.CleanupFailed || _music.CleanupFailed)
                throw new InvalidDataException("Parameter/wrapper cleanup did not return confirmed ownership.");
            _first = null; _second = null; _cover = null; _music = null;
            _metadata.Dispose(); if (_metadata.CleanupUnknown || _metadata.RootCount != 0) throw new InvalidDataException("Metadata cleanup is unknown.");
            _metadata = null;
            _chart.Dispose(); _chart = null; _judge.Dispose(); _judge = null;
            Destroy(_sprite.Target); Destroy(_audio.Target); Destroy(_texture.Target);
            _destroyFrame = Frame(); _phase = 1;
            Log("release-requested", new { frame = _destroyFrame, parameterRootRecordsReleased = 2,
                metadataRootRecordsReleased = 10, borrowedWrappersReleased = 2, scratchUnityObjects = 3,
                disposedReadRejected = _disposedReadRejected, borrowWithdrawalRejected = _borrowWithdrawalRejected,
                destructionVerified = false, gameCatalogModified = false, gameResultOrSavePathInvoked = false });
        }
        catch (Exception error) { Fail(error); }
    }

    internal void Tick()
    {
        _main.VerifyAccess(); if (_phase != 1) return;
        try
        {
            var frame = Frame(); if (frame <= _destroyFrame) return;
            if (Alive(_sprite!.Target) || Alive(_audio!.Target) || Alive(_texture!.Target))
            { if (++_polls < 5) return; throw new InvalidDataException("Scratch Unity destruction was not observed on five later-frame polls."); }
            // A failed GC-handle free must never be retried by the failure cleanup path.
            _cleanupAttempted = true; ReleaseObservationRoots(); _phase = 2; ProcessOwners.Remove(this);
            Log("verified", new { frame, destructionRequestedFrame = _destroyFrame, constructorFiveReferencesPreserved = true,
                gettersAfterNativeGc = true, twoIndependentParameterOwners = true, disposedReadRejected = _disposedReadRejected,
                borrowWithdrawalRejected = _borrowWithdrawalRejected, wrapperFalsePreservedOwnAssets = true,
                allThreeScratchUnityObjectsDestroyed = true, allObservationRootRecordsReleased = true,
                controllerCreated = false, gameCatalogModified = false, gameResultOrSavePathInvoked = false, canLaunch = false });
        }
        catch (Exception error) { Fail(error); }
    }

    private NativeRuntime.RootHandle RootPendingAllocation()
    { var root = _api.Root(_unrootedAllocation); _unrootedAllocation = IntPtr.Zero; return root; }
    private void RequireConstructor(IntPtr cls, uint rva)
    {
        var method = _api.InspectDeclaredMethod(cls, ".ctor", Array.Empty<string>(), "System.Void", _module);
        if (method.Entry?.Rva != rva) throw new UnsupportedSignatureException("Scratch constructor has no pinned executable entry.");
    }
    private bool Alive(IntPtr pointer) => _api.Unbox<bool>(_api.CallStatic(_unityObject, "op_Implicit", A.Ref(pointer)));
    private int Frame() => _api.Unbox<int>(_api.CallStatic(_time, "get_frameCount"));
    private void Destroy(IntPtr pointer)
    { if (Alive(pointer)) _api.Invoke(_api.Method(_unityObject, "Destroy", 1, true, new[] { "UnityEngine.Object" }), IntPtr.Zero, A.Ref(pointer)); }
    private void Collect()
    {
        var gc = _api.Class("mscorlib.dll", "System", "GC"); var method = _api.Method(gc, "Collect", 0, true, Array.Empty<string>());
        if (_api.DescribeReturnType(method) != "System.Void") throw new UnsupportedSignatureException("Native GC signature changed.");
        _api.Invoke(method, IntPtr.Zero);
    }
    private void Log(string phase, object value)
    {
        var body = JsonSerializer.Serialize(value); if (body.Length > 1800) throw new InvalidDataException("Parameter probe row exceeds diagnostic limits.");
        _log.Log(LogLevel.Info, "LocalParametersProbe " + phase + " " + body);
    }
    private void Fail(Exception error)
    {
        _phase = 3;
        try { _log.Log(LogLevel.Warning, "LocalParametersProbe unavailable: " + error.GetType().Name + ": " + error.Message); } catch { }
        if (_cleanupAttempted)
        {
            // The only possible pending native cleanup here is the once-only root release.
            // Keep its existing records; calling Dispose again could turn an unknown free
            // into a no-op and erase the original responsibility.
            if (_texture is not null || _sprite is not null || _audio is not null || _chart is not null || _judge is not null)
            {
                _cleanupUnknown = true; ProcessOwners.Add(this);
                try { _log.Log(LogLevel.Error, "LocalParametersProbe cleanup is unconfirmed; root frees will not be retried.", error); } catch { }
            }
            return;
        }
        Dispose();
    }
    private void ReleaseObservationRoots()
    {
        var failures = new List<Exception>();
        void Free(ref NativeRuntime.RootHandle? root)
        { if (root is null) return; try { root.Dispose(); root = null; } catch (Exception error) { failures.Add(error); } }
        Free(ref _judge); Free(ref _chart); Free(ref _sprite); Free(ref _audio); Free(ref _texture);
        if (failures.Count != 0) throw new AggregateException("Scratch observation roots have unknown cleanup.", failures);
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_cleanupAttempted) return; _cleanupAttempted = true; _phase = 3;
        var failures = new List<Exception>();
        void Attempt(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
        Attempt(() => _first?.Dispose()); Attempt(() => _second?.Dispose());
        Attempt(() => _cover?.Dispose()); Attempt(() => _music?.Dispose()); Attempt(() => _metadata?.Dispose());
        // Only scratch Unity objects created by this probe are ever destroyed.
        if (_sprite is not null) Attempt(() => Destroy(_sprite.Target));
        if (_audio is not null) Attempt(() => Destroy(_audio.Target));
        if (_texture is not null) Attempt(() => Destroy(_texture.Target));
        if (_unrootedAllocation != IntPtr.Zero)
            failures.Add(new InvalidOperationException("Scratch Unity allocation could not acquire a root; pointer reuse is forbidden and cleanup is unconfirmed."));
        // On failure keep the observation roots: delayed native Destroy has not been verified.
        // No next tick/retry may mistake a repeated no-op for confirmation.
        if (_sprite is not null || _audio is not null || _texture is not null)
            failures.Add(new InvalidOperationException("Scratch Unity destruction is pending or unverified; observation roots retained until process exit."));
        if (failures.Count == 0) Attempt(ReleaseObservationRoots);
        _cleanupUnknown = failures.Count != 0;
        if (!_cleanupUnknown) { ProcessOwners.Remove(this); return; }
        ProcessOwners.Add(this);
        try { _log.Log(LogLevel.Error, "LocalParametersProbe cleanup is unconfirmed; bounded responsibility retained until process exit.", new AggregateException(failures)); } catch { }
    }
}
