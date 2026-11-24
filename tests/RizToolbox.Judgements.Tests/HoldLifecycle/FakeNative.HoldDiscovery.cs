using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal enum HoldDiscoverySignatureFault { Return, Parameter, Static, Duplicate, ParameterByRef, ReturnByRef, Rank }
    internal uint HoldDiscoveryRank { get; set; } = 1;
    internal bool HoldDiscoveryExceptionalCall { get; set; }
    internal uint[] HoldDiscoveryTypeHandles => _handles.Where(row => _boxedTypes.GetValueOrDefault(row.Value) == _gameplayType)
        .Select(row => row.Key).ToArray();
    internal IntPtr[] HoldDiscoveryFreshControllers(int count)
        => Enumerable.Range(0, count).Select(_ => SeekObject(SeekType("NormalModeInLevelController"))).ToArray();
    private void ConfigureHoldDiscoveryMetadata()
    {
        var resources = ClassOf(SeekType("UnityEngine.Resources"));
        var method = _gameplayMethods.Single(pair => pair.Value.Class == resources && pair.Value.Name == "FindObjectsOfTypeAll");
        // The pinned declaration returns UnityEngine.Object[], rather than
        // the older general gameplay fixture's System.Object placeholder.
        _gameplayMethods[method.Key] = method.Value with { Return = _referenceArrayType };
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(HoldDiscoveryInvoke));
    }

    private IntPtr HoldDiscoveryInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        var result = FlagInvoke(method, receiver, arguments, exception);
        if (HoldDiscoveryExceptionalCall && _gameplayMethods.TryGetValue(method, out var row) && row.Name == "FindObjectsOfTypeAll")
            Marshal.WriteIntPtr(exception, new IntPtr(0x12345));
        return result;
    }

    internal void HoldDiscoveryFaultSignature(HoldDiscoverySignatureFault fault)
    {
        var resources = ClassOf(SeekType("UnityEngine.Resources"));
        var method = _gameplayMethods.Single(row => row.Value.Class == resources && row.Value.Name == "FindObjectsOfTypeAll");
        var value = method.Value;
        switch (fault)
        {
            case HoldDiscoverySignatureFault.Return: _gameplayMethods[method.Key] = value with { Return = Object }; break;
            case HoldDiscoverySignatureFault.Parameter: _gameplayMethods[method.Key] = value with { Parameters = new[] { Object } }; break;
            case HoldDiscoverySignatureFault.Static: _gameplayMethods[method.Key] = value with { Static = false }; break;
            case HoldDiscoverySignatureFault.Duplicate: GameplayMethodAdd(_classes[value.Class], value.Name, true, value.Return, value.Parameters); break;
            case HoldDiscoverySignatureFault.ParameterByRef: _types[value.Parameters[0]] = _types[value.Parameters[0]] with { ByRef = true }; break;
            case HoldDiscoverySignatureFault.ReturnByRef: _types[value.Return] = _types[value.Return] with { ByRef = true }; break;
            case HoldDiscoverySignatureFault.Rank: HoldDiscoveryRank = 2; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private IntPtr _holdResultArrayClass, _holdResultElementClass;

    internal void HoldTypedDiscoverySlots(string element, params IntPtr[] values)
    {
        var type = AddType(element + "[]", 29, false, IntPtr.Size);
        _holdResultArrayClass = ClassOf(type);
        _holdResultElementClass = ClassOf(SeekType(element));
        SetHoldDiscoverySlots(type, values);
    }

    internal void HoldDiscoverySlots(params IntPtr[] values) => SetHoldDiscoverySlots(_referenceArrayType, values);

    private void SetHoldDiscoverySlots(IntPtr type, IntPtr[] values)
    {
        GameplayControllers = Box(type, new byte[checked(ArrayHeaderBytes + values.Length * IntPtr.Size)]);
        _referenceArrays[GameplayControllers] = values.ToArray(); _referenceRanks[GameplayControllers] = 1;
        Marshal.WriteIntPtr(GameplayControllers, ClassOf(type));
        for (var index = 0; index < values.Length; ++index)
            Marshal.WriteIntPtr(GameplayControllers, ArrayHeaderBytes + index * IntPtr.Size, values[index]);
    }
}
