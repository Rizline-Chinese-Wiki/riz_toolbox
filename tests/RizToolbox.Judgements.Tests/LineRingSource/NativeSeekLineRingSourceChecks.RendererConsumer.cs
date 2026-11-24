using System.Collections;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Judgements.Tests.NoteFlags;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    internal static readonly (string Name, Action Run)[] RendererConsumerCases =
    {
        ("Line ring source renderer dispatches two distinct Lines over one original scratch and reverses selected storage", RendererDistinctLines),
        ("Line ring source renderer leaves culled occurrences and group only saved aliases untouched", RendererCull),
        ("Line ring source renderer restores saved fields after an entered native exception", RendererExceptionalFields),
        ("Line ring source renderer quarantines a native callback third state without inverse", RendererThirdState),
        ("Line ring source renderer never inverses an external matching producer word before its own store", RendererExternalTarget),
        ("Line ring source renderer rejects workers and preparation callback reentry before native", RendererWorkerReentry),
        ("Line ring source renderer retains original roots when a native callback disposes its owner", RendererCallbackDisposal),
        ("Line ring source renderer retains actual native entry after exact temporary pin cleanup fails", RendererUnknownCleanup),
        ("Line ring source renderer refuses actual insufficient scratch capacity before any write", RendererCapacity),
        ("Line ring source renderer rechecks original Canvas values after native dispatch admission", RendererFinalCanvas),
        ("Line ring source renderer rejects duplicate Canvas child acquisition and keeps its original accessors", RendererOriginalCanvasChildren),
        ("Line ring source renderer restores a longer original saved count before its array reference", RendererLongerOriginalArray)
    };

    private static NativeVector3BufferBits[] RendererOriginalScratch()
    {
        var values = Enumerable.Range(0, 107).Select(i => new NativeVector3BufferBits(
            BitConverter.SingleToInt32Bits(i + .125f), BitConverter.SingleToInt32Bits(i + .25f),
            BitConverter.SingleToInt32Bits(i + .5f))).ToArray();
        values[103] = new(unchecked((int)0x7fc12345), unchecked((int)0x80000000), 0);
        return values;
    }

    private static (WriterFixture Fixture, NativeSeekLineEndpointCommands Writer) StartRenderer(
        RendererSyntheticImage image, Action<FakeNative>? configure = null, int capacity = 107)
    {
        var f = new WriterFixture(parentEqual: false, configure: n =>
        {
            ConfigureDynamicInputs(n);
            // A small actual parent TRS keeps the second pair close. The first
            // pair's large X span still produces 101 points. Both counts come
            // from production arithmetic over real synthetic world getters.
            n.GeometrySetTransform(n.GeometryUiRoot, n.GeometryTransforms[n.GeometryUiRoot] with
            { Scale = new(.001f, .001f, .001f) });
            n.LineRingSet(n.NoteViewPoints[0][0], "xPosition", 500f);
            n.LineRingSet(n.NoteViewPoints[0][1], "xPosition", -500f);
            n.ConfigureLineBuffer(RendererOriginalScratch().Take(capacity).ToArray());
            n.ConfigureLineRendererConsumer(image.Address);
            configure?.Invoke(n);
        });
        try
        {
            f.Basis.Execute();
            var writer = f.Prepare(); writer.Execute();
            // Entered endpoint effects already retain this exact backend. Mark
            // it before assertions so failure diagnostics cannot free roots.
            f.Retain();
            f.Native.ProducerBeginBoundedHistory();
            return (f, writer);
        }
        catch { f.Dispose(); throw; }
    }

    private static NativeVector3BufferBits[] RendererBits(SeekLineRingDynamicOccurrence row)
        => row.Projection.Points.Select(p => new NativeVector3BufferBits(Bits(p.X), Bits(p.Y), Bits(p.Z))).ToArray();

    private static NativeVector3BufferBits RendererWorldBits(NativeVector3 value)
        => new(Bits(value.X), Bits(value.Y), Bits(value.Z));

    private static NativeLineProducerControlSnapshot RendererOriginalControl(FakeNative native)
        => new(native.LineRingControl, native.ProducerInitialStart, native.ProducerInitialEnd,
            native.ProducerInitialNew, -17);

    private static void CheckRendererProducerTrace(SeekLineRendererJournal journal,
        SeekLineRingSourceSnapshot source)
    {
        var cursor = 0;
        void Control(SeekLineRingDynamicOccurrence row, NativeLineProducerField field,
            int component, int components, NativeVector3BufferBits bits)
        {
            var command = journal.Commands[cursor];
            Check(command.Sequence == cursor++ && command.Occurrence == row.Ordinal && command.Line == row.Line
                && command.Kind == SeekLineRendererCommandKind.ProducerControl
                && command.ControlPatch == new NativeLineProducerPatch(field, component, components, bits)
                && command.BeforeControl is not null && command.TargetControl is not null,
                "The complete producer journal changed a control word's source order, identity or value.");
        }
        void Slice(SeekLineRingDynamicOccurrence row, int sample, int component, int components,
            NativeVector3BufferBits bits)
        {
            var command = journal.Commands[cursor];
            Check(command.Sequence == cursor++ && command.Occurrence == row.Ordinal && command.Line == row.Line
                && command.Kind == SeekLineRendererCommandKind.ArraySlice
                && command.ArraySlice == new NativeVector3BufferSlice(sample, component, components, bits)
                && command.BeforePoint is not null && command.TargetPoint is not null,
                "The complete producer journal changed an array word's source order, index or value.");
        }
        foreach (var row in journal.Occurrences)
        {
            Check(row.WouldSetPositions, "The distinct-Line producer trace unexpectedly contains a culled occurrence.");
            var start = RendererWorldBits(row.Projection.Input.StartWorld);
            var end = RendererWorldBits(row.Projection.Input.EndWorld);
            Control(row, NativeLineProducerField.StartPosition, 0, 2, start);
            Control(row, NativeLineProducerField.StartPosition, 2, 1, start);
            Control(row, NativeLineProducerField.EndPosition, 0, 2, end);
            Control(row, NativeLineProducerField.EndPosition, 2, 1, end);
            var ease = source.Points.Single(point => point.Reference == row.StartPoint).EaseType;
            Control(row, NativeLineProducerField.EaseType, 0, 1, new(ease, 0, 0));
            var samples = RendererBits(row);
            for (var sample = 0; sample < samples.Length; ++sample)
            {
                Control(row, NativeLineProducerField.NewLinePosition, 2, 1, samples[sample]);
                Control(row, NativeLineProducerField.NewLinePosition, 0, 1, samples[sample]);
                Control(row, NativeLineProducerField.NewLinePosition, 1, 1, samples[sample]);
                Slice(row, sample, 0, 2, samples[sample]);
                Slice(row, sample, 2, 1, samples[sample]);
            }
            var dispatch = journal.Commands[cursor];
            Check(dispatch.Sequence == cursor++ && dispatch.Occurrence == row.Ordinal
                && dispatch.Line == row.Line && dispatch.Kind == SeekLineRendererCommandKind.SetPositions
                && dispatch.TargetSaved?.SavedCount == samples.Length,
                "The complete producer journal did not dispatch after its final array word.");
        }
        Check(cursor == journal.Commands.Count && journal.Commands.All(command =>
            command.Kind is not SeekLineRendererCommandKind.Prefix and not SeekLineRendererCommandKind.RestorePrefix),
            "The complete producer journal omitted a word or wrote a synthetic prefix.");
    }

    private static void RendererDistinctLines()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image);
        using (f)
        {
            var n = f.Native;
            var result = writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile);
            Check(result.Prepared && result.ExecutionFinished && result.DispatchedOccurrences == 3
                && result.Occurrences.Count == 3 && result.Occurrences.Select(row => row.Line).Distinct().Count() == 2
                && result.Occurrences[0].Line == result.Occurrences[2].Line
                && result.Occurrences.All(row => row.WouldSetPositions)
                && result.Occurrences.Select(row => row.Projection.PositionCount)
                    .SequenceEqual(new int?[] { 101, 6, 101 })
                && result.Baseline.Count == f.Source!.Snapshot.Lines.Count
                && result.Baseline.Any(row => !result.Occurrences.Any(o => o.Line == row.Line))
                && n.LineRendererNativeCalls.Count == 3 && n.LineRendererArgumentsRooted
                && n.LineRendererNativeCalls.All(call => call.Array == n.LineBufferArray)
                && n.LineBufferAssociation == n.LineBufferArray
                && result.Commands.Count == 1058 && result.Commands.All(step => step.AdmissionEntered
                    && step.NativeEntered && step.NativeReturned && step.TargetObserved)
                && result.BaselineControl == RendererOriginalControl(n)
                && result.ExpectedControl == n.ProducerRawControl()
                && n.ProducerRawCount == FakeNative.ProducerCountCanary,
                "The original consumer omitted distinct Lines, counts, alias occurrences, full domain or exact native entry.");
            CheckRendererProducerTrace(result, f.Source!.Snapshot);
            var expected = RendererOriginalScratch();
            for (var i = 0; i < result.Occurrences.Count; ++i)
            {
                var row = result.Occurrences[i]; var prefix = RendererBits(row);
                Array.Copy(prefix, expected, prefix.Length);
                Check(n.LineRendererNativeCalls[i].Line.ToInt64() == row.Line
                    && n.LineRendererNativeCalls[i].Count == prefix.Length
                    && n.LineRendererNativeCalls[i].CompleteScratch.SequenceEqual(expected),
                    "Native dispatch order, explicit shorter count, shared prefix or complete unwritten tail differs.");
            }
            Check(n.LineRendererUnityCalls == 2 && result.Expected.All(row =>
                result.Occurrences.Any(o => o.Line == row.Line) ? row.SavedArray == n.LineBufferArray.ToInt64()
                    : row == result.Baseline.Single(before => before.Line == row.Line))
                && result.Baseline.Any(row => row.SavedArray == n.LineBufferArray.ToInt64()
                    && !result.Occurrences.Any(o => o.Line == row.Line))
                && !result.CompleteSeekPermitted && !result.AtomicSnapshot && !result.ExternalConsumersClosed
                && ReferenceEquals(result.OriginalSource, f.Source) && ReferenceEquals(result.EffectsOwner, writer),
                "Inactive saved-field branch or original retained owner was lost.");
            writer.RestoreDynamicRendererSavedValues();
            Check(writer.VerifyRestoredDynamicRendererSavedValues(), "The selected renderer baseline did not restore.");
            var restored = writer.GetDynamicRendererJournal();
            Check(restored.Expected.SequenceEqual(result.Baseline)
                && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(RendererOriginalScratch())
                && restored.ExpectedControl == result.BaselineControl
                && n.ProducerRawControl() == RendererOriginalControl(n)
                && n.ProducerRawCount == FakeNative.ProducerCountCanary
                && n.LineRendererNativeCalls.Count == 3 && n.LineRendererUnityCalls == 2
                && restored.HasRetainedResponsibility && restored.SideEffectsUnknown
                && result.Commands.Count == 1058 && result.ExpectedScratch!.Items.SequenceEqual(expected)
                && n.LineRendererInverseStores.Select(row => row.Name).SequenceEqual(new[]
                    { "_positionCountPlan", "_positionsPlan", "_positionCountPlan" }),
                "Recovery replayed Unity, changed history, lost inverse order or failed full raw storage restoration.");
            var error = Reject(writer.Dispose);
            Check(ReferenceEquals(Data(error, "SeekLineRendererEffectsOwner"), writer)
                && ReferenceEquals(Data(error, "SeekLineRendererOriginalSource"), f.Source),
                "Restoration released the original renderer effects/source responsibility.");
        }
    }

    private static void RendererCull()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image, n =>
        {
            n.LineRingSet(n.NoteViewPoints[0][0], "floorPosition", 10000f);
            n.LineRingSet(n.NoteViewPoints[0][1], "floorPosition", 10000f);
        });
        using (f)
        {
            var n = f.Native;
            var result = writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile);
            Check(result.SkippedOccurrences == 2 && result.DispatchedOccurrences == 1
                && result.Occurrences.Select(row => row.Projection.PositionCount)
                    .SequenceEqual(new int?[] { null, 6, null })
                && result.Commands.Count == 38
                && result.Commands.Count(step => step.Kind == SeekLineRendererCommandKind.ProducerControl) == 23
                && result.Commands.Count(step => step.Kind == SeekLineRendererCommandKind.ArraySlice) == 12
                && result.Commands.Count(step => step.Kind == SeekLineRendererCommandKind.SetPositions) == 1
                && result.Commands.Count(step => step.Kind == SeekLineRendererCommandKind.Cull) == 2
                && result.Commands.Where(step => step.Kind == SeekLineRendererCommandKind.Cull)
                    .All(step => !step.AdmissionEntered && !step.NativeEntered && step.TargetScratch is null)
                && result.Occurrences.Where(row => !row.WouldSetPositions).All(row => row.Projection.PositionCount is null)
                && n.LineRendererNativeCalls.Single().Line == n.LineRendererSecondDynamic
                && n.LineRendererSavedArray(n.LineRendererFirstDynamic) == n.LineBufferArray
                && n.LineRendererSavedCount(n.LineRendererFirstDynamic) == 2
                && result.BaselineControl == RendererOriginalControl(n)
                && result.ExpectedControl == n.ProducerRawControl()
                && n.ProducerRawCount == FakeNative.ProducerCountCanary,
                "A culled occurrence wrote producer words/count zero or lost its previous saved alias.");
            writer.RestoreDynamicRendererSavedValues();
            Check(writer.VerifyRestoredDynamicRendererSavedValues()
                && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(RendererOriginalScratch())
                && n.ProducerRawControl() == RendererOriginalControl(n)
                && n.ProducerRawCount == FakeNative.ProducerCountCanary,
                "Recovery lost the culled line's original shared array tail.");
        }
    }

    private static void RendererExceptionalFields()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image);
        using (f)
        {
            var n = f.Native; n.LineRendererExceptionAfterFields = true;
            var error = Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile));
            n.LineRendererExceptionAfterFields = false;
            var journal = writer.GetDynamicRendererJournal(); var command = journal.Commands.Last();
            Check(command.Kind == SeekLineRendererCommandKind.SetPositions && command.NativeEntered
                && !command.NativeReturned && command.TargetObserved && !journal.UnrecognizedState
                && n.LineRendererNativeCalls.Count == 1 && !journal.ExecutionFinished
                && ReferenceEquals(Data(error, "SeekLineRendererEffectsOwner"), writer),
                "An exception after native saved fields lost actual entry, observed target or original owner.");
            writer.RestoreDynamicRendererSavedValues();
            Check(writer.VerifyRestoredDynamicRendererSavedValues() && n.LineRendererNativeCalls.Count == 1
                && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(RendererOriginalScratch())
                && n.ProducerRawControl() == RendererOriginalControl(n)
                && n.ProducerRawCount == FakeNative.ProducerCountCanary,
                "Exceptional selected recovery replayed native rendering or failed baseline restoration.");
        }
    }

    private static void RendererThirdState()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image);
        using (f)
        {
            var n = f.Native;
            n.AfterLineRendererFieldsSaved = call => n.LineRendererSetSaved(call.Line, call.Array, -123);
            Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile));
            n.AfterLineRendererFieldsSaved = null;
            var journal = writer.GetDynamicRendererJournal();
            Check(journal.UnrecognizedState && journal.Commands.Last().NativeEntered
                && n.LineRendererSavedCount(n.LineRendererFirstDynamic) == -123,
                "The entered consumer accepted an external third saved-count state.");
            var before = n.LineBufferReadBits(n.LineBufferArray);
            var beforeControl = n.ProducerRawControl();
            Reject(writer.RestoreDynamicRendererSavedValues);
            Check(n.LineRendererInverseStores.Count == 0 && n.LineRendererNativeCalls.Count == 1
                && before.SequenceEqual(n.LineBufferReadBits(n.LineBufferArray))
                && beforeControl == n.ProducerRawControl(),
                "An unrecognized native state was overwritten by selected recovery.");
        }
    }

    private static void RendererExternalTarget()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image);
        using (f)
        {
            var n = f.Native;
            var preview = NativeSeekLineRingDynamicConsumer.Project(f.Basis, writer);
            var target = NativeRuntime.PatchLineProducerControl(RendererOriginalControl(n),
                new(NativeLineProducerField.StartPosition, 0, 2,
                    RendererWorldBits(preview.Occurrences[0].Projection.Input.StartWorld)));
            var changed = false;
            n.AfterCanvasSourceRead = (name, owner) =>
            {
                if (changed || name != "startPosition" || owner != n.LineRingControl
                    || Field(writer, "_rendererPending") is not int pending || pending < 0) return;
                var commands = (IList)Field(writer, "_rendererCommands")!;
                if (commands[pending] is not SeekLineRendererCommandEffect command
                    || command.Kind != SeekLineRendererCommandKind.ProducerControl
                    || command.ControlPatch?.Field != NativeLineProducerField.StartPosition
                    || command.ControlPatch?.Component != 0) return;
                changed = true; n.ProducerOverwrite(target);
            };
            Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile));
            n.AfterCanvasSourceRead = null;
            var journal = writer.GetDynamicRendererJournal();
            Check(changed && journal.UnrecognizedState && journal.Commands.Count == 1
                && journal.Commands[0].AdmissionEntered && !journal.Commands[0].NativeEntered
                && journal.Commands[0].TargetControl == target
                && n.LineRendererNativeCalls.Count == 0,
                "A matching externally written producer word was adopted as this owner's entered store.");
            Reject(writer.RestoreDynamicRendererSavedValues);
            Check(n.ProducerRawControl() == target
                && n.ProducerRawCount == FakeNative.ProducerCountCanary
                && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(RendererOriginalScratch())
                && n.LineRendererInverseStores.Count == 0,
                "Recovery inverted an external producer word despite no own store entry.");
        }
    }

    private static void RendererWorkerReentry()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image);
        using (f)
        {
            var n = f.Native;
            NoNative(n, () => Task.Run(() => Reject(() => writer.ApplyDynamicRenderer(f.Basis,
                n.LineRendererNativeProfile))).GetAwaiter().GetResult());
            var entered = false;
            n.AfterCanvasSourceRead = (name, _) =>
            {
                if (entered || name != "_positionsPlan") return;
                entered = true;
                NoNative(n, () => Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile)));
                NoNative(n, () => Reject(() => writer.PrepareOriginalLinePositionsBuffer()));
                NoNative(n, () => Reject(writer.RestoreSelectedValues));
            };
            writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile);
            n.AfterCanvasSourceRead = null;
            Check(entered && n.LineRendererNativeCalls.Count == 3, "A renderer callback recursively entered another operation.");
            writer.RestoreDynamicRendererSavedValues(); Check(writer.VerifyRestoredDynamicRendererSavedValues(), "Reentry refusal damaged normal recovery.");
        }
    }

    private static void RendererCallbackDisposal()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image);
        using (f)
        {
            var n = f.Native; var roots = n.CanvasBasisCurrentRoots();
            n.AfterLineRendererFieldsSaved = _ => writer.Dispose();
            var error = Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile));
            n.AfterLineRendererFieldsSaved = null;
            var journal = writer.GetDynamicRendererJournal();
            Check(journal.Commands.Last().NativeEntered && journal.HasRetainedResponsibility
                && n.LineRendererNativeCalls.Count == 1
                && ReferenceEquals(Data(error, "SeekLineRendererEffectsOwner"), writer),
                "Disposal after native storage lost actual entry or the original retained effects owner.");
            OriginalRootsRemain(n, roots, "Native renderer callback disposal freed the original source roots.");
            NoNative(n, () => Reject(writer.RestoreDynamicRendererSavedValues));
        }
    }

    private static void RendererUnknownCleanup()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image);
        using (f)
        {
            var n = f.Native; uint failed = 0;
            n.AfterLineRendererFieldsSaved = _ =>
            {
                var active = n.LineBufferActiveHandles(n.LineBufferArray);
                failed = n.LineBufferFailActiveTemporaryPin(n.LineBufferArray, active[0]);
            };
            var error = Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile));
            n.AfterLineRendererFieldsSaved = null;
            var journal = writer.GetDynamicRendererJournal();
            Check(failed != 0 && journal.Commands.Last().NativeEntered && journal.Commands.Last().NativeReturned
                && journal.HasRetainedResponsibility && n.LineRendererNativeCalls.Count == 1
                && Data(error, "NativePublicationCleanupOwner") is not null,
                "Unknown temporary cleanup lost actual dispatch and exact publication responsibility.");
            var frees = n.FreeHandleAttempts;
            NoNative(n, () => Reject(writer.RestoreDynamicRendererSavedValues));
            Check(n.FreeHandleAttempts == frees, "Unknown exact native cleanup was retried during renderer recovery.");
        }
    }

    private static void RendererCapacity()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image, capacity: 5);
        using (f)
        {
            var n = f.Native;
            Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile));
            var journal = writer.GetDynamicRendererJournal();
            Check(journal.Prepared && !journal.ExecutionEntered && journal.Commands.Count == 0
                && n.LineRendererNativeCalls.Count == 0
                && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(RendererOriginalScratch().Take(5))
                && n.ProducerRawControl() == RendererOriginalControl(n)
                && n.ProducerRawCount == FakeNative.ProducerCountCanary,
                "The consumer assumed capacity 101 instead of refusing the actual short array before any effect.");
            writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(),
                "A renderer preflight refusal with no admitted effect blocked independent endpoint recovery.");
        }
    }

    private static void RendererFinalCanvas()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image);
        using (f)
        {
            var n = f.Native; var changed = false;
            n.AfterCanvasSourceRead = (name, owner) =>
            {
                if (changed || name != "_positionCountPlan"
                    || Field(writer, "_rendererPending") is not int pending || pending < 0) return;
                var commands = (IList)Field(writer, "_rendererCommands")!;
                if (commands[pending] is not SeekLineRendererCommandEffect command
                    || command.Kind != SeekLineRendererCommandKind.SetPositions) return;
                changed = true;
                n.CanvasCacheSetBits(0, 0, Bits(4321f));
            };
            Reject(() => writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile));
            n.AfterCanvasSourceRead = null;
            var journal = writer.GetDynamicRendererJournal();
            Check(changed && journal.Commands.Count == 511
                && journal.Commands.Take(510).All(command => command.NativeEntered && command.NativeReturned)
                && journal.Commands[510].Kind == SeekLineRendererCommandKind.SetPositions
                && journal.Commands[510].AdmissionEntered && !journal.Commands[510].NativeEntered
                && n.LineRendererNativeCalls.Count == 0 && n.LineRendererSavedCount(n.LineRendererFirstDynamic) == 2,
                "Canvas cache drift after dispatch admission reached the original native SetPositions.");
            writer.RestoreDynamicRendererSavedValues(); Check(writer.VerifyRestoredDynamicRendererSavedValues(),
                "A rejected before-native dispatch lost selected scratch recovery.");
            Check(n.CanvasBasisDetachedBits(n.GeometryFloorArray)[0] == Bits(4321f)
                && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(RendererOriginalScratch())
                && n.ProducerRawControl() == RendererOriginalControl(n)
                && n.ProducerRawCount == FakeNative.ProducerCountCanary,
                "Renderer recovery overwrote the independent external Canvas mutation.");
        }
    }

    private static void RendererOriginalCanvasChildren()
    {
        using var image = new RendererSyntheticImage();
        var (f, writer) = StartRenderer(image);
        using (f)
        {
            var n = f.Native;
            var basis = (INativeSeekCanvasBasisAccess)Field(f.Basis, "_basis")!;
            NoNative(n, () => Reject(() => basis.CreateCacheAccess()));
            NoNative(n, () => Reject(() => basis.CreatePositionAccess(n.CanvasPositionProfile)));
            var journal = writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile);
            Check(journal.ExecutionFinished && n.LineRendererNativeCalls.Count == 3,
                "Rejected duplicate child acquisition replaced or withdrew the original aggregate's exact Canvas accessors.");
            writer.RestoreDynamicRendererSavedValues(); Check(writer.VerifyRestoredDynamicRendererSavedValues(),
                "Duplicate Canvas child acquisition refusal damaged selected renderer recovery.");
        }
    }

    private static void RendererLongerOriginalArray()
    {
        using var image = new RendererSyntheticImage();
        IntPtr original = default;
        var (f, writer) = StartRenderer(image, n =>
        {
            original = n.LineBufferNewArray(Enumerable.Range(0, 112).Select(i => new NativeVector3BufferBits(
                Bits(i), Bits(-i), unchecked((int)0x80000000))).ToArray());
            n.LineRendererSetSaved(n.LineRendererSecondDynamic, original, 110);
        });
        using (f)
        {
            var n = f.Native; var before = n.LineBufferReadBits(original);
            writer.ApplyDynamicRenderer(f.Basis, n.LineRendererNativeProfile);
            writer.RestoreDynamicRendererSavedValues();
            Check(writer.VerifyRestoredDynamicRendererSavedValues()
                && n.LineRendererSavedArray(n.LineRendererSecondDynamic) == original
                && n.LineRendererSavedCount(n.LineRendererSecondDynamic) == 110
                && n.LineBufferReadBits(original).SequenceEqual(before),
                "Count-first inverse rejected its bounded intermediate count or changed the longer original saved array.");
            var stores = n.LineRendererInverseStores.Where(row => row.Line == n.LineRendererSecondDynamic).ToArray();
            Check(stores.Select(row => row.Name).SequenceEqual(new[] { "_positionCountPlan", "_positionsPlan" })
                && stores[0].Value == 110 && stores[1].Value == original.ToInt64(),
                "Selected inverse did not restore count before original saved-array reference.");
        }
    }
}
