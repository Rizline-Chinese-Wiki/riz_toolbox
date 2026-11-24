using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            private sealed partial class CacheAccess
            {
                private int[] ReadArray(int kind)
                {
                    var field = _fields[kind]; var root = _arrays[kind];
                    var observed = N(() => _source._core._owner._api.ReadRootedGeometrySingleArray(root, field.FieldClass, 65536, VerifyLocal, canvasCache: true));
                    if (observed.Array.ToInt64() != _arrayAddresses[kind] || observed.Class != field.FieldClass
                        || observed.Length != _length || observed.Items.Count != _length)
                        throw new InvalidDataException("The complete retained Canvas cache vector changed its actual storage identity or length.");
                    return observed.Items.Select(BitConverter.SingleToInt32Bits).ToArray();
                }
                private int ScalarBits(int kind)
                {
                    var field = CurrentField(kind);
                    return BitConverter.SingleToInt32Bits(N(() => _source._core._owner._api.ReadExactField<float>(_source._canvas, field)));
                }
                private int[] Snapshot()
                {
                    var bits = new int[checked(3 * _length + 2)]; var observed = 0;
                    try
                    {
                        for (var i = 0; i < 3; ++i) { ReadArray(i).CopyTo(bits, observed); observed += _length; }
                        bits[observed] = ScalarBits(3); ++observed;
                        bits[observed] = ScalarBits(4); ++observed; return bits;
                    }
                    catch (Exception error)
                    {
                        NativeCanvasCacheConflict.PrependObservations(error, Observations(bits, observed)); throw;
                    }
                }
                private NativeCanvasCacheObservation[] Observations(IReadOnlyList<int> bits, int count)
                {
                    var observations = new NativeCanvasCacheObservation[count];
                    for (var i = 0; i < count; ++i)
                        observations[i] = new(_slots[i].Owner, _slots[i].Index, _slots[i].Kind.ToString(), bits[i]);
                    return observations;
                }
                public IReadOnlyList<int> ReadSnapshot()
                {
                    VerifySource(); var first = Snapshot(); int[] second;
                    try { second = Snapshot(); }
                    catch (Exception error)
                    {
                        NativeCanvasCacheConflict.PrependObservations(error, Observations(first, first.Length)); throw;
                    }
                    VerifySource();
                    for (var i = 0; i < first.Length; ++i)
                        if (first[i] != second[i])
                            throw NativeCanvasCacheConflict.Error(_slots[i].Owner, _slots[i].Index, _slots[i].Kind.ToString(), first[i], second[i], true,
                                Observations(first, first.Length).Concat(Observations(second, second.Length)).ToArray());
                    return Array.AsReadOnly(second);
                }
                private int ReadOne(int ordinal, List<NativeCanvasCacheObservation>? observations = null)
                {
                    VerifyLocal();
                    if ((uint)ordinal >= (uint)_slots.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
                    var slot = _slots[ordinal]; var kind = (int)slot.Kind;
                    CurrentField(kind);
                    if (kind < 3)
                    {
                        var bits = ReadArray(kind);
                        if (observations is not null)
                            for (var i = 0; i < bits.Length; ++i)
                                observations.Add(new(slot.Owner, i, slot.Kind.ToString(), bits[i]));
                        return bits[slot.Index];
                    }
                    var scalar = ScalarBits(kind);
                    observations?.Add(new(slot.Owner, slot.Index, slot.Kind.ToString(), scalar)); return scalar;
                }
                public int ReadSlot(int ordinal)
                {
                    VerifySource(); var observations = new List<NativeCanvasCacheObservation>(); int first, second;
                    try { first = ReadOne(ordinal, observations); second = ReadOne(ordinal, observations); }
                    catch (Exception error)
                    {
                        NativeCanvasCacheConflict.PrependObservations(error, observations); throw;
                    }
                    VerifySource();
                    if (first != second)
                        throw NativeCanvasCacheConflict.Error(_slots[ordinal].Owner, _slots[ordinal].Index, _slots[ordinal].Kind.ToString(), first, second, true,
                            observations);
                    return second;
                }
                public void WriteSlot(int ordinal, int bits, int expectedBits, Action beforeDispatch)
                {
                    ArgumentNullException.ThrowIfNull(beforeDispatch); VerifySource();
                    var observed = ReadOne(ordinal);
                    if (observed != expectedBits)
                        throw NativeCanvasCacheConflict.Error(_slots[ordinal].Owner, _slots[ordinal].Index, _slots[ordinal].Kind.ToString(), expectedBits, observed);
                    var slot = _slots[ordinal]; var kind = (int)slot.Kind; var field = CurrentField(kind);
                    // The complete source capture already holds a physical
                    // NoteView lease for this owner's whole lifetime. Do not
                    // enter its in-flight gate here: the final VerifySource
                    // must independently revalidate that same retained view.
                    N(() =>
                    {
                        if (kind < 3)
                            _source._core._owner._api.WriteRootedCanvasCacheSingle(_arrays[kind], field.FieldClass, _length,
                                slot.Index, expectedBits, bits, VerifyLocal, () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); }, VerifySource);
                        else
                            _source._core._owner._api.WriteCanvasCacheScalar(_source._canvas, field, expectedBits, bits, VerifyLocal,
                                () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); }, VerifySource);
                        return 0;
                    });
                    VerifyLocal();
                }
            }
        }
    }
}
