using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    // This deliberately does not extend the general instance-hook resolver with
    // a caller-selected static signature/address authority.
    internal IntPtr ResolveLocalTimelineClass()
    {
        VerifyAccess(); using var size = Arg.Value(IntPtr.Zero);
        var assemblies = _assemblies(_domain(), size.Pointer);
        var count = Marshal.ReadIntPtr(size.Pointer).ToInt64();
        if (count is < 1 or > 4096) throw new UnsupportedSignatureException("Timeline assembly metadata is unavailable.");
        var found = IntPtr.Zero;
        for (var index = 0; index < count; ++index)
        {
            var cls = _findClass(_image(Marshal.ReadIntPtr(assemblies, index * IntPtr.Size)), "Steamworks", "SteamTimeline");
            if (cls == IntPtr.Zero) continue;
            if (found != IntPtr.Zero && found != cls) throw new UnsupportedSignatureException("SteamTimeline metadata class is ambiguous.");
            found = cls;
        }
        return found != IntPtr.Zero ? found : throw new MissingMemberException("Steamworks.SteamTimeline metadata is absent.");
    }

    internal VerifiedHookTarget ResolveLocalTimelineHook(IntPtr module, IntPtr cls, ExactHookAbi abi)
    {
        VerifyAccess();
        var (name, rva, parameters) = abi switch
        {
            ExactHookAbi.StaticTimelineModeVoid => ("SetTimelineGameMode", 0x48A950, new[] { "Steamworks.TimelineGameMode" }),
            ExactHookAbi.StaticTimelineInstantaneousHandle => ("AddInstantaneousTimelineEvent", 0x48A3E0,
                new[] { "System.String", "System.String", "System.String", "System.UInt32", "System.Single", "Steamworks.TimelineEventClipPriority" }),
            ExactHookAbi.StaticTimelineEndRangeVoid => ("EndRangeTimelineEvent", 0x48A730,
                new[] { "Steamworks.Data.TimelineEventHandle", "System.Single" }),
            ExactHookAbi.StaticTimelineTooltipVoid => ("SetTimelineTooltip", 0x48A980, new[] { "System.String", "System.Single" }),
            _ => throw new UnsupportedSignatureException("No sealed Timeline ABI exists for this operation.")
        };
        if (module == IntPtr.Zero || cls == IntPtr.Zero || Utf8(_className(cls)) != "SteamTimeline" || Utf8(_classNamespace(cls)) != "Steamworks")
            throw new UnsupportedSignatureException("Verified GameAssembly module and exact SteamTimeline class are required.");
        var method = Method(cls, name, parameters.Length, true, parameters);
        var flags = _methodFlags(method, out _);
        if (_methodClass(method) != cls || (flags & (0x10 | 0x400)) != 0x10 || _isGenericMethod(method) || _isInflatedMethod(method))
            throw new UnsupportedSignatureException("Timeline gate requires its declared non-generic static method.");
        if (abi == ExactHookAbi.StaticTimelineInstantaneousHandle) ValidateLocalTimelineHandle(_returnType(method));
        else if (Describe(_returnType(method)) is not { Kind: NativeValueKind.Void, ByRef: false })
            throw new UnsupportedSignatureException("Timeline void return metadata differs.");
        for (var index = 0; index < parameters.Length; ++index)
        {
            var type = _parameterType(method, (uint)index);
            var expectedName = parameters[index];
            if (expectedName == "Steamworks.Data.TimelineEventHandle") { ValidateLocalTimelineHandle(type); continue; }
            var layout = Describe(type);
            var valid = expectedName switch
            {
                "Steamworks.TimelineGameMode" or "Steamworks.TimelineEventClipPriority" =>
                    layout is { Kind: NativeValueKind.Enum, Size: 4, Underlying.Kind: NativeValueKind.Int32 },
                "System.String" => layout is { Kind: NativeValueKind.Reference, Size: 8 },
                "System.UInt32" => layout is { Kind: NativeValueKind.UInt32, Size: 4 },
                "System.Single" => layout is { Kind: NativeValueKind.Single, Size: 4 },
                _ => false
            };
            if (!valid || layout.ByRef || layout.Name != expectedName)
                throw new UnsupportedSignatureException("Timeline static argument ABI differs from its exact metadata signature.");
        }
        var address = IntPtr.Add(module, rva);
        HookTargetValidation.VerifyReadable(method, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        HookTargetValidation.ValidateMetadata(Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method),
            _methodName(method), cls, _returnType(method), _methodFlags(method, out _), _parameterCount(method), address);
        HookTargetValidation.VerifyExecutable(address, module);
        return new("local.timeline." + name, address, method, cls, abi);
    }

    private void ValidateLocalTimelineHandle(IntPtr type)
    {
        var cls = _typeClass(type);
        if (_byRef(type) || MetadataTypeName(type) != "Steamworks.Data.TimelineEventHandle" || cls == IntPtr.Zero
            || !_isValueType(cls) || _isEnum(cls) || _valueSize(cls, out var alignment) != 8 || alignment != 8
            )
            throw new UnsupportedSignatureException("TimelineEventHandle requires the verified single UInt64 value layout.");
        using var iterator = Arg.Value(IntPtr.Zero); var instanceFields = 0;
        for (var index = 0; index < 512; ++index)
        {
            var field = _fields(cls, iterator.Pointer);
            if (field == IntPtr.Zero)
            {
                if (instanceFields == 1)
                {
                    HookTargetValidation.ValidateTimelineHandleLayout(new("Steamworks.Data.TimelineEventHandle", NativeValueKind.Struct, 8,
                        Fields: new[] { new NativeFieldLayout("Value", "System.UInt64", 0) }), alignment);
                    return;
                }
                break;
            }
            if ((_fieldFlags(field) & 0x10) != 0) continue;
            ++instanceFields;
            if (instanceFields != 1 || Utf8(_fieldName(field)) != "Value" || _fieldOffset(field) != 16
                || Describe(_fieldType(field)) is not { Name: "System.UInt64", Kind: NativeValueKind.UInt64, Size: 8, ByRef: false })
                break;
        }
        throw new UnsupportedSignatureException("TimelineEventHandle fields differ from the boxed Value@0x10 UInt64 layout.");
    }
}
