using System.Reflection;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class HoldLifecycleChecks
{
    internal static readonly (string Name, Action Run)[] SourceGuardCases =
    {
        ("Hold source guard stops at every observed exact reference metadata export", SourceGuardMetadataExports),
        ("Hold source guard roots an output withdrawn by fieldGet before checking its source", SourceGuardPublishedWithdrawal),
        ("Hold source guard retains an unknown temporary publication free exactly once", SourceGuardPublicationCleanup),
        ("Hold source guard stops actual backend source reads after a field callback disposes it", SourceGuardBackendField),
        ("Hold source guard separates synchronous root target from class read on disposal", SourceGuardSynchronousTarget),
        ("Hold source guard accepts an in flight old Start handle after run retirement", SourceGuardOldStart),
        ("Hold source guard cleans a late root when its original capture closes during allocation", SourceGuardLateRoot),
        ("Hold source guard retains a failed late root cleanup without retry", SourceGuardLateRootUnknown),
        ("Hold source guard keeps an actually transferred output root owned across withdrawal", SourceGuardTransferredRoot),
        ("Hold source guard preserves unconfirmed publication responsibility when its publisher throws", SourceGuardThrowingPublisher),
        ("Hold reads the exact inherited controller field on the canonical Normal receiver", SourceGuardInheritedControllerField),
        ("Hold refuses an exact-looking controller field redeclared by the base or child", SourceGuardControllerRedeclaration),
        ("Hold requires the immediate canonical controller base", SourceGuardControllerBase),
        ("Hold rejects a base instance or forged field declaration before field_get_value", SourceGuardControllerReceiver)
    };

    private static NativeExactReadField InspectControllerInformation(FakeNative native)
    {
        var info = native.GameplayApi.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        return native.GameplayApi.InspectHoldCaptureReferenceField(native.GameplayControllerClass, "LevelInformation",
            "InLevel.LevelInformation", info, 0x28, () => { });
    }

    private static void SourceGuardInheritedControllerField()
    {
        using var fixture = new Fixture(); var native = fixture.Native; var api = native.GameplayApi;
        var field = InspectControllerInformation(native);
        Check(field.DeclaringClass == native.GameplayControllerBaseClass && field.Field == native.GameplayControllerInformationField,
            "The receipt did not retain the exact base declaration.");
        using var receiver = api.Root(native.GameplayController);
        var before = native.FieldReads; var published = false;
        api.PublishHoldCaptureReferenceField(receiver, field, (token, temporary) =>
        {
            Check(token == native.GameplayInformation && temporary.Target == token,
                "The exact derived receiver did not return its rooted controller information.");
            published = true; return false;
        }, () => { });
        Check(published && native.FieldReads == before + 1, "The inherited physical field must be read exactly once.");
    }

    private static void SourceGuardControllerRedeclaration()
    {
        foreach (var onDerivedClass in new[] { false, true })
        {
            using var fixture = new Fixture(); var native = fixture.Native;
            native.AddHoldControllerInformationRedeclaration(onDerivedClass);
            var before = native.FieldReads;
            Expect<UnsupportedSignatureException>(() => InspectControllerInformation(native));
            Check(native.FieldReads == before, "An ambiguous controller field reached storage.");
        }
    }

    private static void SourceGuardControllerBase()
    {
        using var fixture = new Fixture(); var native = fixture.Native;
        native.ReplaceHoldControllerParent();
        var before = native.FieldReads;
        Expect<UnsupportedSignatureException>(() => InspectControllerInformation(native));
        Check(native.FieldReads == before, "A foreign controller base reached storage.");
    }

    private static void SourceGuardControllerReceiver()
    {
        using var fixture = new Fixture(); var native = fixture.Native; var api = native.GameplayApi;
        var field = InspectControllerInformation(native);
        using var wrongReceiver = api.Root(native.CreateHoldControllerBaseObject());
        using var rightReceiver = api.Root(native.GameplayController);
        var before = native.FieldReads;
        bool RejectPublication(IntPtr _, NativeRuntime.RootHandle __)
            => throw new InvalidOperationException("An unverified Hold receiver or field reached publication.");
        Expect<UnsupportedSignatureException>(() => api.PublishHoldCaptureReferenceField(wrongReceiver,
            field, RejectPublication, () => { }));
        Expect<UnsupportedSignatureException>(() => api.PublishHoldCaptureReferenceField(rightReceiver,
            field with { DeclaringClass = native.GameplayControllerClass }, RejectPublication, () => { }));
        Check(native.FieldReads == before, "An unverified Hold receiver or declaration reached field_get_value.");
    }

    private static NativeExactReadField SourceGuardField(FakeNative n, Action guard)
    {
        var info = n.GameplayApi.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        return n.GameplayApi.InspectHoldCaptureReferenceField(n.HoldManagedOwnerClass, "levelInformation",
            "InLevel.LevelInformation", info, 0x20, guard);
    }

    private static readonly HashSet<string> SourceGuardPublicationExports = new(StringComparer.Ordinal)
    { "il2cpp_gchandle_new", "il2cpp_gchandle_get_target", "il2cpp_gchandle_free" };

    private static void SourceGuardMetadataExports()
    {
        foreach (var shape in new[] { "levelInformation", "noteJudgeControl", "LevelInformation", "<>4__this", "holdNoteInformation" })
            SourceGuardMetadataShape(shape);
    }

    private static (IntPtr Declaring, string Type, IntPtr Target, int Offset) SourceGuardShape(FakeNative n, string name)
    {
        var info = n.GameplayApi.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        return name switch
        {
            "levelInformation" => (n.HoldManagedOwnerClass, "InLevel.LevelInformation", info, 0x20),
            "noteJudgeControl" => (info, "InLevel.NoteJudgeControl", n.HoldManagedOwnerClass, 0x58),
            "LevelInformation" => (n.GameplayApi.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController"), "InLevel.LevelInformation", info, 0x28),
            "<>4__this" => (n.HoldIteratorClass, "InLevel.NoteJudgeControl", n.HoldManagedOwnerClass, 0x20),
            "holdNoteInformation" => (n.HoldIteratorClass, "InLevel.NoteInformation", n.GameplayApi.Class("Assembly-CSharp.dll", "InLevel", "NoteInformation"), 0x28),
            _ => throw new InvalidOperationException("Unknown exact Hold source shape.")
        };
    }

    private static void SourceGuardMetadataShape(string shape)
    {
        string[] names;
        using (var baseline = new Fixture())
        {
            // Resolve the caller's expected class before arming the source
            // guard. This lookup is not part of the guarded primitive.
            var input = SourceGuardShape(baseline.Native, shape);
            baseline.Native.HoldSourceGuardExportLog.Clear();
            baseline.Native.GameplayApi.InspectHoldCaptureReferenceField(input.Declaring,
                shape, input.Type, input.Target, input.Offset, () => { });
            names = baseline.Native.HoldSourceGuardExportLog.Distinct().ToArray();
            Check(names.Contains("il2cpp_field_get_parent") && names.Contains("il2cpp_class_instance_size"),
                "The real inspector did not exercise exact declaration and storage metadata.");
            if (shape is "<>4__this" or "holdNoteInformation")
                Check(names.Contains("il2cpp_class_get_nested_types"), "The exact nested iterator path was not observed.");
        }
        foreach (var selected in names)
        {
            using var f = new Fixture(); var n = f.Native;
            var input = SourceGuardShape(n, shape);
            var cancelled = false; var failure = new InvalidOperationException("Expected Hold metadata withdrawal.");
            n.HoldSourceGuardExportLog.Clear();
            n.AfterHoldSourceGuardExport = name =>
            {
                if (name != selected) return;
                n.AfterHoldSourceGuardExport = null; cancelled = true;
            };
            var error = Expect<InvalidOperationException>(() => n.GameplayApi.InspectHoldCaptureReferenceField(
                input.Declaring, shape, input.Type, input.Target, input.Offset,
                () => { if (cancelled) throw failure; }));
            n.AfterHoldSourceGuardExport = null;
            Check(cancelled && ReferenceEquals(error, failure) && n.HoldSourceGuardExportLog.Last() == selected,
                "Metadata inspection continued after withdrawal at " + shape + ":" + selected);
        }
    }

    private static void SourceGuardPublishedWithdrawal()
    {
        using var f = new Fixture(); var n = f.Native; var field = SourceGuardField(n, () => { });
        using var receiver = n.GameplayApi.Root(n.HoldJudge); using var originalInfo = n.GameplayApi.Root(n.GameplayInformation);
        var before = (n.NewRoots, n.FreeHandleAttempts, n.FieldReads); var valid = true; var published = false; var boundary = -1;
        var failure = new InvalidOperationException("Expected publication source withdrawal.");
        n.HoldSourceGuardExportLog.Clear();
        n.AfterHoldSourceGuardExport = name =>
        {
            if (name != "il2cpp_field_get_value") return;
            n.AfterHoldSourceGuardExport = null; valid = false; boundary = n.HoldSourceGuardExportLog.Count;
        };
        var error = Expect<InvalidOperationException>(() => n.GameplayApi.PublishHoldCaptureReferenceField(receiver, field,
            (token, temporary) =>
            {
                published = true;
                Check(token == n.GameplayInformation && temporary.Target == originalInfo.Target,
                    "The withdrawn output was lost before an actual temporary root existed.");
                return false; // The test already owns this exact information root.
            }, () => { if (!valid) throw failure; }));
        n.AfterHoldSourceGuardExport = null;
        Check(ReferenceEquals(error, failure) && published && boundary >= 0
            && n.NewRoots == before.NewRoots + 1 && n.FreeHandleAttempts == before.FreeHandleAttempts + 1
            && n.FieldReads == before.FieldReads + 1
            && n.HoldSourceGuardExportLog.Skip(boundary).All(SourceGuardPublicationExports.Contains),
            "A withdrawn output leaked, repeated a field read or reached later metadata/class reads.");
    }

    private static void SourceGuardPublicationCleanup()
    {
        using var f = new Fixture(); var n = f.Native; var field = SourceGuardField(n, () => { });
        using var receiver = n.GameplayApi.Root(n.HoldJudge); using var info = n.GameplayApi.Root(n.GameplayInformation);
        var valid = true; uint failed = 0;
        n.AfterHoldSourceGuardExport = name =>
        {
            if (name == "il2cpp_field_get_value") valid = false;
            if (valid || name != "il2cpp_gchandle_new") return;
            n.AfterHoldSourceGuardExport = null;
            failed = n.NoteViewHandlesFor(n.GameplayInformation).Max(); n.FailFreeHandle = failed;
        };
        var error = Expect<AggregateException>(() => n.GameplayApi.PublishHoldCaptureReferenceField(receiver, field,
            (_, _) => false, () => { if (!valid) throw new InvalidOperationException("Expected publication withdrawal."); }));
        n.AfterHoldSourceGuardExport = null; f.RetainStorage = true; UnknownOwners.Add(f);
        Check(failed != 0 && error.Data["NativePublicationCleanupOwner"] is not null
            && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
            "Unknown temporary publication cleanup lost its exact retained owner/free attempt.");
        var attempts = n.FreeHandleAttempts; f.Backend.TickHoldLifecycle(); f.Backend.Dispose(); f.Backend.Dispose();
        Check(n.FreeHandleAttempts == attempts, "Backend withdrawal retried the unknown temporary publication free.");
    }

    private static void SourceGuardBackendField()
    {
        using var f = new Fixture(); var n = f.Native; var closed = false; var boundary = -1;
        n.HoldSourceGuardExportLog.Clear();
        n.AfterHoldSourceGuardExport = name =>
        {
            if (name != "il2cpp_field_get_value" || closed) return;
            n.AfterHoldSourceGuardExport = null; closed = true; f.Backend.Dispose(); boundary = n.HoldSourceGuardExportLog.Count;
        };
        var result = f.Start.Invoke(f.StartFrame()); n.AfterHoldSourceGuardExport = null;
        Check(closed && result == unchecked((nuint)n.HoldHandle.ToInt64()) && f.Start.Originals == 1
            && f.Snapshot().Records == 0 && f.Snapshot().NaturallyCompleted == 0
            && n.HoldSourceGuardExportLog.Skip(boundary).Count(name => name == "il2cpp_gchandle_new") == 1
            && n.HoldSourceGuardExportLog.Skip(boundary).Contains("il2cpp_gchandle_free")
            && n.HoldSourceGuardExportLog.Skip(boundary).All(SourceGuardPublicationExports.Contains),
            "Backend field withdrawal continued source reads, lost output-root responsibility or suppressed the original.");
    }

    private static void SourceGuardSynchronousTarget()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame()); f.Move.Original = _ => 0xff;
        var n = f.Native; var closed = false; var boundary = -1;
        n.HoldSourceGuardExportLog.Clear();
        n.AfterHoldSourceGuardExport = name =>
        {
            if (name != "il2cpp_gchandle_get_target" || closed) return;
            n.AfterHoldSourceGuardExport = null; closed = true; f.Backend.Dispose(); boundary = n.HoldSourceGuardExportLog.Count;
        };
        Check(f.Move.Invoke(f.MoveFrame()) == 0xff, "Context withdrawal suppressed the original Move.");
        n.AfterHoldSourceGuardExport = null;
        Check(closed && f.Move.Originals == 1 && boundary >= 0
            && n.HoldSourceGuardExportLog.Skip(boundary).All(SourceGuardPublicationExports.Contains)
            && !n.HoldSourceGuardExportLog.Skip(boundary).Contains("il2cpp_object_get_class"),
            "A synchronous target callback withdrawal reached the subsequent class read.");
    }

    private static void SourceGuardOldStart()
    {
        using var f = new Fixture(); HoldCoroutineLifecycle.CancellationProvider? old = null;
        f.Start.Original = _ =>
        {
            old = NativeOwnerProvider(f);
            f.State.Scene("Play-guard-old-start-retired");
            f.State.Controller(f.Native.GameplayInformation.ToInt64(), true, false,
                "synthetic-hold-source-guard-next", f.Native.GameplayController.ToInt64());
            f.Backend.TickHoldLifecycle();
            Check(f.Snapshot().InFlight == 1 && old.HasResponsibility, "Retirement discarded the in-flight original capture.");
            return unchecked((nuint)f.Native.HoldHandle.ToInt64());
        };
        Check(f.Start.Invoke(f.StartFrame()) == unchecked((nuint)f.Native.HoldHandle.ToInt64()),
            "An old-run Start lost its actual returned Coroutine wrapper.");
        Check(old is { Retired: true, HasResponsibility: false } && f.Snapshot().InFlight == 0
            && f.Snapshot().RootResponsibilities == 0 && f.Native.ActiveRoots == 0 && f.Log.Errors.Count == 0,
            "Completion used current-run admission or lost known old-run handle cleanup.");
    }

    private static void SourceGuardLateRoot() => SourceGuardLateRootCore(false);
    private static void SourceGuardLateRootUnknown() => SourceGuardLateRootCore(true);

    private static void SourceGuardLateRootCore(bool failFree)
    {
        using var f = new Fixture(); var n = f.Native; var frame = f.StartFrame();
        var observation = f.Backend.CaptureHoldStart(frame) ?? throw new InvalidOperationException("No actual Start capture.");
        var closed = false; uint lateHandle = 0;
        n.AfterHoldSourceGuardExport = name =>
        {
            if (name != "il2cpp_gchandle_new" || closed) return;
            n.AfterHoldSourceGuardExport = null; closed = true;
            lateHandle = n.NoteViewHandlesFor(n.HoldHandle).Single();
            observation.Capture.Dispose();
            if (failFree) n.FailFreeHandle = lateHandle;
        };
        var error = Expect<Exception>(() => f.Backend.CompleteHoldStart(observation, frame, unchecked((nuint)n.HoldHandle.ToInt64())));
        n.AfterHoldSourceGuardExport = null;
        if (failFree) { f.RetainStorage = true; UnknownOwners.Add(f); }
        Check(closed && lateHandle != 0 && n.NoteViewFreeLog.Count(row => row.Handle == lateHandle) == 1
            && !f.Snapshot().Rows.Single().StartReturned && !f.Snapshot().FullCoverageKnown,
            "A root returned after capture closure was adopted, dropped or freed more than once.");
        if (failFree)
        {
            var roots = observation.Capture.GetType().GetField("_roots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(observation.Capture);
            Check(ReferenceEquals(error.Data["HoldLateRootCleanupOwner"], roots)
                && error.Data["HoldLateRootCleanupFailure"] is not null,
                "A failed late-root free did not preserve its original exact roots owner.");
        }
        observation.Dispose(); var attempts = n.FreeHandleAttempts;
        f.Backend.Dispose(); f.Backend.Dispose();
        Check(n.FreeHandleAttempts == attempts && n.ActiveRoots == 0,
            "Later source/lifecycle disposal retried late-root cleanup.");
    }

    private static void SourceGuardTransferredRoot()
    {
        using var f = new Fixture(); var n = f.Native; var field = SourceGuardField(n, () => { });
        using var receiver = n.GameplayApi.Root(n.HoldJudge);
        NativeRuntime.RootHandle? adopted = null; var valid = true; var freeBefore = n.FreeHandleAttempts;
        var failure = new InvalidOperationException("Expected withdrawal after owned publication.");
        n.AfterHoldSourceGuardExport = name =>
        {
            if (name != "il2cpp_field_get_value") return;
            n.AfterHoldSourceGuardExport = null; valid = false;
        };
        try
        {
            var error = Expect<InvalidOperationException>(() => n.GameplayApi.PublishHoldCaptureReferenceField(receiver,
                field, (token, root) =>
                {
                    Check(token == n.GameplayInformation && root.Target == token, "A transferred publication has another native token.");
                    adopted = root; return true;
                }, () => { if (!valid) throw failure; }));
            n.AfterHoldSourceGuardExport = null;
            Check(ReferenceEquals(error, failure) && adopted is not null && adopted.Target == n.GameplayInformation
                && n.FreeHandleAttempts == freeBefore && n.NoteViewHandlesFor(n.GameplayInformation).Length == 1,
                "The helper freed or lost a root already transferred to the actual caller.");
        }
        finally { n.AfterHoldSourceGuardExport = null; adopted?.Dispose(); }
        Check(n.FreeHandleAttempts == freeBefore + 1 && n.NoteViewHandlesFor(n.GameplayInformation).Length == 0,
            "The caller's transferred root was not freed exactly once by its actual owner.");
    }

    private static void SourceGuardThrowingPublisher()
    {
        using var f = new Fixture(); var n = f.Native; var field = SourceGuardField(n, () => { });
        using var receiver = n.GameplayApi.Root(n.HoldJudge);
        var before = NativePublicationCall.UnconfirmedPublicationCount; var frees = n.FreeHandleAttempts;
        var failure = new IOException("Expected unconfirmed Hold publisher failure."); uint temporary = 0;
        var error = Expect<IOException>(() => n.GameplayApi.PublishHoldCaptureReferenceField(receiver, field,
            (token, root) =>
            {
                Check(token == n.GameplayInformation && root.Target == token, "Publisher did not receive an actual rooted token.");
                temporary = n.NoteViewHandlesFor(token).Single(); throw failure;
            }, () => { }));
        Check(ReferenceEquals(error, failure) && temporary != 0 && NativePublicationCall.UnconfirmedPublicationCount == before + 1
            && n.FreeHandleAttempts == frees + 1 && n.NoteViewFreeLog.Count(row => row.Handle == temporary) == 1,
            "A throwing publisher lost its unconfirmed responsibility or repeated/dropped temporary root cleanup.");
        var after = n.FreeHandleAttempts; f.Backend.TickHoldLifecycle(); f.Backend.Dispose(); f.Backend.Dispose();
        Check(n.FreeHandleAttempts == after, "Unconfirmed publication cleanup was retried by later backend withdrawal.");
    }
}
