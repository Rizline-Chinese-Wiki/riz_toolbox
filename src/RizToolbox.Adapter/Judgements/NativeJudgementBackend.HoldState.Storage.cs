using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class HoldAccess
        {
            private void ValidateLayout(NativeNoteJudgeListLayout layout)
            {
                if (layout.Class != _profile.NoteListClass || layout.ElementClass != _core._owner._note)
                    throw new UnsupportedSignatureException("A Hold List has another actual closed class or element class.");
                foreach (var field in new[] { layout.BackingField, layout.SizeField, layout.VersionField })
                    if (field.DeclaringClass != layout.Class || field.IsStatic || (field.Flags & (0x10u | 0x20u | 0x40u)) != 0)
                        throw new UnsupportedSignatureException("Hold List restoration requires exact mutable declared storage.");
            }
            private NativeNoteJudgeListLayout CurrentLayout(int slot)
            {
                if ((uint)slot >= 2) throw new ArgumentOutOfRangeException(nameof(slot));
                var c = _core; var field = CurrentField(_listFields[slot]);
                if (c.ReferenceToken(_hold, _profile.HoldClass, field.Name, field.TypeName, field.Offset) != _listAddresses[slot]
                    || c.Native(() => c._owner._api.ObjectClass(c.Target(_lists[slot]))) != _profile.NoteListClass)
                    throw SourceChanged("An actual Hold List field lost its retained receiver identity.");
                var layout = c.Native(() => c._owner._api.InspectNoteJudgeListClass(field.FieldClass));
                ValidateLayout(layout);
                if (layout != _layouts[slot]) throw new UnsupportedSignatureException("A Hold List changed its complete official storage descriptors.");
                return layout;
            }
            private int ArrayCount(NativeRuntime.RootHandle array, string name, int maximum)
            {
                var c = _core; var cls = c.Native(() => c._owner._api.ObjectClass(c.Target(array)));
                var method = c.Native(() => c._owner._api.Method(cls, name, 0));
                var value = c.Native(() => c._owner._api.InvokeValueWithPublication<int>(method, c.Target(array)));
                if (value < 0 || value > maximum) throw new InvalidDataException("The complete Hold backing array exceeds its bound.");
                return value;
            }
            private long ArrayItem(NativeRuntime.RootHandle array, IntPtr arrayClass, int index)
            {
                var c = _core;
                var method = c.Native(() => c._owner._api.Method(arrayClass, "GetValue", 1, parameterTypes: new[] { "System.Int32" }));
                NativeRuntime.RootHandle? result = null; var published = false;
                c.Native(() =>
                {
                    using var argument = A.Value(index);
                    c._owner._api.InvokeReferenceWithPublication(method, c.Target(array), token =>
                    {
                        if (published) throw SourceChanged("A Hold backing slot was published twice.");
                        published = true;
                        if (token != IntPtr.Zero) result = c.TargetAdopt(token);
                    }, argument); return 0;
                });
                if (!published) throw new InvalidDataException("A Hold backing slot had no publication.");
                if (result is null) return 0;
                if (c.Native(() => c._owner._api.ObjectClass(c.Target(result))) != c._owner._note)
                    throw new UnsupportedSignatureException("A Hold backing capacity slot has another exact note class.");
                return c.Target(result).ToInt64();
            }
            private void RegisterBacking(long reference, int capacity)
            {
                if (_backingCapacities.TryGetValue(reference, out var previous))
                {
                    if (previous != capacity) throw SourceChanged("A retained Hold backing changed its immutable array length.");
                    return;
                }
                // Two originals plus every 4/doubling growth generation. The
                // total includes historical arrays, not just attached storage.
                if (_backingCapacities.Count >= 36 || capacity > 4 * NativeSeekHoldStateParticipant.MaximumCapacity - _totalCapacity)
                    throw new NotSupportedException("Complete retained Hold backing storage exceeds its historical budget.");
                _backingCapacities.Add(reference, capacity); _totalCapacity += capacity;
            }
            private SeekHoldArrayStorage ReadArray(NativeRuntime.RootHandle array, IntPtr arrayClass)
            {
                var c = _core;
                if (c.Native(() => c._owner._api.ObjectClass(c.Target(array))) != arrayClass || ArrayCount(array, "get_Rank", 1) != 1)
                    throw new UnsupportedSignatureException("A Hold backing is not the exact retained note vector.");
                var capacity = ArrayCount(array, "get_Length", NativeSeekHoldStateParticipant.MaximumCapacity);
                var address = c.Target(array).ToInt64(); RegisterBacking(address, capacity);
                var slots = new long[capacity];
                for (var i = 0; i < slots.Length; ++i) slots[i] = ArrayItem(array, arrayClass, i);
                if (ArrayCount(array, "get_Length", NativeSeekHoldStateParticipant.MaximumCapacity) != capacity)
                    throw SourceChanged("A complete Hold backing changed during observation.");
                VerifyLocal(); return new(address, arrayClass.ToInt64(), Array.AsReadOnly(slots));
            }
            private SeekHoldListStorage ReadList(int slot)
            {
                var c = _core; var layout = CurrentLayout(slot); var list = _lists[slot];
                int Read(NativeExactReadField field) => c.Native(() => c._owner._api.ReadExactField<int>(list, field));
                var size = Read(layout.SizeField); var version = Read(layout.VersionField);
                if (size < 0 || size > NativeSeekHoldStateParticipant.MaximumCapacity)
                    throw new InvalidDataException("A Hold List size exceeds its complete logical bound.");
                var backing = c.TargetReference(list, layout.Class, layout.BackingField.Name, layout.BackingField.TypeName, layout.BackingField.Offset);
                var storage = ReadArray(backing, layout.ArrayClass);
                if (size > storage.CapacitySlots.Count) throw new InvalidDataException("A Hold List size exceeds its complete backing capacity.");
                for (var i = 0; i < size; ++i)
                    if (!_holdOrdinals.ContainsKey(storage.CapacitySlots[i]))
                        throw new InvalidDataException("An active Hold occurrence is not an actual Hold in the independently owned NoteView.");
                if (Read(layout.SizeField) != size || Read(layout.VersionField) != version
                    || c.ReferenceToken(list, layout.Class, layout.BackingField.Name, layout.BackingField.TypeName, layout.BackingField.Offset) != storage.Reference
                    || CurrentLayout(slot) != layout)
                    throw SourceChanged("Actual Hold List storage changed during complete observation.");
                return new(_listAddresses[slot], layout.Class.ToInt64(), storage.Reference, storage.Class, size, version, storage.CapacitySlots);
            }
            private static bool Same(SeekHoldListStorage a, SeekHoldListStorage b)
                => a.Reference == b.Reference && a.Class == b.Class && a.Backing == b.Backing && a.ArrayClass == b.ArrayClass
                    && a.Size == b.Size && a.Version == b.Version && a.CapacitySlots.SequenceEqual(b.CapacitySlots);
            private static bool Same(SeekHoldListsState a, SeekHoldListsState b)
                => Same(a.HoldNotes, b.HoldNotes) && Same(a.RunningHoldNotes, b.RunningHoldNotes);
            private static bool Same(SeekHoldArrayStorage a, SeekHoldArrayStorage b)
                => a.Reference == b.Reference && a.Class == b.Class && a.CapacitySlots.SequenceEqual(b.CapacitySlots);
            private static SeekHoldListStorage At(SeekHoldListsState value, int slot) => slot switch
            { 0 => value.HoldNotes, 1 => value.RunningHoldNotes, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
            public SeekHoldListsState ReadLists() => ObserveSource(() =>
            {
                VerifyCurrent(); var first = new SeekHoldListsState(ReadList(0), ReadList(1));
                VerifyCurrent(); var second = new SeekHoldListsState(ReadList(0), ReadList(1)); VerifyCurrent();
                if (!Same(first, second)) throw SourceChanged("Actual Hold Lists changed between complete observations.");
                return second;
            });
            public SeekHoldArrayStorage ReadBacking(long reference) => ObserveSource(() =>
            {
                VerifyCurrent(); var c = _core;
                if (!_backingCapacities.ContainsKey(reference) || !c._retained.TryGetValue(reference, out var array))
                    throw new InvalidOperationException("Only previously observed retained Hold backings may be read.");
                var first = ReadArray(array, _layouts[0].ArrayClass); VerifyCurrent();
                var second = ReadArray(array, _layouts[0].ArrayClass); VerifyCurrent();
                if (!Same(first, second)) throw SourceChanged("A retained Hold backing changed between complete observations.");
                return second;
            });
            private void RequireExpected(SeekHoldListsState expected)
            {
                if (!Same(ReadLists(), expected)) throw SourceChanged("Complete actual Hold Lists changed before conditional mutation.");
                if (expected.HoldNotes.Reference != expected.RunningHoldNotes.Reference
                    && expected.HoldNotes.Backing == expected.RunningHoldNotes.Backing && expected.HoldNotes.CapacitySlots.Count != 0)
                    throw new NotSupportedException("Different Hold Lists sharing nonempty backing are not supported by this writer.");
            }
        }
    }
}
