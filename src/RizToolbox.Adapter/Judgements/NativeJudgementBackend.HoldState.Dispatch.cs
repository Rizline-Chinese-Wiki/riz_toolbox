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
            private NativeRuntime.RootHandle HoldNote(int ordinal)
            {
                if ((uint)ordinal >= (uint)_ordinals.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
                _core.VerifyNote(_ordinals[ordinal]); return _core._notes[_ordinals[ordinal]];
            }
            private IntPtr ListMethod(NativeNoteJudgeListLayout layout, bool add)
            {
                var c = _core;
                var method = c.Native(() => c._owner._api.InspectDeclaredMethod(layout.Class, add ? "Add" : "Clear",
                    add ? new[] { "InLevel.NoteInformation" } : Array.Empty<string>(), "System.Void", IntPtr.Zero));
                if (method.MethodInfo != (add ? _profile.AddMethod : _profile.ClearMethod)
                    || method.DeclaringClass.Class != layout.Class || (method.Flags & 0x10u) != 0)
                    throw new UnsupportedSignatureException("The actual Hold List method differs from its current exact native profile.");
                if (add && c.Native(() => c._owner._api.InspectMethodParameterClass(method.MethodInfo, 0, "InLevel.NoteInformation")).Class != c._owner._note)
                    throw new UnsupportedSignatureException("The official Hold List.Add parameter has another exact class.");
                return method.MethodInfo;
            }
            private void FinalGuard(Action beforeDispatch)
            { VerifyLocal(); beforeDispatch(); VerifyLocal(); }
            public void Clear(int listSlot, SeekHoldListsState expectedCurrent, Action beforeDispatch)
                => ObserveSource(() => { WriteList(listSlot, null, expectedCurrent, beforeDispatch); return 0; });
            public void Add(int listSlot, long reference, SeekHoldListsState expectedCurrent, Action beforeDispatch)
                => ObserveSource(() => { WriteList(listSlot, reference, expectedCurrent, beforeDispatch); return 0; });
            private void WriteList(int slot, long? reference, SeekHoldListsState expected, Action beforeDispatch)
            {
                ArgumentNullException.ThrowIfNull(beforeDispatch); RequireExpected(expected);
                var c = _core; var layout = CurrentLayout(slot);
                NativeRuntime.RootHandle? note = null;
                if (reference is long token)
                {
                    if (!_holdOrdinals.TryGetValue(token, out var ordinal))
                        throw new InvalidDataException("An explicit Hold occurrence is not an independently owned actual Hold.");
                    note = HoldNote(ordinal);
                }
                var method = ListMethod(layout, reference is not null); var receiver = c.Target(_lists[slot]);
                using var argument = note is null ? null : A.Ref(c.Target(note));
                c.Native(() =>
                {
                    if (argument is null) c._owner._api.InvokeVoidWithPublicationGuarded(method, receiver, () => FinalGuard(beforeDispatch));
                    else c._owner._api.InvokeVoidWithPublicationGuarded(method, receiver, () => FinalGuard(beforeDispatch), argument);
                    return 0;
                });
            }
            public void RestoreCapacitySlot(int listSlot, int capacitySlot, long value, SeekHoldListsState expectedCurrent,
                SeekHoldArrayStorage expectedBacking, Action beforeDispatch) => ObserveSource(() =>
            {
                ArgumentNullException.ThrowIfNull(beforeDispatch); RequireExpected(expectedCurrent);
                var c = _core; var layout = CurrentLayout(listSlot);
                if ((uint)capacitySlot >= (uint)expectedBacking.CapacitySlots.Count
                    || expectedBacking.Class != layout.ArrayClass.ToInt64()
                    || !Same(ReadBacking(expectedBacking.Reference), expectedBacking)
                    || !c._retained.TryGetValue(expectedBacking.Reference, out var array))
                    throw SourceChanged("The retained original Hold backing changed before conditional restoration.");
                IntPtr pointer = IntPtr.Zero;
                if (value != 0)
                {
                    if (!c._retained.TryGetValue(value, out var item)
                        || c.Native(() => c._owner._api.ObjectClass(c.Target(item))) != c._owner._note)
                        throw SourceChanged("The original Hold capacity reference is no longer exactly retained.");
                    pointer = c.Target(item);
                }
                var method = c.Native(() => c._owner._api.Method(layout.ArrayClass, "SetValue", 2,
                    parameterTypes: new[] { "System.Object", "System.Int32" }));
                if (c.Native(() => c._owner._api.DescribeReturnType(method)) != "System.Void")
                    throw new UnsupportedSignatureException("The official Hold backing setter has another return type.");
                var receiver = c.Target(array);
                using var itemArgument = A.Ref(pointer); using var indexArgument = A.Value(capacitySlot);
                c.Native(() =>
                {
                    c._owner._api.InvokeVoidWithPublicationGuarded(method, receiver, () => FinalGuard(beforeDispatch), itemArgument, indexArgument);
                    return 0;
                });
                return 0;
            });
            public void RestoreListField(int listSlot, SeekHoldListRestoreField field, long value,
                SeekHoldListsState expectedCurrent, Action beforeDispatch) => ObserveSource(() =>
            {
                ArgumentNullException.ThrowIfNull(beforeDispatch); RequireExpected(expectedCurrent);
                var c = _core; var layout = CurrentLayout(listSlot); var storage = At(expectedCurrent, listSlot);
                var descriptor = field switch
                {
                    SeekHoldListRestoreField.Size => layout.SizeField,
                    SeekHoldListRestoreField.Items => layout.BackingField,
                    SeekHoldListRestoreField.Version => layout.VersionField,
                    _ => throw new ArgumentOutOfRangeException(nameof(field))
                };
                A argument;
                if (field == SeekHoldListRestoreField.Items)
                {
                    var original = ReadBacking(value);
                    if (original.Class != layout.ArrayClass.ToInt64() || storage.Size > original.CapacitySlots.Count
                        || !c._retained.TryGetValue(value, out var array))
                        throw SourceChanged("The original Hold backing cannot be reattached at the current size.");
                    argument = A.Ref(c.Target(array));
                }
                else
                {
                    var integer = checked((int)value);
                    if (field == SeekHoldListRestoreField.Size && (integer < 0 || integer > storage.CapacitySlots.Count))
                        throw SourceChanged("The restored Hold size exceeds its current backing capacity.");
                    argument = A.Value(integer);
                }
                using (argument) c.Native(() =>
                {
                    c._owner._api.WriteExactGuarded(_lists[listSlot], layout.Class, descriptor.Name, descriptor.TypeName, argument,
                        () => FinalGuard(beforeDispatch)); return 0;
                });
                return 0;
            });
            public void WriteHold(int ordinal, SeekHoldNoteField field, SeekHoldNoteTarget desired,
                SeekHoldNoteTarget expectedCurrent, Action beforeDispatch) => ObserveSource(() =>
            {
                ArgumentNullException.ThrowIfNull(beforeDispatch); ArgumentNullException.ThrowIfNull(desired); ArgumentNullException.ThrowIfNull(expectedCurrent);
                var current = ReadHold(ordinal);
                if (!Same(current, expectedCurrent) || desired.Reference != current.Reference)
                    throw SourceChanged("The actual Hold value changed before conditional mutation.");
                var slot = field switch
                { SeekHoldNoteField.IsStaticNote => 0, SeekHoldNoteField.LastFrameHoldLineLength => 1, _ => throw new ArgumentOutOfRangeException(nameof(field)) };
                var c = _core; var note = HoldNote(ordinal); var descriptor = CurrentField(_noteFields[slot]);
                using var argument = slot == 0 ? A.Value(desired.IsStaticNote) : A.Value(desired.LastFrameHoldLineLength);
                c.Native(() =>
                {
                    c._owner._api.WriteExactGuarded(note, c._owner._note, descriptor.Name, descriptor.TypeName, argument,
                        () => FinalGuard(beforeDispatch)); return 0;
                });
                return 0;
            });
            public void WriteControl(SeekHoldControlField field, SeekHoldControlValues desired,
                SeekHoldControlValues expectedCurrent, Action beforeDispatch) => ObserveSource(() =>
            {
                ArgumentNullException.ThrowIfNull(beforeDispatch); ArgumentNullException.ThrowIfNull(desired); ArgumentNullException.ThrowIfNull(expectedCurrent);
                if (!Same(ReadControl(), expectedCurrent)) throw SourceChanged("The actual Hold control changed before conditional mutation.");
                var slot = field switch
                { SeekHoldControlField.NowTime => 0, SeekHoldControlField.Speed => 1, SeekHoldControlField.HoldLineLength => 2, _ => throw new ArgumentOutOfRangeException(nameof(field)) };
                var value = slot == 0 ? desired.NowTime : slot == 1 ? desired.Speed : desired.HoldLineLength;
                var descriptor = CurrentField(_controlFields[slot]); var c = _core;
                using var argument = A.Value(value);
                c.Native(() =>
                {
                    c._owner._api.WriteExactGuarded(_hold, _profile.HoldClass, descriptor.Name, descriptor.TypeName, argument,
                        () => FinalGuard(beforeDispatch)); return 0;
                });
                return 0;
            });
        }
    }
}
