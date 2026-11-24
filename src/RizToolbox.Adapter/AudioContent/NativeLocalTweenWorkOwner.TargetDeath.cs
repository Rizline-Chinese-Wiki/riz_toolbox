using RizToolbox.Runtime;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenTargetBinding(NativeRuntime.RootHandle TweenRoot, NativeRuntime.RootHandle? Component,
    NativeRuntime.RootHandle? GameObject, NativeRuntime.RootHandle? Transform, long ParentTransform);
internal interface INativeTweenTargetDeathBackend
{
    NativeTweenTargetBinding BorrowTweenTargetBinding(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle tweenRoot);
    NativeTweenTargetDeathObservation ReadTweenTargetDeath(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle root);
    void VerifyTweenTargetBinding(NativeLocalTweenWorkOwner owner, NativeTweenTargetBinding binding);
}
internal sealed record NativeTweenTargetDeathSnapshot(long Tween, long Object, string Role, int LiveFrame,
    int ReturnFrame, int FirstDeathFrame, int ConfirmedFrame, ulong SourceEpoch, bool Production, bool Unknown);

internal sealed partial class NativeLocalTweenWorkOwner
{
    private readonly object _targetDeathIssuer = new();
    private readonly Dictionary<long, TargetDeathSet> _targetDeathSets = new();
    private bool _capturingTweenTargets, _pumpingTweenTargets;

    private sealed class TargetDeathSet
    {
        internal readonly NativeTweenTargetBinding Binding;
        internal readonly long Tween;
        internal readonly List<TargetDeathRecord> Records = new();
        internal bool Coverage => Binding.Component is null || Binding.GameObject is not null && Binding.Transform is not null;
        internal TargetDeathSet(NativeTweenTargetBinding binding)
        {
            Binding = binding; Tween = binding.TweenRoot.Target.ToInt64();
            if (binding.Component is not null) Records.Add(new(binding.Component, "Component"));
            if (binding.GameObject is not null) Records.Add(new(binding.GameObject, "GameObject"));
            if (binding.Transform is not null) Records.Add(new(binding.Transform, "Transform"));
        }
    }
    private sealed class TargetDeathRecord
    {
        internal readonly NativeRuntime.RootHandle Root;
        internal readonly string Role;
        internal readonly long Object;
        internal NativeTweenTargetDeathObservation? Previous;
        internal int Live = -1, Return = -1, FirstDeath = -1, Confirmed = -1;
        internal ulong Epoch;
        internal TargetDeathRecord(NativeRuntime.RootHandle root, string role) => (Root, Role, Object) = (root, role, root.Target.ToInt64());
        internal void Observe(NativeTweenTargetDeathObservation sample, bool baseline)
        {
            if (sample.Object != Object || Root.Target.ToInt64() != Object || sample.Class == IntPtr.Zero || sample.CachedPointerField == IntPtr.Zero
                || sample.Frame < 0 || sample.Alive != (sample.CachedNative != 0)
                || Previous is { } previous && (sample.Class != previous.Class || sample.CachedPointerField != previous.CachedPointerField
                    || sample.Frame < previous.Frame || !previous.Alive && sample.Alive
                    || sample.Alive && previous.Alive && sample.CachedNative != previous.CachedNative))
                throw new InvalidOperationException("A concrete target changed its retained root, native identity, class/field, frame or resurrected.");
            Previous = sample;
            if (baseline && sample.Alive) Live = sample.Frame;
        }
    }

