using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekJudgeStateReader : INativeSeekJudgeQueuesAccess
    {
        private NativeRuntime.RootHandle? _queueController, _queueChart, _queueProgress, _queuePause, _queueControl;
        private IntPtr _queueProgressClass, _queuePauseClass, _queueControlClass, _queueTimeClass, _queueTouchBlockerClass, _queueWindowClass;
        private long _queueChartAddress, _queueProgressAddress, _queuePauseAddress, _queueControlAddress;
        private readonly Dictionary<long, SeekRuntimeNote> _queueNotes = new();
        private bool _queueWriter;

        internal NativeSeekJudgeStateReader(NativeJudgementBackend owner, RunValue run, ISeekChartNativeProfile profile, bool queueWriter)
            : this(owner, run, profile)
        {
            try
            {
                if (!queueWriter) throw new ArgumentException("The explicit Judge writer constructor requires its write mode.", nameof(queueWriter));
                var view = _view ?? throw new InvalidOperationException("The independent actual Judge NoteView is absent.");
                _queueChartAddress = view.Identity.Chart.Chart; _queueProgressAddress = view.Identity.Chart.Progress;
                _queueControlAddress = view.Identity.NoteControl;
                _queueProgressClass = Native(() => profile.ProgressClass);
                if (Native(() => profile.ChartClass) != owner._chart || _queueProgressClass == IntPtr.Zero)
                    throw new UnsupportedSignatureException("The Judge writer profile has another chart/progress identity.");
                _queuePauseClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "PauseUIControl"));
                _queueControlClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "InLevel", "NoteControl"));
                _queueTimeClass = Native(() => owner._api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time"));
                _queueTouchBlockerClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "TouchBlocker"));
                _queueWindowClass = Native(() => owner._api.Class("Assembly-CSharp.dll", "", "WindowControl"));
                _queueController = Exact(new(_controllerAddress), owner._controller);
                _queueChart = Exact(new(_queueChartAddress), owner._chart);
                _queueProgress = Exact(new(_queueProgressAddress), _queueProgressClass);
                _queueControl = Exact(new(_queueControlAddress), _queueControlClass);
                _queuePause = ReferenceField(_queueController, owner._controller, "pauseUIControl", "PauseUIControl", 0xb0);
                _queuePauseAddress = Target(_queuePause).ToInt64();
                if (Native(() => owner._api.ObjectClass(Target(_queuePause))) != _queuePauseClass)
                    throw new UnsupportedSignatureException("The actual Judge writer has another exact pause class.");
                foreach (var note in view.Notes)
                {
                    if (!_queueNotes.TryAdd(note.Reference, note)) throw new InvalidDataException("The actual Judge NoteView contains duplicate reference identities.");
                    Exact(new(note.Reference), owner._note);
                }
                for (var slot = 0; slot < 3; ++slot) QueueLayout(slot);
                _queueWriter = true; VerifyQueueCurrent();
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup) { error.Data["SeekJudgeQueuesCleanupOwner"] = this; error.Data["SeekJudgeQueuesCleanupFailure"] = cleanup; }
                throw;
            }
        }
        void INativeSeekJudgeQueuesAccess.VerifyLocal() => VerifyLocal();
        public void VerifySource() => VerifyQueueCurrent(fullGraph: true);
        private T QueueValue<T>(NativeRuntime.RootHandle owner, IntPtr cls, string name, string type, int offset) where T : struct
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
            return Native(() => _owner._api.ReadExactField<T>(owner, field));
        }
        private long QueueReference(NativeRuntime.RootHandle owner, IntPtr cls, string name, string type, int offset)
        {
            Native(() => _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
            var target = Target(owner);
            return Native(() => _owner._api.ReadWithPublication<IntPtr>(target, name)).ToInt64();
        }
        private bool QueueBoolean(IntPtr cls, string name, NativeRuntime.RootHandle? receiver = null)
        {
            var method = Native(() => _owner._api.Method(cls, name, 0, isStatic: receiver is null));
            var target = receiver is null ? IntPtr.Zero : Target(receiver);
            return Native(() => _owner._api.InvokeValueWithPublication<bool>(method, target));
        }
        private NativeNoteJudgeListLayout QueueLayout(int slot)
        {
            var layout = CurrentLayout(slot);
            foreach (var field in new[] { layout.BackingField, layout.SizeField, layout.VersionField })
                if (field.DeclaringClass != layout.Class || field.IsStatic || (field.Flags & (0x10u | 0x20u | 0x40u)) != 0)
                    throw new UnsupportedSignatureException("Judge restoration requires exact mutable declared List storage.");
            return layout;
        }
        private void VerifyQueueCurrent(bool fullGraph = false)
        {
            try
            {
                VerifyLocal();
                if (!_queueWriter || _queueController is null || _queueChart is null || _queueProgress is null
                    || _queuePause is null || _queueControl is null)
                    throw new InvalidOperationException("The independently retained Judge writer is not prepared.");
                ReadIdentity(fullGraph); // Its own NoteView, never a Judge baseline view.
                foreach (var pair in new[] { (_queueController, _owner._controller), (_information, _owner._info),
                    (_queueChart, _owner._chart), (_queueProgress, _queueProgressClass), (_queuePause, _queuePauseClass),
                    (_queueControl, _queueControlClass), (_judge, _owner._owner) })
                    if (Native(() => _owner._api.ObjectClass(Target(pair.Item1))) != pair.Item2)
                        throw new UnsupportedSignatureException("An independent Judge writer source changed its exact class.");
                if (!Native(() => _owner._api.MatchesNormalControllerInformation(_queueController, _information,
                    _owner._controller, _owner._info, VerifyLocal))
                    || QueueReference(_information, _owner._info, "chart", "InLevel.Chart", 0x88) != _queueChartAddress
                    || QueueReference(_information, _owner._info, "progressControl", "InLevel.ProgressControl", 0x28) != _queueProgressAddress
                    || QueueReference(_queueProgress, _queueProgressClass, "levelInformation", "InLevel.LevelInformation", 0x20) != _informationAddress
                    || QueueReference(_queueController, _owner._controller, "pauseUIControl", "PauseUIControl", 0xb0) != _queuePauseAddress
                    || QueueReference(_information, _owner._info, "noteControl", "InLevel.NoteControl", 0x40) != _queueControlAddress
                    || QueueReference(_queueControl, _queueControlClass, "levelInformation", "InLevel.LevelInformation", 0x20) != _informationAddress
                    || QueueReference(_queueControl, _queueControlClass, "notes", "InLevel.NoteInformation[]", 0x28) != _view!.Identity.RenderNotes
                    || QueueReference(_queueControl, _queueControlClass, "allNotes", "InLevel.NoteInformation[]", 0x30) != _view.Identity.AllNotes)
                    throw new InvalidOperationException("The actual Judge writer run/chart/progress/pause association changed.");
                if (!Alive(_queueController) || !Alive(_information) || !Alive(_queueProgress) || !Alive(_queuePause)
                    || !Alive(_queueControl) || !Alive(_judge)
                    || !QueueBoolean(_owner._controller, "get_isActiveAndEnabled", _queueController)
                    || !QueueBoolean(_queuePauseClass, "get_isActiveAndEnabled", _queuePause))
                    throw new InvalidOperationException("The retained Judge writer source is inactive or dead.");
                var countdown = QueueValue<float>(_queuePause, _queuePauseClass, "_pauseTime", "System.Single", 0xbc);
                var timeMethod = Native(() => _owner._api.Method(_queueTimeClass, "get_timeSinceLevelLoad", 0, isStatic: true));
                var sceneTime = Native(() => _owner._api.InvokeValueWithPublication<float>(timeMethod, IntPtr.Zero));
                if (!QueueValue<bool>(_information, _owner._info, "loadOver", "System.Boolean", 0xbe)
                    || QueueValue<bool>(_information, _owner._info, "levelOver", "System.Boolean", 0xc0)
                    || QueueValue<bool>(_information, _owner._info, "crashed", "System.Boolean", 0xc1)
                    || !QueueValue<bool>(_queueProgress, _queueProgressClass, "timeReady", "System.Boolean", 0x65)
                    || QueueValue<bool>(_queueProgress, _queueProgressClass, "isPlaying", "System.Boolean", 0x42)
                    || QueueValue<bool>(_queueProgress, _queueProgressClass, "prepareToPlay", "System.Boolean", 0x40)
                    || !QueueValue<bool>(_queuePause, _queuePauseClass, "_pausing", "System.Boolean", 0xb4)
                    || QueueValue<bool>(_queuePause, _queuePauseClass, "_over", "System.Boolean", 0xc0)
                    || QueueValue<bool>(_queuePause, _queuePauseClass, "_terminated", "System.Boolean", 0xc1)
                    || !float.IsFinite(countdown) || countdown > 0 || !float.IsFinite(sceneTime) || sceneTime < 1.8f
                    || !QueueBoolean(_queuePauseClass, "get_CanDoOperation", _queuePause)
                    || QueueBoolean(_queueTouchBlockerClass, "get_Blocked") || QueueBoolean(_queueWindowClass, "get_HasActiveWindow"))
                    throw new InvalidOperationException("Judge queue writes require the actual ready paused non-ending source.");
                VerifyLocal();
            }
            catch (Exception error)
            {
                _faulted = true;
                if (error.Data["SeekRuntimeNoteCleanupFailure"] is Exception cleanup) RecordViewFailure(cleanup);
                throw;
            }
        }
        public SeekJudgeRawState Read()
        {
            VerifyQueueCurrent(); var first = ReadSnapshot(fullGraph: false);
            VerifyQueueCurrent(); var second = ReadSnapshot(fullGraph: false); VerifyQueueCurrent();
            if (!SeekJudgeQueueState.Same(first, second))
                throw new InvalidOperationException("Actual Judge storage changed between complete writer snapshots.");
            return second;
        }
        private SeekJudgeArrayStorage ReadQueueBacking(NativeRuntime.RootHandle backing, IntPtr arrayClass)
        {
            if (Native(() => _owner._api.ObjectClass(Target(backing))) != arrayClass || ArrayCount(backing, "get_Rank", 1) != 1)
                throw new UnsupportedSignatureException("The retained original Judge backing changed its exact array class or rank.");
            var count = ArrayCount(backing, "get_Length", NativeSeekJudgeStateView.MaximumCapacity);
            var slots = new long[count];
            for (var index = 0; index < count; ++index) slots[index] = ArrayItem(backing, arrayClass, index);
            if (ArrayCount(backing, "get_Length", NativeSeekJudgeStateView.MaximumCapacity) != count)
                throw new InvalidOperationException("The retained complete Judge backing changed capacity during observation.");
            return new(Target(backing).ToInt64(), arrayClass.ToInt64(), Array.AsReadOnly(slots));
        }
        public SeekJudgeArrayStorage ReadBacking(long reference)
        {
            VerifyQueueCurrent();
            if (!_retained.TryGetValue(reference, out var backing))
                throw new InvalidOperationException("Only independently retained actual Judge backing arrays can be observed.");
            var arrayClass = _layouts[0].ArrayClass;
            var first = ReadQueueBacking(backing, arrayClass); VerifyQueueCurrent();
            var second = ReadQueueBacking(backing, arrayClass); VerifyQueueCurrent();
            if (!SeekJudgeQueueState.Same(first, second))
                throw new InvalidOperationException("The complete retained Judge backing changed between independent observations.");
            return second;
        }
        private void RequireQueueExpected(SeekJudgeRawState expectedCurrent)
        {
            if (!SeekJudgeQueueState.Same(Read(), expectedCurrent))
                throw new InvalidOperationException("Complete actual Judge storage changed before its conditional official mutation.");
            for (var slot = 0; slot < 3; ++slot)
                for (var previous = 0; previous < slot; ++previous)
                {
                    var first = SeekJudgeQueueState.At(expectedCurrent, previous); var second = SeekJudgeQueueState.At(expectedCurrent, slot);
                    if (first.Reference != second.Reference && first.Backing == second.Backing && first.CapacitySlots.Count != 0)
                        throw new NotSupportedException("Distinct Judge Lists sharing nonempty backing storage are not supported by this writer.");
                }
        }
        private NativeRuntime.RootHandle QueueNote(long reference)
        {
            if (!_queueNotes.TryGetValue(reference, out var expected) || !_retained.TryGetValue(reference, out var note)
                || Target(note).ToInt64() != reference || !Alive(note)
                || Native(() => _owner._api.ObjectClass(Target(note))) != _owner._note
                || QueueReference(note, _owner._note, "noteData", "InLevel.Note", 0x28) != expected.NoteData
                || QueueReference(note, _owner._note, "lineInformation", "InLevel.LineInformation", 0x20) != expected.Line.Reference)
                throw new InvalidOperationException("An explicit Judge occurrence lost its independently owned actual note identity.");
            return note;
        }
        private IntPtr QueueVoidMethod(IntPtr cls, string name, params string[] parameters)
        {
            var declared = Native(() => _owner._api.InspectDeclaredMethod(cls, name, parameters, "System.Void", IntPtr.Zero));
            if (declared.DeclaringClass.Class != cls || declared.ReturnType != "System.Void" || (declared.Flags & 0x10u) != 0)
                throw new UnsupportedSignatureException("The actual Judge list method is not its exact official instance void declaration.");
            return declared.MethodInfo;
        }
        public void Clear(int listSlot, SeekJudgeRawState expectedCurrent, Action beforeDispatch)
            => WriteQueue(listSlot, null, expectedCurrent, beforeDispatch);
        public void Add(int listSlot, long reference, SeekJudgeRawState expectedCurrent, Action beforeDispatch)
            => WriteQueue(listSlot, reference, expectedCurrent, beforeDispatch);
        private void WriteQueue(int listSlot, long? reference, SeekJudgeRawState expectedCurrent, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            if ((uint)listSlot >= 3) throw new ArgumentOutOfRangeException(nameof(listSlot));
            RequireQueueExpected(expectedCurrent); var layout = QueueLayout(listSlot);
            var note = reference is long token ? QueueNote(token) : null;
            var method = reference is null ? QueueVoidMethod(layout.Class, "Clear")
                : QueueVoidMethod(layout.Class, "Add", "InLevel.NoteInformation");
            if (reference is not null && Native(() => _owner._api.InspectMethodParameterClass(method, 0, "InLevel.NoteInformation")).Class != _owner._note)
                throw new UnsupportedSignatureException("Official Judge List.Add has another exact note parameter class.");
            var receiver = Target(_lists[listSlot]);
            var noteTarget = note is null ? IntPtr.Zero : Target(note);
            using var noteArgument = reference is null ? null : A.Ref(noteTarget);
            Native(() =>
            {
                void FinalGuard() { VerifyLocal(); beforeDispatch(); VerifyLocal(); }
                if (reference is null) _owner._api.InvokeVoidWithPublicationGuarded(method, receiver, FinalGuard);
                else _owner._api.InvokeVoidWithPublicationGuarded(method, receiver, FinalGuard, noteArgument!);
                return 0;
            });
        }
        public void RestoreCapacitySlot(int listSlot, int capacitySlot, long value,
            SeekJudgeRawState expectedCurrent, SeekJudgeArrayStorage expectedBacking, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            if ((uint)listSlot >= 3 || (uint)capacitySlot >= (uint)expectedBacking.CapacitySlots.Count)
                throw new ArgumentOutOfRangeException(nameof(capacitySlot));
            RequireQueueExpected(expectedCurrent); QueueLayout(listSlot);
            if (!SeekJudgeQueueState.Same(ReadBacking(expectedBacking.Reference), expectedBacking)
                || !_retained.TryGetValue(expectedBacking.Reference, out var backing))
                throw new InvalidOperationException("The independently retained original backing changed before conditional slot restoration.");
            IntPtr item = IntPtr.Zero;
            if (value != 0)
            {
                if (!_retained.TryGetValue(value, out var retained)
                    || Native(() => _owner._api.ObjectClass(Target(retained))) != _owner._note)
                    throw new InvalidOperationException("The original capacity reference is no longer independently retained with its exact class.");
                item = Target(retained);
            }
            var arrayClass = new IntPtr(expectedBacking.Class);
            var method = Native(() => _owner._api.Method(arrayClass, "SetValue", 2,
                parameterTypes: new[] { "System.Object", "System.Int32" }));
            if (Native(() => _owner._api.DescribeReturnType(method)) != "System.Void")
                throw new UnsupportedSignatureException("Official backing SetValue has another return type.");
            var receiver = Target(backing);
            using var itemArgument = A.Ref(item);
            using var indexArgument = A.Value(capacitySlot);
            Native(() =>
            {
                _owner._api.InvokeVoidWithPublicationGuarded(method, receiver, () =>
                { VerifyLocal(); beforeDispatch(); VerifyLocal(); }, itemArgument, indexArgument);
                return 0;
            });
        }
        public void RestoreListField(int listSlot, SeekJudgeQueueRestoreField field, long value,
            SeekJudgeRawState expectedCurrent, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            if ((uint)listSlot >= 3) throw new ArgumentOutOfRangeException(nameof(listSlot));
            RequireQueueExpected(expectedCurrent); var layout = QueueLayout(listSlot);
            var storage = SeekJudgeQueueState.At(expectedCurrent, listSlot);
            var descriptor = field switch
            {
                SeekJudgeQueueRestoreField.Items => layout.BackingField,
                SeekJudgeQueueRestoreField.Size => layout.SizeField,
                SeekJudgeQueueRestoreField.Version => layout.VersionField,
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            NativeRuntime.Arg argument;
            if (field == SeekJudgeQueueRestoreField.Items)
            {
                var original = ReadBacking(value);
                if (original.Class != layout.ArrayClass.ToInt64() || storage.Size > original.CapacitySlots.Count
                    || !_retained.TryGetValue(value, out var retained))
                    throw new InvalidOperationException("The original backing cannot be conditionally reattached with the current size.");
                argument = A.Ref(Target(retained));
            }
            else
            {
                var integer = checked((int)value);
                if (field == SeekJudgeQueueRestoreField.Size && (integer < 0 || integer > storage.CapacitySlots.Count))
                    throw new InvalidOperationException("The original List size exceeds the current complete backing capacity.");
                argument = A.Value(integer);
            }
            using (argument) Native(() =>
            {
                _owner._api.WriteExactGuarded(_lists[listSlot], layout.Class, descriptor.Name, descriptor.TypeName, argument,
                    () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); });
                return 0;
            });
        }
    }
}
