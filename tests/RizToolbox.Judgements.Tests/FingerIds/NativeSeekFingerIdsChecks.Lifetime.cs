using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekFingerIdsChecks
{
    private static void ConstructorFailures()
    {
        for (var mode = 0; mode < 3; ++mode)
        {
            using var f = new Fixture(); var roots = f.Native.ActiveRoots;
            if (mode == 0) f.Native.FailFingerAllocation = true;
            else
            {
                void Fault(FakeNative.FingerWrite row)
                {
                    if (row.Operation != "Ctor") return;
                    if (mode == 1) f.Native.FailFingerBeforeStore = row.Ordinal; else f.Native.FailFingerAfterStore = row.Ordinal;
                }
                if (mode == 1) f.Native.BeforeFingerStore = Fault; else f.Native.AfterFingerStore = Fault;
            }
            if (mode == 0) Expect<OutOfMemoryException>(() => f.Prepare()); else Expect<IOException>(() => f.Prepare());
            f.ClearCallbacks();
            Check(f.Native.ActiveRoots == roots && f.Native.FingerStorageEquals(f.Baseline)
                && !f.Native.FingerWrites.Any(row => row.Operation == "fingerIds"),
                "Allocation/constructor failure published a field or leaked an unrecorded newly allocated List root.");
            using var retry = f.Prepare(); retry.VerifyPrepared();
        }
    }
    private static void PublicationCancellation()
    {
        using var f = new Fixture(); var before = PublicationOwners(); var roots = f.Native.ActiveRoots; var fired = false;
        f.Native.AfterFingerRoot = (_, target) =>
        {
            if (fired || !f.Native.FingerCreatedLists.Contains(target) || !InMethod("NewExactCore", "NativeRuntime")) return;
            fired = true; f.State.ObservePause(true);
        };
        var error = Expect<InvalidOperationException>(() => f.Prepare()); f.ClearCallbacks();
        var retained = PublicationOwners().Except(before).ToArray();
        Check(fired && f.Native.FingerStorageEquals(f.Baseline) && !f.Native.FingerWrites.Any(row => row.Operation == "fingerIds"),
            "A constructor publication callback withdrew its run but a scratch List reached a live note field.");
        if (retained.Length != 0)
        {
            Check(retained.Length == 1 && (bool)Field(retained[0], "_publicationFailed"), "Constructor publication failure retained an unrelated or unbounded owner.");
            f.RetainPublication(retained[0], error, f.Native.ActiveRoots - roots);
        }
        else Check(f.Native.ActiveRoots == roots, "Failed publication lost native object responsibility without a retained owner.");
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
    private static void Reentry()
    {
        using var f = new Fixture();
        foreach (var metadata in new[] { false, true })
        {
            var fired = false;
            void Nested()
            {
                if (fired) return; fired = true; var calls = Calls.Read(f.Native);
                var error = Expect<InvalidOperationException>(() => f.Run.PrepareSeekFingerIds(f.Native.SeekProfile, f.Targets));
                Check(error.Message.Contains("reenter", StringComparison.OrdinalIgnoreCase) && calls == Calls.Read(f.Native), "Same-RunValue fingerIds constructor reentry reached native work.");
            }
            var profile = new CallbackProfile(f.Native.SeekProfile);
            if (metadata) f.Native.AfterFingerMetadata = (_, _) => Nested(); else profile.OnAccess = Nested;
            using var p = f.Prepare(profile: profile); f.ClearCallbacks(); profile.OnAccess = null;
            Check(fired, "The constructor reentry callback did not fire."); p.VerifyPrepared();
        }
        var p2 = f.Prepare(); var callback = false;
        f.Native.AfterFingerStore = _ =>
        {
            if (callback) return; callback = true; var calls = Calls.Read(f.Native);
            Expect<InvalidOperationException>(p2.Commit); Expect<InvalidOperationException>(p2.Restore);
            Expect<InvalidOperationException>(p2.VerifyPrepared); Expect<InvalidOperationException>(() => p2.Confirm());
            Check(calls == Calls.Read(f.Native), "A participant reentry reached a native getter/setter.");
        };
        p2.Commit(); f.ClearCallbacks(); Check(callback && p2.Confirm().Observed == true, "Rejected reentry disrupted the admitted outer commit."); Restored(f, p2);
    }
    private static void CallbackDispose()
    {
        foreach (var setter in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); f.Run.Dispose(); var fired = false; Calls? calls = null;
            var ownersBefore = PublicationOwners(); var responsibilityBefore = Publications.Read(); IntPtr? publishedToken = null;
            var owned = (NativeSeekRuntimeNoteView)Field(Field(p, "_access"), "_view");
            void Close(IntPtr receiver)
            {
                if (fired) return; fired = true;
                if (!setter) publishedToken = f.Native.FingerReference(f.Native.FingerNotes.IndexOf(receiver));
                p.Dispose(); Expect<ObjectDisposedException>(owned.VerifySource);
                Check(f.Native.HasRootFor(receiver), "A callback released its still-active writer receiver root."); calls = Calls.Read(f.Native);
            }
            if (setter) f.Native.AfterFingerStore = row => Close(row.Receiver);
            else f.Native.AfterNoteViewFieldRead = (name, owner) => { if (name == "fingerIds") Close(owner); };
            var error = Expect<ObjectDisposedException>(p.Commit); f.ClearCallbacks();
            Check(fired && calls == Calls.Read(f.Native) && f.Native.ActiveRoots == 0 && p.AttemptedWrites == (setter ? 1 : 0),
                "Dispose callback crossed the next high-level native boundary or lost deferred cleanup/attempt responsibility.");
            var retained = PublicationOwners().Except(ownersBefore).ToArray();
            if (!setter)
            {
                // The getter returned before its publisher could accept the
                // nullable token. A null token retains a failed call record,
                // with no invented native root or nonnull output responsibility.
                Check(publishedToken.HasValue && retained.Length == 1, "The interrupted field publication lost its exact bounded call owner.");
                var token = publishedToken ?? throw new InvalidOperationException("No interrupted field token was sampled.");
                var owner = retained[0]; var expected = responsibilityBefore with { Calls = responsibilityBefore.Calls + 1,
                    Unconfirmed = responsibilityBefore.Unconfirmed + (token == IntPtr.Zero ? 0 : 1) };
                Check((bool)Field(owner, "_publicationFailed") && (bool)Field(owner, "_cleanupAttempted")
                    && (IntPtr)Field(owner, "_unconfirmedPublishedToken") == token
                    && Field(owner, "_unconfirmedPublishedRoot") is null && ReferenceEquals(Field(owner, "_operationFailure"), error)
                    && Items(Field(owner, "_roots")).All(value => value is null)
                    && Items(Field(owner, "_arguments")).All(value => value is null)
                    && (int)Field(owner, "_failureCount") == 0 && Publications.Read() == expected,
                    "Interrupted field publication retained an incorrect token, root, exception or cleanup responsibility shape.");
                var frees = f.Native.FreeHandleAttempts; Expect<InvalidOperationException>(((RizToolbox.Runtime.NativePublicationCall)owner).Dispose);
                Check(f.Native.FreeHandleAttempts == frees && Publications.Read() == expected, "Retained failed publication cleanup was retried or its quota record was lost.");
                f.RetainPublication(owner, error);
            }
            else Check(retained.Length == 0 && Publications.Read() == responsibilityBefore,
                "A void setter callback invented an unconfirmed reference-publication owner.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); p.Dispose(); Check(ActiveEquals(f.Native, f.Targets), "Dispose silently undid committed input targets.");
        }
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
                Expect<InvalidOperationException>(() => p.ConfirmRestored()); Expect<InvalidOperationException>(p.Dispose);
                Expect<InvalidOperationException>(() => f.Run.PrepareSeekFingerIds(f.Native.SeekProfile, f.Targets));
            }).GetAwaiter().GetResult();
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0, "Worker fingerIds admission reached an IL2CPP export.");
        p.Commit(); Check(p.Confirm().Observed == true, "Worker rejection consumed owner-thread preparation."); Restored(f, p);
    }
    private static void UnknownFree()
    {
        using var f = new Fixture(); var p = f.Prepare(); f.Run.Dispose(); var target = f.Native.FingerNotes[1];
        var handle = f.Native.NoteViewHandlesFor(target).Last(); f.Native.FailFreeHandle = handle;
        var error = Expect<AggregateException>(p.Dispose); f.RetainUnknown(p, error); var access = Field(p, "_access");
        var roots = Items(Field(access, "_roots")); var unknown = Items(Field(access, "_unknownFrees"));
        Check(roots.Length == 1 && unknown.Length == 1 && ReferenceEquals(roots[0], unknown[0]) && p.CleanupFailure is not null
            && f.Native.NoteViewFreeLog.Count(row => row.Handle == handle && row.Target == target && row.InjectedFailure) == 1,
            "Unknown accessor free lost the exact retained root wrapper, cleanup evidence or original handle/target.");
        var frees = f.Native.FreeHandleAttempts; Expect<AggregateException>(p.Dispose); Check(f.Native.FreeHandleAttempts == frees, "Unknown accessor root was freed twice.");
    }
}