    internal sealed class TargetDeathReceipt
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly TargetDeathSet _set;
        private readonly TweenDeathReturnSource _return;
        private readonly ulong _epoch;
        internal TargetDeathReceipt(object issuer, NativeLocalTweenWorkOwner owner, object source, TweenDeathReturnSource request, ulong epoch)
        {
            if (!ReferenceEquals(issuer, owner._targetDeathIssuer) || source is not TargetDeathSet set || !owner.TargetsConfirmed(set, epoch))
                throw new InvalidOperationException("Only concrete confirmed target intervals issue their private receipt.");
            (_issuer, _owner, _set, _return, _epoch) = (issuer, owner, set, request, epoch);
        }
        internal void VerifyCurrent(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle tweenRoot)
        {
            owner.VerifyTweenSourceOwner();
            if (!ReferenceEquals(_issuer, owner._targetDeathIssuer) || !ReferenceEquals(owner, _owner)
                || !ReferenceEquals(tweenRoot, _set.Binding.TweenRoot) || !ReferenceEquals(owner._tweenDeathReturn, _return)
                || !_return.IsFor(owner, owner._launch) || !owner.TargetsConfirmed(_set, _epoch))
                throw new InvalidOperationException("Target death receipt crossed its concrete root, Return or source epoch.");
            try
            {
                if (owner._top is not null || owner._reading) throw new InvalidOperationException("A concrete target receipt overlaps a physical operation.");
                owner.PollSourceJournal(); var sequence = owner._sourceReadSequence; var operations = owner._operationRevision;
                var activity = owner._backend.ReadActivity(); owner.Known();
                if (!activity.Known || activity.InFlight != 0) throw new InvalidOperationException("A concrete target receipt has active native borrowers.");
                owner.ValidateTargetSet(_set, requireDead: true);
                if (owner._tweenDeaths[_set.Tween].Source.ReadSealedEpoch() != _epoch)
                    throw new InvalidOperationException("Target death receipt lost its same sealed Tween source interval.");
                owner.PollSourceJournal(); activity = owner._backend.ReadActivity(); owner.Known();
                if (!activity.Known || activity.InFlight != 0 || sequence != owner._sourceReadSequence || operations != owner._operationRevision || owner._top is not null)
                    throw new InvalidOperationException("Actual native/source work changed across concrete target receipt consumption.");
            }
            catch (Exception error) { owner.Fault("Tween target receipt: " + error.Message); throw; }
        }
    }

    internal void RequireTweenTargetBaseline(NativeRuntime.RootHandle tweenRoot)
    {
        VerifyTweenSourceOwner();
        if (!_capturingTweenTargets || !(_initializing && !_ready && _top is null
                || _backend is not NativeLocalTweenBackend && _capturingTweenDeaths && _operationRevision == 0 && _top is null
                || IsCurrentDynamicSourceBaseline(tweenRoot, tweenRoot.Target.ToInt64())))
            throw new InvalidOperationException("Concrete target live baselines belong only to initial adoption or the exact first-call Before.");
    }
    private void CaptureTweenTargetSources(long tween, NativeRuntime.RootHandle tweenRoot)
    {
        if (_backend is not INativeTweenTargetDeathBackend backend || _targetDeathSets.ContainsKey(tween)) return;
        _capturingTweenTargets = true;
        try
        {
            RequireTweenTargetBaseline(tweenRoot);
            var binding = backend.BorrowTweenTargetBinding(this, tweenRoot);
            if (!ReferenceEquals(binding.TweenRoot, tweenRoot)) throw new InvalidOperationException("Target borrowing changed its exact Tween root.");
            var set = new TargetDeathSet(binding); _targetDeathSets.Add(tween, set); // Preserve partial responsibility before sampling.
            foreach (var row in set.Records) row.Observe(backend.ReadTweenTargetDeath(this, row.Root), baseline: true);
            backend.VerifyTweenTargetBinding(this, binding); Known();
        }
        finally { _capturingTweenTargets = false; }
    }
    private void ValidateTargetSet(TargetDeathSet set, bool requireDead)
    {
        var backend = (INativeTweenTargetDeathBackend)_backend;
        backend.VerifyTweenTargetBinding(this, set.Binding); Known();
        foreach (var row in set.Records)
        {
            var sample = backend.ReadTweenTargetDeath(this, row.Root); row.Observe(sample, baseline: false); Known();
            if (requireDead && (sample.Alive || sample.CachedNative != 0))
                throw new InvalidOperationException("A confirmed concrete target is no longer dead at receipt consumption.");
        }
    }
    private bool TargetsConfirmed(TargetDeathSet set, ulong epoch) => !Unknown && epoch != 0 && set.Coverage
        && _targetDeathSets.TryGetValue(set.Tween, out var current) && ReferenceEquals(current, set)
        && set.Records.All(row => row.Live >= 0 && row.Return >= row.Live && row.FirstDeath > row.Return
            && row.Confirmed > row.FirstDeath && row.Epoch == epoch && row.Previous is { Alive: false, CachedNative: 0 });

    private void PumpTweenTargetDeaths()
    {
        if (_targetDeathSets.Count == 0 || _tweenDeathReturn is not { } request || _top is not null || _reading) return;
        if (_pumpingTweenTargets) throw new InvalidOperationException("Concrete target death pump reentered.");
        _pumpingTweenTargets = true;
        try
        {
            request.Require(); var activity = _backend.ReadActivity(); Known(); if (!activity.Known || activity.InFlight != 0) return;
            PollSourceJournal(); var sequence = _sourceReadSequence; var operations = _operationRevision;
            foreach (var set in _targetDeathSets.Values)
            {
                var epoch = _tweenDeaths[set.Tween].Source.ReadSealedEpoch();
                ValidateTargetSet(set, requireDead: false);
                foreach (var row in set.Records)
                {
                    var sample = row.Previous!.Value;
                    if (row.Return < 0) { row.Return = sample.Frame; continue; }
                    if (epoch == 0) continue;
                    if (row.Epoch != 0 && row.Epoch != epoch) throw new InvalidOperationException("Concrete target interval changed its actual source epoch.");
                    row.Epoch = epoch;
                    if (row.Live < 0 || sample.Alive || sample.Frame <= row.Return || sample.Frame <= row.Live) continue;
                    if (row.FirstDeath < 0) row.FirstDeath = sample.Frame;
                    else if (sample.Frame > row.FirstDeath && row.Confirmed < 0) row.Confirmed = sample.Frame;
                }
                if (_tweenDeaths[set.Tween].Source.ReadSealedEpoch() != epoch)
                    throw new InvalidOperationException("Concrete target source changed across its sample.");
            }
            PollSourceJournal(); activity = _backend.ReadActivity(); Known(); request.Require();
            if (!activity.Known || activity.InFlight != 0 || _top is not null || sequence != _sourceReadSequence || operations != _operationRevision)
                throw new InvalidOperationException("Native/source activity changed across the concrete target observation interval.");
        }
        catch (Exception error) { Fault("Tween target death: " + error.Message); throw; }
        finally { _pumpingTweenTargets = false; }
    }
    internal TargetDeathReceipt? ReadTweenTargetDeathReceipt(NativeRuntime.RootHandle tweenRoot)
    {
        VerifyTweenSourceOwner();
        if (!_targetDeathSets.TryGetValue(tweenRoot.Target.ToInt64(), out var set) || !ReferenceEquals(set.Binding.TweenRoot, tweenRoot))
            throw new InvalidOperationException("Concrete target receipt requires the exact retained source root instance.");
        if (set.Binding.Component is null) return null; // Explicit nullable target coverage is a separate fact.
        var epoch = _tweenDeaths[set.Tween].Source.ReadSealedEpoch();
        if (!TargetsConfirmed(set, epoch) || _tweenDeathReturn is null) return null;
        var receipt = new TargetDeathReceipt(_targetDeathIssuer, this, set, _tweenDeathReturn, epoch);
        receipt.VerifyCurrent(this, tweenRoot); return receipt;
    }
    internal IReadOnlyList<NativeTweenTargetDeathSnapshot> ReadTweenTargetDeaths()
    {
        _main.VerifyAccess();
        return _targetDeathSets.Values.SelectMany(set => set.Records.Select(row => new NativeTweenTargetDeathSnapshot(
            set.Tween, row.Object, row.Role, row.Live, row.Return, row.FirstDeath, row.Confirmed, row.Epoch, _backend is NativeLocalTweenBackend, Unknown))).ToArray();
    }
}
