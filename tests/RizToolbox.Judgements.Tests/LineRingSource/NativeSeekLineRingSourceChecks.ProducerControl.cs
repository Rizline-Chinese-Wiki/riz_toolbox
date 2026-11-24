using System.Collections;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Judgements.Tests.NoteFlags;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    internal static readonly (string Name, Action Run)[] ProducerControlCases =
    {
        ("Line ring source producer rooted word groups preserve raw control bits and count canary", ProducerPrimitiveWords),
        ("Line ring source producer original array stores only one selected XY or Z group", ProducerPrimitiveArray),
        ("Line ring source producer refuses an external matching control target before its own store", ProducerPrimitiveExternalTarget),
        ("Line ring source producer retains actual entry and refuses a third control state", ProducerPrimitiveThirdState),
        ("Line ring source producer refuses a short actual control before any field read", ProducerPrimitiveExtent),
        ("Line ring source producer preserves completed word observation after unknown temporary pin cleanup", ProducerPrimitiveCleanup),
        ("Line ring source producer rejects a worker before native inspection", ProducerPrimitiveWorker),
        ("Line ring source producer follows exact word order and global reverse on the original backend", ProducerBackendOrder),
        ("Line ring source producer floor cull skips every control and array store", ProducerFloorCull),
        ("Line ring source producer world cull keeps start and end stores before culling", ProducerWorldCull),
        ("Line ring source producer owner quarantines an external matching word without inverse", ProducerBackendExternalTarget),
        ("Line ring source producer owner stops after an entered array word reaches a third control state", ProducerBackendThirdState)
    };

    // These seven primitive cases enter the real Runtime through official-export
    // metadata and physical storage. The later cases additionally enter the
    // original CaptureRun/LineRingReader/endpoint owner; neither seam executes
    // the synthetic SEC_IMAGE's mapped machine code or loads the game.
    private sealed class ProducerPrimitiveFixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        private bool _retain;
        internal ProducerPrimitiveFixture(RendererSyntheticImage image)
        {
            Native.CanvasBasisUseStableBorrowedMetadata(); Native.ConfigureLineRingSource();
            ConfigureTargetProvider(Native); ConfigureDynamicInputs(Native);
            Native.ConfigureLineBuffer(OriginalScratch);
            Native.ConfigureLineRendererConsumer(image.Address);
            Native.ProducerBeginBoundedHistory();
        }
        internal void Retain(object owner, Exception error)
        { _retain = true; RetainedOwners.Add((Native, owner, error)); }
        public void Dispose() { if (!_retain) Native.Dispose(); }
    }

    private static NativeVector3BufferBits ProducerFieldBits(NativeLineProducerControlSnapshot state,
        NativeLineProducerField field) => field switch
    {
        NativeLineProducerField.StartPosition => state.StartPosition,
        NativeLineProducerField.EndPosition => state.EndPosition,
        NativeLineProducerField.NewLinePosition => state.NewLinePosition,
        _ => new(state.EaseType, 0, 0)
    };

    private static void ProducerPrimitiveWords()
    {
        using var image = new RendererSyntheticImage(); using var f = new ProducerPrimitiveFixture(image);
        var n = f.Native; var api = n.GameplayApi; using var root = api.Root(n.LineRingControl);
        var baseline = api.ReadRootedLineProducerControl(root, () => { });
        Check(baseline == n.ProducerRawControl() && baseline.StartPosition == n.ProducerInitialStart
            && baseline.EndPosition == n.ProducerInitialEnd && baseline.NewLinePosition == n.ProducerInitialNew
            && baseline.EaseType == -17 && !baseline.AtomicSnapshot, "The actual rooted raw control baseline was changed or normalized.");
        var a = new NativeVector3BufferBits(Bits(7f), Bits(8f), Bits(9f));
        var b = new NativeVector3BufferBits(Bits(10f), Bits(11f), Bits(12f));
        var c = new NativeVector3BufferBits(Bits(13f), Bits(14f), 0);
        var patches = new[]
        {
            new NativeLineProducerPatch(NativeLineProducerField.StartPosition, 0, 2, a),
            new NativeLineProducerPatch(NativeLineProducerField.StartPosition, 2, 1, a),
            new NativeLineProducerPatch(NativeLineProducerField.EndPosition, 0, 2, b),
            new NativeLineProducerPatch(NativeLineProducerField.EndPosition, 2, 1, b),
            new NativeLineProducerPatch(NativeLineProducerField.EaseType, 0, 1, new(123, 0, 0)),
            new NativeLineProducerPatch(NativeLineProducerField.NewLinePosition, 2, 1, c),
            new NativeLineProducerPatch(NativeLineProducerField.NewLinePosition, 0, 1, c),
            new NativeLineProducerPatch(NativeLineProducerField.NewLinePosition, 1, 1, c)
        };
        var observations = new List<NativeLineProducerControlWriteObservation>(); var current = baseline;
        foreach (var patch in patches)
        {
            var expected = NativeRuntime.PatchLineProducerControl(current, patch);
            var result = api.WriteRootedLineProducerControlSlice(root, current, patch, () => { }, () => { }, () => { });
            Check(result.Before == current && result.After == expected && result.Patch == patch && result.StoreEntered
                && !result.AtomicConditionalWrite && n.ProducerRawControl() == expected
                && n.ProducerRawCount == FakeNative.ProducerCountCanary, "A selected physical word group changed another control word or linePointCount.");
            observations.Add(result); current = result.After;
        }
        foreach (var row in observations.AsEnumerable().Reverse())
        {
            var inverse = row.Patch with { Value = ProducerFieldBits(row.Before, row.Patch.Field) };
            var result = api.WriteRootedLineProducerControlSlice(root, current, inverse, () => { }, () => { }, () => { });
            Check(result.After == row.Before, "A conditional reverse word lost its exact intermediate predecessor.");
            current = result.After;
        }
        Check(current == baseline && n.ProducerRawControl() == baseline && n.ProducerRawCount == FakeNative.ProducerCountCanary,
            "Reverse storage did not retain NaN payloads, negative zero or the untouched count canary.");
    }

    private static void ProducerPrimitiveArray()
    {
        using var image = new RendererSyntheticImage(); using var f = new ProducerPrimitiveFixture(image);
        var n = f.Native; var api = n.GameplayApi; using var root = api.Root(n.LineBufferArray);
        var baseline = api.ReadRootedLineVector3Buffer(root, n.ProducerArrayClass, 107, () => { });
        var value = new NativeVector3BufferBits(Bits(21f), Bits(22f), 0);
        var xy = api.WriteRootedLineVector3BufferSlice(root, n.ProducerArrayClass, baseline,
            new(1, 0, 2, value), () => { }, () => { }, () => { });
        Check(xy.StoreEntered && xy.After.Items[1] == new NativeVector3BufferBits(value.X, value.Y, baseline.Items[1].Z)
            && xy.After.Items.Where((_, i) => i != 1).SequenceEqual(baseline.Items.Where((_, i) => i != 1)),
            "The XY store rewrote Z, an earlier prefix element or the complete tail.");
        var z = api.WriteRootedLineVector3BufferSlice(root, n.ProducerArrayClass, xy.After,
            new(1, 2, 1, value), () => { }, () => { }, () => { });
        Check(z.After.Items[1] == value && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(z.After.Items),
            "The separate physical Z store did not complete the selected element.");
        var inverseZ = api.WriteRootedLineVector3BufferSlice(root, n.ProducerArrayClass, z.After,
            z.Slice with { Value = z.Before.Items[1] }, () => { }, () => { }, () => { });
        var inverseXy = api.WriteRootedLineVector3BufferSlice(root, n.ProducerArrayClass, inverseZ.After,
            xy.Slice with { Value = xy.Before.Items[1] }, () => { }, () => { }, () => { });
        Check(inverseXy.After.Items.SequenceEqual(baseline.Items)
            && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(OriginalScratch), "Selected array reverse lost complete raw storage.");
    }

    private static void ProducerPrimitiveExternalTarget()
    {
        using var image = new RendererSyntheticImage(); using var f = new ProducerPrimitiveFixture(image);
        var n = f.Native; var api = n.GameplayApi; using var root = api.Root(n.LineRingControl);
        var before = api.ReadRootedLineProducerControl(root, () => { });
        var patch = new NativeLineProducerPatch(NativeLineProducerField.StartPosition, 0, 2, new(Bits(31f), Bits(32f), 0));
        var target = NativeRuntime.PatchLineProducerControl(before, patch);
        var error = Reject(() => api.WriteRootedLineProducerControlSlice(root, before, patch,
            () => { }, () => { }, () => n.ProducerOverwrite(target)));
        Check(Data(error, "LineProducerControlStoreEntered") is false
            && Data(error, "LineProducerControlWriteObservation") is null && n.ProducerRawControl() == target
            && ReferenceEquals(Data(error, "LineProducerControlOriginalRoot"), root),
            "An externally matching target was adopted as this call's own entered word store.");
    }

    private static void ProducerPrimitiveThirdState()
    {
        using var image = new RendererSyntheticImage(); using var f = new ProducerPrimitiveFixture(image);
        var n = f.Native; var api = n.GameplayApi; using var root = api.Root(n.LineRingControl);
        var before = api.ReadRootedLineProducerControl(root, () => { });
        var patch = new NativeLineProducerPatch(NativeLineProducerField.StartPosition, 0, 2, new(Bits(41f), Bits(42f), 0));
        var target = NativeRuntime.PatchLineProducerControl(before, patch);
        var third = target with { EaseType = 777 }; var admitted = false; var changed = false;
        n.AfterCanvasSourceRead = (_, _) =>
        {
            if (admitted && !changed && n.ProducerRawControl() == target)
            { changed = true; n.ProducerOverwrite(third); }
        };
        var error = Reject(() => api.WriteRootedLineProducerControlSlice(root, before, patch,
            () => { }, () => { }, () => admitted = true));
        n.AfterCanvasSourceRead = null;
        Check(changed && Data(error, "LineProducerControlStoreEntered") is true && n.ProducerRawControl() == third,
            "Post-store third state lost actual entry or was silently replaced.");
        var inverseAdmitted = false;
        var inverseError = Reject(() => api.WriteRootedLineProducerControlSlice(root, target,
            patch with { Value = before.StartPosition }, () => { }, () => { }, () => inverseAdmitted = true));
        Check(!inverseAdmitted && Data(inverseError, "LineProducerControlStoreEntered") is false
            && n.ProducerRawControl() == third, "Conditional inverse overwrote a third control state.");
    }

    private static void ProducerPrimitiveExtent()
    {
        using var image = new RendererSyntheticImage(); using var f = new ProducerPrimitiveFixture(image);
        var n = f.Native; var api = n.GameplayApi; using var root = api.Root(n.LineRingControl);
        var before = n.ProducerRawControl(); n.ProducerObjectSizeOverride = 0x80;
        var reads = n.FieldReads; var admitted = false;
        var error = Reject(() => api.WriteRootedLineProducerControlSlice(root, before,
            new(NativeLineProducerField.EaseType, 0, 1, new(3, 0, 0)), () => { }, () => { }, () => admitted = true));
        Check(!admitted && n.FieldReads == reads && Data(error, "LineProducerControlStoreEntered") is false
            && n.ProducerRawControl() == before && n.ProducerRawCount == FakeNative.ProducerCountCanary,
            "A short actual receiver reached field observation or raw storage.");
    }

    private static void ProducerPrimitiveCleanup()
    {
        using var image = new RendererSyntheticImage(); using var f = new ProducerPrimitiveFixture(image);
        var n = f.Native; var api = n.GameplayApi; var root = api.Root(n.LineRingControl);
        var before = api.ReadRootedLineProducerControl(root, () => { });
        var patch = new NativeLineProducerPatch(NativeLineProducerField.EaseType, 0, 1, new(5, 0, 0));
        var retained = NativePublicationCall.RetainedRootRecordCount;
        uint failedPin = 0;
        var error = Reject(() => api.WriteRootedLineProducerControlSlice(root, before, patch,
            () => { }, () => { }, () =>
            {
                var active = n.LineBufferActiveHandles(n.LineRingControl);
                failedPin = n.LineBufferFailActiveTemporaryPin(n.LineRingControl, active[0]);
            }));
        // A failed exact free is retained, not retried or freed by fixture teardown.
        f.Retain(root, error);
        Check(failedPin != 0 && Data(error, "LineProducerControlStoreEntered") is true
            && Data(error, "LineProducerControlWriteObservation") is NativeLineProducerControlWriteObservation observation
            && observation.Before == before && observation.After == n.ProducerRawControl() && observation.StoreEntered
            && Data(error, "NativePublicationCleanupOwner") is not null
            && NativePublicationCall.RetainedRootRecordCount == retained + 1
            && n.NoteViewFreeLog.Count(row => row.Handle == failedPin) == 1,
            "Unknown cleanup hid completed physical storage or lost its exact publication owner.");
    }

    private static void ProducerPrimitiveWorker()
    {
        using var image = new RendererSyntheticImage(); using var f = new ProducerPrimitiveFixture(image);
        var n = f.Native; using var root = n.GameplayApi.Root(n.LineRingControl);
        NoNative(n, () =>
        {
            var error = Task.Run(() => Reject(() => n.GameplayApi.ReadRootedLineProducerControl(root, () => { }))).GetAwaiter().GetResult();
            Check(error is InvalidOperationException, "A worker did not refuse the owner thread gate.");
        });
    }

    private static string ProducerCommandKey(SeekLineRendererCommandEffect row) => row.ControlPatch is { } control
        ? $"C:{control.Field}:{control.Component}:{control.Components}"
        : row.ArraySlice is { } slice ? $"A:{slice.Index}:{slice.Component}:{slice.Components}" : row.Kind.ToString();

    private static void ProducerBackendOrder()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image, n => n.LineRingSetDynamic(n.LineRendererSecondDynamic));
        using (f)
        {
            var n = f.Native; var baseline = n.ProducerRawControl();
            var journal = writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile);
            var row = journal.Occurrences.Single(); Check(row.Projection.Points.Count == 6, "The actual short line no longer yields six samples.");
            var expectedOrder = new List<string> { "C:StartPosition:0:2", "C:StartPosition:2:1", "C:EndPosition:0:2", "C:EndPosition:2:1", "C:EaseType:0:1" };
            for (var i = 0; i < 6; ++i) expectedOrder.AddRange(new[]
                { "C:NewLinePosition:2:1", "C:NewLinePosition:0:1", "C:NewLinePosition:1:1", $"A:{i}:0:2", $"A:{i}:2:1" });
            expectedOrder.Add("SetPositions");
            Check(journal.Commands.Select(ProducerCommandKey).SequenceEqual(expectedOrder)
                && journal.Commands.All(command => command.AdmissionEntered && command.NativeEntered && command.NativeReturned && command.TargetObserved)
                && journal.BaselineControl == baseline && journal.ExpectedControl == n.ProducerRawControl()
                && n.ProducerRawCount == FakeNative.ProducerCountCanary
                && ReferenceEquals(journal.OriginalSource, f.Source) && ReferenceEquals(journal.EffectsOwner, writer)
                && !journal.AtomicSnapshot && !journal.ExternalConsumersClosed && !journal.CompleteSeekPermitted,
                "The actual original reader/owner lost native word order, actual entry, baseline, or retained responsibility.");
            var forward = journal.Commands.ToArray();
            var inverseOrder = forward.Reverse().SelectMany(command => command.Kind switch
            {
                SeekLineRendererCommandKind.ProducerControl => command.BeforeControl == command.TargetControl ? Array.Empty<string>() : new[] { ProducerCommandKey(command) },
                SeekLineRendererCommandKind.ArraySlice => command.BeforePoint == command.TargetPoint ? Array.Empty<string>() : new[] { ProducerCommandKey(command) },
                SeekLineRendererCommandKind.SetPositions => new[] { "RestoreCount", "RestoreArray" },
                _ => Array.Empty<string>()
            }).ToArray();
            writer.RestoreDynamicRendererSavedValues(); Check(writer.VerifyRestoredDynamicRendererSavedValues(), "Complete selected producer recovery failed.");
            var restored = writer.GetDynamicRendererJournal();
            Check(restored.Commands.Take(forward.Length).SequenceEqual(forward)
                && restored.Commands.Skip(forward.Length).Select(ProducerCommandKey).SequenceEqual(inverseOrder)
                && n.ProducerRawControl() == baseline && n.ProducerRawCount == FakeNative.ProducerCountCanary
                && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(RendererOriginalScratch())
                && n.LineRendererNativeCalls.Count == 1 && restored.HasRetainedResponsibility,
                "Recovery lost global inverse order/raw baseline or replayed the actual native consumer.");
        }
    }

    private static void ProducerFloorCull()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image, n =>
        {
            n.LineRingSetDynamic(n.LineRendererSecondDynamic);
            n.LineRingSet(n.NoteViewPoints[1][0], "floorPosition", 10000f);
            n.LineRingSet(n.NoteViewPoints[1][1], "floorPosition", 10000f);
        });
        using (f)
        {
            var n = f.Native; var baseline = n.ProducerRawControl();
            var journal = writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile);
            Check(journal.SkippedOccurrences == 1 && journal.DispatchedOccurrences == 0
                && journal.Commands.Single().Kind == SeekLineRendererCommandKind.Cull
                && !journal.Commands[0].AdmissionEntered && n.LineRendererNativeCalls.Count == 0
                && n.ProducerRawControl() == baseline && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(RendererOriginalScratch()),
                "A floor-cull branch wrote control words, a count-zero array or native saved fields.");
            writer.RestoreDynamicRendererSavedValues(); Check(writer.VerifyRestoredDynamicRendererSavedValues(), "Floor-cull baseline verification failed.");
        }
    }

    private static void ProducerWorldCull()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image, n =>
        {
            n.LineRingSetDynamic(n.LineRendererSecondDynamic);
            n.GeometrySetTransform(n.GeometryUiRoot, n.GeometryTransforms[n.GeometryUiRoot] with { Position = new(0, 10, 0) });
        });
        using (f)
        {
            var n = f.Native; var baseline = n.ProducerRawControl();
            var journal = writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile);
            Check(journal.Commands.Select(ProducerCommandKey).SequenceEqual(new[]
                { "C:StartPosition:0:2", "C:StartPosition:2:1", "C:EndPosition:0:2", "C:EndPosition:2:1", "Cull" })
                && journal.SkippedOccurrences == 1 && journal.DispatchedOccurrences == 0
                && n.ProducerRawControl().StartPosition != baseline.StartPosition && n.ProducerRawControl().EndPosition != baseline.EndPosition
                && n.ProducerRawControl().NewLinePosition == baseline.NewLinePosition && n.ProducerRawControl().EaseType == baseline.EaseType
                && n.ProducerRawCount == FakeNative.ProducerCountCanary && n.LineRendererNativeCalls.Count == 0
                && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(RendererOriginalScratch()),
                "A world-Y cull omitted preceding world stores or wrote ease/sample/array/renderer fields.");
            writer.RestoreDynamicRendererSavedValues();
            Check(writer.VerifyRestoredDynamicRendererSavedValues() && n.ProducerRawControl() == baseline,
                "World-Y cull did not restore its actually entered control word groups.");
        }
    }

    private static SeekLineRendererCommandEffect? ProducerPending(NativeSeekLineEndpointCommands writer)
    {
        if (Field(writer, "_rendererPending") is not int pending || pending < 0) return null;
        return ((IList)Field(writer, "_rendererCommands")!)[pending] as SeekLineRendererCommandEffect;
    }

    private static void ProducerBackendExternalTarget()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image, n => n.LineRingSetDynamic(n.LineRendererSecondDynamic));
        using (f)
        {
            var n = f.Native; var changed = false; NativeLineProducerControlSnapshot? external = null;
            n.AfterCanvasSourceRead = (_, _) =>
            {
                if (changed || ProducerPending(writer) is not { Kind: SeekLineRendererCommandKind.ProducerControl, AdmissionEntered: true, NativeEntered: false } step) return;
                changed = true; external = step.TargetControl!; n.ProducerOverwrite(external);
            };
            var error = Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile));
            n.AfterCanvasSourceRead = null; var journal = writer.GetDynamicRendererJournal();
            Check(changed && journal.UnrecognizedState && journal.Commands.Single().AdmissionEntered
                && !journal.Commands[0].NativeEntered && n.ProducerRawControl() == external && n.LineRendererNativeCalls.Count == 0
                && ReferenceEquals(Data(error, "SeekLineRendererEffectsOwner"), writer),
                "The original owner adopted an external matching control target as its own effect.");
            NoNative(n, () => Reject(writer.RestoreDynamicRendererSavedValues));
            Check(n.ProducerRawControl() == external, "Recovery overwrote an unowned matching control word.");
        }
    }

    private static void ProducerBackendThirdState()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image, n => n.LineRingSetDynamic(n.LineRendererSecondDynamic));
        using (f)
        {
            var n = f.Native; var changed = false; NativeLineProducerControlSnapshot? external = null;
            n.AfterCanvasSourceRead = (_, _) =>
            {
                if (changed || ProducerPending(writer) is not { Kind: SeekLineRendererCommandKind.ArraySlice, AdmissionEntered: true } step
                    || step.ArraySlice is not { Component: 0 } slice || n.LineBufferReadBits(n.LineBufferArray)[slice.Index] != step.TargetPoint) return;
                changed = true; external = n.ProducerRawControl() with { EaseType = 9876 }; n.ProducerOverwrite(external);
            };
            var error = Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile));
            n.AfterCanvasSourceRead = null; var journal = writer.GetDynamicRendererJournal();
            Check(changed && journal.UnrecognizedState && journal.Commands.Last().Kind == SeekLineRendererCommandKind.ArraySlice
                && journal.Commands.Last().NativeEntered && !journal.ExecutionFinished && n.ProducerRawControl() == external
                && n.LineRendererNativeCalls.Count == 0 && ReferenceEquals(Data(error, "SeekLineRendererOriginalSource"), f.Source),
                "An entered physical array word lost original ownership or continued through a third control state.");
            NoNative(n, () => Reject(writer.RestoreDynamicRendererSavedValues));
            Check(n.ProducerRawControl() == external, "Selected recovery overwrote the observed third control state.");
        }
    }
}
