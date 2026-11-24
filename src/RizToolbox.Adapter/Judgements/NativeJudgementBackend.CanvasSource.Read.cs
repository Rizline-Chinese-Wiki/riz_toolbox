using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            private NativeRuntime.RootHandle Root(long reference)
            {
                VerifyLocal();
                if (reference == 0 || !_core._retained.TryGetValue(reference, out var root))
                    throw new InvalidDataException("An active Canvas source occurrence has no independently retained root.");
                return root;
            }
            private SeekCanvasTargetKeyPoint KeyPoint(long reference)
            {
                var root = Root(reference);
                return new(reference, Scalar<float>(root, _classes[5], "time", "System.Single", 0x10),
                    Scalar<float>(root, _classes[5], "value", "System.Single", 0x14),
                    Scalar<int>(root, _classes[5], "easeType", "System.Int32", 0x18),
                    Scalar<float>(root, _classes[5], "floorPosition", "System.Single", 0x1c));
            }
            private SeekCanvasSourceList Points(NativeRuntime.RootHandle move, string name, int offset, ref int total)
            {
                var list = Reference(move, _classes[4], name, "System.Collections.Generic.List<InLevel.KeyPoint>", offset);
                var cls = N(() => _core._owner._api.ObjectClass(Target(list)));
                N(() => { _core._owner._api.VerifyCanvasKeyPointListClass(cls); return 0; });
                var backing = Reference(list, cls, "_items", "InLevel.KeyPoint[]", 0x10);
                var size = Scalar<int>(list, cls, "_size", "System.Int32", 0x18);
                var version = Scalar<int>(list, cls, "_version", "System.Int32", 0x1c);
                if (size <= 0 || size > NativeSeekCanvasTargetPlan.MaxKeypoints)
                    throw new InvalidDataException("An actual Canvas keypoint List is empty or exceeds its complete bound.");
                var capacity = ArrayReferences(backing, _classes[5], NativeSeekCanvasTargetPlan.MaxKeypoints);
                if (size > capacity.Length || Count(list, cls) != size)
                    throw new InvalidDataException("The actual Canvas List count, size and complete backing disagree.");
                total = checked(total + capacity.Length);
                if (total > NativeSeekCanvasTargetPlan.MaxTotalKeypoints)
                    throw new NotSupportedException("Complete Canvas keypoint capacity exceeds the aggregate source bound.");
                var points = new SeekCanvasTargetKeyPoint[size];
                for (var i = 0; i < size; ++i)
                {
                    if (capacity[i] == 0) throw new InvalidDataException("An active actual Canvas keypoint occurrence is absent.");
                    if (Item(list, cls, i) != capacity[i]) throw Changed("The official Canvas List item disagrees with its active backing slot.");
                    points[i] = KeyPoint(capacity[i]);
                }
                var listAddress = Target(list).ToInt64(); var backingAddress = Target(backing).ToInt64();
                if (Scalar<int>(list, cls, "_size", "System.Int32", 0x18) != size
                    || Scalar<int>(list, cls, "_version", "System.Int32", 0x1c) != version || Count(list, cls) != size
                    || Target(Reference(list, cls, "_items", "InLevel.KeyPoint[]", 0x10)).ToInt64() != backingAddress
                    || Target(Reference(move, _classes[4], name, "System.Collections.Generic.List<InLevel.KeyPoint>", offset)).ToInt64() != listAddress)
                    throw Changed("The actual Canvas keypoint container changed during complete observation.");
                return new(listAddress, backingAddress, size, version, Array.AsReadOnly(capacity), Array.AsReadOnly(points));
            }
            public SeekCanvasSourceSnapshot ReadSnapshot()
            {
                try
                {
                    VerifyAccess();
                    var transforms = Reference(_canvas, _classes[3], "canvasTransforms", "UnityEngine.Transform[]", 0x30);
                    var moves = Reference(_canvas, _classes[3], "canvasMoveData", "InLevel.CanvasMove[]", 0x28);
                    var ease = Reference(null, _classes[8], "EaseInfos", "InLevel.EaseInfor[]", 0, isStatic: true);
                    var transformItems = ArrayReferences(transforms, _classes[6], NativeSeekCanvasTargetPlan.MaxCanvases);
                    var moveItems = ArrayReferences(moves, _classes[4], NativeSeekCanvasTargetPlan.MaxCanvases);
                    var easeItems = ArrayReferences(ease, _classes[9], NativeSeekCanvasTargetCapture.MaximumEaseRows);
                    if (transformItems.Length != moveItems.Length || transformItems.Any(p => p == 0) || moveItems.Any(p => p == 0)
                        || easeItems.Length == 0 || easeItems.Any(p => p == 0))
                        throw new InvalidDataException("The complete Canvas source vectors are absent, mismatched or contain null active slots.");
                    var total = 0; var rows = new SeekCanvasSourceMove[moveItems.Length];
                    for (var i = 0; i < rows.Length; ++i)
                    {
                        var move = Root(moveItems[i]); var index = Scalar<int>(move, _classes[4], "index", "System.Int32", 0x10);
                        var speed = Points(move, "speedKeyPoints", 0x20, ref total);
                        var x = Points(move, "xPositionKeyPoints", 0x18, ref total);
                        rows[i] = new(moveItems[i], index, speed, x);
                    }
                    var eases = new SeekCanvasSourceEase[easeItems.Length];
                    for (var i = 0; i < eases.Length; ++i)
                    {
                        var row = Root(easeItems[i]); var code = Scalar<int>(row, _classes[9], "code", "System.Int32", 0x10);
                        var progress = Reference(row, _classes[9], "progress", "System.Single[]", 0x18);
                        var cls = N(() => _core._owner._api.ObjectClass(Target(progress)));
                        var array = N(() => _core._owner._api.ReadRootedGeometrySingleArray(progress, cls, 101, VerifyLocal));
                        var progressAddress = Target(progress).ToInt64();
                        if (array.Array.ToInt64() != progressAddress || array.Class != cls || array.Items.Count != 101)
                            throw new InvalidDataException("An initialized Canvas ease row lacks its complete actual 101-sample vector.");
                        eases[i] = new(easeItems[i], code, progressAddress, Array.AsReadOnly(array.Items.ToArray()));
                    }
                    var downField = Field(_classes[7], "downVector", "UnityEngine.Vector3", 0x24, true);
                    var rightField = Field(_classes[7], "rightVector", "UnityEngine.Vector3", 0x3c, true);
                    var down = N(() => _core._owner._api.ReadExactStaticField<NativeVector3>(downField));
                    var right = N(() => _core._owner._api.ReadExactStaticField<NativeVector3>(rightField));
                    var width = Scalar<float>(_canvas, _classes[3], "screenWidth", "System.Single", 0x58);
                    var identity = new SeekCanvasSourceIdentity(_core._noteViewIdentity, _core._informationAddress, _core._progressAddress, _canvasAddress);
                    var transformAddress = Target(transforms).ToInt64(); var movesAddress = Target(moves).ToInt64(); var easeAddress = Target(ease).ToInt64();
                    VerifyAccess();
                    return new(identity, transformAddress, Array.AsReadOnly(transformItems), movesAddress, Array.AsReadOnly(rows),
                        width, down, right, easeAddress, Array.AsReadOnly(eases));
                }
                catch (Exception error) { if (!SeekCanvasBasisCallerWithdrawal.Is(error)) _expired = true; throw; }
            }
            public float ChartTimeRange(double from, double to)
            {
                VerifyAccess();
                var receiver = Target(_core._progress);
                var value = 0f;
                _core._view!.WithRetainedSource(() => value = N(() =>
                {
                    using var a = A.Value(from); using var b = A.Value(to);
                    return _core._owner._api.InvokeValueWithPublicationGuarded<float>(_range.MethodInfo, receiver, VerifyLocal, a, b);
                }));
                VerifyLocal();
                if (!float.IsFinite(value)) throw new InvalidDataException("The actual Canvas range converter returned a nonfinite Single.");
                return value;
            }
        }
    }
}
