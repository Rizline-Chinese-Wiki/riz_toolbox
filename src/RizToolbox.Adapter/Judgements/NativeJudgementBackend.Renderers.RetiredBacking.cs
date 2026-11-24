using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekRendererAccess
    {
        private readonly HashSet<(long List, long Backing)> _observedRendererListBackings = new();
        private readonly Dictionary<(long List, long Backing), RetiredBackingReceipt> _retiredRendererBackings = new();
        private sealed record RetiredBackingReceipt(NativeSeekRendererAccess Owner, SeekRuntimeNoteSourceIdentity Source,
            long Pool, SeekRendererPoolListState Storage);

        private void AcceptRetiredBackings(SeekRendererGraphSnapshot observed, IReadOnlyList<SeekRendererRetiredBackingState> records)
        {
            ArgumentNullException.ThrowIfNull(records);
            foreach (var record in records)
            {
                var storage = record.Storage;
                var pool = observed.Pools.Single(value => value.Reference == record.Pool);
                var current = new[] { pool.Available, pool.Dirty }.Single(value => value.Reference == storage.Reference);
                var originalPool = _restorationBaseline!.Pools.Single(value => value.Reference == record.Pool);
                var original = new[] { originalPool.Available, originalPool.Dirty }.Single(value => value.Reference == storage.Reference);
                var key = (storage.Reference, storage.Backing);
                if (current.Backing == storage.Backing || !SameListOwner(current, storage) || !SameListOwner(original, storage)
                    || storage.Size != storage.CapacitySlots.Count || !_observedRendererListBackings.Contains(key)
                    || _retiredRendererBackings.ContainsKey(key))
                    throw new InvalidOperationException("The retired renderer backing lacks its original observed List and once-only model transition.");
                // The model supplies expected slots, not write authority. Only
                // this exact retained access can issue the receipt after reading
                // the old array independently beside the recognized live List.
                var list = Root(storage.Reference); var backing = Root(storage.Backing);
                var layout = Native(() => _owner._api.InspectRendererChildListClass(ClassOf(list)));
                if (layout.Class.ToInt64() != storage.Class || layout.ArrayClass.ToInt64() != storage.ArrayClass
                    || layout.ElementClass != _childClass || !SeekRendererState.Same(ReadRendererList(list), current)
                    || !ReadRendererArray(backing, layout.ArrayClass, _childClass).SequenceEqual(storage.CapacitySlots))
                    throw new InvalidOperationException("The actual retired renderer slots do not match the recognized native append sequence.");
                VerifySource();
                if (!SeekRendererState.Same(ReadRendererList(list), current)
                    || !ReadRendererArray(backing, layout.ArrayClass, _childClass).SequenceEqual(storage.CapacitySlots))
                    throw new InvalidOperationException("The renderer List or retired backing changed during receipt readback.");
                var frozen = storage with { CapacitySlots = Array.AsReadOnly(storage.CapacitySlots.ToArray()) };
                _retiredRendererBackings.Add(key, new(this, _identity, record.Pool, frozen));
            }
        }

        private SeekRendererPoolListState RetiredBackingStorage(SeekRendererPoolListState original)
        {
            VerifyLocal();
            if (!_retiredRendererBackings.TryGetValue((original.Reference, original.Backing), out var receipt)
                || !ReferenceEquals(receipt.Owner, this) || receipt.Source != _identity
                || receipt.Source != _restorationBaseline!.Identity || !SameListOwner(receipt.Storage, original))
                throw new InvalidOperationException("The original renderer array has no same-source verified retired-backing receipt.");
            return receipt.Storage;
        }
    }
}
