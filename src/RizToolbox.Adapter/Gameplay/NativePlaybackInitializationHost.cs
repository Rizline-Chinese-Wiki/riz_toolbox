using System.Runtime.ExceptionServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Host-only initialization descriptor. Captured from the exact shared
/// observer and actual controller/information/progress/pause graph; it contains
/// no player, source, clip, native source epoch or predecessor Clock.</summary>
internal sealed class NativePlaybackInitializationHost : IDisposable
{
    private readonly NativeRuntime _api;
    private readonly GameEventState.PlaybackRunAssociation _run;
    private readonly NativePlaybackInitializationHost _registered;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<NativeRuntime.RootHandle> _roots;
    private NativePlaybackStockClockRouter? _consumer;
    private PlaybackSourceGroupLease? _group;
    private bool _released;
    private ExceptionDispatchInfo? _rootReleaseFailure;
    internal NativePlaybackRateOrigin Origin { get; }
    internal long Progress => Origin.Progress;
    internal long GetTimeMethod { get; }
    private NativePlaybackInitializationHost(NativeRuntime api, GameEventState.PlaybackRunAssociation run,
        NativePlaybackRateOrigin origin, long getTimeMethod, List<NativeRuntime.RootHandle> roots)
        => (_api, _run, Origin, GetTimeMethod, _roots, _registered) = (api, run, origin, getTimeMethod, roots, this);
    internal static NativePlaybackInitializationHost Capture(NativeRuntime api, GameEventState.PlaybackRunAssociation run)
    {
        run.VerifyHostCurrent();
        var roots = new List<NativeRuntime.RootHandle>();
        try
        {
            // Retain each observed receiver before any dependent graph read.
            // The host observer is an association, not a GC-handle owner.
            var controller = api.RetainExactObject(new(run.Controller), api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController")); roots.Add(controller);
            var information = api.RetainExactObject(new(run.Information), api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation")); roots.Add(information);
            if (api.Read<IntPtr>(controller.Target, "LevelInformation") != information.Target)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The host controller no longer owns its observed information.");
            var progress = api.RetainExactObject(api.Read<IntPtr>(information.Target, "progressControl"), api.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl")); roots.Add(progress);
            var pause = api.RetainExactObject(api.Read<IntPtr>(controller.Target, "pauseUIControl"), api.Class("Assembly-CSharp.dll", "", "PauseUIControl")); roots.Add(pause);
            var playerClass = api.Class("Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core", "MUGMusicPlayer");
            var host = new NativePlaybackInitializationHost(api, run,
                new(run.RunId, run.Epoch, run.Controller, run.Information, progress.Target.ToInt64(), pause.Target.ToInt64()),
                api.Method(playerClass, "GetTime", 0).ToInt64(), roots);
            host.VerifyCurrent(api, run); return host;
        }
        catch { foreach (var root in roots.AsEnumerable().Reverse()) root.Dispose(); throw; }
    }
    private void Registered()
    {
        if (!ReferenceEquals(_registered, this)) throw new GameplayStateException(ErrorCode.TargetExpired, "A copied initialization host owns no root handles.");
        if (_thread != Environment.CurrentManagedThreadId) throw new GameplayStateException(ErrorCode.WrongThread, "Initialization host roots belong to their owner thread.");
    }
    internal bool RootsRetained => !_released;
    internal NativePlaybackStockSourceHub.CompletedInitializationReceipt? TryCaptureCompletedInitializer(PlaybackSourceGroupLease group)
    {
        VerifyCurrent(_api, _run);
        return group.TryCaptureCompletedInitializer(_api, _roots[2]);
    }
    internal void Claim(NativePlaybackStockClockRouter consumer, NativeRuntime api, GameEventState.PlaybackRunAssociation run)
    {
        VerifyCurrent(api, run);
        if (_consumer is not null) throw new GameplayStateException(ErrorCode.Conflict, "The retained initialization host already belongs to its sole Router.");
        _consumer = consumer;
    }
    internal void BindGroup(NativePlaybackStockClockRouter consumer, PlaybackSourceGroupLease group)
    {
        Owned(consumer);
        if (_group is not null) throw new GameplayStateException(ErrorCode.Conflict, "The initialization host already owns a physical association.");
        group.VerifyCurrent(); _group = group;
        group.BindHostProgressWatch(this, _api, _roots[2]);
    }
    private void Owned(NativePlaybackStockClockRouter consumer)
    {
        Registered();
        if (_released || !ReferenceEquals(_consumer, consumer)) throw new GameplayStateException(ErrorCode.TargetExpired, "Only the exact Router owns retained initialization host roots.");
    }
    internal void ReleaseAfterSourceOpen(NativePlaybackStockClockRouter consumer, NativePlaybackStockRootedSource source,
        NativePlaybackSingleReturnBridge.InitialCapture capture)
    {
        Owned(consumer); capture.Read(3);
        source.VerifyFirstHostRootSuccession(this, _group!); // seven exact roots exist before these four are released
        if (!_group!.TryRetireProgressWatch(this))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The actual first source did not retire its predecessor host Progress watch.");
        ReleaseRoots();
    }
    internal bool TryReleaseStopped(NativePlaybackStockClockRouter consumer, PlaybackSourceGroupLease? group)
    {
        Owned(consumer);
        if (!ReferenceEquals(_group, group)) throw new GameplayStateException(ErrorCode.TargetExpired, "Another physical group cannot release this initialization host.");
        if (group is not null)
        {
            var source = group.ReadActivity(); var getTime = group.GetTimeLease.ReadActivity();
            if (!source.Known || source.InFlight != 0 || !getTime.Known || getTime.InFlight != 0) return false;
            if (!group.TryRetireProgressWatch(this)) return false;
        }
        ReleaseRoots(); return true;
    }
    private void ReleaseRoots()
    {
        Registered(); _rootReleaseFailure?.Throw();
        Exception? first = null;
        foreach (var root in _roots.AsEnumerable().Reverse().ToArray())
        {
            try { root.Dispose(); _roots.Remove(root); }
            catch (Exception error) { first ??= error; }
        }
        // A failed native free has already consumed its RootHandle id. Keep
        // its record and unknown outcome; a later no-op Dispose proves no exit.
        if (first is not null) { _rootReleaseFailure = ExceptionDispatchInfo.Capture(first); _rootReleaseFailure.Throw(); }
        _released = true;
    }
    public void Dispose()
    {
        Registered(); if (_released) return;
        if (_consumer is not null) throw new GameplayStateException(ErrorCode.Conflict, "The Router must drain its actual responsibilities before releasing the adopted host roots.");
        ReleaseRoots();
    }
    internal void VerifyCurrent(NativeRuntime api, GameEventState.PlaybackRunAssociation run)
    {
        Registered();
        if (_released || !ReferenceEquals(_api, api) || !ReferenceEquals(_run, run))
            throw new GameplayStateException(ErrorCode.TargetExpired, "A copied or foreign initialization host has no registered graph.");
        run.VerifyHostCurrent();
        var controllerClass = api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        var informationClass = api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        var progressClass = api.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl");
        var pauseClass = api.Class("Assembly-CSharp.dll", "", "PauseUIControl");
        var expected = new[] { Origin.Controller, Origin.Information, Origin.Progress, Origin.Pause };
        var classes = new[] { controllerClass, informationClass, progressClass, pauseClass };
        if (_roots.Count != 4 || _roots.Where((root, i) => root.Target.ToInt64() != expected[i] || api.ObjectClass(root.Target) != classes[i]).Any())
            throw new GameplayStateException(ErrorCode.TargetExpired, "The independent initialization host roots or classes changed.");
        var controller = _roots[0]; var information = _roots[1]; var progress = _roots[2]; var pause = _roots[3];
        if (api.Read<IntPtr>(controller.Target, "LevelInformation") != information.Target
            || api.Read<IntPtr>(controller.Target, "pauseUIControl") != pause.Target
            || api.Read<IntPtr>(information.Target, "progressControl") != progress.Target
            || api.Read<IntPtr>(progress.Target, "levelInformation") != information.Target
            || !api.Unbox<bool>(api.Call(controller.Target, "get_isActiveAndEnabled"))
            || api.Method(api.Class("Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core", "MUGMusicPlayer"), "GetTime", 0).ToInt64() != GetTimeMethod)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The actual initialization host graph or GetTime metadata changed.");
        run.VerifyHostCurrent();
    }
    internal NativePlaybackBinding BindCompleted(NativePlaybackSingleReturnBridge.InitialCapture capture)
    {
        VerifyCurrent(_api, _run);
        var native = capture.Read(2);
        var epoch = native.Epoch; var player = checked((long)native.Player); var source = checked((long)native.Source); var clip = checked((long)native.Clip);
        if (epoch == 0 || epoch > long.MaxValue || player == 0 || source == 0 || clip == 0)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The native first-capture has no complete admitted graph.");
        return new(_run.RunId, _run.SceneGeneration, _run.Epoch, (long)epoch,
            Origin.Controller, Origin.Information, Origin.Progress, player, source, clip, GetTimeMethod);
    }
}
