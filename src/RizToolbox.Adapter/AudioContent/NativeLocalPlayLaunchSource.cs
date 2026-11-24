using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal interface INativeLocalPlayLaunchSourceBackend
{
    bool CleanupUnknown { get; }
    void Capture(int judgeIndex, Action<int, Func<NativeRuntime.RootHandle>> retain);
    void VerifyStable(int judgeIndex, IReadOnlyList<NativeRuntime.RootHandle?> roots);
}

internal sealed class NativeLocalPlaySourceNotReadyException : InvalidOperationException
{
    internal NativeLocalPlaySourceNotReadyException(string message) : base(message) { }
}

/// <summary>Only borrowed source roots, never a launch/cancellation/retirement
/// permission. The actual coordinator and every native factory still validate
/// their own inputs. Pure capture failure can release these roots before any
/// controller allocation or activation; failed frees remain owned without retry.</summary>
internal sealed class NativeLocalPlayLaunchSource : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly INativeLocalPlayLaunchSourceBackend _backend;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[4];
    private readonly bool[] _freeAttempted = new bool[4];
    private bool _attempted, _captured, _busy, _releaseAttempted, _released, _cleanupUnknown;
    private int _judgeIndex;
    internal NativeLocalPlayLaunchSource(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile, IntPtr module)
        : this(main, new NativeLocalPlayLaunchSourceBackend(api, main, profile, module)) { }
    private NativeLocalPlayLaunchSource(IMainThreadDispatcher main, INativeLocalPlayLaunchSourceBackend backend)
        => (_main, _backend) = (main, backend);
    internal static NativeLocalPlayLaunchSource CreateControlled(IMainThreadDispatcher main, INativeLocalPlayLaunchSourceBackend backend)
    {
        main.VerifyAccess();
        if (backend is NativeLocalPlayLaunchSourceBackend)
            throw new InvalidOperationException("Controlled launch input observation cannot adopt a production native backend.");
        return new(main, backend);
    }
    internal bool CleanupUnknown => _cleanupUnknown || _backend.CleanupUnknown;
    internal bool Released => _released && !CleanupUnknown;
    internal int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    internal NativeRuntime.RootHandle Manager { get { Guard(); return _roots[0]!; } }
    internal NativeRuntime.RootHandle InLevelControl { get { Guard(); return _roots[1]!; } }
    internal NativeRuntime.RootHandle Judge { get { Guard(); return _roots[3]!; } }
    internal void Capture(int judgeIndex)
    {
        _main.VerifyAccess();
        if (_attempted || _busy || _releaseAttempted || CleanupUnknown)
            throw new InvalidOperationException("Launch source capture is once-only and cannot reenter.");
        if (judgeIndex is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(judgeIndex));
        _attempted = true; _busy = true; _judgeIndex = judgeIndex;
        try
        {
            _backend.Capture(judgeIndex, (slot, create) => {
                _main.VerifyAccess();
                if (!_busy || _releaseAttempted || slot is < 0 or >= 4 || _roots[slot] is not null)
                    throw new InvalidOperationException("Launch source root publication is absent, duplicate or outside its exact capture interval.");
                _roots[slot] = create() ?? throw new InvalidOperationException("Launch source returned no retained root.");
            });
            if (_roots.Any(root => root is null)) throw new InvalidOperationException("The native launch source omitted a retained input.");
            _backend.VerifyStable(judgeIndex, _roots);
            if (CleanupUnknown) throw new InvalidOperationException("Launch source native observation became Unknown.");
            _captured = true;
        }
        finally { _busy = false; }
    }
    internal void VerifyStable()
    {
        Guard(); _busy = true;
        try { _backend.VerifyStable(_judgeIndex, _roots); }
        finally { _busy = false; }
        Guard();
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (!_captured || _busy || _releaseAttempted || CleanupUnknown)
            throw new InvalidOperationException("The exact native launch source is unavailable, released or Unknown.");
    }
    public void Dispose()
    {
        _main.VerifyAccess();
        if (_busy) throw new InvalidOperationException("Borrowed launch source roots cannot be released while capture is in flight.");
        if (_released && !CleanupUnknown) return;
        if (_releaseAttempted) throw new InvalidOperationException("Unknown launch source root cleanup cannot be retried.");
        _releaseAttempted = true; var errors = new List<Exception>();
        for (var index = _roots.Length - 1; index >= 0; --index)
        {
            if (_roots[index] is not { } root) continue;
            if (_freeAttempted[index]) throw new InvalidOperationException("A launch source free was already attempted.");
            _freeAttempted[index] = true;
            try { root.Dispose(); _roots[index] = null; } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) { _cleanupUnknown = true; throw new AggregateException("Borrowed launch source cleanup remains owned.", errors); }
        _released = true;
        if (CleanupUnknown) throw new InvalidOperationException("Native launch input publication remains Unknown after independent source-root cleanup.");
    }
}
