using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Retained input transaction only. A completed old launch grants no
/// current native source identity. This owner acquires and revalidates fresh
/// source roots before a new run can borrow them; it issues no launch permission.</summary>
internal sealed class NativeLocalPlayRetrySourceRefresh
{
    private readonly IMainThreadDispatcher _main;
    private readonly int _judgeIndex;
    private readonly Func<NativeLocalPlayLaunchSource> _createSource;
    private readonly Func<IntPtr, NativeRuntime.RootHandle> _retain;
    private readonly Action<NativeLocalPlayLaunchSource> _sourceReleased;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[3];
    private readonly bool[] _freeAttempted = new bool[3];
    private NativeLocalPlayLaunchSource? _source;
    private bool _busy, _prepared, _transferred, _completed, _unknown;
    internal bool Unknown { get { _main.VerifyAccess(); return _unknown; } }
    internal int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    internal NativeLocalPlayRetrySourceRefresh(IMainThreadDispatcher main, int judgeIndex,
        Func<NativeLocalPlayLaunchSource> createSource, Func<IntPtr, NativeRuntime.RootHandle> retain,
        Action<NativeLocalPlayLaunchSource> sourceReleased)
    {
        main.VerifyAccess();
        if (judgeIndex is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(judgeIndex));
        (_main, _judgeIndex, _createSource, _retain, _sourceReleased) = (main, judgeIndex, createSource, retain, sourceReleased);
    }
    internal bool TryPrepare()
    {
        Guard(); _busy = true;
        try
        {
            if (_prepared) { VerifyRootsAndSource(); return true; }
            _source = _createSource() ?? throw new InvalidOperationException("Retry source factory returned no retained owner.");
            _source.Capture(_judgeIndex); _source.VerifyStable();
            var inputs = new[] { _source.Judge, _source.Manager, _source.InLevelControl };
            for (var slot = 0; slot < _roots.Length; ++slot)
            {
                if (_roots[slot] is not null || _freeAttempted[slot])
                    throw new InvalidOperationException("Retry input publication overlaps earlier root responsibility.");
                _roots[slot] = _retain(inputs[slot].Target)
                    ?? throw new InvalidOperationException("Retry input root allocation returned no root.");
            }
            VerifyRootsAndSource(); _prepared = true; return true;
        }
        catch (NativeLocalPlaySourceNotReadyException)
        {
            // Only a completely known read/capture with every independent
            // borrowed root released can be retried on the next host frame.
            if (_source?.CleanupUnknown == true) { _unknown = true; throw; }
            try { ReleaseForks(); ReleaseSource(); }
            catch { _unknown = true; throw; }
            Array.Clear(_freeAttempted, 0, _freeAttempted.Length); _prepared = false; return false;
        }
        catch
        {
            _unknown = true;
            // Pure borrowed roots can be released independently even after a
            // partial fork failure. Native publication responsibility remains
            // in its actual source owner if cleanup is Unknown.
            try { ReleaseForks(); } catch { }
            try { ReleaseSource(); } catch { }
            throw;
        }
        finally { _busy = false; }
    }
    internal void VerifyStable()
    {
        Guard(); if (!_prepared) throw new InvalidOperationException("Retry source inputs have not completed capture.");
        _busy = true;
        try { VerifyRootsAndSource(verifyForks: !_transferred); }
        catch { _unknown = true; throw; }
        finally { _busy = false; }
    }
    internal void TransferRootsTo(NativeRuntime.RootHandle?[] destination)
    {
        Guard(); ArgumentNullException.ThrowIfNull(destination);
        if (!_prepared || _transferred || destination.Length != 3 || destination.Any(root => root is not null))
            throw new InvalidOperationException("Fresh retry roots require one exact empty run input table.");
        _busy = true;
        try
        {
            VerifyRootsAndSource();
            for (var slot = 0; slot < _roots.Length; ++slot) { destination[slot] = _roots[slot]; _roots[slot] = null; }
            _transferred = true;
        }
        catch { _unknown = true; throw; }
        finally { _busy = false; }
    }
    internal void ReleaseBorrowedSourceAfterBegin()
    {
        Guard();
        if (!_transferred) throw new InvalidOperationException("Retry source cannot release before its new run adopted the independent inputs.");
        _busy = true;
        try { ReleaseSource(); _completed = true; }
        catch { _unknown = true; throw; }
        finally { _busy = false; }
    }
    private void VerifyRootsAndSource(bool verifyForks = true)
    {
        if (_source is null) throw new InvalidOperationException("The retained retry source is absent.");
        _source.VerifyStable();
        if (verifyForks)
        {
            var expected = new[] { _source.Judge.Target, _source.Manager.Target, _source.InLevelControl.Target };
            if (_roots.Any(root => root is null) || _roots.Distinct(ReferenceEqualityComparer.Instance).Count() != 3)
                throw new InvalidOperationException("The fresh retry run has missing or aliased input root owners.");
            for (var slot = 0; slot < _roots.Length; ++slot)
                if (_roots[slot]!.Target != expected[slot])
                    throw new InvalidOperationException("A fresh retry input differs from the actual revalidated source.");
        }
    }
    private void ReleaseForks()
    {
        var errors = new List<Exception>();
        for (var slot = _roots.Length - 1; slot >= 0; --slot)
        {
            if (_roots[slot] is not { } root) continue;
            if (_freeAttempted[slot]) { errors.Add(new InvalidOperationException("Retry input cleanup cannot be repeated.")); continue; }
            _freeAttempted[slot] = true;
            try { root.Dispose(); _roots[slot] = null; } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Partial retry inputs remain owned after Unknown cleanup.", errors);
    }
    private void ReleaseSource()
    {
        if (_source is null) return;
        var held = _source; held.Dispose();
        if (!held.Released) throw new InvalidOperationException("Retry source did not finish independent cleanup.");
        _sourceReleased(held); _source = null;
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (_busy || _completed || _unknown) throw new InvalidOperationException("The retry input transaction is in-flight, completed or Unknown.");
    }
}
