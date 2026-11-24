using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess : INativeSeekFingerIdsAccess
    {
        private const string FingerIdsType = "System.Collections.Generic.List<System.Int32>";
        private NativeExactReadField? _fingerField;
        private NativeInt32ListLayout? _fingerLayout;
        private bool _fingerIdsEnabled;

        internal NativeSeekNoteFlagsAccess(NativeJudgementBackend owner, RunValue run,
            ISeekChartNativeProfile profile, bool fingerIds) : this(owner, run, profile)
        {
            try
            {
                if (!fingerIds) throw new ArgumentException("This constructor requires explicit fingerIds mode.", nameof(fingerIds));
                _rootLimit = checked(6 + 4 * NativeSeekRuntimeNoteView.MaximumNotes + NativeSeekFingerIdsParticipant.MaximumTotalCapacity);
                _fingerField = Native(() => owner._api.InspectExactDeclaredReadField(owner._note, "fingerIds", FingerIdsType, false, 0x58));
                if (_fingerField.DeclaringClass != owner._note || _fingerField.Size != IntPtr.Size
                    || _fingerField.IsStatic || (_fingerField.Flags & (0x10u | 0x20u | 0x40u)) != 0)
                    throw new UnsupportedSignatureException("fingerIds is not exact mutable declared List<Int32> reference storage.");
                _fingerLayout = Native(() => owner._api.InspectInt32ListClass(_fingerField.FieldClass));
                _fingerIdsEnabled = true; VerifySource();
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup) { error.Data["SeekFingerIdsCleanupOwner"] = this; error.Data["SeekFingerIdsCleanupFailure"] = cleanup; }
                throw;
            }
        }
        public IReadOnlyList<SeekRuntimeNote> RuntimeNotes
        {
            get { VerifyLocal(); return (_view ?? throw new InvalidOperationException("The independently owned actual NoteView is absent.")).Notes; }
        }
        public void VerifySource() => FingerReady(fullGraph: true);
        private NativeExactReadField FingerField()
        {
            VerifyLocal();
            if (!_fingerIdsEnabled || _fingerField is null || _fingerLayout is null)
                throw new InvalidOperationException("The independently retained fingerIds source is not prepared.");
            var current = Native(() => _owner._api.InspectExactDeclaredReadField(_owner._note, "fingerIds", FingerIdsType, false, 0x58));
            if (current != _fingerField) throw new UnsupportedSignatureException("The prepared fingerIds official field descriptor changed.");
            return current;
        }
        private NativeInt32ListLayout FingerLayout()
        {
            var field = FingerField();
            var current = Native(() => _owner._api.InspectInt32ListClass(field.FieldClass));
            if (current != _fingerLayout) throw new UnsupportedSignatureException("The exact closed Int32 List changed its official storage descriptors.");
            foreach (var descriptor in new[] { current.BackingField, current.SizeField, current.VersionField })
                if (descriptor.DeclaringClass != current.Class || descriptor.IsStatic || (descriptor.Flags & (0x10u | 0x20u | 0x40u)) != 0)
                    throw new UnsupportedSignatureException("fingerIds restoration requires exact mutable declared List storage.");
            return current;
        }
        private void FingerReady(bool fullGraph = false)
        {
            FingerField(); VerifyCurrent(fullGraph); VerifyLocal();
        }
        private long FingerFieldToken(int ordinal)
        {
            var field = FingerField(); var receiver = Target(_notes[ordinal]);
            // Compare a foreign reference only as an address. No metadata or
            // method is dispatched on a field token before its owner accepts it.
            return Native(() => _owner._api.ReadWithPublication<IntPtr>(receiver, field.Name)).ToInt64();
        }
        private SeekFingerIdsNoteField[] ReadFingerFields()
        {
            var fields = new SeekFingerIdsNoteField[_notes.Length]; var field = FingerField();
            for (var ordinal = 0; ordinal < fields.Length; ++ordinal)
            {
                VerifyNote(ordinal); var published = false; long reference = 0;
                Native(() =>
                {
                    _owner._api.PublishExactReferenceField(_notes[ordinal], field, value =>
                    {
                        VerifyLocal();
                        if (published) throw new InvalidOperationException("An actual fingerIds field was published more than once.");
                        published = true;
                        if (value != IntPtr.Zero)
                        {
                            var retained = Adopt(value);
                            if (Native(() => _owner._api.ObjectClass(Target(retained))) != field.FieldClass)
                                throw new UnsupportedSignatureException("An actual fingerIds field has another exact closed List class.");
                            reference = Target(retained).ToInt64();
                        }
                        VerifyLocal();
                    });
                    return 0;
                });
                if (!published) throw new InvalidDataException("An actual nullable fingerIds field had no official publication.");
                fields[ordinal] = new(_noteReferences[ordinal], reference);
            }
            return fields;
        }
        public SeekFingerIdsFieldSnapshot ReadFields()
        {
            FingerReady(); var first = ReadFingerFields(); FingerReady();
            var second = ReadFingerFields(); FingerReady();
            if (!first.SequenceEqual(second)) throw new InvalidOperationException("Actual fingerIds fields changed between complete observations.");
            return new(_noteViewIdentity, Array.AsReadOnly(second));
        }
        SeekFingerIdsNoteField INativeSeekFingerIdsAccess.ReadNote(int ordinal) => ReadFingerNote(ordinal);
        private SeekFingerIdsNoteField ReadFingerNote(int ordinal)
        {
            if ((uint)ordinal >= (uint)_notes.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
            FingerReady(); VerifyNote(ordinal); var first = FingerFieldToken(ordinal);
            FingerReady(); VerifyNote(ordinal); var second = FingerFieldToken(ordinal); FingerReady();
            if (first != second) throw new InvalidOperationException("The selected actual fingerIds field changed between observations.");
            return new(_noteReferences[ordinal], second);
        }
        private SeekFingerIdsArrayStorage ReadFingerBacking(NativeRuntime.RootHandle backing, IntPtr arrayClass)
        {
            var observed = Native(() => _owner._api.ReadRootedInt32ArrayWithPublication(backing, arrayClass, NativeSeekFingerIdsParticipant.MaximumCapacity));
            if (observed.Array.ToInt64() != Target(backing).ToInt64() || observed.Class != arrayClass
                || observed.ElementClass != _fingerLayout!.ElementClass || observed.Length != observed.Items.Count)
                throw new UnsupportedSignatureException("The complete fingerIds array observation changed its retained identity or element class.");
            return new(observed.Array.ToInt64(), observed.Class.ToInt64(), Array.AsReadOnly(observed.Items.ToArray()));
        }
        public SeekFingerIdsArrayStorage ReadBacking(long reference)
        {
            FingerReady(); var layout = FingerLayout();
            if (!_retained.TryGetValue(reference, out var backing))
                throw new InvalidOperationException("Only independently retained actual Int32 arrays can be observed.");
            var first = ReadFingerBacking(backing, layout.ArrayClass); FingerReady();
            var second = ReadFingerBacking(backing, layout.ArrayClass); FingerReady();
            if (!SeekFingerIdsState.Same(first, second)) throw new InvalidOperationException("Complete retained fingerIds backing storage changed between observations.");
            return second;
        }
        private NativeRuntime.RootHandle FingerListRoot(long reference)
        {
            var layout = FingerLayout();
            if (reference == 0 || !_retained.TryGetValue(reference, out var list)
                || Target(list).ToInt64() != reference || Native(() => _owner._api.ObjectClass(Target(list))) != layout.Class)
                throw new InvalidOperationException("The selected fingerIds List is not independently retained with its exact closed class.");
            return list;
        }
        private SeekFingerIdsListStorage ReadFingerList(long reference)
        {
            var layout = FingerLayout(); var list = FingerListRoot(reference);
            var size = Native(() => _owner._api.ReadExactField<int>(list, layout.SizeField));
            var version = Native(() => _owner._api.ReadExactField<int>(list, layout.VersionField));
            var backing = ReferenceField(list, layout.Class, layout.BackingField.Name, layout.BackingField.TypeName, layout.BackingField.Offset);
            var observed = ReadFingerBacking(backing, layout.ArrayClass);
            if (size < 0 || size > observed.CapacitySlots.Count || size > NativeSeekFingerIdsParticipant.MaximumCapacity)
                throw new InvalidDataException("The actual fingerIds List size exceeds its complete backing capacity.");
            if (Native(() => _owner._api.ReadExactField<int>(list, layout.SizeField)) != size
                || Native(() => _owner._api.ReadExactField<int>(list, layout.VersionField)) != version
                || ReferenceToken(list, layout.Class, layout.BackingField.Name, layout.BackingField.TypeName, layout.BackingField.Offset) != observed.Reference
                || FingerLayout() != layout)
                throw new InvalidOperationException("Official fingerIds List size/version/backing changed during complete observation.");
            return new(reference, layout.Class.ToInt64(), observed.Reference, observed.Class, size, version, observed.CapacitySlots);
        }
        public SeekFingerIdsListStorage ReadList(long reference)
        {
            FingerReady(); var first = ReadFingerList(reference); FingerReady();
            var second = ReadFingerList(reference); FingerReady();
            if (!SeekFingerIdsState.Same(first, second)) throw new InvalidOperationException("Complete fingerIds List storage changed between observations.");
            return second;
        }
        public void AllocateList(int ordinal, Action<long> published)
        {
            ArgumentNullException.ThrowIfNull(published);
            if ((uint)ordinal >= (uint)_notes.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
            FingerReady(); var layout = FingerLayout();
            if (ReadFingerNote(ordinal).ListReference != 0) throw new InvalidOperationException("Scratch construction requires the actual selected fingerIds field to remain null.");
            if (_roots.Count >= _rootLimit) throw new InvalidOperationException("The independent fingerIds root budget is exhausted before allocation.");
            // No allocation is needed in the native publisher's first ownership
            // operation. Reentrant disposal defers physical release until the
            // participant's preparation operation has returned from the ctor.
            _roots.EnsureCapacity(checked(_roots.Count + 1));
            try
            {
                Native(() => _owner._api.NewExactWithPublication(layout.Class, Array.Empty<string>(), root =>
                {
                    _roots.Add(root); // Responsibility precedes Target/Local/ctor.
                    VerifyLocal(); var target = Native(() => root.Target); var address = target.ToInt64();
                    if (target == IntPtr.Zero || _retained.ContainsKey(address))
                        throw new InvalidOperationException("Official scratch allocation reused a previously retained object identity.");
                    _retained.Add(address, root); _addresses.Add(root, address);
                    if (Native(() => _owner._api.ObjectClass(Target(root))) != layout.Class)
                        throw new UnsupportedSignatureException("Official scratch allocation returned another exact closed List class.");
                    published(address); VerifyLocal();
                }));
            }
            catch (Exception error)
            {
                error.Data["SeekFingerIdsAllocationOwner"] = this;
                throw;
            }
        }
        private void RequireFingerSelected(int ordinal, long expectedField, SeekFingerIdsListStorage? expectedList = null)
        {
            if ((uint)ordinal >= (uint)_notes.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
            FingerReady();
            if (ReadFingerNote(ordinal).ListReference != expectedField)
                throw new InvalidOperationException("The actual selected fingerIds field changed before conditional mutation.");
            if (expectedList is not null && !SeekFingerIdsState.Same(ReadList(expectedList.Reference), expectedList))
                throw new InvalidOperationException("Complete selected fingerIds List storage changed before conditional mutation.");
        }
        private IntPtr FingerVoidMethod(NativeInt32ListLayout layout, string name, params string[] parameters)
        {
            var method = Native(() => _owner._api.InspectDeclaredMethod(layout.Class, name, parameters, "System.Void", IntPtr.Zero));
            if (method.DeclaringClass.Class != layout.Class || method.ReturnType != "System.Void" || (method.Flags & 0x10u) != 0)
                throw new UnsupportedSignatureException("fingerIds mutation requires its exact official closed List instance void method.");
            return method.MethodInfo;
        }
        public void Clear(int ordinal, long expectedField, SeekFingerIdsListStorage expectedList, Action beforeDispatch)
            => WriteFingerList(ordinal, expectedField, expectedList, null, beforeDispatch);
        public void Add(int ordinal, long expectedField, SeekFingerIdsListStorage expectedList, int value, Action beforeDispatch)
            => WriteFingerList(ordinal, expectedField, expectedList, value, beforeDispatch);
        private void WriteFingerList(int ordinal, long expectedField, SeekFingerIdsListStorage expectedList, int? value, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            RequireFingerSelected(ordinal, expectedField, expectedList); var layout = FingerLayout();
            var method = value is null ? FingerVoidMethod(layout, "Clear") : FingerVoidMethod(layout, "Add", "System.Int32");
            if (value is not null && Native(() => _owner._api.InspectMethodParameterClass(method, 0, "System.Int32")).Class != layout.ElementClass)
                throw new UnsupportedSignatureException("Official List<Int32>.Add has another exact element parameter class.");
            var receiver = Target(FingerListRoot(expectedList.Reference));
            using var argument = value is int member ? A.Value(member) : null;
            Native(() =>
            {
                void FinalGuard() { VerifyLocal(); beforeDispatch(); VerifyLocal(); }
                if (argument is null) _owner._api.InvokeVoidWithPublicationGuarded(method, receiver, FinalGuard);
                else _owner._api.InvokeVoidWithPublicationGuarded(method, receiver, FinalGuard, argument);
                return 0;
            });
        }
        public void WriteField(int ordinal, long value, long expectedField, SeekFingerIdsListStorage? expectedCurrentList,
            SeekFingerIdsListStorage? expectedTargetList, SeekFingerIdsArrayStorage? expectedTargetOriginalBacking, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            RequireFingerSelected(ordinal, expectedField, expectedCurrentList);
            var descriptor = FingerField(); IntPtr target = IntPtr.Zero;
            if (value != 0)
            {
                if (expectedTargetList is null || expectedTargetList.Reference != value
                    || !SeekFingerIdsState.Same(ReadList(value), expectedTargetList))
                    throw new InvalidOperationException("The complete target fingerIds List changed before field publication.");
                if (expectedTargetOriginalBacking is not null
                    && !SeekFingerIdsState.Same(ReadBacking(expectedTargetOriginalBacking.Reference), expectedTargetOriginalBacking))
                    throw new InvalidOperationException("The target original backing changed before fingerIds field reconnection.");
                target = Target(FingerListRoot(value));
            }
            else if (expectedTargetList is not null || expectedTargetOriginalBacking is not null)
                throw new InvalidOperationException("A null fingerIds publication must not substitute a nonnull target List.");
            using var argument = A.Ref(target);
            Native(() =>
            {
                _owner._api.WriteExactGuarded(_notes[ordinal], _owner._note, descriptor.Name, descriptor.TypeName, argument,
                    () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); });
                return 0;
            });
        }
        public void RestoreCapacitySlot(int ordinal, long expectedField, SeekFingerIdsListStorage expectedList,
            SeekFingerIdsArrayStorage expectedBacking, int capacitySlot, int value, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            if ((uint)capacitySlot >= (uint)expectedBacking.CapacitySlots.Count) throw new ArgumentOutOfRangeException(nameof(capacitySlot));
            RequireFingerSelected(ordinal, expectedField, expectedList); var layout = FingerLayout();
            if (expectedBacking.Class != layout.ArrayClass.ToInt64()
                || !SeekFingerIdsState.Same(ReadBacking(expectedBacking.Reference), expectedBacking)
                || !_retained.TryGetValue(expectedBacking.Reference, out var backing))
                throw new InvalidOperationException("The original fingerIds backing changed before conditional slot restoration.");
            Native(() =>
            {
                _owner._api.SetInt32ArrayElementGuarded(backing, layout.ArrayClass, capacitySlot, value,
                    () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); });
                return 0;
            });
        }
        public void RestoreListField(int ordinal, long expectedField, SeekFingerIdsListStorage expectedList,
            SeekFingerIdsRestoreField field, long value, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            RequireFingerSelected(ordinal, expectedField, expectedList); var layout = FingerLayout();
            var descriptor = field switch
            {
                SeekFingerIdsRestoreField.Items => layout.BackingField,
                SeekFingerIdsRestoreField.Size => layout.SizeField,
                SeekFingerIdsRestoreField.Version => layout.VersionField,
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            NativeRuntime.Arg argument;
            if (field == SeekFingerIdsRestoreField.Items)
            {
                var original = ReadBacking(value);
                if (original.Class != layout.ArrayClass.ToInt64() || expectedList.Size > original.CapacitySlots.Count
                    || !_retained.TryGetValue(value, out var retained))
                    throw new InvalidOperationException("The original fingerIds backing cannot safely be reattached with the current size.");
                argument = A.Ref(Target(retained));
            }
            else
            {
                var integer = checked((int)value);
                if (field == SeekFingerIdsRestoreField.Size && (integer < 0 || integer > expectedList.CapacitySlots.Count))
                    throw new InvalidOperationException("The restored fingerIds size exceeds the complete current backing.");
                argument = A.Value(integer);
            }
            using (argument) Native(() =>
            {
                _owner._api.WriteExactGuarded(FingerListRoot(expectedList.Reference), layout.Class, descriptor.Name, descriptor.TypeName, argument,
                    () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); });
                return 0;
            });
        }
    }
}
