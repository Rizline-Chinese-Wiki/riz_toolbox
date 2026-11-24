using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekHoldGeometryCommands
{
    private SeekHoldGeometryPlan Evaluate(SeekHoldGeometryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!float.IsFinite(request.ChartTime) || !float.IsFinite(request.Speed) || request.Speed <= 0)
            throw new ArgumentException("Finite chart time and a positive finite geometry speed are required.");
        var baseline = _baseline!; var byReference = baseline.Notes.Select((row, ordinal) => (row.Reference, ordinal)).ToDictionary(pair => pair.Reference, pair => pair.ordinal);
        var pending = request.Seed is null ? baseline.Lists.HoldNotes.CapacitySlots.Take(baseline.Lists.HoldNotes.Size).ToArray()
            : SeekHoldState.Copy(request.Seed.HoldNotes, Local);
        var running = request.Seed is null ? baseline.Lists.RunningHoldNotes.CapacitySlots.Take(baseline.Lists.RunningHoldNotes.Size).ToArray()
            : SeekHoldState.Copy(request.Seed.RunningHoldNotes, Local);
        if (pending.Concat(running).Any(reference => !byReference.ContainsKey(reference)))
            throw new InvalidDataException("Every explicit Hold geometry seed occurrence must be an independently owned actual Hold.");
        if (baseline.Lists.HoldNotes.Reference == baseline.Lists.RunningHoldNotes.Reference && !pending.SequenceEqual(running))
            throw new InvalidDataException("One aliased actual Hold List requires identical explicit seed occurrences.");
        var steps = new List<SeekHoldGeometryStep>(); var required = new List<SeekHoldGeometryRequiredWork>
        {
            new(SeekHoldGeometryRequiredWorkKind.TargetCanvasLineRingBasis, null, null,
                "Geometry uses the captured actual canvas/line/ring/camera basis. That basis is not proven to represent the requested chart time; canvas movement, segment selection and ring producers remain required."),
            new(SeekHoldGeometryRequiredWorkKind.HoldEngineClosure, null, null, "Old Hold scheduling and engine consumers must close before complete Seek."),
            new(SeekHoldGeometryRequiredWorkKind.InputEpochClosure, null, null, "This explicit practice reset does not reconstruct historical player input."),
            new(SeekHoldGeometryRequiredWorkKind.TargetVisibility, null, null, "Target note visibility and renderer producers remain separate."),
            new(SeekHoldGeometryRequiredWorkKind.MissingRuntimeInstances, null, null, "Only actual retained Hold instances are operated; missing chart instances require independent creation."),
            new(SeekHoldGeometryRequiredWorkKind.TransformCallbackClosure, null, null, "Official Transform calls may dispatch callbacks or hidden effects; no inverse or consumer closure is claimed.")
        };
        void Add(int ordinal, SeekHoldGeometryStepKind kind)
        {
            if (steps.Count >= MaximumSteps) throw new NotSupportedException("The complete Hold geometry command plan exceeds its bounded journal.");
            steps.Add(new(ordinal, kind));
        }
        void Event(long reference, int index) => required.Add(new(SeekHoldGeometryRequiredWorkKind.RendererEvent, reference, index,
            "Native Hold evaluation would dispatch this renderer event; geometry execution records the requirement without replaying the event."));
        for (var ordinal = 0; ordinal < baseline.Notes.Count; ++ordinal)
        {
            var note = baseline.Notes[ordinal];
            Add(ordinal, SeekHoldGeometryStepKind.ParentEndFlagToCanvas); Add(ordinal, SeekHoldGeometryStepKind.EndFlagLocalPosition);
            if (baseline.EaseTables is not null && (uint)note.EaseType < (uint)baseline.EaseTables.Count)
            {
                Add(ordinal, SeekHoldGeometryStepKind.ParentHeadToLine); Add(ordinal, SeekHoldGeometryStepKind.HeadLocalPosition);
            }
            else required.Add(new(SeekHoldGeometryRequiredWorkKind.EaseTableInitialization, note.Reference, null,
                "The actual initialized ease sample row is absent. Head placement is not guessed and GetEase initialization is not invoked during preparation."));
        }
        // Explicit practice reset first rebuilds the head/end-flag placement.
        // Reanchor existing running occurrences before their actual length read.
        foreach (var reference in running)
        {
            var ordinal = byReference[reference]; Add(ordinal, SeekHoldGeometryStepKind.ParentToRing); Add(ordinal, SeekHoldGeometryStepKind.ZeroLocalXY);
        }
        var pendingModel = new LinkedList<long>(pending); var runningModel = new List<long>(running);
        var node = pendingModel.First;
        while (node is not null)
        {
            var ordinal = byReference[node.Value]; var note = baseline.Notes[ordinal];
            if (!note.IsActive) { node = node.Next; continue; }
            Add(ordinal, SeekHoldGeometryStepKind.PendingLineScale);
            while (node is not null && baseline.Notes[byReference[node.Value]].Missed)
            { Event(node.Value, 0); var next = node.Next; pendingModel.Remove(node); node = next; }
            if (node is null) break;
            ordinal = byReference[node.Value]; note = baseline.Notes[ordinal];
            if (request.ChartTime >= note.HeadChartTime)
            {
                Add(ordinal, SeekHoldGeometryStepKind.ParentToRing); Add(ordinal, SeekHoldGeometryStepKind.ZeroLocalXY);
                if (runningModel.Count >= MaximumNotes) throw new NotSupportedException("The explicit native Hold transition would exceed the retained running occurrence bound.");
                runningModel.Add(node.Value); var next = node.Next; pendingModel.Remove(node); node = next;
            }
            else node = node.Next;
        }
        var finalRunning = new List<long>();
        foreach (var reference in runningModel)
        {
            var ordinal = byReference[reference]; var note = baseline.Notes[ordinal];
            if (request.ChartTime > note.TailChartTime)
            { Event(reference, 1); Add(ordinal, SeekHoldGeometryStepKind.CompletedLineScale); continue; }
            Add(ordinal, SeekHoldGeometryStepKind.RunningLineScale);
            if (!note.RingActive) Add(ordinal, SeekHoldGeometryStepKind.AlignWorldXToLine);
            if (note.Missed) { Add(ordinal, SeekHoldGeometryStepKind.ParentToCanvas); Event(reference, 0); }
            else finalRunning.Add(reference);
        }
        var finalPending = pendingModel.ToArray(); var finalRunningArray = finalRunning.ToArray();
        if (baseline.Lists.HoldNotes.Reference == baseline.Lists.RunningHoldNotes.Reference && !finalPending.SequenceEqual(finalRunningArray))
            throw new NotSupportedException("The native Hold transition would require contradictory targets for one aliased actual List.");
        return new(request with { Seed = request.Seed is null ? null : new(Array.AsReadOnly(pending), Array.AsReadOnly(running)) },
            Array.AsReadOnly(steps.ToArray()), Array.AsReadOnly(finalPending), Array.AsReadOnly(finalRunningArray), Array.AsReadOnly(required.ToArray()));
    }
    private static float Ease(float progress, IReadOnlyList<float> samples)
    {
        var s = progress * 100f;
        if (!float.IsFinite(s) || (double)s < int.MinValue || (double)s > int.MaxValue)
            throw new NotSupportedException("The captured line basis produces an unrepresentable native ease sample index.");
        var index = (int)s;
        if (index < 0) return Math.Clamp(samples[0], 0f, 1f);
        if (index >= 100) return Math.Clamp(samples[100], 0f, 1f);
        var fraction = s - (float)index; var difference = samples[index + 1] - samples[index];
        var sampled = fraction * difference + samples[index]; return Math.Clamp(sampled, 0f, 1f);
    }
    private SeekHoldGeometryOperation? Resolve(SeekHoldGeometryStep step, SeekHoldGeometrySnapshot before)
    {
        var note = _baseline!.Notes[step.Ordinal]; var transforms = before.Transforms.ToDictionary(row => row.Reference);
        SeekHoldGeometryTransformState Transform(long reference) => transforms.TryGetValue(reference, out var value) ? value
            : throw new InvalidDataException("The selected actual Hold transform closure is incomplete.");
        var head = Transform(note.NoteTransform); var flag = Transform(note.EndFlagTransform); var uiX = Transform(before.UiRootTransform).LocalScale.X;
        SeekHoldGeometryOperation Op(SeekHoldGeometryOperationKind kind, long target, NativeVector3 value, long parent = 0)
        {
            if (!SeekHoldGeometryState.Finite(value)) throw new NotSupportedException("A chosen Hold geometry vector is not finite.");
            return new(step.Ordinal, kind, target, parent, true, value);
        }
        switch (step.Kind)
        {
            case SeekHoldGeometryStepKind.ParentEndFlagToCanvas: return Op(SeekHoldGeometryOperationKind.SetParent, note.EndFlagTransform, default, note.EndFlagCanvasParent);
            case SeekHoldGeometryStepKind.EndFlagLocalPosition: return Op(SeekHoldGeometryOperationKind.SetLocalPosition, note.EndFlagTransform, new(0, note.EndFloorPosition * _plan!.Request.Speed, 0));
            case SeekHoldGeometryStepKind.ParentHeadToLine: return Op(SeekHoldGeometryOperationKind.SetParent, note.NoteTransform, default, note.LineTransform);
            case SeekHoldGeometryStepKind.HeadLocalPosition:
            {
                var cameraX = Transform(before.CameraTransform).LocalScale.X; var scale = uiX * cameraX;
                var a = Transform(note.LineTransform).WorldPosition; var b = Transform(note.EndLineTransform).WorldPosition;
                var y = ((note.NoteFloorPosition - note.StartFloorPosition) * _plan!.Request.Speed) * scale;
                var q = y / (b.Y - a.Y); var eased = Ease(q, before.EaseTables![note.EaseType].Progress);
                var x = (((b.X - a.X) - 0f) * eased + 0f) / scale;
                var z = ((2f * .05f) - .5f) / scale;
                return Op(SeekHoldGeometryOperationKind.SetLocalPosition, note.NoteTransform, new(x, y / scale, z));
            }
            case SeekHoldGeometryStepKind.ParentToRing: return Op(SeekHoldGeometryOperationKind.SetParent, note.NoteTransform, default, note.RingTransform);
            case SeekHoldGeometryStepKind.ZeroLocalXY:
                _noteTargets[step.Ordinal] = _noteTargets[step.Ordinal] with { IsStaticNote = true };
                return Op(SeekHoldGeometryOperationKind.SetLocalPosition, note.NoteTransform, new(0, 0, head.LocalPosition.Z));
            case SeekHoldGeometryStepKind.PendingLineScale:
            {
                var length = (flag.WorldPosition.Y - head.WorldPosition.Y) / uiX;
                if (!float.IsFinite(length)) throw new NotSupportedException("The captured pending Hold basis does not yield a finite length.");
                _lastPendingLength = length;
                var previous = _noteTargets[step.Ordinal].LastFrameHoldLineLength;
                if (Math.Abs((double)(previous - length)) <= 1e-6 || float.IsNaN(previous - length)) return null;
                _noteTargets[step.Ordinal] = _noteTargets[step.Ordinal] with { LastFrameHoldLineLength = length };
                return Op(SeekHoldGeometryOperationKind.SetLocalScale, note.HoldLineTransform,
                    new(1f + 0f * length, 0f + 1f * length, 1f + 0f * length));
            }
            case SeekHoldGeometryStepKind.RunningLineScale:
            {
                var difference = flag.WorldPosition.Y - head.WorldPosition.Y;
                return Op(SeekHoldGeometryOperationKind.SetLocalScale, note.HoldLineTransform,
                    new((0f * difference) / uiX + 1f, (1f * difference) / uiX + 0f, (0f * difference) / uiX + 1f));
            }
            case SeekHoldGeometryStepKind.AlignWorldXToLine:
                return Op(SeekHoldGeometryOperationKind.SetWorldPosition, note.NoteTransform, new(Transform(note.LineTransform).WorldPosition.X, head.WorldPosition.Y, head.WorldPosition.Z));
            case SeekHoldGeometryStepKind.ParentToCanvas: return Op(SeekHoldGeometryOperationKind.SetParent, note.NoteTransform, default, note.CanvasParent);
            case SeekHoldGeometryStepKind.CompletedLineScale: return Op(SeekHoldGeometryOperationKind.SetLocalScale, note.HoldLineTransform, new(1f, 0f, 1f));
            default: throw new ArgumentOutOfRangeException(nameof(step));
        }
    }
}
