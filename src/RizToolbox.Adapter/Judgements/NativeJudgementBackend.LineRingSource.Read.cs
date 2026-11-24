using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            private sealed partial class LineRingReader
            {
                // An incomplete/unsupported baseline observation does not prove
                // that the immutable source graph changed. Old proof is still
                // revoked, but a later stable supported observation may retry.
                private sealed class LineRingObservationRefusedException : InvalidOperationException
                {
                    internal LineRingObservationRefusedException(string reason) : base(reason) { }
                }
                private static bool SameBits(float first, float second)
                    => BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);
                private static bool SamePoint(SeekLineRingPoint first, SeekLineRingPoint second)
                    => first.Reference == second.Reference && SameBits(first.Time, second.Time)
                        && SameBits(first.XPosition, second.XPosition) && first.EaseType == second.EaseType
                        && first.CanvasIndex == second.CanvasIndex && SameBits(first.FloorPosition, second.FloorPosition);
                private static bool SameGroup(SeekLineRingGroup first, SeekLineRingGroup second)
                    => first.Reference == second.Reference && first.RingTransform == second.RingTransform
                        && first.LinesArray == second.LinesArray && first.Lines.SequenceEqual(second.Lines);

                /// <summary>One complete observation. The public capture owns
                /// the independent second sample and source/baseline comparisons.</summary>
                public SeekLineRingSourceSnapshot ReadSnapshot()
                {
                    try
                    {
                        VerifySource();
                        var dynamicArray = RequiredReference(_control, _classes[3], "dynamicLines", "InLevel.LineInformation[]", 0x28);
                        var groupsArray = RequiredReference(_control, _classes[3], "lineGroups", "InLevel.LinesGroupInformation[]", 0x30);
                        var dynamicReferences = ArrayReferences(dynamicArray, _classes[4], NativeSeekLineRingSourceCapture.MaximumDynamicOccurrences);
                        var groupReferences = ArrayReferences(groupsArray, _classes[6], NativeSeekLineRingSourceCapture.MaximumGroupOccurrences);
                        var lines = new Dictionary<long, SeekLineRingLine>();
                        var groups = new Dictionary<long, SeekLineRingGroup>();
                        var points = new Dictionary<long, SeekLineRingPoint>();
                        var groupBaselines = new Dictionary<long, SeekLineRingGroupBaseline>();
                        var transformSeeds = new HashSet<long>();
                        var pending = new Queue<(bool Group, long Reference)>();
                        foreach (var reference in dynamicReferences) if (reference != 0) pending.Enqueue((false, reference));
                        foreach (var reference in groupReferences) if (reference != 0) pending.Enqueue((true, reference));
                        var totalSegments = 0;
                        while (pending.Count != 0)
                        {
                            VerifyLocal();
                            var next = pending.Dequeue();
                            if (next.Group)
                            {
                                var value = ReadGroup(next.Reference);
                                if (groups.TryGetValue(next.Reference, out var previous))
                                {
                                    if (!SameGroup(previous, value)) throw Changed("Repeated actual group observations disagree on their complete source graph.");
                                    continue;
                                }
                                if (groups.Count >= NativeSeekLineRingSourceCapture.MaximumGroups)
                                    throw new NotSupportedException("The complete actual line/ring group closure exceeds its supported bound.");
                                totalSegments = checked(totalSegments + value.Lines.Count);
                                if (totalSegments > NativeSeekLineRingSourceCapture.MaximumGroupLineOccurrences)
                                    throw new NotSupportedException("Complete group.lines occurrences exceed the aggregate supported bound.");
                                groups.Add(next.Reference, value);
                                var root = Root(next.Reference);
                                groupBaselines.Add(next.Reference, new(next.Reference,
                                    Scalar<int>(root, _classes[6], "nowLineIndex", "System.Int32", 0x70),
                                    Scalar<bool>(root, _classes[6], "ringActive", "System.Boolean", 0x29)));
                                if (value.RingTransform != 0) transformSeeds.Add(value.RingTransform);
                                foreach (var reference in value.Lines) if (reference != 0) pending.Enqueue((false, reference));
                            }
                            else
                            {
                                var value = ReadLine(next.Reference);
                                ObservePoint(value.StartPoint, points); ObservePoint(value.EndPoint, points);
                                if (lines.TryGetValue(next.Reference, out var previous))
                                {
                                    if (previous != value) throw Changed("Repeated actual segment observations disagree on their source references or inputs.");
                                    continue;
                                }
                                if (lines.Count >= NativeSeekLineRingSourceCapture.MaximumLines)
                                    throw new NotSupportedException("The complete actual line/end-line closure exceeds its supported bound.");
                                lines.Add(next.Reference, value);
                                transformSeeds.Add(value.Transform);
                                if (value.EndTransform != 0) transformSeeds.Add(value.EndTransform);
                                if (value.EndLine != 0) pending.Enqueue((false, value.EndLine));
                                if (value.Group != 0) pending.Enqueue((true, value.Group));
                            }
                        }

                        var controlBaseline = new SeekLineRingControlBaseline(
                            BitConverter.SingleToInt32Bits(Scalar<float>(_control, _classes[3], "_nowTime", "System.Single", 0x40)),
                            BitConverter.SingleToInt32Bits(Scalar<float>(_control, _classes[3], "_sceneWidth", "System.Single", 0x44)),
                            BitConverter.SingleToInt32Bits(Scalar<float>(_control, _classes[3], "_speed", "System.Single", 0x48)),
                            BitConverter.SingleToInt32Bits(Scalar<float>(_control, _classes[3], "_gameSceneScale", "System.Single", 0x4c)));
                        var transforms = ReadTransformClosure(transformSeeds);
                        var chartPoints = ReadChartPoints(points);
                        var noteJoins = ReadNoteJoins(lines);
                        var identity = new SeekLineRingSourceIdentity(SourceCapture, SourceCapture.Plan,
                            _source._core._noteViewIdentity, _source._core._informationAddress, _source._canvasAddress, _controlAddress);
                        var dynamicAddress = Address(dynamicArray); var groupsAddress = Address(groupsArray);
                        // Revisit every complete source vector after traversal,
                        // retaining every occurrence rather than only a prefix.
                        if (Address(RequiredReference(_control, _classes[3], "dynamicLines", "InLevel.LineInformation[]", 0x28)) != dynamicAddress
                            || Address(RequiredReference(_control, _classes[3], "lineGroups", "InLevel.LinesGroupInformation[]", 0x30)) != groupsAddress
                            || !ArrayReferences(dynamicArray, _classes[4], NativeSeekLineRingSourceCapture.MaximumDynamicOccurrences).SequenceEqual(dynamicReferences)
                            || !ArrayReferences(groupsArray, _classes[6], NativeSeekLineRingSourceCapture.MaximumGroupOccurrences).SequenceEqual(groupReferences))
                            throw Changed("The original complete LineControl source arrays changed during observation.");
                        foreach (var group in groups.Values)
                            if (!SameGroup(group, ReadGroup(group.Reference)))
                                throw Changed("A complete actual group.lines source changed during graph traversal.");
                        VerifySource(); VerifyLocal();
                        return new(identity, dynamicAddress, Array.AsReadOnly(dynamicReferences), groupsAddress, Array.AsReadOnly(groupReferences),
                            Array.AsReadOnly(lines.Values.OrderBy(x => x.Reference).ToArray()),
                            Array.AsReadOnly(points.Values.OrderBy(x => x.Reference).ToArray()),
                            Array.AsReadOnly(groups.Values.OrderBy(x => x.Reference).ToArray()), controlBaseline,
                            Array.AsReadOnly(groupBaselines.Values.OrderBy(x => x.Reference).ToArray()), Array.AsReadOnly(transforms),
                            Array.AsReadOnly(chartPoints), Array.AsReadOnly(noteJoins));
                    }
                    catch (Exception error)
                    {
                        if (!SeekCanvasBasisCallerWithdrawal.Is(error) && error is not LineRingObservationRefusedException) _expired = true;
                        if (_expired) error.Data["SeekLineRingSourceExpired"] = true;
                        ObserveCleanupFailure(error); _basis.AttachProvenance(error); throw;
                    }
                }

                private SeekLineRingLine ReadLine(long reference)
                {
                    var root = Root(reference);
                    if (N(() => _source._core._owner._api.ObjectClass(_source.Target(root))) != _classes[4] || !Alive(root))
                        throw Changed("An actual line/ring segment died or changed its exact class.");
                    return new(reference, Scalar<bool>(root, _classes[4], "lineIsStatic", "System.Boolean", 0x20),
                        Scalar<int>(root, _classes[4], "canvasIndex", "System.Int32", 0x24),
                        Address(TransformReference("get_transform", root, required: true)),
                        Address(Reference(root, _classes[4], "startLinePoint", "InLevel.LinePoint", 0x30)),
                        Address(Reference(root, _classes[4], "endLinePoint", "InLevel.LinePoint", 0x38)),
                        Address(Reference(root, _classes[4], "endTransform", "UnityEngine.Transform", 0x40)),
                        Address(Reference(root, _classes[4], "endLineInformation", "InLevel.LineInformation", 0x58)),
                        Address(Reference(root, _classes[4], "linesGroup", "InLevel.LinesGroupInformation", 0x60)));
                }
                private SeekLineRingGroup ReadGroup(long reference)
                {
                    var root = Root(reference);
                    if (N(() => _source._core._owner._api.ObjectClass(_source.Target(root))) != _classes[6])
                        throw new UnsupportedSignatureException("An actual line/ring group has another exact class.");
                    var ring = Reference(root, _classes[6], "judgeRingTransform", "UnityEngine.Transform", 0x20);
                    var array = RequiredReference(root, _classes[6], "lines", "InLevel.LineInformation[]", 0x68);
                    var references = ArrayReferences(array, _classes[4], NativeSeekLineRingSourceCapture.MaximumGroupLineOccurrences);
                    return new(reference, Address(ring), Address(array), Array.AsReadOnly(references));
                }
                private void ObservePoint(long reference, Dictionary<long, SeekLineRingPoint> points)
                {
                    if (reference == 0) return; // The occurrence keeps its actual null identity.
                    var root = Root(reference);
                    if (N(() => _source._core._owner._api.ObjectClass(_source.Target(root))) != _classes[5])
                        throw new UnsupportedSignatureException("An actual segment endpoint has another exact LinePoint class.");
                    var value = new SeekLineRingPoint(reference,
                        Scalar<float>(root, _classes[5], "time", "System.Single", 0x10),
                        Scalar<float>(root, _classes[5], "xPosition", "System.Single", 0x14),
                        Scalar<int>(root, _classes[5], "easeType", "System.Int32", 0x1c),
                        Scalar<int>(root, _classes[5], "canvasIndex", "System.Int32", 0x20),
                        Scalar<float>(root, _classes[5], "floorPosition", "System.Single", 0x24));
                    if (points.TryGetValue(reference, out var previous))
                    {
                        if (!SamePoint(previous, value)) throw Changed("Repeated actual endpoint observations disagree on their bitwise source inputs.");
                        return;
                    }
                    if (points.Count >= NativeSeekLineRingSourceCapture.MaximumPoints)
                        throw new NotSupportedException("The complete actual endpoint closure exceeds its supported bound.");
                    points.Add(reference, value);
                }
                private SeekLineRingChartPoint[] ReadChartPoints(IReadOnlyDictionary<long, SeekLineRingPoint> points)
                {
                    var chart = _source._core._view!.ChartLines;
                    var result = new List<SeekLineRingChartPoint>();
                    var total = 0;
                    for (var line = 0; line < chart.Count; ++line)
                    {
                        total = checked(total + chart[line].Points.Count);
                        if (total > NativeSeekLineRingSourceCapture.MaximumChartPoints)
                            throw new NotSupportedException("The complete original chart point occurrence map exceeds its supported bound.");
                        for (var point = 0; point < chart[line].Points.Count; ++point)
                        {
                            var current = chart[line].Points[point];
                            if (points.TryGetValue(current.Reference, out var endpoint) && !SameBits(current.Time, endpoint.Time))
                                throw Changed("An actual endpoint contradicts the original retained chart point time.");
                            result.Add(new(line, point, chart[line].Data.Reference, current.Reference, current.Time));
                        }
                    }
                    VerifyLocal(); return result.ToArray();
                }
                private SeekLineRingNoteJoin[] ReadNoteJoins(IReadOnlyDictionary<long, SeekLineRingLine> lines)
                {
                    var notes = _source._core._view!.Notes;
                    if (notes.Count > NativeSeekLineRingSourceCapture.MaximumNoteJoins)
                        throw new NotSupportedException("Complete original runtime Note joins exceed their supported bound.");
                    var result = new SeekLineRingNoteJoin[notes.Count];
                    for (var i = 0; i < result.Length; ++i)
                    {
                        var note = notes[i]; var expected = note.Line;
                        if (lines.TryGetValue(expected.Reference, out var actual)
                            && (actual.StartPoint != expected.StartPoint || actual.EndPoint != expected.EndPoint
                                || actual.Group != expected.LinesGroup || actual.IsStatic != expected.IsStatic))
                            throw Changed("An actual line/ring source contradicts an exact original runtime Note association.");
                        result[i] = new(note.Reference, note.NoteData, note.Location, note.ChartLine, note.SegmentOrdinal,
                            expected.Reference, expected.StartPoint, expected.EndPoint, expected.LinesGroup, expected.IsStatic);
                    }
                    VerifyLocal(); return result;
                }

                private SeekLineRingTransformBaseline[] ReadTransformClosure(IEnumerable<long> seeds)
                {
                    var parents = new Dictionary<long, long>();
                    var depths = new Dictionary<long, int>();
                    foreach (var seed in seeds.OrderBy(x => x))
                    {
                        var chain = new List<long>(); var visiting = new HashSet<long>(); var cursor = seed;
                        while (cursor != 0 && !depths.ContainsKey(cursor))
                        {
                            if (!visiting.Add(cursor)) throw new LineRingObservationRefusedException("The current line/ring baseline Transform parent graph is cyclic.");
                            // Count this path before reading another parent. Its
                            // already known tail is included when depths unwind.
                            if (chain.Count >= NativeSeekLineRingSourceCapture.MaximumParentDepth)
                                throw new LineRingObservationRefusedException("The current line/ring baseline parent chain exceeds the supported depth.");
                            chain.Add(cursor);
                            if (!parents.TryGetValue(cursor, out var parent))
                            {
                                if (parents.Count >= NativeSeekLineRingSourceCapture.MaximumTransforms)
                                    throw new LineRingObservationRefusedException("The current complete line/ring baseline Transform parent closure exceeds its supported bound.");
                                var root = Root(cursor); VerifyTransform(root);
                                parent = Address(TransformReference("get_parent", root, required: false));
                                parents.Add(cursor, parent);
                            }
                            cursor = parent;
                        }
                        var depth = cursor == 0 ? 0 : depths[cursor];
                        for (var i = chain.Count - 1; i >= 0; --i)
                        {
                            depth = checked(depth + 1);
                            if (depth > NativeSeekLineRingSourceCapture.MaximumParentDepth)
                                throw new LineRingObservationRefusedException("The current line/ring baseline parent chain plus its known tail exceeds the supported depth.");
                            depths.Add(chain[i], depth);
                        }
                    }
                    // The complete bounded parent graph is admitted before a
                    // pose getter runs. Pose/parents remain baseline, not source.
                    var result = new SeekLineRingTransformBaseline[parents.Count]; var index = 0;
                    foreach (var pair in parents.OrderBy(x => x.Key))
                    {
                        var root = Root(pair.Key); VerifyTransform(root);
                        var value = new SeekLineRingTransformBaseline(pair.Key, pair.Value,
                            TransformValue<NativeVector3>("get_position", root),
                            TransformValue<NativeVector3>("get_localPosition", root),
                            TransformValue<NativeRendererQuaternion>("get_localRotation", root),
                            TransformValue<NativeVector3>("get_localScale", root));
                        if (Address(TransformReference("get_parent", root, required: false)) != pair.Value)
                            throw new LineRingObservationRefusedException("An actual line/ring Transform parent changed during baseline observation.");
                        result[index++] = value;
                    }
                    VerifyLocal(); return result;
                }
            }
        }
    }
}
