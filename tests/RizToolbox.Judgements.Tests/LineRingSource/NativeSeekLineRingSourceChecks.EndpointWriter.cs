using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Judgements.Tests.NoteFlags;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    internal static readonly (string Name, Action Run)[] EndpointWriterCases =
    {
        ("Line ring source endpoint writer applies and restores parent-equal no Note targets", WriterApplyRestore),
        ("Line ring source endpoint writer applies parent mismatch with SetParent false and restores", WriterParentMismatch),
        ("Line ring source endpoint writer preflights every local value before first setter", WriterWholePreflight),
        ("Line ring source endpoint writer retains admitted before-storage setter and source roots", WriterBeforeFailure),
        ("Line ring source endpoint writer restores admitted after-storage setter", WriterAfterFailure),
        ("Line ring source endpoint writer quarantines third state after native setter", WriterThirdState),
        ("Line ring source endpoint writer refuses worker and callback reentry before native", WriterThreadAndReentry),
        ("Line ring source endpoint writer transfers one source to one reserved owner", WriterExclusiveOwnership),
        ("Line ring source endpoint writer stops native inspection after getter callback disposal", WriterGetterDisposal),
        ("Line ring source endpoint writer stops after setter callback disposal and retains effects", WriterSetterDisposal),
        ("Line ring source endpoint writer rejects local mutation within conditional endpoint observation", WriterConditionalReadDrift),
        ("Line ring source endpoint writer preflights a stable complete endpoint vector", WriterLateVectorDrift),
        ("Line ring source endpoint writer revalidates selected state after temporary rooting", WriterRootAdmissionDrift),
        ("Line ring source endpoint writer rejects typed setter profile drift before native entry", WriterProfileDrift),
        ("Line ring source endpoint writer retains exact unknown temporary cleanup owner without retry", WriterUnknownCleanup),
        ("Line ring source endpoint writer preserves its independent hold after outer disposal", WriterOuterDisposal),
        ("Line ring source endpoint writer deduplicates identical physical Transform aliases", WriterPhysicalAliases)
        ,("Line ring source parent writer retains before-storage SetParent entry", WriterParentBeforeFailure)
        ,("Line ring source parent writer restores after-storage SetParent entry", WriterParentAfterFailure)
        ,("Line ring source parent writer quarantines local third state between parent and local", WriterParentThirdState)
        ,("Line ring source parent writer refuses full planned cycle before setter", WriterParentCycle)
        ,("Line ring source parent writer plans current-descendant targets without transient cycle", WriterParentOrdering)
        ,("Line ring source parent writer rejects pinned SetParent profile drift", WriterParentProfileDrift)
        ,("Line ring source parent writer stops on SetParent callback withdrawal", WriterParentCallbackWithdrawal)
        ,("Line ring source parent writer dispatches safe order for a current descendant", WriterActualParentOrdering)
        ,("Line ring source parent writer deduplicates physical aliases for parent and local", WriterPhysicalParentAliases)
        ,("Line ring source parent writer restores original root parent using SetParent null false", WriterOriginalRootParent)
        ,("Line ring source parent writer plans depth safe intermediate graphs before native entry", WriterParentDepthOrdering)
        ,("Line ring source parent writer metadata inspector stops immediately on callback withdrawal", WriterParentMetadataWithdrawal)
        ,("Line ring source parent writer rejects warmed Boolean and Void class identity drift", WriterParentCanonicalClassDrift)
        ,("Line ring source parent writer rejects endpoint and graph parent disagreement during preparation", WriterParentGraphDisagreement)
        ,("Line ring source writer observes actual post-write endpoint world with its original owner", WriterAppliedWorldObservation)
        ,("Line ring source writer rejects a changing parent local world tuple", WriterAppliedWorldDrift)
        ,("Line ring source writer rejects a nonfinite actual post-write world getter", WriterAppliedWorldNonfinite)
        ,("Line ring source writer rejects world observation after selected restoration", WriterAppliedWorldAfterRestore)
        ,("Line ring source writer stops world getter inspection on callback withdrawal", WriterAppliedWorldWithdrawal)
        ,("Line ring source target ownership releases duplicate roots and revokes before recovery", WriterTargetOwnershipCleanup)
        ,("Line ring source restored ownership releases actual roots and rejects changed originals", WriterRestoredOwnershipCleanup)
    };

    private sealed class WriterFixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend;
        internal readonly JudgementRunCapture Run;
        internal readonly NativeSeekCanvasBasisCommands Basis;
        internal NativeSeekLineRingSourceCapture? Source;
        internal NativeSeekLineEndpointCommands? Writer;
        internal SeekLineEndpointLocalWrite[] Targets = Array.Empty<SeekLineEndpointLocalWrite>();
        private bool _retain;
        internal WriterFixture(bool parentEqual = true, Action<FakeNative>? configure = null)
        {
            Native.CanvasBasisUseStableBorrowedMetadata(); Native.ConfigureLineRingSource();
            ConfigureTargetProvider(Native); configure?.Invoke(Native); Native.CanvasSourceRemember();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-line-endpoint-writer", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State);
            Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No real line/ring RunValue.");
            Basis = Run.PrepareSeekCanvasBasis(Native.CanvasSourceProfile, Native.CanvasPositionProfile, 5, 3);
            Source = Basis.CaptureLineRingSource(Native.LineRingSourceProfile);
            Targets = Source.ReadLocalTargetProjection().Endpoints.LocalTargets.ToArray();
            Check(Targets.Length >= 3, "Writer fixture lacks complete physical endpoint targets.");
            if (parentEqual)
                foreach (var target in Targets)
                {
                    var pointer = new IntPtr(target.Transform);
                    Native.GeometrySetTransform(pointer, Native.GeometryTransforms[pointer] with { Parent = new(target.ParentTransform) });
                }
        }
        internal NativeSeekLineEndpointCommands Prepare()
        {
            var source = Source ?? throw new InvalidOperationException("Writer fixture consumed its original line source.");
            try { Writer = NativeSeekLineEndpointCommands.Prepare(() => source, Native.CanvasPositionProfile); }
            catch { Source = null; throw; }
            return Writer;
        }
        internal void Retain()
        {
            _retain = true;
            if (Writer is not null) RetainedOwners.Add((Native, Writer, null));
            if (Source is not null) RetainedOwners.Add((Native, Source, null));
        }
        public void Dispose()
        {
            Native.AfterCanvasSourceRead = null; Native.BeforeGeometryCall = null;
            Native.AfterGeometryStore = null; Native.AfterGeometryGetter = null;
            Native.AfterGeometryRoot = null; Native.BeforeExport = null;
            Native.CanvasPositionProfile.OnVerify = null;
            if (Writer is not null && !_retain)
            {
                Source = null; // Writer consumed this original hold exactly once.
                Writer.Dispose();
            }
            if (Source is not null && !_retain) Source.Dispose();
            try { Basis.Dispose(); }
            catch (Exception error)
            {
                Check(_retain && Basis.HasRetainedResponsibility
                    && ReferenceEquals(Data(error, "SeekCanvasBasisOwner"), Basis),
                    "Canvas cleanup failed without its exact retained basis owner.");
                RetainedOwners.Add((Native, Basis, error));
                NoNative(Native, () => Reject(Basis.Dispose));
            }
            Run.Dispose(); Backend.Dispose();
            Check(Native.GeometryReceiversRooted && Native.GeometryPublishedBeforeInspection
                && Native.CanvasSourceForbidden == 0 && Native.AllGameplayReceiversRooted,
                "Endpoint writer used an unrooted or noncanonical native receiver.");
            if (_retain)
            {
                Check(Native.ActiveRoots > 0, "Admitted endpoint effects lost the original retained native source roots.");
                Native.CanvasBasisTrimFinishedDiagnostics();
            }
            else
            {
                Check(Native.ActiveRoots == 0, "Known endpoint writer cleanup leaked original source roots.");
                Native.Dispose();
            }
        }
    }

    private static void WriterApplyRestore()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        var baseline = writer.GetJournal().Baseline.ToArray();
        writer.VerifyPrepared(); writer.Execute();
        Check(writer.VerifyAppliedValues(), "The actual endpoint local targets were not verified.");
        var applied = writer.GetJournal();
        Check(applied.AttemptedWrites == 3 && applied.ExecutionFinished && applied.AppliedValuesVerified
            && applied.Commands.Count == 3 && applied.Commands.All(row => row.NativeEntered && row.NativeReturned && row.TargetObserved)
            && applied.Targets.Count == 3 && applied.UniqueTargets.Count == 3
            && ReferenceEquals(applied.OriginalSource, f.Source) && !applied.CompleteSeekPermitted,
            "The writer did not enter three exact physical endpoint setters or preserve original source ownership.");
        for (var i = 0; i < f.Targets.Length; ++i)
            Check(Same(n.GeometryTransforms[new(f.Targets[i].Transform)].Position, f.Targets[i].LocalPosition),
                "The fake native Transform did not receive its exact local target.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "Selected endpoint raw values did not restore.");
        var restored = writer.GetJournal();
        Check(restored.AttemptedRestores == 3 && restored.Commands.Count == 6
            && restored.Commands.Skip(3).Select(row => row.Transform).SequenceEqual(applied.Commands.Select(row => row.Transform).Reverse())
            && restored.Expected.SequenceEqual(baseline)
            && restored.ExternalEffectsUnclosed && restored.HasRetainedResponsibility,
            "Inverse order, whole baseline, or retained Unity effects were lost.");
        var roots = n.CanvasBasisCurrentRoots();
        var refusal = Reject(writer.Dispose);
        Check(ReferenceEquals(Data(refusal, "SeekLineEndpointEffectsOwner"), writer)
            && ReferenceEquals(Data(refusal, "SeekLineEndpointOriginalSource"), f.Source),
            "Post-restoration disposal lost exact endpoint effects and original source ownership.");
        OriginalRootsRemain(n, roots, "Post-restoration disposal freed source roots despite entered Unity effects.");
        f.Retain();
    }

    private static void WriterParentMismatch()
    {
        using var f = new WriterFixture(parentEqual: false); var n = f.Native;
        var writer = f.Prepare(); var baseline = writer.GetJournal().Baseline.ToArray();
        var mismatched = baseline.Count(row => row.Parent != f.Targets.Single(target => target.Transform == row.Transform).ParentTransform);
        Check(mismatched > 0, "The parent fixture did not start with a real parent mismatch.");
        writer.VerifyPrepared(); writer.Execute(); Check(writer.VerifyAppliedValues(), "Parent/local targets were not verified.");
        var applied = writer.GetJournal();
        Check(applied.AttemptedParentWrites == mismatched && applied.AttemptedLocalWrites == f.Targets.Length
            && applied.Commands.Take(mismatched).All(row => row.Kind == SeekLineEndpointCommandKind.Parent)
            && n.GeometryCalls.Where(row => row.Operation == "SetParent").Count() == mismatched
            && n.GeometryCalls.Where(row => row.Operation == "SetParent").All(row => row.WorldPositionStays == false),
            "The writer omitted an exact SetParent(Transform, false) before local writes.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "Parent/local selected baseline did not restore.");
        var restored = writer.GetJournal();
        Check(restored.AttemptedParentRestores == mismatched && restored.AttemptedLocalRestores == f.Targets.Length
            && restored.Expected.SequenceEqual(baseline)
            && restored.Commands.Skip(applied.Commands.Count).Select(row => row.Kind)
                .SequenceEqual(applied.Commands.Select(row => row.Kind).Reverse()),
            "Parent/local selected inverses did not follow actual entered commands in reverse.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterWholePreflight()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        var pointer = new IntPtr(f.Targets[^1].Transform); var before = n.GeometryTransforms[pointer];
        n.GeometrySetTransform(pointer, before with { Position = new(9, 8, 7) });
        Reject(writer.Execute);
        Check(n.GeometryCalls.Count == 0 && writer.GetJournal().AttemptedWrites == 0,
            "A later endpoint changed before preflight but an earlier setter ran.");
    }

    private static void WriterBeforeFailure()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        var roots = n.CanvasBasisCurrentRoots(); n.FailGeometryBefore = 1;
        Reject(writer.Execute); n.FailGeometryBefore = 0;
        var journal = writer.GetJournal();
        Check(journal.AttemptedWrites == 1 && journal.Commands.Count == 1
            && journal.Commands[0].NativeEntered && !journal.Commands[0].NativeReturned
            && journal.Commands[0].OriginalObserved && n.GeometryCalls.Count == 1,
            "A before-storage setter did not record admitted effects and exact original readback.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "A before-storage setter lost its known baseline.");
        var refusal = Reject(writer.Dispose);
        Check(ReferenceEquals(Data(refusal, "SeekLineEndpointEffectsOwner"), writer),
            "A native-entered before-storage failure released its effects owner.");
        OriginalRootsRemain(n, roots, "A before-storage failure lost original line source roots.");
        f.Retain();
    }

    private static void WriterAfterFailure()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        n.FailGeometryAfter = 1; Reject(writer.Execute); n.FailGeometryAfter = 0;
        var journal = writer.GetJournal();
        Check(journal.AttemptedWrites == 1 && journal.Commands.Count == 1 && journal.Commands[0].TargetObserved,
            "An after-storage setter lost its selected target readback.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "An after-storage setter failed selected inverse.");
        Check(writer.GetJournal().AttemptedRestores == 1, "After-storage inverse issued another setter count.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterThirdState()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        n.AfterGeometryStore = row =>
        {
            if (row.Ordinal != 1 || row.Operation != "set_localPosition") return;
            n.GeometrySetTransform(row.Receiver, n.GeometryTransforms[row.Receiver] with { Position = new(33, 44, 55) });
        };
        Reject(writer.Execute); n.AfterGeometryStore = null;
        var journal = writer.GetJournal();
        Check(journal.UnrecognizedState && journal.SideEffectsUnknown && journal.AttemptedWrites == 1
            && n.GeometryCalls.Count == 1, "A third local state did not stop later endpoint setters.");
        Reject(writer.RestoreSelectedValues);
        Check(n.GeometryCalls.Count == 1, "Unknown third state admitted a selected inverse.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterThreadAndReentry()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        NoNative(n, () => Task.Run(() => Reject(writer.VerifyPrepared)).GetAwaiter().GetResult());
        writer.VerifyPrepared();
        var reentered = false;
        n.AfterGeometryGetter = (name, receiver) =>
        {
            if (reentered || name != "get_localPosition" || receiver != new IntPtr(f.Targets[0].Transform)) return;
            reentered = true; NoNative(n, () => Reject(writer.VerifyPrepared));
        };
        writer.VerifyPrepared(); n.AfterGeometryGetter = null;
        Check(reentered && n.GeometryCalls.Count == 0, "Getter callback reentry reached another native writer call.");
    }

    private static void WriterExclusiveOwnership()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        var source = f.Source!; var roots = n.CanvasBasisCurrentRoots();
        NoNative(n, () => Reject(source.Dispose));
        NoNative(n, () => Reject(() => NativeSeekLineEndpointCommands.Prepare(() => source, n.CanvasPositionProfile)));
        Check(ReferenceEquals(writer.OriginalSource, source) && !source.GetJournal().Disposed
            && n.GeometryCalls.Count == 0, "A second claimant or external disposal consumed the first writer's source.");
        OriginalRootsRemain(n, roots, "A failed second claimant freed the first writer's exact native roots.");
    }

    private static int WriterCommandCount(NativeSeekLineEndpointCommands writer)
        => ((System.Collections.ICollection)Field(writer, "_commands")).Count;

    private static void WriterGetterDisposal()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare(); var fired = false;
        n.AfterGeometryGetter = (_, _) =>
        {
            if (fired) return;
            fired = true; writer.Dispose(); n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
        };
        Reject(writer.VerifyPrepared); n.AfterGeometryGetter = null; n.CanvasSourceAuditExports = false;
        Check(fired && n.GeometryCalls.Count == 0 && writer.GetJournal().Disposed
            && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
            "An adopted writer withdrawn in a getter continued native metadata/result inspection.");
    }

    private static void WriterSetterDisposal()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare(); var fired = false;
        n.AfterGeometryStore = _ =>
        {
            if (fired) return;
            fired = true; writer.Dispose(); n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
        };
        var error = Reject(writer.Execute); n.AfterGeometryStore = null; n.CanvasSourceAuditExports = false;
        Check(fired && n.GeometryCalls.Count == 1 && writer.GetJournal().AttemptedWrites == 1
            && writer.GetJournal().Disposed && writer.GetJournal().HasRetainedResponsibility
            && ReferenceEquals(Data(error, "SeekLineEndpointEffectsOwner"), writer)
            && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
            "Setter callback disposal lost entered effects or continued later native inspection.");
        f.Retain();
    }

    private static void WriterConditionalReadDrift()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare(); var fired = false;
        var target = new IntPtr(f.Targets[0].Transform);
        n.AfterGeometryGetter = (name, receiver) =>
        {
            if (fired || name != "get_localPosition" || receiver != target || WriterCommandCount(writer) == 0) return;
            fired = true; n.GeometrySetTransform(target, n.GeometryTransforms[target] with { Position = new(222, 333, 444) });
        };
        Reject(writer.Execute); n.AfterGeometryGetter = null;
        Check(fired && n.GeometryCalls.Count == 0 && writer.GetJournal().AttemptedWrites == 0
            && writer.GetJournal().UnrecognizedState,
            "A local changed after its boxed getter sample but conditional setter overwrote that third state.");
    }

    private static void WriterLateVectorDrift()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        var last = new IntPtr(f.Targets[^1].Transform); var earlier = new IntPtr(f.Targets[1].Transform);
        var samples = 0; var fired = false;
        n.AfterGeometryGetter = (name, receiver) =>
        {
            // Two complete source observations precede endpoint preflight. A
            // later endpoint getter then changes an already sampled endpoint.
            if (name != "get_localPosition" || receiver != last || ++samples != 4) return;
            fired = true; n.GeometrySetTransform(earlier, n.GeometryTransforms[earlier] with { Position = new(91, 92, 93) });
        };
        Reject(writer.Execute); n.AfterGeometryGetter = null;
        Check(fired && n.GeometryCalls.Count == 0 && writer.GetJournal().AttemptedWrites == 0
            && writer.GetJournal().UnrecognizedState,
            "A late getter changed an earlier endpoint but complete preflight still allowed the first setter.");
    }

    private static void WriterRootAdmissionDrift()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        var selected = new IntPtr(f.Targets[0].Transform); var armed = false; var fired = false;
        var parentReads = 0;
        n.AfterGeometryGetter = (name, receiver) =>
        {
            if (name == "get_parent" && receiver == selected && WriterCommandCount(writer) == 1 && ++parentReads >= 3)
                armed = true;
        };
        n.AfterGeometryRoot = (_, receiver) =>
        {
            if (!armed || fired || receiver != selected) return;
            fired = true; n.GeometrySetTransform(selected, n.GeometryTransforms[selected] with { Position = new(17, 18, 19) });
        };
        Reject(writer.Execute); n.AfterGeometryGetter = null; n.AfterGeometryRoot = null;
        Check(fired && n.GeometryCalls.Count == 0 && writer.GetJournal().AttemptedWrites == 0
            && writer.GetJournal().UnrecognizedState,
            "Temporary rooting changed selected endpoint state after observation but dispatch ignored it.");
    }

    private static void WriterProfileDrift()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        n.CanvasPositionProfile.WrongMember = nameof(ISeekCanvasPositionNativeProfile.SetLocalPositionMethod);
        Reject(writer.Execute); n.CanvasPositionProfile.WrongMember = null;
        Check(n.GeometryCalls.Count == 0 && writer.GetJournal().AttemptedWrites == 0
            && !writer.GetJournal().AppliedValuesVerified,
            "Fresh writer admission accepted another exact setter MethodInfo.");
    }

    private static void WriterUnknownCleanup()
    {
        using var f = new WriterFixture(); var n = f.Native; var writer = f.Prepare();
        var fired = false; uint failed = 0;
        n.AfterGeometryGetter = (_, receiver) =>
        {
            if (fired) return;
            fired = true; failed = n.NoteViewHandlesFor(receiver).Last(); n.FailFreeHandle = failed;
        };
        var error = Reject(writer.VerifyPrepared); n.AfterGeometryGetter = null;
        Check(fired && failed != 0 && n.GeometryCalls.Count == 0
            && ReferenceEquals(Data(error, "SeekLineEndpointCleanupOwner"), writer)
            && Data(error, "SeekLineEndpointCleanupFailure") is Exception
            && Data(error, "SeekLineEndpointSourceCleanupOwner") is object
            && writer.GetJournal().HasRetainedResponsibility && writer.GetJournal().SideEffectsUnknown
            && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
            "Unknown temporary native cleanup lost exact writer/source responsibility.");
        var frees = n.FreeHandleAttempts;
        NoNative(n, () => { Reject(writer.VerifyPrepared); Reject(writer.Execute); Reject(writer.Dispose); });
        Check(n.FreeHandleAttempts == frees, "Endpoint owner retried an unknown native cleanup.");
        f.Retain();
    }

    private static void WriterOuterDisposal()
    {
        using var f = new WriterFixture(); var writer = f.Prepare();
        f.Basis.Dispose(); f.Run.Dispose();
        writer.VerifyPrepared(); writer.Execute(); Check(writer.VerifyAppliedValues(), "Outer disposal invalidated independent writer hold.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "Independent writer could not verify selected inverse.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterPhysicalAliases()
    {
        using var f = new WriterFixture(configure: n =>
        {
            var original = n.CanvasSourceSlots(n.LineRingDynamicArray)[0];
            var group = n.CanvasSourceSlots(n.LineRingGroupsArray)[0];
            var alias = n.LineRingNewLine(group, n.NoteViewPoints[0][0], n.NoteViewPoints[0][1], false);
            n.GeometryComponentTransforms[alias] = n.LineRingLineTransform(original);
            n.LineRingSet(alias, "endTransform", n.LineRingEndTransform(original));
            n.LineRingSet(alias, "endLineInformation", IntPtr.Zero);
            n.LineRingSetGroupLines(group, n.CanvasSourceSlots(n.LineRingGroupArray(group)).Append(alias).ToArray());
            n.LineRingSetDynamic(original, original, alias);
        });
        var writer = f.Prepare(); var before = writer.GetJournal();
        Check(before.Targets.Count == 5 && before.UniqueTargets.Count == 3,
            "Identical physical Transform aliases were collapsed from occurrence history or not deduplicated for effects.");
        writer.Execute(); Check(writer.GetJournal().AttemptedWrites == 3 && f.Native.GeometryCalls.Count == 3,
            "Identical endpoint aliases executed duplicate native setters.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "Aliased selected inverse lost its original unique baseline.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterParentBeforeFailure()
    {
        using var f = new WriterFixture(parentEqual: false); var n = f.Native; var writer = f.Prepare();
        n.FailGeometryBefore = 1; Reject(writer.Execute); n.FailGeometryBefore = 0;
        var journal = writer.GetJournal();
        Check(journal.AttemptedParentWrites == 1 && journal.AttemptedLocalWrites == 0
            && journal.Commands.Count == 1 && journal.Commands[0].Kind == SeekLineEndpointCommandKind.Parent
            && journal.Commands[0].NativeEntered && !journal.Commands[0].NativeReturned
            && journal.Commands[0].OriginalObserved && journal.ExternalEffectsUnclosed,
            "A before-storage SetParent entry was not journaled with original parent/local readback.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "Before-storage SetParent lost baseline.");
        Check(writer.GetJournal().AttemptedParentRestores == 0, "A before-storage parent failure retried a setter.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterParentAfterFailure()
    {
        using var f = new WriterFixture(parentEqual: false); var n = f.Native; var writer = f.Prepare();
        n.FailGeometryAfter = 1; Reject(writer.Execute); n.FailGeometryAfter = 0;
        var journal = writer.GetJournal();
        Check(journal.AttemptedParentWrites == 1 && journal.AttemptedLocalWrites == 0
            && journal.Commands[0].Kind == SeekLineEndpointCommandKind.Parent
            && journal.Commands[0].TargetObserved && journal.Commands[0].ReadbackParent == journal.Commands[0].TargetParent,
            "An after-storage SetParent entry lost target parent/local readback.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "After-storage SetParent did not restore selected baseline.");
        Check(writer.GetJournal().AttemptedParentRestores == 1 && n.GeometryCalls.Count == 2
            && n.GeometryCalls.All(row => row.Operation == "SetParent" && row.WorldPositionStays == false),
            "After-storage SetParent inverse did not use one exact false SetParent.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterParentThirdState()
    {
        using var f = new WriterFixture(parentEqual: false); var n = f.Native; var writer = f.Prepare();
        n.AfterGeometryStore = row =>
        {
            if (row.Ordinal != 1 || row.Operation != "SetParent") return;
            n.GeometrySetTransform(row.Receiver, n.GeometryTransforms[row.Receiver] with { Position = new(77, 88, 99) });
        };
        Reject(writer.Execute); n.AfterGeometryStore = null;
        var journal = writer.GetJournal();
        Check(journal.UnrecognizedState && journal.SideEffectsUnknown && journal.AttemptedParentWrites == 1
            && journal.AttemptedLocalWrites == 0 && n.GeometryCalls.Count == 1,
            "A third local state after SetParent admitted a later local setter.");
        Reject(writer.RestoreSelectedValues); Reject(writer.Dispose); f.Retain();
    }

    private static void WriterParentCycle()
    {
        using var f = new WriterFixture(parentEqual: false); var n = f.Native;
        var child = new IntPtr(f.Targets[0].Transform); var targetParent = new IntPtr(f.Targets[0].ParentTransform);
        n.GeometrySetTransform(targetParent, n.GeometryTransforms[targetParent] with { Parent = child });
        var error = Reject(() => f.Prepare());
        Check(error is NotSupportedException && n.GeometryCalls.Count == 0,
            "A complete planned parent cycle escaped zero-setter preflight.");
    }

    private static void WriterParentOrdering()
    {
        var type = typeof(NativeSeekLineEndpointCommands);
        var owner = (NativeSeekLineEndpointCommands)Activator.CreateInstance(type, nonPublic: true)!;
        var unique = type.GetField("_unique", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var plan = type.GetMethod("PlanParentOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        SeekLineEndpointLocalWrite Row(long child, long parent)
            => new(1, SeekLineEndpointLocalRole.Start, child, 0, parent, new NativeVector3(0, 0, 0));
        var graph = new Dictionary<long, long> { [10] = 0, [20] = 10, [30] = 0 };
        unique.SetValue(owner, new[] { Row(10, 20), Row(20, 30) });
        var order = (int[])plan.Invoke(owner, new object[] { graph })!;
        Check(order.SequenceEqual(new[] { 1, 0 }),
            "A final-acyclic parent plan would transiently reparent an ancestor below its current descendant.");
        unique.SetValue(owner, new[] { Row(10, 20), Row(20, 10) });
        var cycle = Reject(() => plan.Invoke(owner, new object[] { graph }));
        Check(cycle is System.Reflection.TargetInvocationException { InnerException: NotSupportedException },
            "A final cycle was not refused by whole-plan admission.");
    }

    private static void WriterParentProfileDrift()
    {
        using var f = new WriterFixture(parentEqual: false); var n = f.Native; var writer = f.Prepare();
        n.CanvasPositionProfile.WrongMember = nameof(ISeekCanvasPositionNativeProfile.SetParentMethod);
        Reject(writer.Execute); n.CanvasPositionProfile.WrongMember = null;
        Check(n.GeometryCalls.Count == 0 && writer.GetJournal().AttemptedParentWrites == 0,
            "A foreign SetParent MethodInfo reached native setter entry.");
    }

    private static void WriterParentCallbackWithdrawal()
    {
        using var f = new WriterFixture(parentEqual: false); var n = f.Native; var writer = f.Prepare(); var fired = false;
        n.AfterGeometryStore = row =>
        {
            if (fired || row.Operation != "SetParent") return;
            fired = true; writer.Dispose(); n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
        };
        var error = Reject(writer.Execute); n.AfterGeometryStore = null; n.CanvasSourceAuditExports = false;
        Check(fired && n.GeometryCalls.Count == 1 && writer.GetJournal().AttemptedParentWrites == 1
            && writer.GetJournal().Disposed && writer.GetJournal().HasRetainedResponsibility
            && ReferenceEquals(Data(error, "SeekLineEndpointEffectsOwner"), writer)
            && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
            "A SetParent callback withdrawal continued native inspection or released effects roots.");
        f.Retain();
    }

    private static void WriterActualParentOrdering()
    {
        using var f = new WriterFixture(parentEqual: false, configure: n =>
        {
            var dynamic = n.CanvasSourceSlots(n.LineRingDynamicArray)[0];
            var start = n.LineRingLineTransform(dynamic); var end = n.LineRingEndTransform(dynamic);
            n.CanvasSourceSlot(n.GeometryCanvasArray, 0, end);
            n.GeometrySetTransform(end, n.GeometryTransforms[end] with { Parent = start });
        });
        var n = f.Native; var a = f.Targets.Single(row => row.CanvasSlot == 0).Transform;
        var b = f.Targets.Single(row => row.Role == SeekLineEndpointLocalRole.DedicatedCrossCanvasEnd).Transform;
        Check(f.Targets.Single(row => row.Transform == a).ParentTransform == b,
            "The configured final-acyclic target did not place A under current descendant B.");
        var writer = f.Prepare(); writer.Execute(); Check(writer.VerifyAppliedValues(), "Safe parent order did not apply all targets.");
        var parentCalls = writer.GetJournal().Commands.Where(row => row.Direction == SeekLineEndpointDirection.Apply
            && row.Kind == SeekLineEndpointCommandKind.Parent).ToArray();
        Check(Array.FindIndex(parentCalls, row => row.Transform == b) >= 0
            && Array.FindIndex(parentCalls, row => row.Transform == a) >= 0
            && Array.FindIndex(parentCalls, row => row.Transform == b) < Array.FindIndex(parentCalls, row => row.Transform == a)
            && n.GeometryCalls.Where(row => row.Operation == "SetParent").All(row => row.WorldPositionStays == false),
            "The writer dispatched A below B before moving B out of A's current subtree.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "Safe-order parent inverses lost baseline.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterPhysicalParentAliases()
    {
        using var f = new WriterFixture(parentEqual: false, configure: n =>
        {
            var original = n.CanvasSourceSlots(n.LineRingDynamicArray)[0];
            var group = n.CanvasSourceSlots(n.LineRingGroupsArray)[0];
            var alias = n.LineRingNewLine(group, n.NoteViewPoints[0][0], n.NoteViewPoints[0][1], false);
            n.GeometryComponentTransforms[alias] = n.LineRingLineTransform(original);
            n.LineRingSet(alias, "endTransform", n.LineRingEndTransform(original));
            n.LineRingSet(alias, "endLineInformation", IntPtr.Zero);
            n.LineRingSetGroupLines(group, n.CanvasSourceSlots(n.LineRingGroupArray(group)).Append(alias).ToArray());
            n.LineRingSetDynamic(original, original, alias);
        });
        var writer = f.Prepare(); var baseline = writer.GetJournal();
        Check(baseline.Targets.Count == 5 && baseline.UniqueTargets.Count == 3,
            "Physical occurrence aliases were collapsed before the parent/local journal.");
        writer.Execute(); Check(writer.VerifyAppliedValues(), "Aliased parent/local targets were not verified.");
        var applied = writer.GetJournal();
        Check(applied.AttemptedParentWrites == 3 && applied.AttemptedLocalWrites == 3
            && applied.Commands.Where(row => row.Direction == SeekLineEndpointDirection.Apply)
                .GroupBy(row => (row.Transform, row.Kind)).All(group => group.Count() == 1),
            "A physical endpoint alias dispatched a duplicate parent or local setter.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "Aliased parent/local inverse lost baseline.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterOriginalRootParent()
    {
        using var f = new WriterFixture(parentEqual: false);
        var n = f.Native; var rootEndpoint = new IntPtr(f.Targets[0].Transform);
        n.GeometrySetTransform(rootEndpoint, n.GeometryTransforms[rootEndpoint] with { Parent = IntPtr.Zero });
        var writer = f.Prepare(); var original = writer.GetJournal().Baseline.ToArray();
        Check(original.Any(row => row.Transform == rootEndpoint.ToInt64() && row.Parent == 0),
            "The endpoint root-parent baseline was fabricated.");
        writer.Execute(); Check(writer.VerifyAppliedValues(), "Root-parent endpoint did not accept the actual Canvas parent.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "An original root-parent endpoint could not restore parent null.");
        Check(n.GeometryTransforms[rootEndpoint].Parent == IntPtr.Zero
            && n.GeometryCalls.Any(row => row.Receiver == rootEndpoint && row.Operation == "SetParent"
                && row.Parent == IntPtr.Zero && row.WorldPositionStays == false)
            && writer.GetJournal().Expected.SequenceEqual(original),
            "The parent inverse omitted the original null Transform or used worldPositionStays=true.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterParentDepthOrdering()
    {
        var type = typeof(NativeSeekLineEndpointCommands);
        var owner = (NativeSeekLineEndpointCommands)Activator.CreateInstance(type, nonPublic: true)!;
        var unique = type.GetField("_unique", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var plan = type.GetMethod("PlanParentOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var graph = new Dictionary<long, long> { [10] = 0, [30] = 0 };
        long previous = 0;
        for (var i = 0; i < 100; ++i) { graph[1000 + i] = previous; previous = 1000 + i; }
        var targetParent = previous;
        previous = 10;
        for (var i = 0; i < 200; ++i) { graph[2000 + i] = previous; previous = 2000 + i; }
        SeekLineEndpointLocalWrite Row(long child, long parent)
            => new(1, SeekLineEndpointLocalRole.Start, child, 0, parent, new NativeVector3(0, 0, 0));
        unique.SetValue(owner, new[] { Row(10, targetParent), Row(2099, 30) });
        var order = (int[])plan.Invoke(owner, new object[] { graph })!;
        Check(order.SequenceEqual(new[] { 1, 0 }),
            "The planner entered a too-deep intermediate graph instead of moving its selected descendant first.");
        foreach (var index in order)
        {
            if (index == 1) graph[2099] = 30; else graph[10] = targetParent;
            foreach (var node in graph.Keys)
            {
                var depth = 0; var cursor = node;
                while (cursor != 0) { ++depth; cursor = graph[cursor]; }
                Check(depth <= NativeSeekLineRingSourceCapture.MaximumParentDepth,
                    "The returned order contains an intermediate parent depth outside the supported domain.");
            }
        }
    }

    private static void WriterParentMetadataWithdrawal()
    {
        using var f = new WriterFixture(); var n = f.Native;
        var revoked = false; var fired = false;
        n.AfterJudgeMetadata = (operation, _) =>
        {
            if (fired || operation != "field.parent") return;
            fired = true; revoked = true;
            n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
        };
        var error = Reject(() => n.GameplayApi.InspectLineEndpointPositionMethod(
            n.CanvasPositionProfile.TransformClass, "set_localPosition", IntPtr.Zero,
            () => { if (revoked) throw new OperationCanceledException("Synthetic metadata callback withdrawal."); }));
        n.AfterJudgeMetadata = null; n.CanvasSourceAuditExports = false;
        Check(fired && error is OperationCanceledException && n.GeometryCalls.Count == 0
            && n.CanvasSourceExportLog.All(name => name is "il2cpp_free" or "il2cpp_gchandle_free"),
            "Guarded endpoint metadata inspection continued native exports after callback withdrawal.");
    }

    private sealed class ParentClassExportDrift : IDisposable
    {
        private readonly Delegate _image, _name, _namespace;
        private readonly IntPtr _foreignImage, _foreignName, _foreignNamespace;
        internal IntPtr Subject;
        internal string? Mode;

        internal ParentClassExportDrift(IReadOnlyDictionary<string, Delegate> exports, IntPtr foreignImage)
        {
            _image = exports["il2cpp_class_get_image"];
            _name = exports["il2cpp_class_get_name"];
            _namespace = exports["il2cpp_class_get_namespace"];
            _foreignImage = foreignImage;
            _foreignName = Marshal.StringToHGlobalAnsi("ForeignBooleanOrVoid");
            _foreignNamespace = Marshal.StringToHGlobalAnsi("Foreign");
        }
        public IntPtr Image(IntPtr cls) => cls == Subject && Mode == "image"
            ? _foreignImage : (IntPtr)_image.DynamicInvoke(cls)!;
        public IntPtr Name(IntPtr cls) => cls == Subject && Mode == "name"
            ? _foreignName : (IntPtr)_name.DynamicInvoke(cls)!;
        public IntPtr Namespace(IntPtr cls) => cls == Subject && Mode == "namespace"
            ? _foreignNamespace : (IntPtr)_namespace.DynamicInvoke(cls)!;
        public void Dispose()
        {
            Marshal.FreeHGlobal(_foreignName);
            Marshal.FreeHGlobal(_foreignNamespace);
        }
    }

    private static void WriterParentCanonicalClassDrift()
    {
        using var f = new WriterFixture(); var n = f.Native;
        var exports = (Dictionary<string, Delegate>)Field(n, "_exports");
        var images = (Dictionary<IntPtr, string>)Field(n, "_gameplayImages");
        var types = (Dictionary<IntPtr, FakeNative.TypeInfo>)Field(n, "_types");
        var foreignImage = images.Single(row => row.Value == "Assembly-CSharp.dll").Key;
        using var drift = new ParentClassExportDrift(exports, foreignImage);
        var ptr1 = typeof(NativeRuntime).GetNestedType("Ptr1", BindingFlags.NonPublic)!;
        Delegate Wrap(string method) => Delegate.CreateDelegate(ptr1, drift,
            typeof(ParentClassExportDrift).GetMethod(method, BindingFlags.Public | BindingFlags.Instance)!);
        var replacements = new Dictionary<string, Delegate>
        {
            ["il2cpp_class_get_image"] = Wrap(nameof(ParentClassExportDrift.Image)),
            ["il2cpp_class_get_name"] = Wrap(nameof(ParentClassExportDrift.Name)),
            ["il2cpp_class_get_namespace"] = Wrap(nameof(ParentClassExportDrift.Namespace))
        };
        var api = new NativeRuntime(name => Marshal.GetFunctionPointerForDelegate(
            replacements.TryGetValue(name, out var replacement) ? replacement : exports[name]));
        api.BindCurrentThread();
        var transform = n.CanvasPositionProfile.TransformClass;
        _ = api.InspectLineEndpointParentMethod(transform, IntPtr.Zero, () => { }); // Warm canonical class/layout caches.
        foreach (var type in new[] { n.Boolean, n.Void })
        {
            var originalName = types[type].Name;
            foreach (var mode in new[] { "image", "name", "namespace" })
            {
                drift.Subject = n.ClassOf(type); drift.Mode = mode;
                var error = Reject(() => api.InspectLineEndpointParentMethod(transform, IntPtr.Zero, () => { }));
                drift.Mode = null;
                Check(error is UnsupportedSignatureException && types[type].Name == originalName
                    && n.ClassOf(type) == drift.Subject && n.GeometryCalls.Count == 0,
                    "SetParent accepted warmed " + originalName + " class " + mode
                    + " drift while its class pointer and type name remained unchanged.");
            }
        }
        GC.KeepAlive(replacements);
    }

    private static void WriterParentGraphDisagreement()
    {
        using var f = new WriterFixture(); var n = f.Native;
        var uniqueCount = f.Targets.Select(row => row.Transform).Distinct().Count();
        var selected = new IntPtr(f.Targets[0].Transform);
        var original = n.GeometryTransforms[selected];
        Check(original.Parent != IntPtr.Zero, "The graph disagreement fixture lacks an original parent edge.");
        var checks = 0; var fired = false;
        n.CanvasPositionProfile.OnVerify = () =>
        {
            if (++checks != 4 * uniqueCount + 2) return;
            fired = true;
            n.GeometrySetTransform(selected, n.GeometryTransforms[selected] with { Parent = IntPtr.Zero });
        };
        var error = Reject(() => f.Prepare());
        n.CanvasPositionProfile.OnVerify = null;
        n.GeometrySetTransform(selected, original);
        Check(fired && error is SeekLineEndpointObservationConflict && n.GeometryCalls.Count == 0,
            "Preparation accepted stable endpoint parents from one read and a different complete parent graph.");
    }

    private static bool SameWorldBits(NativeVector3 a, NativeVector3 b)
        => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
        && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
        && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

    private static void WriterAppliedWorldObservation()
    {
        using var f = new WriterFixture(parentEqual: false);
        var n = f.Native; var writer = f.Prepare(); var source = f.Source!;
        var oldWorld = source.Snapshot.Transforms.ToDictionary(row => row.Reference, row => row.Position);
        writer.Execute();
        var actual = writer.ReadAppliedWorldObservation();
        var journal = writer.GetJournal();
        Check(ReferenceEquals(actual.OriginalSource, source) && ReferenceEquals(actual.CommandOwner, writer)
            && ReferenceEquals(actual.SourceIdentity.CanvasCapture, source.CanvasCapture)
            && ReferenceEquals(actual.SourceIdentity.CanvasPlan, source.Snapshot.Identity.CanvasPlan)
            && actual.SourceIdentity == source.Snapshot.Identity
            && !actual.AtomicSnapshot && !actual.CompleteSeekPermitted
            && actual.Endpoints.Count == journal.UniqueTargets.Count
            && actual.Endpoints.Select(row => row.Transform).SequenceEqual(journal.UniqueTargets.Select(row => row.Transform))
            && actual.Endpoints.All(row => row.Parent == n.GeometryTransforms[new(row.Transform)].Parent.ToInt64()
                && SameWorldBits(row.Local, n.GeometryTransforms[new(row.Transform)].Position)
                && float.IsFinite(row.World.X) && float.IsFinite(row.World.Y) && float.IsFinite(row.World.Z)
                && SameWorldBits(row.World, n.GeometryWorldPosition(new(row.Transform))))
            && actual.Endpoints.Any(row => !SameWorldBits(row.World, oldWorld[row.Transform]))
            && journal.AppliedValuesVerified && journal.ExternalEffectsUnclosed && journal.HasRetainedResponsibility,
            "A historical baseline world pose replaced actual post-write get_position or lost source/owner provenance.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "World observation disturbed selected inverse.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterAppliedWorldDrift()
    {
        using var f = new WriterFixture(parentEqual: false);
        var n = f.Native; var writer = f.Prepare(); writer.Execute();
        var journal = writer.GetJournal();
        var selected = new IntPtr(journal.UniqueTargets[0].Transform);
        var parent = new IntPtr(journal.UniqueTargets[0].ParentTransform);
        Check(parent != selected && parent != IntPtr.Zero, "World drift fixture lacks an actual selected parent.");
        var count = 0; var fired = false;
        // The first complete Observe performs 2N endpoint and 2G parent
        // profile checks. The next two checks are the first paired endpoint
        // and its ReadWorld admission; only then arm the getter callback.
        var armAt = checked(2 * journal.UniqueTargets.Count + 2 * journal.BaselineParentGraph.Count + 2);
        n.CanvasPositionProfile.OnVerify = () =>
        {
            if (++count != armAt) return;
            n.AfterGeometryGetter = (name, receiver) =>
            {
                if (fired || name != "get_position" || receiver != selected) return;
                fired = true;
                var prior = n.GeometryTransforms[parent];
                n.GeometrySetTransform(parent, prior with
                { Position = prior.Position with { X = prior.Position.X + 7f } });
            };
        };
        var error = Reject(() => writer.ReadAppliedWorldObservation());
        n.CanvasPositionProfile.OnVerify = null; n.AfterGeometryGetter = null;
        Check(fired && error is SeekLineEndpointObservationConflict
            && writer.GetJournal().UnrecognizedState && writer.GetJournal().ExternalEffectsUnclosed
            && n.GeometryCalls.Count == journal.AttemptedWrites,
            "A changed actual world position passed two full parent/local/world observations or admitted another setter.");
        Reject(writer.RestoreSelectedValues); Reject(writer.Dispose); f.Retain();
    }

    private static void WriterAppliedWorldNonfinite()
    {
        using var f = new WriterFixture(parentEqual: false);
        var n = f.Native; var writer = f.Prepare(); writer.Execute();
        var journal = writer.GetJournal();
        var parent = new IntPtr(journal.UniqueTargets[0].ParentTransform);
        var prior = n.GeometryTransforms[parent];
        n.GeometrySetTransform(parent, prior with { Position = prior.Position with { X = float.NaN } });
        var error = Reject(() => writer.ReadAppliedWorldObservation());
        Check(error is NotSupportedException && writer.GetJournal().UnrecognizedState
            && writer.GetJournal().ExternalEffectsUnclosed
            && n.GeometryCalls.Count == journal.AttemptedWrites,
            "A nonfinite actual get_position result became a target world input or admitted another setter.");
        Reject(writer.RestoreSelectedValues); Reject(writer.Dispose); f.Retain();
    }

    private static void WriterAppliedWorldAfterRestore()
    {
        using var f = new WriterFixture(parentEqual: false);
        var n = f.Native; var writer = f.Prepare(); writer.Execute();
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "World-read fixture did not restore selected values.");
        var entered = n.GeometryCalls.Count;
        Reject(() => writer.ReadAppliedWorldObservation());
        Check(n.GeometryCalls.Count == entered && writer.GetJournal().ExternalEffectsUnclosed,
            "A restored endpoint writer published an applied-world observation or issued another native setter.");
        Reject(writer.Dispose); f.Retain();
    }

    private static void WriterAppliedWorldWithdrawal()
    {
        using var f = new WriterFixture(parentEqual: false);
        var n = f.Native; var writer = f.Prepare(); writer.Execute();
        var journal = writer.GetJournal(); var selected = new IntPtr(journal.UniqueTargets[0].Transform);
        var checks = 0; var fired = false;
        var armAt = checked(2 * journal.UniqueTargets.Count + 2 * journal.BaselineParentGraph.Count + 2);
        n.CanvasPositionProfile.OnVerify = () =>
        {
            if (++checks != armAt) return;
            n.AfterGeometryGetter = (name, receiver) =>
            {
                if (fired || name != "get_position" || receiver != selected) return;
                fired = true; writer.Dispose();
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
        };
        var error = Reject(() => writer.ReadAppliedWorldObservation());
        n.AfterGeometryGetter = null; n.CanvasPositionProfile.OnVerify = null;
        n.CanvasSourceAuditExports = false;
        Check(fired && writer.GetJournal().Disposed && writer.GetJournal().HasRetainedResponsibility
            && ReferenceEquals(Data(error, "SeekLineEndpointEffectsOwner"), writer)
            && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
            "A world getter callback withdrawal continued native inspection or lost entered Unity effects ownership.");
        f.Retain();
    }
}
