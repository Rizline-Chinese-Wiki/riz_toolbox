using System.Diagnostics;
using System.Reflection;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static class NativeSeekNoteFlagsChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Note flags map shuffled explicit true/false targets and copy caller input", ExplicitTargets),
        ("Note flags keep distinct runtime instances sharing chart data", DistinctInstances),
        ("Note flags allow partial chart coverage and independent caller lifetime", PartialAndLifetime),
        ("Note flags reject missing extra duplicate and zero identities before setters", InvalidCoverage),
        ("Note flags restore the attempted multi-note prefix after before/after-store faults", PartialWrites),
        ("Note flags never restore across an externally changed unattempted flag", UnattemptedForeignValue),
        ("Note flags preserve a previously restored flag changed by another writer", RestoredForeignValue),
        ("Note flags selected-slot read protects a write-time foreign value", ConditionalSetter),
        ("Note flags Dispose callbacks close the owned view and defer writer roots", CallbackDispose),
        ("Note flags reject participant reentry without disrupting outer commit", ParticipantReentry),
        ("Note flags same-RunValue constructor reentry rejects and gate resets", PrepareReentry),
        ("Note flags owner-thread checks precede every synthetic export", WrongThread),
        ("Note flags require ready-paused and unchanged runtime note identities", SourceChanges),
        ("Note flags reject invalid official Boolean storage metadata", InvalidMetadata),
        ("Note flags reject dead Unity wrappers independently of destroyed Boolean", DeadNotes),
        ("Note flags retain unknown native-free owner and never retry", UnknownFree),
        ("Note flags empty actual collection is a zero-setter transaction", EmptyActual),
        ("Note flags full snapshots remain constant and selected reads scale linearly", LinearAccess)
    };
    private sealed record Responsibility(FakeNative Storage, NativeSeekNoteFlagsParticipant Participant, Exception Failure);
    private static readonly List<Responsibility> UnknownOwners = new();
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static object Field(object owner, string name)
        => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object[] Items(object collection) => ((System.Collections.IEnumerable)collection).Cast<object>().ToArray();
    private static bool InAccess(string method)
        => new StackTrace(false).GetFrames().Any(frame => frame.GetMethod() is { } member
            && member.Name == method && member.DeclaringType?.Name == "NativeSeekNoteFlagsAccess");
    private sealed record Calls(int Reads, int Invokes, int Resolutions, int Roots, int Writes)
    {
        internal static Calls Read(FakeNative n) => new(n.FieldReads, n.Invocations, n.NoteViewMethodResolutions, n.NewRoots, n.FieldWrites);
    }
    private sealed record Publications(int Calls, int Roots, int Arguments, int Unconfirmed)
    {
        internal static Publications Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
            NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend;
        internal readonly JudgementRunCapture Run;
        internal readonly SeekNoteFlagTarget[] Baseline;
        internal SeekNoteFlagTarget[] Targets => Baseline.Select(row => row with
            { IsActive = !row.IsActive, Destroyed = !row.Destroyed, Missed = !row.Missed }).ToArray();
        private readonly List<NativeSeekNoteFlagsParticipant> _participants = new();
        private readonly HashSet<NativeSeekNoteFlagsParticipant> _unknown = new();
        private readonly Publications _publications = Publications.Read();
        internal Fixture(FakeNative.NoteViewShape shape = FakeNative.NoteViewShape.Complete, bool empty = false)
        {
            Native.ConfigureNoteFlags(shape); if (empty) Native.FlagEmptyRuntime(); Baseline = Native.FlagValues();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-note-flags-only", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State);
            Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No actual RunValue was captured.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5, "The test must use the actual retained RunValue.");
        }
        internal NativeSeekNoteFlagsParticipant Prepare(IReadOnlyList<SeekNoteFlagTarget>? targets = null, ISeekChartNativeProfile? profile = null)
        {
            var value = Run.PrepareSeekNoteFlags(profile ?? Native.SeekProfile, targets ?? Targets);
            _participants.Add(value); return value;
        }
        internal void ClearCallbacks()
        {
            Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.AfterFlagMetadata = null; Native.BeforeFlagStore = null; Native.AfterFlagStore = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null;
        }
        internal void RetainUnknown(NativeSeekNoteFlagsParticipant participant, Exception failure)
        { _unknown.Add(participant); UnknownOwners.Add(new(Native, participant, failure)); }
        public void Dispose()
        {
            ClearCallbacks();
            foreach (var participant in _participants.AsEnumerable().Reverse())
            {
                if (_unknown.Contains(participant))
                {
                    var attempts = Native.FreeHandleAttempts; Expect<AggregateException>(participant.Dispose);
                    Check(attempts == Native.FreeHandleAttempts, "Cleanup retried an unknown native free.");
                }
                else participant.Dispose();
            }
            Run.Dispose(); Backend.Dispose();
            Check(Native.ActiveRoots == 0 && Native.NewRoots == Native.FreedRoots
                && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "An actual native responsibility escaped cleanup or was freed twice.");
            Check(Native.AllGameplayReceiversRooted && Native.AllArrayReadsRooted && Native.FlagOwnersRooted,
                "A source/array/Boolean setter receiver was used without an owned root.");
            Check(Native.FlagFullGraphReadsInsideSelected == 0, "A selected-note read/write scanned the complete owned chart instead of using fixed source guards.");
            Check(Native.FieldWrites == Native.FlagWrites.Count && Native.ObjectFieldWrites == 0
                && Native.FlagViolations.Count == 0 && Native.NoteViewDispatchViolations.Count == 0
                && Native.FlagOtherStorageUnchanged() && Native.CursorWriteLog.Count == 0
                && Native.GameplayCommandCalls == 0 && Native.GameplayPitchWrites == 0 && Native.SeekConversions == 0,
                "An unselected/derived field, reference, fingerIds/renderer, cursor, command or conversion was changed/dispatched.");
            Check(Publications.Read() == _publications, "A flags-only operation left an unaccounted publication owner.");
            if (_unknown.Count == 0) Native.Dispose();
        }
    }
    private static void Restored(Fixture f, NativeSeekNoteFlagsParticipant value)
    {
        value.Restore(); Check(value.ConfirmRestored().OriginalStateRestored && !value.SideEffectsUnknown
            && f.Native.FlagValues().SequenceEqual(f.Baseline), "Independent readback did not restore every original per-note Boolean.");
    }
    private static void ExplicitTargets()
    {
        using var f = new Fixture(); var expected = f.Targets; var caller = expected.Reverse().ToList(); var p = f.Prepare(caller);
        caller.Clear(); caller.Add(new(0, false, false, false)); p.VerifyPrepared(); p.Commit();
        Check(p.AttemptedWrites == expected.Length * 3 && f.Native.FlagValues().SequenceEqual(expected)
            && expected.Any(row => row.IsActive || row.Destroyed || row.Missed) && p.Confirm().Observed == true,
            "Shuffled/caller-mutated explicit targets were reduced to clearing or matched to the wrong runtime note.");
        Check(f.Native.FlagWrites.Select(row => row.Note * 3 + row.Slot).SequenceEqual(Enumerable.Range(0, expected.Length * 3))
            && f.Native.FlagWrites.All(row => row.Owner == f.Native.FlagNotes[row.Note]), "Native setter order or actual note receiver differs.");
        var committed = f.Native.FlagWrites.Count; Restored(f, p);
        Check(f.Native.FlagWrites.Skip(committed).Select(row => row.Note * 3 + row.Slot)
            .SequenceEqual(Enumerable.Range(0, expected.Length * 3).Reverse()), "Restoration did not reverse the actual attempted note/field order.");
    }
    private static void DistinctInstances()
    {
        using var f = new Fixture(FakeNative.NoteViewShape.MultipleSegments); var expected = f.Targets; var p = f.Prepare(expected.Reverse().ToArray());
        Check(f.Native.FlagNotes.Count == 6 && expected[0].Reference != expected[^1].Reference
            && expected[0].IsActive != expected[^1].IsActive, "Synthetic alias-location instances must remain distinct.");
        p.Commit(); Check(f.Native.FlagValues().SequenceEqual(expected) && p.Confirm().Observed == true,
            "Distinct runtime references sharing one chart Note were collapsed."); Restored(f, p);
    }
    private static void PartialAndLifetime()
    {
        using var f = new Fixture(FakeNative.NoteViewShape.Partial); var p = f.Prepare(); f.Run.Dispose();
        Check(f.Native.FlagNotes.Count == 4, "Partial chart fixture must expose only four actual runtime notes.");
        p.Commit(); Check(p.AttemptedWrites == 12 && p.Confirm().Observed == true, "Partial coverage or caller disposal revoked independent flag receivers.");
        Restored(f, p); p.Dispose(); Check(f.Native.ActiveRoots == 0, "Independent flag roots outlived all known owners.");
    }
    private static void InvalidCoverage()
    {
        using var f = new Fixture(); var good = f.Targets;
        var invalid = new[]
        {
            good[..^1], good.Append(new SeekNoteFlagTarget(long.MaxValue, true, true, true)).ToArray(),
            good.Select((row, index) => index == 0 ? row with { Reference = long.MaxValue } : row).ToArray(),
            good.Select((row, index) => index == 1 ? good[0] : row).ToArray(),
            good.Select((row, index) => index == 0 ? row with { Reference = 0 } : row).ToArray()
        };
        foreach (var rows in invalid)
        {
            var roots = f.Native.ActiveRoots; Expect<InvalidDataException>(() => f.Prepare(rows));
            Check(f.Native.FlagWrites.Count == 0 && f.Native.ActiveRoots == roots,
                "Invalid complete target coverage entered a setter or leaked its partial capture roots.");
        }
        using var retry = f.Prepare(); retry.VerifyPrepared();
    }
    private static void PartialWrites()
    {
        foreach (var afterStore in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare();
            if (afterStore) f.Native.FailFlagAfterStore = 5; else f.Native.FailFlagBeforeStore = 5;
            Expect<IOException>(p.Commit);
            Check(p.AttemptedWrites == 5 && p.SideEffectsUnknown && p.Failure is not null
                && f.Native.FlagWrites.Count == 5 && f.Native.FlagWrites[4].Stored == afterStore,
                "A cross-note partial setter fault lost its conservative attempted prefix.");
            f.Native.FailFlagBeforeStore = f.Native.FailFlagAfterStore = 0; Restored(f, p);
            var restored = f.Native.FlagWrites.Skip(5).Select(row => row.Note * 3 + row.Slot);
            Check(restored.SequenceEqual(Enumerable.Range(0, afterStore ? 5 : 4).Reverse()),
                "Partial restoration wrote an unattempted flag or did not reverse the stored prefix.");
        }
    }
    private static void UnattemptedForeignValue()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Native.FailFlagAfterStore = 4; Expect<IOException>(p.Commit);
        f.Native.FailFlagAfterStore = 0;
        var last = f.Baseline.Length - 1; var foreign = !f.Baseline[last].Missed; f.Native.FlagForeignValue(last, 2, foreign);
        var writes = f.Native.FlagWrites.Count; p.Restore();
        Check(f.Native.FlagWrites.Count == writes && f.Native.FlagValue(last, 2) == foreign
            && !p.ConfirmRestored().OriginalStateRestored && p.SideEffectsUnknown,
            "Restoration overwrote or passed an unattempted externally changed Boolean. Equal samples cannot exclude ABA.");
    }
    private static void RestoredForeignValue()
    {
        using var f = new Fixture(); var p = f.Prepare(); p.Commit(); var committed = f.Native.FlagWrites.Count;
        var last = f.Baseline.Length - 1; var changed = false;
        f.Native.AfterFlagStore = row =>
        {
            if (changed || row.Ordinal != committed + 2) return;
            changed = true; f.Native.FlagForeignValue(last, 2, !f.Baseline[last].Missed);
        };
        p.Restore(); f.ClearCallbacks();
        Check(changed && f.Native.FlagValue(last, 2) != f.Baseline[last].Missed
            && f.Native.FlagWrites.Skip(committed).Count(row => row.Note == last && row.Slot == 2) == 1
            && !p.ConfirmRestored().OriginalStateRestored && p.SideEffectsUnknown,
            "A flag changed by another writer after its restoration was overwritten or falsely confirmed.");
    }
    private static void ConditionalSetter()
    {
        using var f = new Fixture(); var p = f.Prepare(); var fired = false;
        f.Native.AfterNoteViewFieldRead = (name, owner) =>
        {
            if (fired || name != "loadOver" || owner != f.Native.GameplayInformation || !InAccess("Write")) return;
            fired = true; f.Native.FlagForeignValue(0, 0, !f.Baseline[0].IsActive);
        };
        Expect<InvalidOperationException>(p.Commit); f.ClearCallbacks();
        Check(fired && p.AttemptedWrites == 0 && f.Native.FlagWrites.Count == 0
            && f.Native.FlagValue(0, 0) != f.Baseline[0].IsActive,
            "The selected Boolean changed during source verification but reached a setter despite its expectedCurrent check.");
    }
    private static void CallbackDispose()
    {
        foreach (var setter in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); f.Run.Dispose(); var fired = false;
            var owned = (NativeSeekRuntimeNoteView)Field(Field(p, "_access"), "_view");
            Calls? calls = null;
            void DisposeInside()
            {
                if (fired) return; fired = true; p.Dispose();
                Expect<ObjectDisposedException>(owned.VerifySource);
                Check(f.Native.NoteViewHandlesFor(f.Native.FlagNotes[0]).Length >= 1,
                    "In-flight writer receiver roots released before their setter/getter returned.");
                calls = Calls.Read(f.Native);
            }
            if (setter) f.Native.AfterFlagStore = _ => DisposeInside();
            else f.Native.AfterNoteViewFieldRead = (name, owner) =>
            { if (name == "isActive" && owner == f.Native.FlagNotes[0]) DisposeInside(); };
            Expect<ObjectDisposedException>(p.Commit); f.ClearCallbacks();
            Check(fired && calls == Calls.Read(f.Native) && f.Native.ActiveRoots == 0
                && p.AttemptedWrites == (setter ? 1 : 0) && f.Native.FlagWrites.Count == (setter ? 1 : 0),
                "Dispose callback crossed the next high-level API or lost its attempted setter/deferred cleanup state.");
        }
    }
    private static void ParticipantReentry()
    {
        using var f = new Fixture(); var p = f.Prepare(); var fired = false;
        f.Native.AfterFlagStore = _ =>
        {
            if (fired) return; fired = true; var before = Calls.Read(f.Native);
            Expect<InvalidOperationException>(p.Commit); Expect<InvalidOperationException>(p.Restore);
            Expect<InvalidOperationException>(p.VerifyPrepared);
            Check(before == Calls.Read(f.Native), "A participant callback reentry reached native getters/setters.");
        };
        p.Commit(); f.ClearCallbacks(); Check(fired && p.Confirm().Observed == true, "Rejected reentry disrupted the admitted outer commit."); Restored(f, p);
    }
    private sealed class CallbackProfile : ISeekChartNativeProfile
    {
        private readonly ISeekChartNativeProfile _basis;
        internal Action? OnAccess;
        internal CallbackProfile(ISeekChartNativeProfile basis) => _basis = basis;
        public IntPtr ChartClass { get { OnAccess?.Invoke(); return _basis.ChartClass; } }
        public IntPtr ProgressClass { get { OnAccess?.Invoke(); return _basis.ProgressClass; } }
        public void VerifyCurrent() { OnAccess?.Invoke(); _basis.VerifyCurrent(); }
    }
    private static void PrepareReentry()
    {
        using var f = new Fixture();
        foreach (var metadata in new[] { false, true })
        {
            var fired = false;
            void Nested()
            {
                if (fired) return; fired = true; var before = Calls.Read(f.Native);
                var error = Expect<InvalidOperationException>(() => f.Run.PrepareSeekNoteFlags(f.Native.SeekProfile, f.Targets));
                Check(error.Message.Contains("reenter", StringComparison.OrdinalIgnoreCase) && before == Calls.Read(f.Native),
                    "Same-RunValue constructor/profile/metadata reentry reached native work or failed for an unrelated reason.");
            }
            var profile = new CallbackProfile(f.Native.SeekProfile);
            if (metadata) f.Native.AfterFlagMetadata = _ => Nested(); else profile.OnAccess = Nested;
            using var outer = f.Prepare(profile: profile); f.ClearCallbacks(); profile.OnAccess = null;
            Check(fired, "The actual constructor reentry callback did not fire."); outer.VerifyPrepared();
            using var later = f.Prepare(); later.VerifyPrepared();
        }
        var failedProfile = new CallbackProfile(f.Native.SeekProfile) { OnAccess = () => throw new IOException("Synthetic profile constructor failure.") };
        var roots = f.Native.ActiveRoots; Expect<IOException>(() => f.Prepare(profile: failedProfile));
        Check(f.Native.ActiveRoots == roots, "Failed accessor construction left retained native roots.");
        using var retry = f.Prepare(); retry.VerifyPrepared();
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Native.BeginLowerIl2CppAudit();
        try
        {
            Task.Run(() =>
            {
                Expect<InvalidOperationException>(p.VerifyPrepared); Expect<InvalidOperationException>(p.Commit);
                Expect<InvalidOperationException>(p.Restore); Expect<InvalidOperationException>(() => p.Confirm());
                Expect<InvalidOperationException>(p.Dispose);
                Expect<InvalidOperationException>(() => f.Run.PrepareSeekNoteFlags(f.Native.SeekProfile, f.Targets));
            }).GetAwaiter().GetResult();
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0,
            "Wrong-thread note-flags admission performed a synthetic export.");
        p.Commit(); Check(p.Confirm().Observed == true, "Wrong-thread rejection consumed the owner-thread preparation."); Restored(f, p);
    }
    private static void SourceChanges()
    {
        using (var f = new Fixture())
        {
            f.Native.FlagForeignSource("pause.paused", false); var roots = f.Native.ActiveRoots;
            Expect<InvalidOperationException>(() => f.Prepare());
            Check(f.Native.FlagWrites.Count == 0 && f.Native.ActiveRoots == roots, "A non-paused source admitted writes or leaked preparation roots.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Native.NoteViewSwapAllSlots(); Expect<InvalidOperationException>(p.Commit);
            Check(p.AttemptedWrites == 0 && f.Native.FlagWrites.Count == 0, "Changed actual runtime-reference storage reached a first setter.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.State.ObservePause(true); var before = Calls.Read(f.Native); Expect<InvalidOperationException>(p.Commit);
            Check(before == Calls.Read(f.Native) && p.AttemptedWrites == 0, "An expired run revision reached native getters/setters.");
        }
    }
    private static void InvalidMetadata()
    {
        foreach (var fault in new[] { FakeNative.FlagMetadataFault.Static, FakeNative.FlagMetadataFault.WrongWidth,
                     FakeNative.FlagMetadataFault.WrongOffset })
        {
            using var f = new Fixture(); f.Native.FlagChangeMetadata(1, fault); var roots = f.Native.ActiveRoots;
            Expect<UnsupportedSignatureException>(() => f.Prepare());
            Check(f.Native.FlagWrites.Count == 0 && f.Native.ActiveRoots == roots, "Invalid official Boolean storage reached a setter or leaked roots: " + fault);
        }
    }
    private static void DeadNotes()
    {
        using var f = new Fixture(); var roots = f.Native.ActiveRoots;
        f.Native.DeadFlagNote = f.Native.FlagNotes[2]; Expect<InvalidOperationException>(() => f.Prepare());
        Check(f.Native.FlagWrites.Count == 0 && f.Native.ActiveRoots == roots, "A dead Unity wrapper was admitted during preparation.");
        f.Native.DeadFlagNote = IntPtr.Zero; var p = f.Prepare(); f.Native.DeadFlagNote = f.Native.FlagNotes[0];
        Expect<InvalidOperationException>(p.Commit);
        Check(p.AttemptedWrites == 0 && f.Native.FlagWrites.Count == 0,
            "A note that died after preparation reached a setter; destroyed Boolean and Unity wrapper liveness are separate facts.");
    }
    private static void UnknownFree()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Run.Dispose(); var target = f.Native.FlagNotes[0];
        var handle = f.Native.NoteViewHandlesFor(target).Last(); f.Native.FailFreeHandle = handle;
        var error = Expect<AggregateException>(p.Dispose); f.RetainUnknown(p, error);
        var access = Field(p, "_access"); var roots = Items(Field(access, "_roots")); var unknown = Items(Field(access, "_unknownFrees"));
        var addresses = (System.Collections.IDictionary)Field(access, "_addresses");
        Check(roots.Length == 1 && unknown.Length == 1 && ReferenceEquals(roots[0], unknown[0])
            && (long)addresses[roots[0]]! == target.ToInt64() && p.CleanupFailure is not null,
            "Unknown receiver free lost its exact wrapper, address or cleanup failure.");
        var failures = f.Native.NoteViewFreeLog.Where(row => row.InjectedFailure).ToArray();
        Check(failures.Length == 1 && failures[0].Handle == handle && failures[0].Target == target && f.Native.ActiveRoots == 0,
            "Unknown native free was not the exact selected single handle/target attempt or blocked known cleanup.");
        var original = Items(Field(access, "_cleanupFailures")).Single();
        Check(original is IOException && error.InnerExceptions.Any(inner => ReferenceEquals(inner, original)),
            "Unknown responsibility did not retain its original native-free exception object.");
        var attempts = f.Native.FreeHandleAttempts; Expect<AggregateException>(p.Dispose);
        Check(attempts == f.Native.FreeHandleAttempts, "A later Dispose retried an unknown native free.");
    }
    private static void EmptyActual()
    {
        using var f = new Fixture(empty: true); var p = f.Prepare(Array.Empty<SeekNoteFlagTarget>());
        p.VerifyPrepared(); p.Commit();
        Check(p.AttemptedWrites == 0 && f.Native.FlagWrites.Count == 0 && p.Confirm().Observed == true,
            "An empty actual runtime-note collection did not remain an explicit zero-setter transaction."); Restored(f, p);
    }
    private sealed class CountingAccess : INativeSeekNoteFlagsAccess
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private Action? _guard;
        internal SeekNoteFlagTarget[] Rows;
        internal int FullReads, SelectedReads, Writes;
        private bool _withdrawn;
        internal CountingAccess(int count) => Rows = Enumerable.Range(0, count)
            .Select(index => new SeekNoteFlagTarget(1000 + index, false, true, false)).ToArray();
        public void BindLifetimeGuard(Action guard) { VerifyLocal(); _guard = guard; }
        public void VerifyOwnerThread()
        { if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Counting access owner thread."); }
        public void VerifyLocal()
        { VerifyOwnerThread(); _guard?.Invoke(); if (_withdrawn) throw new ObjectDisposedException(nameof(CountingAccess)); }
        public IReadOnlyList<SeekNoteFlagTarget> Read()
        { VerifyLocal(); ++FullReads; return Array.AsReadOnly(Rows.ToArray()); }
        public SeekNoteFlagTarget ReadNote(int ordinal)
        { VerifyLocal(); ++SelectedReads; return Rows[ordinal] with { }; }
        public void Write(int ordinal, int slot, bool value, bool expectedCurrent, Action beforeDispatch)
        {
            VerifyLocal(); Check(Rows[ordinal].At(slot) == expectedCurrent, "Counting setter expectedCurrent mismatch.");
            beforeDispatch(); VerifyLocal(); ++Writes; Rows[ordinal] = Rows[ordinal].With(slot, value);
        }
        public void RequestDispose() { VerifyOwnerThread(); _withdrawn = true; }
        public void Dispose() { VerifyOwnerThread(); _withdrawn = true; }
    }
    private static void LinearAccess()
    {
        static (int Full, int Selected, int Writes) Measure(int count)
        {
            var access = new CountingAccess(count); var baseline = access.Rows.ToArray();
            var targets = baseline.Select(row => row with { IsActive = true, Destroyed = false, Missed = true }).ToArray();
            using var p = NativeSeekNoteFlagsParticipant.Prepare(access, targets); p.VerifyPrepared(); p.Commit();
            Check(p.Confirm().Observed == true && access.Rows.SequenceEqual(targets), "Counting transaction did not apply explicit targets.");
            p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored && access.Rows.SequenceEqual(baseline), "Counting transaction did not restore its baseline.");
            return (access.FullReads, access.SelectedReads, access.Writes);
        }
        var small = Measure(4); var large = Measure(64);
        Check(large.Full == small.Full && large.Full <= 12 && large.Selected >= 6 * 64
            && large.Selected <= small.Selected * 16 + 16 && large.Writes == 6 * 64,
            "Full snapshots scaled with setter count or selected-note reads exceeded a linear bound.");
    }
}
