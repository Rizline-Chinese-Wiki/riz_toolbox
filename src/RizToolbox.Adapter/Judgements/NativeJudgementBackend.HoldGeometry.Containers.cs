using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class HoldAccess
        {
            internal sealed partial class GeometryAccess
            {
                private long _canvasArrayReference, _floorArrayReference, _easeArrayReference;
                private readonly Dictionary<int, (long Info, long Progress)> _easeIdentities = new();
                private NativeRuntime.RootHandle? ReferenceField(NativeRuntime.RootHandle? receiver, IntPtr cls,
                    string name, string type, int offset, bool isStatic = false)
                {
                    var c = _source._core;
                    var field = c.Native(() => c._owner._api.InspectExactDeclaredReadField(cls, name, type, isStatic, offset));
                    NativeRuntime.RootHandle? value = null; var published = false;
                    void Publish(IntPtr pointer)
                    {
                        if (published) throw Changed("A geometry container reference was published twice.");
                        published = true;
                        if (pointer != IntPtr.Zero) value = c.TargetAdopt(pointer);
                    }
                    c.Native(() =>
                    {
                        if (isStatic) c._owner._api.PublishExactStaticReferenceField(field, Publish);
                        else c._owner._api.PublishExactReferenceField(receiver!, field, Publish);
                        return 0;
                    });
                    if (!published) throw Changed("A geometry container field had no publication.");
                    if (value is not null && c.Native(() => c._owner._api.ObjectClass(c.Target(value))) != field.FieldClass)
                        throw new UnsupportedSignatureException("The actual geometry container has another exact field class.");
                    return value;
                }
                private long[] References(NativeRuntime.RootHandle array, IntPtr exactElementClass, int maximum)
                {
                    var c = _source._core; var cls = c.Native(() => c._owner._api.ObjectClass(c.Target(array)));
                    var count = _source.ArrayCount(array, "get_Length", maximum);
                    if (_source.ArrayCount(array, "get_Rank", 1) != 1) throw new InvalidDataException("A geometry reference array is not a vector.");
                    var method = c.Native(() => c._owner._api.Method(cls, "GetValue", 1, parameterTypes: new[] { "System.Int32" }));
                    var result = new long[count];
                    for (var i = 0; i < count; ++i)
                    {
                        NativeRuntime.RootHandle? item = null; var published = false;
                        c.Native(() =>
                        {
                            using var argument = A.Value(i);
                            c._owner._api.InvokeReferenceWithPublication(method, c.Target(array), pointer =>
                            {
                                if (published) throw Changed("A geometry array slot was published twice.");
                                published = true;
                                if (pointer != IntPtr.Zero) item = c.TargetAdopt(pointer);
                            }, argument); return 0;
                        });
                        if (!published) throw Changed("A geometry array slot had no publication.");
                        if (item is not null && c.Native(() => c._owner._api.ObjectClass(c.Target(item))) != exactElementClass)
                            throw new UnsupportedSignatureException("A geometry reference array item has another exact element class.");
                        result[i] = Address(item);
                    }
                    if (_source.ArrayCount(array, "get_Length", maximum) != count) throw Changed("A geometry reference vector changed during capture.");
                    return result;
                }
                private float[] Singles(NativeRuntime.RootHandle array)
                {
                    var c = _source._core; var cls = c.Native(() => c._owner._api.ObjectClass(c.Target(array)));
                    var value = c.Native(() => c._owner._api.ReadRootedGeometrySingleArray(array, cls, NativeRuntime.MaximumGeometryArrayLength));
                    if (value.Array != c.Target(array) || value.Class != cls || value.Length != value.Items.Count)
                        throw Changed("The complete geometry Single array lost its retained identity.");
                    return value.Items.ToArray();
                }
                private (long[] Canvases, float[] Floors, long Ease, IReadOnlyList<SeekHoldGeometryEaseTable>? Tables) ReadBasis()
                {
                    var c = _source._core;
                    var canvases = ReferenceField(_canvas, _profile.CanvasClass, "canvasTransforms", "UnityEngine.Transform[]", 0x30)
                        ?? throw new InvalidDataException("The actual canvas Transform vector is absent.");
                    var floors = ReferenceField(_canvas, _profile.CanvasClass, "floorPositions", "System.Single[]", 0x38)
                        ?? throw new InvalidDataException("The actual canvas floor vector is absent.");
                    var ease = ReferenceField(null, _profile.EaseClass, "EaseInfos", "InLevel.EaseInfor[]", 0, isStatic: true);
                    var canvasAddress = Address(canvases); var floorAddress = Address(floors); var easeAddress = Address(ease);
                    if (_context is not null && (_canvasArrayReference != canvasAddress || _floorArrayReference != floorAddress || _easeArrayReference != easeAddress))
                        throw Changed("The geometry basis container references changed after capture.");
                    _canvasArrayReference = canvasAddress; _floorArrayReference = floorAddress; _easeArrayReference = easeAddress;
                    var canvasItems = References(canvases, _profile.TransformClass, NativeRuntime.MaximumGeometryArrayLength);
                    var floorItems = Singles(floors);
                    if (canvasItems.Length != floorItems.Length) throw new InvalidDataException("The actual canvas and floor vectors have different complete lengths.");
                    if (ease is null) return (canvasItems, floorItems, 0, null);
                    var rows = References(ease, _profile.EaseInfoClass, 1024); var tables = new SeekHoldGeometryEaseTable[rows.Length];
                    long total = 0;
                    for (var i = 0; i < rows.Length; ++i)
                    {
                        if (rows[i] == 0) throw new InvalidDataException("An initialized geometry ease array has a missing actual row.");
                        var info = Root(rows[i]);
                        var progress = ReferenceField(info, _profile.EaseInfoClass, "progress", "System.Single[]", 0x18);
                        var identity = (rows[i], Address(progress));
                        if (_context is not null && _easeIdentities.TryGetValue(i, out var old) && old != identity)
                            throw Changed("An initialized geometry ease row changed its actual data/array identity.");
                        _easeIdentities[i] = identity;
                        var code = c.ValueField<int>(info, _profile.EaseInfoClass, "code", "System.Int32", 0x10);
                        if (progress is null) throw new InvalidDataException("An initialized geometry ease row has no actual sample vector.");
                        var values = Singles(progress); total += values.Length;
                        if (total > 4 * NativeRuntime.MaximumGeometryArrayLength) throw new NotSupportedException("The complete ease progress source exceeds its aggregate bound.");
                        tables[i] = new(code, Array.AsReadOnly(values));
                    }
                    return (canvasItems, floorItems, easeAddress, Array.AsReadOnly(tables));
                }
            }
        }
    }
}
