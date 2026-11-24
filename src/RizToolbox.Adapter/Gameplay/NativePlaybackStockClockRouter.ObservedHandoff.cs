using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    private readonly List<ObservedGraph> _unreleasedObservedGraphs = new();
    internal sealed partial class Handoff
    {
        internal bool TryAdmitObserved(GameEventState.PlaybackRunAssociation run)
        {
            Registered(); run.VerifyHostCurrent();
            if (_owner._freshCapture is null)
                throw new GameplayStateException(ErrorCode.TargetExpired, "An observed successor requires the retained formal route lineage.");
            if (_successor is not null) return TryAdmit(run, _admittedGraph!, _admittedPause);
            if (run.RunId == _predecessor.HandoffRunId)
                throw new GameplayStateException(ErrorCode.InvalidArgument, "An ordinary successor needs a distinct host-issued run.");
            if (_native.Read().Phase == 1 && !_native.TryAdmit()) return false;
            var info = _native.Read(2);
            try
            {
                using var graph = ObservedGraph.Capture(_owner, run, checked((long)info.NewEpoch), _owner._binding.GetTimeMethod);
                graph.Verify();
                return TryAdmit(run, graph.Binding, graph.Pause);
            }
            catch (Exception error)
            {
                _consumed = true;
                _owner._transferFailure = "The admitted observed successor failed closed: " + error.Message;
                throw;
            }
        }
    }

    // This short capture owns every object before its dependent reference is
    // read. The actual successor then retains its own seven handles while these
    // remain alive. No host-supplied graph or guessed generation is accepted.
    private sealed class ObservedGraph : IDisposable
    {
        private readonly NativeRuntime _api;
        private readonly NativePlaybackStockClockRouter _owner;
        private readonly GameEventState.PlaybackRunAssociation _run;
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        private readonly ObservedGraph _registered;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private bool _disposed;
        internal NativePlaybackBinding Binding { get; private set; } = null!;
        internal long Pause => Pointer(3).ToInt64();
        private ObservedGraph(NativePlaybackStockClockRouter owner, GameEventState.PlaybackRunAssociation run)
            => (_api, _owner, _run, _registered) = (owner._api, owner, run, this);
        internal static ObservedGraph Capture(NativePlaybackStockClockRouter owner, GameEventState.PlaybackRunAssociation run, long epoch, long getTime)
        {
            run.VerifyHostCurrent();
            var value = new ObservedGraph(owner, run); var api = owner._api;
            try
            {
                value.Retain(new(run.Controller), "Assembly-CSharp.dll", "", "NormalModeInLevelController");
                value.Retain(new(run.Information), "Assembly-CSharp.dll", "InLevel", "LevelInformation");
                if (api.Read<IntPtr>(value.Pointer(0), "LevelInformation") != value.Pointer(1))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The newly observed controller information changed during capture.");
                value.Retain(api.Read<IntPtr>(value.Pointer(1), "progressControl"), "Assembly-CSharp.dll", "InLevel", "ProgressControl");
                value.Retain(api.Read<IntPtr>(value.Pointer(0), "pauseUIControl"), "Assembly-CSharp.dll", "", "PauseUIControl");
                value.Retain(api.Read<IntPtr>(value.Pointer(2), "<MusicPlayer>k__BackingField"), "Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core", "MUGMusicPlayer");
                value.Retain(api.Read<IntPtr>(value.Pointer(2), "audioSource"), "UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource");
                value.Retain(api.Call(value.Pointer(5), "get_clip"), "UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
                value.Binding = new(run.RunId, run.SceneGeneration, run.Epoch, epoch, run.Controller, run.Information,
                    value.Pointer(2).ToInt64(), value.Pointer(4).ToInt64(), value.Pointer(5).ToInt64(), value.Pointer(6).ToInt64(), getTime);
                value.Verify(); return value;
            }
            catch { value.Dispose(); throw; }
        }
        private void Retain(IntPtr target, string image, string ns, string type)
            => _roots.Add(_api.RetainExactObject(target, _api.Class(image, ns, type)));
        private IntPtr Pointer(int index) => _roots[index].Target;
        internal void Verify()
        {
            if (_disposed || _thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_registered, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied or released successor graph owns no retained observation.");
            _run.VerifyCurrent(Binding);
            if (_roots.Count != 7 || _api.Read<IntPtr>(Pointer(0), "LevelInformation") != Pointer(1)
                || _api.Read<IntPtr>(Pointer(0), "pauseUIControl") != Pointer(3)
                || _api.Read<IntPtr>(Pointer(1), "progressControl") != Pointer(2)
                || _api.Read<IntPtr>(Pointer(2), "levelInformation") != Pointer(1)
                || _api.Read<IntPtr>(Pointer(2), "<MusicPlayer>k__BackingField") != Pointer(4)
                || _api.Read<IntPtr>(Pointer(2), "audioSource") != Pointer(5)
                || _api.Read<IntPtr>(Pointer(4), "audioSource") != Pointer(5)
                || _api.Call(Pointer(5), "get_clip") != Pointer(6))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The actual observed successor graph changed before its rooted source was retained.");
            _run.VerifyCurrent(Binding);
        }
        public void Dispose()
        {
            if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_registered, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A foreign cleanup cannot free the observed successor roots.");
            if (_disposed) return;
            _disposed = true;
            Exception? first = null;
            foreach (var root in _roots.AsEnumerable().Reverse())
                try { root.Dispose(); } catch (Exception error) { first ??= error; }
            if (first is not null)
            {
                // A failed native free has an unknown outcome. Retain the
                // actual records; a later no-op Dispose cannot prove release.
                _owner._unreleasedObservedGraphs.Add(this);
                throw first;
            }
        }
    }
}
