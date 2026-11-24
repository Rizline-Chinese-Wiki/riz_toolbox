using System.Numerics;
using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal sealed record GeometryTrs(IntPtr Parent, NativeVector3 Position, NativeRendererQuaternion Rotation, NativeVector3 Scale);
    internal sealed record GeometryNativeCall(int Ordinal, IntPtr Receiver, string Operation, NativeVector3? Vector,
        IntPtr? Parent = null, bool? WorldPositionStays = null)
    { internal bool Stored, Returned; }
    internal readonly List<GeometryNativeCall> GeometryCalls = new();
    internal readonly Dictionary<IntPtr, GeometryTrs> GeometryTransforms = new();
    internal readonly Dictionary<IntPtr, IntPtr> GeometryComponentTransforms = new();
    internal Action<GeometryNativeCall>? BeforeGeometryCall, AfterGeometryStore, AfterGeometryCall;
    internal Action<string, IntPtr>? AfterGeometryGetter;
    internal Action<uint, IntPtr>? AfterGeometryRoot;
    internal int FailGeometryBefore, FailGeometryAfter;
    internal bool GeometryReceiversRooted = true, GeometryPublishedBeforeInspection = true;
    private IntPtr _geometryComponentType, _geometryTransformType, _geometryVectorType, _geometryQuaternionType;
    private readonly Dictionary<string, IntPtr> _geometryMethods = new();

    private void ConfigureGeometryTransforms()
    {
        _geometryComponentType = SeekType("UnityEngine.Component"); _geometryTransformType = SeekType("UnityEngine.Transform");
        _geometryVectorType = AddStruct("UnityEngine.Vector3", 12, new("x", "System.Single", 0), new("y", "System.Single", 4), new("z", "System.Single", 8));
        _geometryQuaternionType = AddStruct("UnityEngine.Quaternion", 16, new("x", "System.Single", 0), new("y", "System.Single", 4), new("z", "System.Single", 8), new("w", "System.Single", 12));
        _geometryMethods.Add("get_transform", GameplayMethodAdd(_geometryComponentType, "get_transform", false, _geometryTransformType));
        _geometryMethods.Add("get_parent", GameplayMethodAdd(_geometryTransformType, "get_parent", false, _geometryTransformType));
        foreach (var name in new[] { "position", "localPosition", "localScale" })
        {
            _geometryMethods.Add("get_" + name, GameplayMethodAdd(_geometryTransformType, "get_" + name, false, _geometryVectorType));
            _geometryMethods.Add("set_" + name, GameplayMethodAdd(_geometryTransformType, "set_" + name, false, Void, _geometryVectorType));
        }
        foreach (var name in new[] { "rotation", "localRotation" })
            _geometryMethods.Add("get_" + name, GameplayMethodAdd(_geometryTransformType, "get_" + name, false, _geometryQuaternionType));
        _geometryMethods.Add("set_localRotation", GameplayMethodAdd(_geometryTransformType, "set_localRotation", false, Void, _geometryQuaternionType));
        _geometryMethods.Add("SetParent", GameplayMethodAdd(_geometryTransformType, "SetParent", false, Void, _geometryTransformType, Boolean));
        foreach (var name in new[] { "TransformPoint", "InverseTransformPoint" })
            _geometryMethods.Add(name, GameplayMethodAdd(_geometryTransformType, name, false, _geometryVectorType, _geometryVectorType));
    }

    internal IntPtr GeometryNewTransform(IntPtr parent, NativeVector3 position, NativeRendererQuaternion rotation, NativeVector3 scale)
    {
        var transform = SeekObject(_geometryTransformType); GeometryTransforms.Add(transform, new(parent, position, rotation, scale));
        GeometryComponentTransforms.Add(transform, transform); return transform;
    }
    internal void GeometrySetTransform(IntPtr transform, GeometryTrs value) => GeometryTransforms[transform] = value;
    internal void GeometryAliasMethod(string name, string target) => _geometryMethods[name] = _geometryMethods[target];
    internal NativeVector3 GeometryWorldPosition(IntPtr transform)
    {
        var value = GeometryTransforms[transform];
        return value.Parent == IntPtr.Zero ? value.Position : GeometryToWorld(value.Parent, value.Position);
    }
    private NativeVector3 GeometryToWorld(IntPtr transform, NativeVector3 point)
    {
        var value = GeometryTransforms[transform];
        var scaled = new Vector3(point.X * value.Scale.X, point.Y * value.Scale.Y, point.Z * value.Scale.Z);
        var rotated = Vector3.Transform(scaled, new Quaternion(value.Rotation.X, value.Rotation.Y, value.Rotation.Z, value.Rotation.W));
        var translated = new NativeVector3(rotated.X + value.Position.X, rotated.Y + value.Position.Y, rotated.Z + value.Position.Z);
        return value.Parent == IntPtr.Zero ? translated : GeometryToWorld(value.Parent, translated);
    }
    private NativeVector3 GeometryFromWorld(IntPtr transform, NativeVector3 point)
    {
        var value = GeometryTransforms[transform]; var local = value.Parent == IntPtr.Zero ? point : GeometryFromWorld(value.Parent, point);
        var translated = new Vector3(local.X - value.Position.X, local.Y - value.Position.Y, local.Z - value.Position.Z);
        var rotated = Vector3.Transform(translated, Quaternion.Inverse(new(value.Rotation.X, value.Rotation.Y, value.Rotation.Z, value.Rotation.W)));
        return new(rotated.X / value.Scale.X, rotated.Y / value.Scale.Y, rotated.Z / value.Scale.Z);
    }
    private NativeRendererQuaternion GeometryWorldRotation(IntPtr transform)
    {
        var value = GeometryTransforms[transform]; var local = new Quaternion(value.Rotation.X, value.Rotation.Y, value.Rotation.Z, value.Rotation.W);
        if (value.Parent == IntPtr.Zero) return value.Rotation;
        var p = GeometryWorldRotation(value.Parent); var world = new Quaternion(p.X, p.Y, p.Z, p.W) * local;
        return new(world.X, world.Y, world.Z, world.W);
    }
    private NativeVector3 GeometryWorldScale(IntPtr transform)
    {
        var value = GeometryTransforms[transform]; if (value.Parent == IntPtr.Zero) return value.Scale;
        var parent = GeometryWorldScale(value.Parent); return new(parent.X * value.Scale.X, parent.Y * value.Scale.Y, parent.Z * value.Scale.Z);
    }
    private bool GeometryMethodClass(IntPtr cls) => cls == ClassOf(_geometryComponentType) || cls == ClassOf(_geometryTransformType);
    private IntPtr GeometryMethods(IntPtr cls, IntPtr iterator) => GeometryArrayClass(cls) ? ClassMethods(ClassOf(_referenceArrayType), iterator)
        : GeometryMethodClass(cls) ? GameplayMethods(cls, iterator) : TargetMethods(cls, iterator);
    private IntPtr GeometryNamedMethod(IntPtr cls, string name, int count) => GeometryArrayClass(cls)
        ? name == "get_Rank" && count == 0 ? _arrayRank : name == "get_Length" && count == 0 ? _arrayLength
            : name == "GetValue" && count == 1 ? _arrayGet : IntPtr.Zero
        : GeometryMethodClass(cls)
        ? _gameplayMethods.FirstOrDefault(row => row.Value.Class == cls && row.Value.Name == name && row.Value.Parameters.Length == count).Key
        : TargetNamedMethod(cls, name, count);
    private IntPtr GeometryClassParent(IntPtr cls) => GeometryArrayClass(cls) ? ClassOf(_referenceArrayType)
        : cls == ClassOf(_geometryTransformType) || cls == ClassOf(SeekType("InLevel.NoteInformation"))
            || cls == ClassOf(_noteViewLineType) || cls == ClassOf(_geometryCameraType) ? ClassOf(_geometryComponentType)
        : cls == ClassOf(_geometryComponentType) ? ClassOf(SeekType("UnityEngine.Object")) : JudgeClassParent(cls);
    private bool GeometryAssignable(IntPtr target, IntPtr source)
    {
        for (var current = source; current != IntPtr.Zero; current = GeometryClassParent(current)) if (current == target) return true;
        return CursorAssignable(target, source);
    }
    private IntPtr GeometryClassImage(IntPtr cls) => GameplayTypeName(cls).StartsWith("UnityEngine.", StringComparison.Ordinal)
        ? _gameplayImages.Single(row => row.Value == "UnityEngine.CoreModule.dll").Key : JudgeClassImage(cls);
    private IntPtr GeometryObjectClass(IntPtr value)
    {
        if (GeometryTransforms.ContainsKey(value) || GeometryComponentTransforms.ContainsKey(value)) GeometryPublishedBeforeInspection &= HasRootFor(value);
        return JudgeObjectClass(value);
    }
    private uint GeometryRoot(IntPtr pointer, bool pinned)
    { var handle = HoldStateRoot(pointer, pinned); AfterGeometryRoot?.Invoke(handle, pointer); return handle; }
    private static byte[] GeometryBytes(NativeVector3 value) => new[] { value.X, value.Y, value.Z }.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] GeometryBytes(NativeRendererQuaternion value) => new[] { value.X, value.Y, value.Z, value.W }.SelectMany(BitConverter.GetBytes).ToArray();
    private IntPtr GeometryInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        var pair = _geometryMethods.FirstOrDefault(row => row.Value == method);
        if (pair.Key is null) return HoldStateInvoke(method, receiver, arguments, exception);
        ++Invocations; GeometryReceiversRooted &= HasRootFor(receiver); Marshal.WriteIntPtr(exception, IntPtr.Zero);
        var name = pair.Key;
        if (name.StartsWith("get_", StringComparison.Ordinal))
        {
            IntPtr result;
            if (name == "get_transform") result = GeometryComponentTransforms[receiver];
            else
            {
                var trs = GeometryTransforms[receiver];
                result = name switch
                {
                    "get_parent" => trs.Parent,
                    "get_position" => Box(_geometryVectorType, GeometryBytes(GeometryWorldPosition(receiver))),
                    "get_localPosition" => Box(_geometryVectorType, GeometryBytes(trs.Position)),
                    "get_localScale" => Box(_geometryVectorType, GeometryBytes(trs.Scale)),
                    "get_localRotation" => Box(_geometryQuaternionType, GeometryBytes(trs.Rotation)),
                    "get_rotation" => Box(_geometryQuaternionType, GeometryBytes(GeometryWorldRotation(receiver))),
                    _ => throw new InvalidOperationException("Unrecognized official geometry getter.")
                };
            }
            AfterGeometryGetter?.Invoke(name, receiver); return result;
        }
        if (name is "TransformPoint" or "InverseTransformPoint")
        {
            var point = Marshal.PtrToStructure<NativeVector3>(Marshal.ReadIntPtr(arguments));
            var result = Box(_geometryVectorType, GeometryBytes(name == "TransformPoint" ? GeometryToWorld(receiver, point) : GeometryFromWorld(receiver, point)));
            AfterGeometryGetter?.Invoke(name, receiver); return result;
        }
        var vector = name is "SetParent" or "set_localRotation" ? (NativeVector3?)null
            : Marshal.PtrToStructure<NativeVector3>(Marshal.ReadIntPtr(arguments));
        var parent = name == "SetParent" ? Marshal.ReadIntPtr(arguments) : (IntPtr?)null;
        var worldPositionStays = name == "SetParent" ? Marshal.ReadByte(Marshal.ReadIntPtr(arguments, IntPtr.Size)) != 0 : (bool?)null;
        var row = new GeometryNativeCall(GeometryCalls.Count + 1, receiver, name, vector, parent, worldPositionStays); GeometryCalls.Add(row);
        BeforeGeometryCall?.Invoke(row);
        if (row.Ordinal == FailGeometryBefore) throw new IOException("Synthetic geometry setter before-storage failure.");
        var before = GeometryTransforms[receiver];
        if (name == "SetParent")
        {
            var position = GeometryWorldPosition(receiver); var rotation = GeometryWorldRotation(receiver); var scale = GeometryWorldScale(receiver);
            var updated = before with { Parent = parent!.Value };
            if (worldPositionStays == true)
            {
                var localPosition = parent == IntPtr.Zero ? position : GeometryFromWorld(parent.Value, position);
                var parentRotation = parent == IntPtr.Zero ? new NativeRendererQuaternion(0, 0, 0, 1) : GeometryWorldRotation(parent.Value);
                var q = Quaternion.Inverse(new(parentRotation.X, parentRotation.Y, parentRotation.Z, parentRotation.W))
                    * new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
                var parentScale = parent == IntPtr.Zero ? new NativeVector3(1, 1, 1) : GeometryWorldScale(parent.Value);
                updated = updated with { Position = localPosition, Rotation = new(q.X, q.Y, q.Z, q.W),
                    Scale = new(scale.X / parentScale.X, scale.Y / parentScale.Y, scale.Z / parentScale.Z) };
            }
            GeometryTransforms[receiver] = updated;
        }
        else if (name == "set_localRotation") GeometryTransforms[receiver] = before with
        { Rotation = Marshal.PtrToStructure<NativeRendererQuaternion>(Marshal.ReadIntPtr(arguments)) };
        else GeometryTransforms[receiver] = name switch
        {
            "set_localScale" => before with { Scale = vector!.Value },
            "set_localPosition" => before with { Position = vector!.Value },
            "set_position" => before with { Position = before.Parent == IntPtr.Zero ? vector!.Value : GeometryFromWorld(before.Parent, vector!.Value) },
            _ => throw new InvalidOperationException("Unrecognized official geometry setter.")
        };
        row.Stored = true; AfterGeometryStore?.Invoke(row);
        if (row.Ordinal == FailGeometryAfter) throw new IOException("Synthetic geometry setter after-storage failure.");
        row.Returned = true; AfterGeometryCall?.Invoke(row); return IntPtr.Zero;
    }
}
