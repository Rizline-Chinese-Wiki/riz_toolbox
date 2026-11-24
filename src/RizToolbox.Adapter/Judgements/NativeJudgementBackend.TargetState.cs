using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private static IRetainedSeekTargetReader CreateSeekTargetStateReader(NativeJudgementBackend owner,
        RunValue run, ISeekTargetNativeProfile profile, GameplaySeekTargetSeconds target)
        => NativeSeekNoteFlagsAccess.CreateTargetSource(owner, run, profile, target);

    private sealed partial class NativeSeekNoteFlagsAccess
    {
        internal static IRetainedSeekTargetReader CreateTargetSource(NativeJudgementBackend owner,
            RunValue run, ISeekTargetNativeProfile profile, GameplaySeekTargetSeconds target)
            => new TargetSource(owner, run, profile, target);

        private long TargetNullableReference(NativeRuntime.RootHandle receiver, IntPtr cls, string name, string type, int offset)
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
            NativeRuntime.RootHandle? value = null; var published = false;
            Native(() =>
            {
                _owner._api.PublishExactReferenceField(receiver, field, token =>
                {
                    if (published) throw new InvalidOperationException("A target reference was published twice.");
                    published = true;
                    if (token != IntPtr.Zero) value = TargetAdopt(token);
                }); return 0;
            });
            if (!published) throw new InvalidOperationException("The nullable target field was not published.");
            if (value is null) return 0;
            if (Native(() => _owner._api.ObjectClass(Target(value))) != field.FieldClass)
                throw new UnsupportedSignatureException("The nullable target field has another actual class.");
            return Target(value).ToInt64();
        }

        // A separate root owner captures chart data, current actual note instances,
        // judgement initialization occurrences and the time conversion receiver.
        // Reading this source never dispatches a game Update or a judgement event.
        private sealed class TargetSource : IRetainedSeekTargetReader
        {
            private readonly NativeSeekNoteFlagsAccess _core;
            private readonly ISeekTargetNativeProfile _profile;
            private readonly NativeRuntime.RootHandle _judge;
            private readonly SeekTargetSourceIdentity _identity;
            private NativeSeekChartPlan? _plan;
            private Action? _lifetime;
            private bool _withdrawn, _released;
            private readonly List<Exception> _cleanup = new();
            public SeekTargetCoordinates Coordinates { get; }
            public NativeSeekChartPlan ChartPlan { get { Local(); return _plan!; } }
            public NativeSeekRuntimeNoteView NoteView { get { Local(); return _core._view!; } }

            internal TargetSource(NativeJudgementBackend owner, RunValue run, ISeekTargetNativeProfile profile,
                GameplaySeekTargetSeconds target)
            {
                ArgumentNullException.ThrowIfNull(profile);
                if (!double.IsFinite(target.Value) || !float.IsFinite((float)target.Value))
                    throw new ArgumentOutOfRangeException(nameof(target));
                _profile = profile;
                _core = new(owner, run, profile);
                try
                {
                    _core._rootLimit = checked(16 + 3 * NativeSeekTargetStatePlan.MaximumNotes
                        + NativeSeekChartPlan.MaximumBpmShifts + NativeSeekTargetStatePlan.MaximumChallengeIntervals);
                    _core.BindLifetimeGuard(Local);
                    _core.Native(() => { profile.VerifyCurrent(); return 0; });
                    if (profile.InformationClass != owner._info || profile.NoteClass != owner._note
                        || profile.JudgeClass != owner._owner || profile.ChallengeTimeClass == IntPtr.Zero)
                        throw new UnsupportedSignatureException("The target profile has different actual source classes.");
                    _judge = _core.TargetReference(_core._information, owner._info, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58);
                    _identity = new(_core._noteViewIdentity, _core.Target(_judge).ToInt64());
                    _plan = NativeSeekChartPlan.Capture(new TargetChartReader(_core, profile), target);
                    Coordinates = new(target.Value, (float)target.Value, _core.TargetConvert("RealTimeToChartTime", target.Value, profile));
                    VerifyAccess();
                }
                catch (Exception error)
                {
                    try { Dispose(); }
                    catch (Exception cleanup) { error.Data["SeekTargetCleanupOwner"] = this; error.Data["SeekTargetCleanupFailure"] = cleanup; }
                    throw;
                }
            }
            public void VerifyOwnerThread() => _core.VerifyOwnerThread();
            private void Local()
            {
                VerifyOwnerThread();
                if (_withdrawn) throw new ObjectDisposedException(nameof(TargetSource));
                _lifetime?.Invoke();
            }
            public void BindLifetimeGuard(Action guard)
            {
                Local(); ArgumentNullException.ThrowIfNull(guard);
                if (_lifetime is not null) throw new InvalidOperationException("The target lifetime guard is once-attached.");
                _lifetime = guard;
            }
            public void VerifyLocal(SeekTargetSourceIdentity expected)
            {
                Local(); _core.VerifyLocal();
                if (_identity != expected) throw new InvalidOperationException("The target source identity changed.");
            }
            public void VerifyAccess()
            {
                Local(); var c = _core;
                c.Native(() => { _profile.VerifyCurrent(); return 0; });
                c.VerifyCurrent();
                if (!c.Alive(_judge) || c.Native(() => c._owner._api.ObjectClass(c.Target(_judge))) != _profile.JudgeClass
                    || c.ReferenceToken(c._information, c._owner._info, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58) != _identity.NoteJudgeControl
                    || c.ReferenceToken(_judge, _profile.JudgeClass, "levelInformation", "InLevel.LevelInformation", 0x20) != c._informationAddress)
                    throw new InvalidOperationException("The target Judge/source association changed or died.");
                _plan?.VerifySource();
                VerifyLocal(_identity);
            }
            private SeekTargetWindows Windows(NativeRuntime.RootHandle receiver, IntPtr cls, bool cached)
            {
                var c = _core; var prefix = cached ? "_" : ""; var offset = cached ? 0x54 : 0xa0;
                return new(c.ValueField<float>(receiver, cls, prefix + "judgeTime", "System.Single", offset),
                    c.ValueField<float>(receiver, cls, prefix + "maxPerfectJudgeTimeInChallengeTime", "System.Single", offset + 4),
                    c.ValueField<float>(receiver, cls, prefix + "normalPerfectJudgeTimeInChallengeTime", "System.Single", offset + 8),
                    c.ValueField<float>(receiver, cls, "badTime", "System.Single", offset + 12));
            }
            private (long List, long Backing, int Version, IReadOnlyList<long> Notes) InitializationNotes()
            {
                var c = _core; const string type = "System.Collections.Generic.List<InLevel.NoteInformation>";
                var list = c.TargetReference(_judge, _profile.JudgeClass, "notes", type, 0x38);
                var cls = c.Native(() => c._owner._api.ObjectClass(c.Target(list)));
                var layout = c.Native(() => c._owner._api.InspectNoteJudgeListClass(cls));
                var backing = c.TargetReference(list, cls, "_items", "InLevel.NoteInformation[]", layout.BackingField.Offset);
                int Read(NativeExactReadField f) => c.Native(() => c._owner._api.ReadExactField<int>(list, f));
                var size = Read(layout.SizeField); var version = Read(layout.VersionField);
                if (size < 0 || size > NativeSeekTargetStatePlan.MaximumNotes || c.TargetCount(list, NativeSeekTargetStatePlan.MaximumNotes) != size)
                    throw new InvalidDataException("The target initialization occurrences exceed their complete bound.");
                var members = new HashSet<long>(c._noteReferences); var notes = new long[size];
                for (var i = 0; i < size; ++i)
                {
                    notes[i] = c.Target(c.TargetItem(list, i, _profile.NoteClass)).ToInt64();
                    if (!members.Contains(notes[i])) throw new InvalidDataException("A current Judge initialization occurrence is outside the complete actual note source.");
                }
                if (Read(layout.SizeField) != size || Read(layout.VersionField) != version || c.TargetCount(list, NativeSeekTargetStatePlan.MaximumNotes) != size
                    || c.ReferenceToken(list, cls, "_items", "InLevel.NoteInformation[]", layout.BackingField.Offset) != c.Target(backing).ToInt64()
                    || c.ReferenceToken(_judge, _profile.JudgeClass, "notes", type, 0x38) != c.Target(list).ToInt64()
                    || c.Native(() => c._owner._api.InspectNoteJudgeListClass(cls)) != layout)
                    throw new InvalidOperationException("The current Judge initialization container changed during capture.");
                return (c.Target(list).ToInt64(), c.Target(backing).ToInt64(), version, Array.AsReadOnly(notes));
            }
            public SeekTargetRawState ReadSnapshot()
            {
                VerifyAccess(); var c = _core; var jc = _profile.JudgeClass; var info = c._information; var ic = c._owner._info;
                var ready = c.ValueField<bool>(_judge, jc, "_instantiateReady", "System.Boolean", 0x6c);
                var cached = Windows(_judge, jc, true); var information = Windows(info, ic, false);
                var complete = c.ValueField<bool>(info, ic, "challengeCompletely", "System.Boolean", 0xbc);
                var auto = c.ValueField<bool>(_judge, jc, "<AutoPlay>k__BackingField", "System.Boolean", 0x79);
                var free = c.ValueField<bool>(_judge, jc, "<FreeHold>k__BackingField", "System.Boolean", 0x7a);
                var bad = c.ValueField<bool>(_judge, jc, "<InstantiateBadHit>k__BackingField", "System.Boolean", 0x78);
                var combo = c.ValueField<int>(info, ic, "challengeTimeComboNum", "System.Int32", 0x94);
                var list = c.TargetReference(c._chart, c._owner._chart, "challengeTimes", "System.Collections.Generic.List<InLevel.ChallengeTime>", 0x28);
                var count = c.TargetCount(list, NativeSeekTargetStatePlan.MaximumChallengeIntervals);
                var intervals = new SeekTargetChallengeInterval[count];
                for (var i = 0; i < count; ++i)
                {
                    var point = c.TargetItem(list, i, _profile.ChallengeTimeClass);
                    intervals[i] = new(c.Target(point).ToInt64(), c.ValueField<float>(point, _profile.ChallengeTimeClass, "start", "System.Single", 0x14),
                        c.ValueField<float>(point, _profile.ChallengeTimeClass, "end", "System.Single", 0x18));
                }
                if (c.TargetCount(list, NativeSeekTargetStatePlan.MaximumChallengeIntervals) != count
                    || c.ReferenceToken(c._chart, c._owner._chart, "challengeTimes", "System.Collections.Generic.List<InLevel.ChallengeTime>", 0x28) != c.Target(list).ToInt64())
                    throw new InvalidOperationException("The challenge interval source changed during capture.");
                var notes = new SeekTargetNoteState[c._notes.Length];
                for (var i = 0; i < notes.Length; ++i)
                {
                    c.VerifyNote(i); var note = c._notes[i]; var nc = c._owner._note;
                    var data = c.TargetReference(note, nc, "noteData", "InLevel.Note", 0x28);
                    var dc = c.Native(() => c._owner._api.ObjectClass(c.Target(data)));
                    notes[i] = new(c._noteReferences[i], c.Target(data).ToInt64(),
                        c.ValueField<int>(data, dc, "type", "System.Int32", 0x10), c.ValueField<float>(data, dc, "time", "System.Single", 0x14),
                        c.ValueField<float>(note, nc, "realTime", "System.Single", 0x34), c.ValueField<float>(note, nc, "holdEndRealTime", "System.Single", 0x38),
                        c.ValueField<bool>(note, nc, "inChallengeTime", "System.Boolean", 0x56),
                        c.ValueField<bool>(note, nc, "isActive", "System.Boolean", 0x30), c.ValueField<bool>(note, nc, "destroyed", "System.Boolean", 0x31),
                        c.ValueField<bool>(note, nc, "missed", "System.Boolean", 0x55),
                        c.TargetNullableReference(note, nc, "fingerIds", "System.Collections.Generic.List<System.Int32>", 0x58));
                    c.VerifyNote(i);
                }
                var initialization = InitializationNotes();
                var challengeListAddress = c.Target(list).ToInt64();
                VerifyAccess();
                return new(_identity, ready, cached, information, complete, auto, free, bad, combo, challengeListAddress,
                    Array.AsReadOnly(intervals), Array.AsReadOnly(notes), initialization.List, initialization.Backing, initialization.Version, initialization.Notes);
            }
            public void RequestDispose()
            {
                VerifyOwnerThread(); _withdrawn = true; _core.RequestDispose();
            }
            public void Dispose()
            {
                VerifyOwnerThread(); _withdrawn = true;
                if (!_released)
                {
                    _released = true;
                    try { _plan?.Dispose(); } catch (Exception error) { _cleanup.Add(error); }
                    try { _core.Dispose(); } catch (Exception error) { _cleanup.Add(error); }
                }
                if (_cleanup.Count == 0) return;
                var failure = new AggregateException("Target source retains unknown native cleanup without retry.", _cleanup);
                failure.Data["SeekTargetCleanupOwner"] = this; throw failure;
            }
        }
    }
}
