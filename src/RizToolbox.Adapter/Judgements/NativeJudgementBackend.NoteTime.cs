using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private static INativeSeekNoteTimeAccess CreateNoteTimeAccess(NativeJudgementBackend owner,
        RunValue run, ISeekChartNativeProfile profile) => NativeSeekNoteFlagsAccess.CreateNoteTimeSource(owner, run, profile);
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        internal static INativeSeekNoteTimeAccess CreateNoteTimeSource(NativeJudgementBackend owner,
            RunValue run, ISeekChartNativeProfile profile)
        {
            try { return new NoteTimeAccess(owner, run, profile); }
            catch (Exception error)
            {
                if (!error.Data.Contains("SeekNoteTimeCleanupFailure") && error.Data["SeekNoteFlagsCleanupFailure"] is Exception cleanup)
                {
                    error.Data["SeekNoteTimeCleanupOwner"] = error.Data["SeekNoteFlagsCleanupOwner"];
                    error.Data["SeekNoteTimeCleanupFailure"] = cleanup;
                }
                throw;
            }
        }
        private sealed class NoteTimeAccess : INativeSeekNoteTimeAccess
        {
            private sealed record Binding(int NoteOrdinal, NativeRuntime.RootHandle Owner, NativeExactReadField Field);
            private readonly NativeSeekNoteFlagsAccess _core;
            private readonly ISeekChartNativeProfile _profile;
            private readonly List<Binding> _bindings = new();
            private readonly IReadOnlyList<SeekNoteTimeNote> _notes;
            private Action? _lifetime;
            private IDisposable? _viewLease;
            private bool _withdrawn, _expired, _released;
            private Exception? _cleanup;
            internal NoteTimeAccess(NativeJudgementBackend owner, RunValue run, ISeekChartNativeProfile profile)
            {
                ArgumentNullException.ThrowIfNull(profile); _profile = profile; _core = new(owner, run, profile);
                try
                {
                    _viewLease = _core._view!.RetainPhysicalSource();
                    _core.BindLifetimeGuard(OwnLifetime); var notes = new List<SeekNoteTimeNote>(); var c = _core;
                    for (var i = 0; i < c._capturedNotes.Length; ++i)
                    {
                        var actual = c._capturedNotes[i];
                        var raw = c._view!.ChartLines[actual.Location.LineOrdinal].Data.Notes[actual.Location.NoteOrdinal];
                        if (raw.Type is < 0 or > 2) throw new InvalidDataException("The retained actual note has an unsupported raw type.");
                        notes.Add(new(actual.Reference, raw.Type == 2));
                        Add(i, "realTime", 0x34);
                        if (raw.Type == 2) Add(i, "holdEndRealTime", 0x38);
                    }
                    _notes = Array.AsReadOnly(notes.ToArray()); VerifyCurrent(true);
                }
                catch (Exception error)
                {
                    try { Dispose(); }
                    catch (Exception cleanup)
                    { error.Data["SeekNoteTimeCleanupOwner"] = this; error.Data["SeekNoteTimeCleanupFailure"] = cleanup; }
                    throw;
                }
            }
            private void Add(int ordinal, string name, int offset)
            { _bindings.Add(new(ordinal, _core._notes[ordinal], Mutable(name, offset))); }
            private NativeExactReadField Mutable(string name, int offset)
            {
                var c = _core;
                var field = c.Native(() => c._owner._api.InspectExactDeclaredReadField(c._owner._note, name, "System.Single", false, offset));
                if (field.IsStatic || field.DeclaringClass != c._owner._note || (field.Flags & (0x10u | 0x20u | 0x40u)) != 0 || field.Size != 4)
                    throw new UnsupportedSignatureException("Note-time writes require exact mutable declared Single storage.");
                return field;
            }
            private NativeExactReadField Current(Binding binding)
            {
                var field = Mutable(binding.Field.Name, binding.Field.Offset);
                if (field != binding.Field) { _expired = true; throw new UnsupportedSignatureException("The selected note-time field descriptor changed."); }
                return field;
            }
            public void VerifyOwnerThread() => _core.VerifyOwnerThread();
            private void OwnLifetime()
            {
                VerifyOwnerThread();
                if (_withdrawn) throw new ObjectDisposedException(nameof(NoteTimeAccess));
                if (_expired) throw new InvalidOperationException("The actual note-time source was observed expired.");
                _lifetime?.Invoke();
            }
            public void VerifyLocal() { OwnLifetime(); _core.VerifyLocal(); }
            public void BindLifetimeGuard(Action guard)
            {
                VerifyLocal(); ArgumentNullException.ThrowIfNull(guard);
                if (_lifetime is not null) throw new InvalidOperationException("The note-time lifetime guard attaches once.");
                _lifetime = guard;
            }
            public IReadOnlyList<SeekNoteTimeNote> Notes { get { VerifyLocal(); return _notes; } }
            private void VerifyCurrent(bool fullGraph = false)
            {
                try
                {
                    VerifyLocal(); var c = _core;
                    c.Native(() => { _profile.VerifyCurrent(); return 0; }); c.VerifyCurrent(fullGraph); VerifyLocal();
                }
                catch { _expired = true; throw; }
            }
            private int ReadOne(int slot)
            {
                if ((uint)slot >= (uint)_bindings.Count) throw new ArgumentOutOfRangeException(nameof(slot));
                var binding = _bindings[slot]; var c = _core; c.VerifyNote(binding.NoteOrdinal);
                var field = Current(binding);
                return BitConverter.SingleToInt32Bits(c.Native(() => c._owner._api.ReadExactField<float>(binding.Owner, field)));
            }
            public IReadOnlyList<int> ReadSnapshot()
            {
                VerifyCurrent(true); var first = Enumerable.Range(0, _bindings.Count).Select(ReadOne).ToArray();
                VerifyCurrent(true); var second = Enumerable.Range(0, _bindings.Count).Select(ReadOne).ToArray();
                VerifyCurrent(true);
                if (!first.SequenceEqual(second)) throw new InvalidOperationException("Actual note-time storage changed between complete observations.");
                return Array.AsReadOnly(second);
            }
            public int ReadSlot(int slot)
            {
                VerifyCurrent(); var first = ReadOne(slot); var second = ReadOne(slot); VerifyCurrent();
                if (first != second) throw new InvalidOperationException("Selected note-time storage changed between observations.");
                return second;
            }
            public void WriteSlot(int slot, int bits, int expectedBits, Action beforeDispatch)
            {
                ArgumentNullException.ThrowIfNull(beforeDispatch); VerifyCurrent();
                if ((uint)slot >= (uint)_bindings.Count) throw new ArgumentOutOfRangeException(nameof(slot));
                var binding = _bindings[slot]; Current(binding);
                if (ReadOne(slot) != expectedBits) throw new InvalidOperationException("The selected note-time scalar changed before its conditional store.");
                var c = _core;
                using var argument = A.Value(BitConverter.Int32BitsToSingle(bits));
                try
                {
                    c._view!.WithRetainedSource(() => c.Native(() =>
                    {
                        c._owner._api.WriteExactGuarded(binding.Owner, c._owner._note, binding.Field.Name, "System.Single", argument,
                            () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); }); return 0;
                    }));
                }
                catch (Exception error)
                {
                    if (error.Data["SeekRuntimeNoteCleanupFailure"] is Exception cleanup) c.RecordViewFailure(cleanup);
                    throw;
                }
            }
            public void RequestDispose() { VerifyOwnerThread(); _withdrawn = true; _core.RequestDispose(); }
            public void Dispose()
            {
                VerifyOwnerThread(); _withdrawn = true;
                if (!_released)
                {
                    _released = true;
                    var lease = _viewLease; _viewLease = null;
                    try { lease?.Dispose(); }
                    catch (Exception error) { _core.RecordViewFailure(error); _cleanup = error; }
                    try { _core.Dispose(); }
                    catch (Exception error) { _cleanup = _cleanup is null ? error : new AggregateException(_cleanup, error); }
                }
                if (_cleanup is null) return;
                var failure = new AggregateException("Note-time source retains unknown cleanup without retry.", _cleanup);
                failure.Data["SeekNoteTimeCleanupOwner"] = this; failure.Data["SeekNoteTimeCleanupFailure"] = _cleanup; throw failure;
            }
        }
    }
}
