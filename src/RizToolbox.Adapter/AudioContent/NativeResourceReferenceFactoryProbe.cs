using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Opt-in bounded native lifecycle proof using two self-created, silent AudioClips.</summary>
internal sealed class NativeResourceReferenceFactoryProbe : IDisposable
{
    private static readonly HashSet<NativeResourceReferenceFactoryProbe> FailedCleanup = new();
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile;
    private NativeRuntime.RootHandle? _preserved, _destroyed, _creating;
    // An allocation whose GC-handle creation failed is diagnostic responsibility only.
    // Never reuse this unrooted pointer after the failure boundary.
    private IntPtr _unrootedAllocation;
    private IntPtr _object, _time;
    private bool _attempted, _preservedAfterRelease, _destroyedAfterRelease, _cleanupFailed;
    private int _phase, _startFrame, _polls;

    internal NativeResourceReferenceFactoryProbe(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile) => (_api, _main, _log, _profile) = (api, main, log, profile);

    internal void TryStart()
    {
        _main.VerifyAccess();
        if (_attempted) return;
        _attempted = true;
        try
        {
            if (FailedCleanup.Count >= 4)
                throw new InvalidOperationException("Probe cleanup failure capacity is exhausted; process restart is required.");
            _object = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            _time = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time");
            _preserved = CreateSilentClip("RizToolbox.ReferenceProbe.Borrowed");
            _destroyed = CreateSilentClip("RizToolbox.ReferenceProbe.Owned");
            var factory = new NativeResourceReferenceFactory(_api, _main, _profile);
            using (var wrapper = factory.CreateAudio(_preserved.Target, canDestroy: false))
            {
                using var borrowed = wrapper.BorrowResource();
                if (borrowed.Target != _preserved.Target || !GeometryMatches(borrowed.Target))
                    throw new InvalidDataException("Non-destroying wrapper did not return its exact source AudioClip.");
                wrapper.Dispose();
                if (!wrapper.IsReleased || wrapper.CleanupFailed)
                    throw new InvalidDataException("Non-destroying wrapper Release did not complete.");
            }
            using (var wrapper = factory.CreateAudio(_destroyed.Target, canDestroy: true))
            {
                using var borrowed = wrapper.BorrowResource();
                if (borrowed.Target != _destroyed.Target || !GeometryMatches(borrowed.Target))
                    throw new InvalidDataException("Destroying wrapper did not return its exact source AudioClip.");
                wrapper.Dispose();
                if (!wrapper.IsReleased || wrapper.CleanupFailed)
                    throw new InvalidDataException("Destroying wrapper Release did not complete.");
            }
            _startFrame = Frame(); _phase = 1;
            Log("factory-started", new { resources = 2, sampleFrames = 80, channels = 1, frequency = 8000,
                canDestroyFalseReleaseReturned = true, canDestroyTrueReleaseReturned = true,
                startFrame = _startFrame, destructionVerified = false });
        }
        catch (Exception error) { Fail(error); }
    }

    internal void Tick()
    {
        _main.VerifyAccess();
        if (_phase is not (1 or 2)) return;
        try
        {
            var frame = Frame();
            if (frame <= _startFrame) return;
            _polls++;
            if (_phase == 1)
            {
                _preservedAfterRelease = Alive(_preserved!.Target) && GeometryMatches(_preserved.Target);
                _destroyedAfterRelease = !Alive(_destroyed!.Target);
                if (!_preservedAfterRelease)
                    throw new InvalidDataException("canDestroy=false failed to preserve the independent AudioClip.");
                if (!_destroyedAfterRelease)
                {
                    if (_polls < 5) return;
                    throw new InvalidDataException("canDestroy=true did not destroy its AudioClip within five later-frame polls.");
                }
                Log("factory-release-observed", new { frame, startFrame = _startFrame,
                    preservedAfterRelease = _preservedAfterRelease, destroyedAfterRelease = _destroyedAfterRelease,
                    source = "UnityEngine.Object.op_Implicit and AudioClip samples/channels/frequency" });
                Destroy(_preserved.Target);
                _phase = 2; _startFrame = frame; _polls = 0;
                return;
            }
            if (Alive(_preserved!.Target))
            {
                if (_polls < 5) return;
                throw new InvalidDataException("Probe-owned non-destroying AudioClip cleanup did not complete.");
            }
            if (Alive(_destroyed!.Target))
                throw new InvalidDataException("Previously destroyed probe AudioClip became live again.");
            ReleaseRoots();
            _phase = 3;
            Log("factory-verified", new { frame, cleanupRequestedFrame = _startFrame, canDestroyFalsePreserved = _preservedAfterRelease,
                canDestroyTrueDestroyed = _destroyedAfterRelease, explicitProbeCleanupDestroyed = true,
                exactGetterReturnedSource = true, checkedOnLaterFrames = true, bothObservationRootsReleased = true,
                gameResourcesUsed = false });
        }
        catch (Exception error) { Fail(error); }
    }

