using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Synthetic exports for the real Canvas writer. The older read-only Canvas
// fixture stays unchanged and continues to reject every Transform consumer.
sealed partial class FakeNative
{
    internal CanvasPositionProfileValue CanvasPositionProfile = null!;
    internal readonly List<IntPtr> CanvasPositionGetterCalls = new();
    internal IntPtr CanvasPositionGetterException, CanvasPositionSetterException, CanvasPositionLastBox;
    internal int CanvasPositionGetterExceptionWrites, CanvasPositionSetterExceptionWrites;

    internal sealed class CanvasPositionProfileValue : ISeekCanvasPositionNativeProfile
    {
        private readonly FakeNative _n;
        internal Action? OnVerify;
        internal string? WrongMember;
        internal CanvasPositionProfileValue(FakeNative n) => _n = n;
        public IntPtr TransformClass => WrongMember == nameof(TransformClass) ? _n.ClassOf(_n.Object) : _n.ClassOf(_n._geometryTransformType);
        public IntPtr GetLocalPositionMethod => WrongMember == nameof(GetLocalPositionMethod)
            ? _n._geometryMethods["get_position"] : _n._geometryMethods["get_localPosition"];
        public IntPtr SetLocalPositionMethod => WrongMember == nameof(SetLocalPositionMethod)
            ? _n._geometryMethods["set_position"] : _n._geometryMethods["set_localPosition"];
        public IntPtr SetParentMethod => WrongMember == nameof(SetParentMethod)
            ? _n._geometryMethods["set_localPosition"] : _n._geometryMethods["SetParent"];
        public void VerifyCurrent() { OnVerify?.Invoke(); _n.SeekProfile.VerifyCurrent(); }
        public void VerifyEndpointCurrent(Action guard)
        { guard(); OnVerify?.Invoke(); guard(); _n.SeekProfile.VerifyCurrent(); guard(); }
    }

    internal void ConfigureCanvasPositions()
    {
        ConfigureCanvasSource(); CanvasPositionProfile = new(this);
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(CanvasPositionInvoke));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }
    private IntPtr CanvasPositionInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        CanvasSourceObserveExport("il2cpp_runtime_invoke");
        var getter = method == _geometryMethods["get_localPosition"];
        var setter = method == _geometryMethods["set_localPosition"];
        var parent = method == _geometryMethods["SetParent"];
        if (!getter && !setter && !parent) return CanvasInvoke(method, receiver, arguments, exception);
        var row = _gameplayMethods[method];
        if (row.Static || row.Class != ClassOf(_geometryTransformType)
            || row.Name != (getter ? "get_localPosition" : parent ? "SetParent" : "set_localPosition")
            || row.Return != (getter ? _geometryVectorType : Void)
            || !row.Parameters.SequenceEqual(getter ? Array.Empty<IntPtr>() : parent
                ? new[] { _geometryTransformType, Boolean } : new[] { _geometryVectorType })
            || _types[_geometryVectorType].Size != 12 || _types[Single].Size != 4)
        {
            ++CanvasSourceForbidden;
            throw new InvalidOperationException("Malformed exact localPosition metadata reached runtime_invoke.");
        }
        if (getter) CanvasPositionGetterCalls.Add(receiver);
        var result = GeometryInvoke(method, receiver, arguments, exception);
        if (getter) CanvasPositionLastBox = result;
        var fault = getter ? CanvasPositionGetterException : CanvasPositionSetterException;
        if (fault != IntPtr.Zero)
        {
            Marshal.WriteIntPtr(exception, fault);
            if (getter) ++CanvasPositionGetterExceptionWrites; else ++CanvasPositionSetterExceptionWrites;
        }
        return result;
    }
    internal IntPtr[] CanvasPositionTransforms => CanvasSourceSlots(SeekValue<IntPtr>(GeometryCanvasControl, "canvasTransforms"));
    internal NativeVector3 CanvasPositionValue(int ordinal) => GeometryTransforms[CanvasPositionTransforms[ordinal]].Position;
    internal void CanvasPositionSet(int ordinal, NativeVector3 value)
    {
        var transform = CanvasPositionTransforms[ordinal];
        GeometrySetTransform(transform, GeometryTransforms[transform] with { Position = value });
    }
    internal void CanvasPositionAlias(int target, int source)
        => CanvasSourceSlot(SeekValue<IntPtr>(GeometryCanvasControl, "canvasTransforms"), target, CanvasPositionTransforms[source]);
    internal void CanvasPositionMethodMetadata(bool getter, string mode)
    {
        var method = _geometryMethods[getter ? "get_localPosition" : "set_localPosition"];
        var row = _gameplayMethods[method];
        IntPtr ByRef(IntPtr type) => AddType(_types[type].Name, _types[type].Code, _types[type].ValueType, _types[type].Size, byRef: true);
        _gameplayMethods[method] = mode switch
        {
            "static" => row with { Static = true },
            "owner" => row with { Class = ClassOf(Object) },
            "name" => row with { Name = getter ? "get_position" : "set_position" },
            "return" => row with { Return = getter ? Single : Int32 },
            "parameters" => row with { Parameters = getter ? new[] { Int32 } : new[] { Single } },
            "arity" => row with { Parameters = getter ? new[] { _geometryVectorType } : Array.Empty<IntPtr>() },
            "byref-return" => row with { Return = ByRef(row.Return) },
            "byref-parameter" => row with { Parameters = new[] { ByRef(_geometryVectorType) } },
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
    internal void CanvasPositionVectorMetadata(string mode)
    {
        if (mode == "width") _types[_geometryVectorType] = _types[_geometryVectorType] with { Size = 16 };
        else if (mode == "single-width") _types[Single] = _types[Single] with { Size = 8 };
        else
        {
            var field = _structFields[ClassOf(_geometryVectorType)][1]; var row = _fieldMetadata[field];
            _fieldMetadata[field] = mode switch
            {
                "offset" => (row.Name, row.Type, row.Offset + 4),
                "field-type" => (row.Name, Int32, row.Offset),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }
    }
}
