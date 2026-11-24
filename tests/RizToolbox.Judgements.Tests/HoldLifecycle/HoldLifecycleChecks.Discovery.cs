using System.Text.Json;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class HoldLifecycleChecks
{
    internal static readonly (string Name, Action Run)[] DiscoveryGuardCases =
    {
        ("Hold discovery guard freshly validates exact Type parameter and complete Unity array return", DiscoverySignatures),
        ("Hold discovery guard stops at every observed method metadata export", DiscoveryMetadata),
        ("Hold discovery guard publishes an actual Type before post factory withdrawal", DiscoveryTypeWithdrawal),
        ("Hold discovery guard publishes an actual native result before post invoke withdrawal", DiscoveryResultWithdrawal),
        ("Hold discovery guard retains a throwing native result publisher without retry", DiscoveryThrowingPublisher),
        ("Hold discovery guard reads all1024 ordered null and duplicate reference slots", DiscoveryCompleteArray),
        ("Hold discovery guard stops after an accepted slot withdraws its source", DiscoverySlotWithdrawal),
        ("Hold discovery guard retains an unknown slot root free without retry", DiscoverySlotCleanup),
        ("Hold discovery guard stops actual backend after Type factory withdrawal", DiscoveryBackendType),
        ("Hold discovery guard stops actual backend after candidate invoke withdrawal", DiscoveryBackendResult),
        ("Hold discovery guard stops actual backend after a slot root allocation withdraws it", DiscoveryBackendSlot),
        ("Hold discovery guard keeps the primary backend failure when scan cleanup is unknown", DiscoveryBackendCleanup),
        ("Hold discovery guard checks exceptional native withdrawal before method name reads", DiscoveryExceptionalWithdrawal),
        ("Hold discovery guard admits the actual backend scan at1024 distinct object roots", DiscoveryBackendFullBound),
        ("Hold Start accepts a controller-typed vector returned by the declared Unity Object[] method", DiscoveryControllerVector),
        ("Hold discovery rejects a foreign typed vector before publishing any slot", DiscoveryForeignVector)
    };

    private static (IntPtr Controller, IntPtr Resources, IntPtr Object) DiscoveryClasses(FakeNative n)
        => (n.GameplayApi.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController"),
            n.GameplayApi.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Resources"), AliveGuardObject(n));

    private static NativeRuntime.RootHandle DiscoveryType(FakeNative n)
    {
        NativeRuntime.RootHandle? result = null;
        n.GameplayApi.PublishHoldControllerType(DiscoveryClasses(n).Controller, (_, root) => { result = root; return true; }, () => { });
        return result ?? throw new InvalidOperationException("No actual Type was published.");
    }

    private static void DiscoverySignatures()
    {
        foreach (var fault in Enum.GetValues<FakeNative.HoldDiscoverySignatureFault>())
        {
            using var f = new Fixture(); var n = f.Native; var classes = DiscoveryClasses(n);
            n.GameplayApi.InspectHoldDiscoveryMethod(classes.Resources, classes.Object, () => { });
            n.HoldDiscoveryFaultSignature(fault); n.HoldSourceGuardExportLog.Clear();
            Expect<UnsupportedSignatureException>(() => n.GameplayApi.InspectHoldDiscoveryMethod(classes.Resources, classes.Object, () => { }));
            Check(!n.HoldSourceGuardExportLog.Contains("il2cpp_runtime_invoke"), "Changed discovery metadata reached actual invocation.");
        }
    }

    private static void DiscoveryMetadata()
    {
        string[] exports;
        using (var f = new Fixture())
        {
            var n = f.Native; var classes = DiscoveryClasses(n); n.HoldSourceGuardExportLog.Clear();
            n.GameplayApi.InspectHoldDiscoveryMethod(classes.Resources, classes.Object, () => { });
            exports = n.HoldSourceGuardExportLog.Distinct().ToArray();
            Check(exports.Contains("il2cpp_class_get_element_class") && exports.Contains("il2cpp_class_get_rank")
                && exports.Contains("il2cpp_method_get_class"), "The real discovery signature did not validate its declared array.");
        }
        foreach (var selected in exports)
        {
            using var f = new Fixture(); var n = f.Native; var classes = DiscoveryClasses(n);
            var valid = true; var boundary = -1; var failure = new InvalidOperationException("Expected discovery metadata withdrawal.");
            n.HoldSourceGuardExportLog.Clear();
            n.AfterHoldSourceGuardExport = name =>
            {
                if (name != selected) return;
                n.AfterHoldSourceGuardExport = null; valid = false; boundary = n.HoldSourceGuardExportLog.Count;
            };
            var error = Expect<InvalidOperationException>(() => n.GameplayApi.InspectHoldDiscoveryMethod(classes.Resources, classes.Object,
                () => { if (!valid) throw failure; }));
            n.AfterHoldSourceGuardExport = null;
            Check(!valid && ReferenceEquals(error, failure) && boundary >= 0
                && n.HoldSourceGuardExportLog.Skip(boundary).All(name => name == "il2cpp_free"), "Discovery metadata continued after " + selected);
        }
    }

    private static void DiscoveryTypeWithdrawal() => DiscoveryOutputWithdrawal(false);
    private static void DiscoveryResultWithdrawal() => DiscoveryOutputWithdrawal(true);

    private static void DiscoveryOutputWithdrawal(bool invoke)
    {
        foreach (var transfer in new[] { false, true })
        {
            using var f = new Fixture(); var n = f.Native; var classes = DiscoveryClasses(n);
            using var type = invoke ? DiscoveryType(n) : null;
            var valid = true; var boundary = -1; var published = false; var tokenSeen = IntPtr.Zero;
            NativeRuntime.RootHandle? adopted = null;
            var failure = new InvalidOperationException("Expected discovery output withdrawal.");
            var before = (n.NewRoots, n.FreeHandleAttempts, n.ActiveRoots); n.HoldSourceGuardExportLog.Clear();
            n.AfterHoldSourceGuardExport = name =>
            {
                if (name != (invoke ? "il2cpp_runtime_invoke" : "il2cpp_type_get_object")) return;
                n.AfterHoldSourceGuardExport = null; valid = false; boundary = n.HoldSourceGuardExportLog.Count;
            };
            bool Publish(IntPtr token, NativeRuntime.RootHandle root)
            { published = true; tokenSeen = token; if (transfer) adopted = root; return transfer; }
            void Guard() { if (!valid) throw failure; }
            var error = Expect<InvalidOperationException>(() =>
            {
                if (invoke) n.GameplayApi.PublishHoldControllerCandidates(type!, classes.Resources, classes.Object, Publish, Guard);
                else n.GameplayApi.PublishHoldControllerType(classes.Controller, Publish, Guard);
            });
            n.AfterHoldSourceGuardExport = null;
            Check(!valid && ReferenceEquals(error, failure) && published && tokenSeen != IntPtr.Zero && boundary >= 0
                && n.HoldSourceGuardExportLog.Skip(boundary).Count(name => name == "il2cpp_gchandle_new") == 1
                && n.HoldSourceGuardExportLog.Skip(boundary).All(SourceGuardPublicationExports.Contains)
                && n.NewRoots == before.NewRoots + (invoke ? 2 : 1)
                && n.FreeHandleAttempts == before.FreeHandleAttempts + (invoke ? 1 : 0) + (transfer ? 0 : 1)
                && n.ActiveRoots == before.ActiveRoots + (transfer ? 1 : 0),
                "An actual discovery output was dropped before rooting/transfer or reached later metadata after withdrawal.");
            if (adopted is not null) { Check(adopted.Target == tokenSeen, "Transferred discovery root lost its actual output."); adopted.Dispose(); }
        }
    }

    private static void DiscoveryThrowingPublisher()
    {
        using var f = new Fixture(); var n = f.Native; var classes = DiscoveryClasses(n); using var type = DiscoveryType(n);
        var before = NativePublicationCall.UnconfirmedPublicationCount; var frees = n.FreeHandleAttempts;
        var failure = new IOException("Expected discovery publisher failure."); uint temporary = 0;
        var error = Expect<IOException>(() => n.GameplayApi.PublishHoldControllerCandidates(type, classes.Resources, classes.Object,
            (token, root) => { Check(root.Target == token && token == n.GameplayControllers, "Publisher did not receive the actual rooted array.");
                temporary = n.NoteViewHandlesFor(token).Single(); throw failure; }, () => { }));
        f.RetainStorage = true; UnknownOwners.Add(f);
        Check(ReferenceEquals(error, failure) && temporary != 0 && NativePublicationCall.UnconfirmedPublicationCount == before + 1
            && n.FreeHandleAttempts == frees + 2 && n.NoteViewFreeLog.Count(row => row.Handle == temporary) == 1,
            "A throwing array publisher lost unconfirmed responsibility or repeated/dropped temporary frees.");
    }

    private static void DiscoveryCompleteArray()
    {
        using var f = new Fixture(); var n = f.Native; var cls = DiscoveryClasses(n).Object;
        var values = Enumerable.Range(0, 1024).Select(index => index % 3 == 0 ? IntPtr.Zero : index % 3 == 1 ? n.GameplayController : n.HoldJudge).ToArray();
        n.HoldDiscoverySlots(values); using var array = n.GameplayApi.Root(n.GameplayControllers);
        var arrayClass = n.GameplayApi.ObjectClass(array.Target); var before = n.ActiveRoots;
        var adopted = new Dictionary<IntPtr, NativeRuntime.RootHandle>(); var slots = new List<int>();
        try
        {
            var observed = n.GameplayApi.ReadHoldControllerCandidates(array, arrayClass, cls, (index, token, root) =>
            {
                slots.Add(index); if (adopted.ContainsKey(token)) return false; adopted.Add(token, root); return true;
            }, () => { });
            Check(observed.Length == 1024 && observed.Items.SequenceEqual(values) && adopted.Count == 2
                && slots.SequenceEqual(Enumerable.Range(0, 1024).Where(i => values[i] != IntPtr.Zero))
                && n.ActiveRoots == before + 2 && array.Target == n.GameplayControllers,
                "The complete bounded scan lost nulls, duplicates, order, actual roots or fixed publication capacity.");
        }
        finally { foreach (var root in adopted.Values) root.Dispose(); }
    }

    private static void DiscoverySlotWithdrawal()
    {
        using var f = new Fixture(); var n = f.Native; var cls = DiscoveryClasses(n).Object;
        n.HoldDiscoverySlots(n.GameplayController, n.HoldJudge);
        using var array = n.GameplayApi.Root(n.GameplayControllers); var arrayClass = n.GameplayApi.ObjectClass(array.Target);
        NativeRuntime.RootHandle? adopted = null; var valid = true; var calls = 0; var boundary = -1;
        var failure = new InvalidOperationException("Expected slot publication withdrawal.");
        n.HoldSourceGuardExportLog.Clear();
        var error = Expect<InvalidOperationException>(() => n.GameplayApi.ReadHoldControllerCandidates(array, arrayClass, cls, (_, _, root) =>
        { ++calls; adopted = root; valid = false; boundary = n.HoldSourceGuardExportLog.Count; return true; },
            () => { if (!valid) throw failure; }));
        Check(ReferenceEquals(error, failure) && calls == 1 && adopted is not null && boundary >= 0
            && n.HoldSourceGuardExportLog.Skip(boundary).All(name => name == "il2cpp_gchandle_free"),
            "A withdrawn slot publisher scanned/published a later slot or lost its accepted root.");
        Check(adopted!.Target == n.GameplayController, "An accepted slot root did not survive withdrawal."); adopted.Dispose();
    }

    private static void DiscoverySlotCleanup()
    {
        using var f = new Fixture(); var n = f.Native; var cls = DiscoveryClasses(n).Object;
        using var array = n.GameplayApi.Root(n.GameplayControllers); var arrayClass = n.GameplayApi.ObjectClass(array.Target); uint failed = 0;
        var error = Expect<AggregateException>(() => n.GameplayApi.ReadHoldControllerCandidates(array, arrayClass, cls, (_, token, _) =>
        { failed = n.NoteViewHandlesFor(token).Single(); n.FailFreeHandle = failed; return false; }, () => { }));
        f.RetainStorage = true; UnknownOwners.Add(f);
        Check(failed != 0 && error.Data["NativePublicationCleanupOwner"] is IDisposable
            && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
            "The exact unknown slot-root free was dropped or retried.");
        var attempts = n.FreeHandleAttempts;
        Expect<InvalidOperationException>(() => ((IDisposable)error.Data["NativePublicationCleanupOwner"]!).Dispose());
        Check(n.FreeHandleAttempts == attempts, "Unknown slot cleanup was retried.");
    }

    private static void DiscoveryBackendType() => DiscoveryBackendWithdrawal(0);
    private static void DiscoveryBackendResult() => DiscoveryBackendWithdrawal(1);
    private static void DiscoveryBackendSlot() => DiscoveryBackendWithdrawal(2);

    private static void DiscoveryBackendWithdrawal(int stage)
    {
        using var f = new Fixture(); var n = f.Native; var before = n.NoteViewInvocations; var closed = false; var boundary = -1;
        n.HoldSourceGuardExportLog.Clear();
        n.AfterHoldSourceGuardExport = name =>
        {
            var hit = stage == 0 ? name == "il2cpp_type_get_object" : stage == 1 ? name == "il2cpp_runtime_invoke" && n.NoteViewInvocations == before + 1
                : name == "il2cpp_gchandle_new" && n.NoteViewInvocations == before + 1 && n.NoteViewHandlesFor(n.GameplayController).Any();
            if (!hit) return;
            n.AfterHoldSourceGuardExport = null; closed = true; f.Backend.Dispose(); boundary = n.HoldSourceGuardExportLog.Count;
        };
        var result = f.Start.Invoke(f.StartFrame()); n.AfterHoldSourceGuardExport = null;
        Check(closed && boundary >= 0 && result == unchecked((nuint)n.HoldHandle.ToInt64()) && f.Start.Originals == 1
            && f.Snapshot().Records == 0 && !f.Snapshot().FullCoverageKnown
            && n.HoldSourceGuardExportLog.Skip(boundary).All(SourceGuardPublicationExports.Contains),
            "The actual discovery backend continued native source reads or suppressed original Start after withdrawal.");
        if (stage != 2) Check(n.HoldSourceGuardExportLog.Skip(boundary).Count(name => name == "il2cpp_gchandle_new") == 1,
            "The actual withdrawn Type/array did not acquire exactly one output root.");
    }

    private static void DiscoveryBackendCleanup()
    {
        using var f = new Fixture(); var n = f.Native; uint failed = 0; var typeReturned = false; var closed = false;
        n.AfterHoldSourceGuardExport = name =>
        {
            if (name == "il2cpp_type_get_object") typeReturned = true;
            if (!typeReturned) return;
            if (name == "il2cpp_gchandle_new" && failed == 0) { failed = n.HoldDiscoveryTypeHandles.Single(); n.FailFreeHandle = failed; }
            if (failed == 0 || name != "il2cpp_class_get_methods") return;
            n.AfterHoldSourceGuardExport = null; closed = true; f.Backend.Dispose();
        };
        var error = Expect<Exception>(() => f.Backend.CaptureHoldStart(f.StartFrame()));
        n.AfterHoldSourceGuardExport = null; f.RetainStorage = true; UnknownOwners.Add(f);
        Check(closed && failed != 0 && error.Data["HoldDiscoveryCleanupOwner"] is IDisposable
            && error.Data["HoldDiscoveryCleanupFailure"] is Exception && !ReferenceEquals(error, error.Data["HoldDiscoveryCleanupFailure"])
            && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1 && f.Start.Originals == 0,
            "Unknown scan cleanup masked the primary source failure or lost its exact scan owner.");
        var attempts = n.FreeHandleAttempts;
        var again = Expect<Exception>(() => ((IDisposable)error.Data["HoldDiscoveryCleanupOwner"]!).Dispose());
        Check(ReferenceEquals(again, error.Data["HoldDiscoveryCleanupFailure"]) && n.FreeHandleAttempts == attempts,
            "Unknown scan cleanup changed identity or retried a free.");
    }

    private static void DiscoveryExceptionalWithdrawal()
    {
        using var f = new Fixture(); var n = f.Native; var classes = DiscoveryClasses(n); using var type = DiscoveryType(n);
        n.HoldDiscoveryExceptionalCall = true; var valid = true; var boundary = -1; var published = false;
        var failure = new InvalidOperationException("Expected exceptional native withdrawal.");
        n.HoldSourceGuardExportLog.Clear();
        n.AfterHoldSourceGuardExport = name =>
        {
            if (name != "il2cpp_runtime_invoke") return;
            n.AfterHoldSourceGuardExport = null; valid = false; boundary = n.HoldSourceGuardExportLog.Count;
        };
        var error = Expect<InvalidOperationException>(() => n.GameplayApi.PublishHoldControllerCandidates(type, classes.Resources, classes.Object,
            (_, _) => { published = true; return false; }, () => { if (!valid) throw failure; }));
        n.AfterHoldSourceGuardExport = null;
        Check(!valid && !published && ReferenceEquals(error, failure) && boundary >= 0
            && n.HoldSourceGuardExportLog.Skip(boundary).All(name => name == "il2cpp_gchandle_free"),
            "An exceptional withdrawn invocation read method-name/result metadata or published a successful output.");
    }

    private static void DiscoveryBackendFullBound()
    {
        using var f = new Fixture(); var n = f.Native;
        var others = n.HoldDiscoveryFreshControllers(1023);
        n.HoldDiscoverySlots(others.Append(n.GameplayController).ToArray());
        Check(f.Start.Invoke(f.StartFrame()) == unchecked((nuint)n.HoldHandle.ToInt64()),
            "The actual full bounded discovery suppressed original Start.");
        var snapshot = f.Snapshot();
        Check(snapshot.Records == 1 && snapshot.Rows.Single().Identity.Run.Controller == n.GameplayController.ToInt64()
            && snapshot.Rows.Single().StartReturned && snapshot.InFlight == 0 && !snapshot.Unknown && !snapshot.FullCoverageKnown
            && n.HoldDiscoveryTypeHandles.Length == 0 && !n.NoteViewHandlesFor(n.GameplayControllers).Any()
            && others.All(value => !n.NoteViewHandlesFor(value).Any()) && n.NoteViewHandlesFor(n.GameplayController).Any(),
            "The1024 distinct objects plus Type/array exhausted scan capacity, selected a foreign run or escaped temporary-root cleanup.");
    }

    private static void DiscoveryControllerVector()
    {
        using var f = new Fixture(); var n = f.Native;
        n.HoldTypedDiscoverySlots("NormalModeInLevelController", IntPtr.Zero, n.GameplayController);
        var diagnostics = new HoldLifecycleDiagnostics(f.Log);
        var original = f.Start.Original;
        f.Start.Original = frame =>
        {
            diagnostics.Sample(f.Backend.HoldLifecycle);
            Check(f.Log.HoldSamples.Count == 0, "A pending Start was reported as returned and exited.");
            return original(frame);
        };
        Check(f.Start.Invoke(f.StartFrame()) == unchecked((nuint)n.HoldHandle.ToInt64()), "Start lost its original return.");
        var snapshot = f.Snapshot();
        Check(snapshot.Records == 1 && !snapshot.Unknown && snapshot.Rows.Single().StartReturned
            && snapshot.Rows.Single().Identity.Run.Controller == n.GameplayController.ToInt64()
            && !n.NoteViewHandlesFor(n.GameplayControllers).Any(),
            "The exact returned controller vector was not associated and released.");
        var rootCount = n.NewRoots;
        var exportCount = n.HoldSourceGuardExportLog.Count;
        diagnostics.Sample(f.Backend.HoldLifecycle); diagnostics.Sample(f.Backend.HoldLifecycle);
        Check(f.Log.HoldSamples.Count == 1 && rootCount == n.NewRoots && exportCount == n.HoldSourceGuardExportLog.Count,
            "Sampling a completed Start touched native state or duplicated the first signal.");
        using var start = JsonDocument.Parse(f.Log.HoldSamples[0]);
        var sample = start.RootElement;
        var sequence = snapshot.Rows.Single().Sequence;
        Check(!sample.GetProperty("Fault").GetBoolean() && sample.GetProperty("RunId").GetString() == snapshot.Run!.RunId
            && sample.GetProperty("StartReturnedAndExitedSequence").GetInt64() == sequence
            && sample.GetProperty("NaturalExitSequence").ValueKind == JsonValueKind.Null,
            "The clean Start sample lost the identity or fabricated a natural exit.");
        f.Move.Original = _ => 0;
        Check(f.Move.Invoke(f.MoveFrame()) == 0, "The source lost the original native terminal result.");
        diagnostics.Sample(f.Backend.HoldLifecycle); diagnostics.Sample(f.Backend.HoldLifecycle);
        Check(f.Log.HoldSamples.Count == 2, "The first natural exit was missing or repeated.");
        using var exit = JsonDocument.Parse(f.Log.HoldSamples[1]);
        Check(!exit.RootElement.GetProperty("Fault").GetBoolean()
            && exit.RootElement.GetProperty("NaturalExitSequence").GetInt64() == sequence,
            "A terminal Move did not report the same successfully captured Start.");
    }

    private static void DiscoveryForeignVector()
    {
        using var f = new Fixture(); var n = f.Native;
        n.HoldTypedDiscoverySlots("InLevel.NoteJudgeControl", n.HoldJudge);
        using var array = n.GameplayApi.Root(n.GameplayControllers);
        int published = 0;
        var error = Expect<UnsupportedSignatureException>(() => n.GameplayApi.ReadHoldControllerCandidates(array,
            n.GameplayApi.ObjectClass(array.Target), DiscoveryClasses(n).Object,
            (_, _, _) => { ++published; return false; }, () => { }));
        Check(published == 0 && error.Message.Contains("InLevel.NoteJudgeControl", StringComparison.Ordinal),
            "A foreign typed result published a slot or did not identify the rejected element type.");
    }
}