    private NativeRuntime.RootHandle CreateSilentClip(string name)
    {
        var clip = _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
        var create = _api.Method(clip, "Create", 5, true,
            new[] { "System.String", "System.Int32", "System.Int32", "System.Int32", "System.Boolean" });
        _unrootedAllocation = _api.Invoke(create, IntPtr.Zero, _api.Str(name), A.Value(80), A.Value(1), A.Value(8000), A.Value(false));
        _creating = _api.Root(_unrootedAllocation);
        _unrootedAllocation = IntPtr.Zero;
        // Keep the pending allocation in the probe until validation completes. If validation or
        // Destroy fails, the outer failure owner still has the root and its bounded responsibility.
        if (_api.ObjectClass(_creating.Target) != clip || !Alive(_creating.Target) || !GeometryMatches(_creating.Target))
            throw new InvalidDataException("Probe AudioClip creation did not match the requested geometry.");
        var result = _creating; _creating = null;
        return result;
    }

    private bool GeometryMatches(IntPtr clip)
        => _api.Unbox<int>(_api.Call(clip, "get_samples")) == 80
            && _api.Unbox<int>(_api.Call(clip, "get_channels")) == 1
            && _api.Unbox<int>(_api.Call(clip, "get_frequency")) == 8000;
    private bool Alive(IntPtr clip) => _api.Unbox<bool>(_api.CallStatic(_object, "op_Implicit", A.Ref(clip)));
    private int Frame() => _api.Unbox<int>(_api.CallStatic(_time, "get_frameCount"));
    private void Destroy(IntPtr clip)
    {
        if (!Alive(clip)) return;
        _api.Invoke(_api.Method(_object, "Destroy", 1, true, new[] { "UnityEngine.Object" }), IntPtr.Zero, A.Ref(clip));
    }
    private void ReleaseRoots()
    {
        var errors = new List<Exception>();
        try { _preserved?.Dispose(); _preserved = null; } catch (Exception error) { errors.Add(error); }
        try { _destroyed?.Dispose(); _destroyed = null; } catch (Exception error) { errors.Add(error); }
        try { _creating?.Dispose(); _creating = null; } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Probe observation root cleanup failed.", errors);
    }
    private void Log(string stage, object values)
    {
        try { _log.Log(LogLevel.Info, "LocalResourceReferenceProbe " + stage + " " + JsonSerializer.Serialize(values)); } catch { }
    }
    private void Fail(Exception error)
    {
        _phase = 4;
        try { _log.Log(LogLevel.Warning, "LocalResourceReferenceProbe factory unavailable: " + error.GetType().Name + ": " + error.Message); } catch { }
        Dispose();
    }
    public void Dispose()
    {
        _main.VerifyAccess();
        _phase = 4;
        if (_cleanupFailed) return;
        try
        {
            if (_preserved is not null) Destroy(_preserved.Target);
            if (_destroyed is not null) Destroy(_destroyed.Target);
            if (_creating is not null) Destroy(_creating.Target);
            ReleaseRoots();
            if (_unrootedAllocation != IntPtr.Zero)
                throw new InvalidOperationException("Probe AudioClip allocation could not acquire a GC root; native cleanup is unconfirmed and pointer reuse is forbidden.");
        }
        catch (Exception error)
        {
            _cleanupFailed = true;
            FailedCleanup.Add(this);
            try { _log.Log(LogLevel.Error, "LocalResourceReferenceProbe cleanup outcome unknown; responsibility retained until process exit.", error); } catch { }
        }
    }
}
