using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private static INativeSeekHoldStateAccess CreateSeekHoldStateAccess(NativeJudgementBackend owner,
        RunValue run, ISeekHoldNativeProfile profile) => NativeSeekNoteFlagsAccess.CreateHoldSource(owner, run, profile);

    private sealed partial class NativeSeekNoteFlagsAccess
    {
        internal static INativeSeekHoldStateAccess CreateHoldSource(NativeJudgementBackend owner,
            RunValue run, ISeekHoldNativeProfile profile)
        {
            try { return new HoldAccess(owner, run, profile); }
            catch (Exception error)
            {
                // The shared core can fail before the outer access constructor
                // receives ownership. Preserve its exact cleanup owner as well.
                if (!error.Data.Contains("SeekHoldStateCleanupFailure")
                    && error.Data["SeekNoteFlagsCleanupFailure"] is Exception cleanup)
                {
                    error.Data["SeekHoldStateCleanupOwner"] = error.Data["SeekNoteFlagsCleanupOwner"];
                    error.Data["SeekHoldStateCleanupFailure"] = cleanup;
                }
                throw;
            }
        }

        // Shares the independently retained NoteView/ready-paused implementation,
        // not a caller's flags participant or chart-plan lease. No Hold Update,
        // renderer, Transform, coroutine or score/event consumer is invoked here.
        private sealed partial class HoldAccess : INativeSeekHoldStateAccess
        {
            private const string ListType = "System.Collections.Generic.List<InLevel.NoteInformation>";
            private static readonly string[] ListNames = { "holdNotes", "runningHoldNotes" };
            private static readonly string[] ControlNames = { "_nowTime", "_speed", "_holdLineLength" };
            private readonly NativeSeekNoteFlagsAccess _core;
            private readonly ISeekHoldNativeProfile _profile;
            private readonly NativeRuntime.RootHandle _hold;
            private readonly long _holdAddress;
            private readonly NativeRuntime.RootHandle[] _lists = new NativeRuntime.RootHandle[2];
            private readonly NativeExactReadField[] _listFields = new NativeExactReadField[2];
            private readonly NativeNoteJudgeListLayout[] _layouts = new NativeNoteJudgeListLayout[2];
            private readonly long[] _listAddresses = new long[2];
            private readonly NativeExactReadField[] _noteFields = new NativeExactReadField[2];
            private readonly NativeExactReadField[] _controlFields = new NativeExactReadField[3];
            private readonly int[] _ordinals;
            private readonly IReadOnlyList<SeekRuntimeNote> _holds;
            private readonly Dictionary<long, int> _holdOrdinals = new();
            private readonly Dictionary<long, int> _backingCapacities = new();
            private int _totalCapacity;
            private readonly SeekHoldStateSourceIdentity _identity;
            private Action? _lifetime;
            private bool _withdrawn, _expired, _released;
            private readonly List<Exception> _cleanup = new();

            internal HoldAccess(NativeJudgementBackend owner, RunValue run, ISeekHoldNativeProfile profile)
            {
                ArgumentNullException.ThrowIfNull(profile); _profile = profile;
                _core = new(owner, run, profile);
                try
                {
                    _core._rootLimit = 4 * NativeSeekHoldStateParticipant.MaximumCapacity + 128;
                    _core.BindLifetimeGuard(OwnLifetime);
                    var c = _core;
                    c.Native(() => { profile.VerifyCurrent(); return 0; });
                    if (profile.InformationClass != owner._info || profile.NoteClass != owner._note
                        || profile.ChartClass != owner._chart || profile.ProgressClass != c._progressClass)
                        throw new UnsupportedSignatureException("The Hold state profile has another exact source class.");
                    _hold = c.TargetReference(c._information, owner._info, "holdControl", "InLevel.HoldControl", 0x48);
                    if (c.Native(() => owner._api.ObjectClass(c.Target(_hold))) != profile.HoldClass)
                        throw new UnsupportedSignatureException("The actual HoldControl has another exact class.");
                    _holdAddress = c.Target(_hold).ToInt64();
                    _identity = new(c._noteViewIdentity, _holdAddress);
                    var ordinals = new List<int>(); var holds = new List<SeekRuntimeNote>();
                    for (var i = 0; i < c._capturedNotes.Length; ++i)
                    {
                        var note = c._capturedNotes[i];
                        var raw = c._view!.ChartLines[note.Location.LineOrdinal].Data.Notes[note.Location.NoteOrdinal];
                        if (raw.Type != 2) continue;
                        _holdOrdinals.Add(note.Reference, holds.Count); ordinals.Add(i); holds.Add(note);
                    }
                    _ordinals = ordinals.ToArray(); _holds = Array.AsReadOnly(holds.ToArray());
                    for (var i = 0; i < 2; ++i)
                    {
                        _listFields[i] = Mutable(profile.HoldClass, ListNames[i], ListType, 0x28 + i * IntPtr.Size);
                        _lists[i] = c.TargetReference(_hold, profile.HoldClass, ListNames[i], ListType, _listFields[i].Offset);
                        _listAddresses[i] = c.Target(_lists[i]).ToInt64();
                        _layouts[i] = c.Native(() => owner._api.InspectNoteJudgeListClass(_listFields[i].FieldClass));
                        ValidateLayout(_layouts[i]);
                    }
                    _noteFields[0] = Mutable(owner._note, "isStaticNote", "System.Boolean", 0x54);
                    _noteFields[1] = Mutable(owner._note, "lastFrameHoldLineLength", "System.Single", 0x50);
                    for (var i = 0; i < 3; ++i)
                        _controlFields[i] = Mutable(profile.HoldClass, ControlNames[i], "System.Single", 0x38 + 4 * i);
                    VerifySource();
                }
                catch (Exception error)
                {
                    try { Dispose(); }
                    catch (Exception cleanup)
                    { error.Data["SeekHoldStateCleanupOwner"] = this; error.Data["SeekHoldStateCleanupFailure"] = cleanup; }
                    throw;
                }
            }
            public void VerifyOwnerThread() => _core.VerifyOwnerThread();
            private void OwnLifetime()
            {
                VerifyOwnerThread();
                if (_withdrawn) throw new ObjectDisposedException(nameof(HoldAccess));
                if (_expired) throw new InvalidOperationException("The actual Hold state source was already observed expired.");
                _lifetime?.Invoke();
            }
            public void VerifyLocal() { OwnLifetime(); _core.VerifyLocal(); }
            public void BindLifetimeGuard(Action guard)
            {
                VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
                if (_lifetime is not null) throw new InvalidOperationException("The Hold state lifetime guard is once-attached.");
                _lifetime = guard;
            }
            public IReadOnlyList<SeekRuntimeNote> RuntimeHolds { get { VerifyLocal(); return _holds; } }
            private void VerifyCurrent(bool fullGraph = false)
            {
                try
                {
                    VerifyLocal(); var c = _core;
                    c.Native(() => { _profile.VerifyCurrent(); return 0; }); c.VerifyCurrent(fullGraph);
                    if (c.Native(() => c._owner._api.ObjectClass(c.Target(_hold))) != _profile.HoldClass || !c.Alive(_hold)
                        || c.ReferenceToken(c._information, c._owner._info, "holdControl", "InLevel.HoldControl", 0x48) != _holdAddress
                        || c.ReferenceToken(_hold, _profile.HoldClass, "levelInformation", "InLevel.LevelInformation", 0x20) != c._informationAddress)
                        throw new InvalidOperationException("The actual Hold control lost its live source association.");
                    VerifyLocal();
                }
                catch { _expired = true; throw; }
            }
            public void VerifySource() => VerifyCurrent(fullGraph: true);
            private InvalidOperationException SourceChanged(string message)
            { _expired = true; return new InvalidOperationException(message); }
            private T ObserveSource<T>(Func<T> operation)
            {
                try { return operation(); }
                catch (Exception error) when (error is InvalidDataException or UnsupportedSignatureException)
                {
                    // A rejected actual shape/descriptor is an observation, even
                    // when no complete snapshot could be returned to the caller.
                    // Transient native getter failures are not generalized here.
                    _expired = true; throw;
                }
            }
            private NativeExactReadField Mutable(IntPtr cls, string name, string type, int offset)
            {
                var c = _core;
                var field = c.Native(() => c._owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
                if (field.DeclaringClass != cls || field.IsStatic || (field.Flags & (0x10u | 0x20u | 0x40u)) != 0
                    || field.Size != (type == "System.Boolean" ? 1 : type == "System.Single" || type == "System.Int32" ? 4 : IntPtr.Size))
                    throw new UnsupportedSignatureException("Hold restoration requires exact mutable declared storage.");
                return field;
            }
            private NativeExactReadField CurrentField(NativeExactReadField baseline)
            {
                var field = Mutable(baseline.DeclaringClass, baseline.Name, baseline.TypeName, baseline.Offset);
                if (field != baseline) throw new UnsupportedSignatureException("An actual Hold value descriptor changed after preparation.");
                return field;
            }
            private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
            private static bool Same(SeekHoldNoteTarget a, SeekHoldNoteTarget b)
                => a.Reference == b.Reference && a.IsStaticNote == b.IsStaticNote && Same(a.LastFrameHoldLineLength, b.LastFrameHoldLineLength);
            private static bool Same(SeekHoldControlValues a, SeekHoldControlValues b)
                => Same(a.NowTime, b.NowTime) && Same(a.Speed, b.Speed) && Same(a.HoldLineLength, b.HoldLineLength);
            public SeekHoldNoteTarget ReadHold(int ordinal) => ObserveSource<SeekHoldNoteTarget>(() =>
            {
                VerifyCurrent();
                if ((uint)ordinal >= (uint)_ordinals.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
                var c = _core; var sourceOrdinal = _ordinals[ordinal]; c.VerifyNote(sourceOrdinal);
                var note = c._notes[sourceOrdinal]; var activeField = CurrentField(_noteFields[0]); var lengthField = CurrentField(_noteFields[1]);
                var active = c.Native(() => c._owner._api.ReadExactField<bool>(note, activeField));
                var length = c.Native(() => c._owner._api.ReadExactField<float>(note, lengthField));
                c.VerifyNote(sourceOrdinal); VerifyCurrent();
                return new(_holds[ordinal].Reference, active, length);
            });
            public SeekHoldControlValues ReadControl() => ObserveSource<SeekHoldControlValues>(() =>
            {
                VerifyCurrent(); var c = _core; var values = new float[3];
                for (var i = 0; i < values.Length; ++i)
                {
                    var field = CurrentField(_controlFields[i]); values[i] = c.Native(() => c._owner._api.ReadExactField<float>(_hold, field));
                }
                VerifyCurrent(); return new(values[0], values[1], values[2]);
            });
            private SeekHoldRawState ReadSnapshotOnce()
            {
                VerifySource(); var lists = ReadLists(); var control = ReadControl(); var notes = new SeekHoldNoteTarget[_holds.Count];
                for (var i = 0; i < notes.Length; ++i) notes[i] = ReadHold(i);
                VerifySource(); return new(_identity, lists, control, Array.AsReadOnly(notes));
            }
            public SeekHoldRawState ReadSnapshot() => ObserveSource(() =>
            {
                var first = ReadSnapshotOnce(); var second = ReadSnapshotOnce();
                if (first.Identity != second.Identity || !Same(first.Lists, second.Lists) || !Same(first.Control, second.Control)
                    || first.Notes.Count != second.Notes.Count
                    || first.Notes.Where((note, ordinal) => !Same(note, second.Notes[ordinal])).Any())
                    throw SourceChanged("Actual Hold Lists and scalars changed between complete observations.");
                VerifyLocal(); return second;
            });
            public void RequestDispose() { VerifyOwnerThread(); _withdrawn = true; _core.RequestDispose(); }
            public void Dispose()
            {
                VerifyOwnerThread(); _withdrawn = true;
                if (!_released)
                {
                    _released = true;
                    try { _core.Dispose(); } catch (Exception error) { _cleanup.Add(error); }
                }
                if (_cleanup.Count == 0) return;
                var failure = new AggregateException("Hold state source retains unknown cleanup responsibility without retry.", _cleanup);
                failure.Data["SeekHoldStateCleanupOwner"] = this; throw failure;
            }
        }
    }
}
