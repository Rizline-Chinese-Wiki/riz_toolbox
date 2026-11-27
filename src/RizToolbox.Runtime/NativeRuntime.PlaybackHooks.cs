using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Resolves the pinned declarations from actual IL2CPP exports. This metadata receipt
    /// neither installs code nor grants production admission; a copied hook target is not this receipt.</summary>
    internal NativePlaybackStockTargets ResolvePlaybackStockTargets(IntPtr module)
        => NativePlaybackStockTargets.Resolve(this, module);

    /// <summary>Reads only the declared constructed resource-reference metadata.
    /// The fixed field/offset belongs to this issuer, including owned captures.</summary>
    internal NativeExactReadField InspectProgressClipResourceField(IntPtr progressClass)
    {
        VerifyAccess();
        const string name = "IResourceReference<UnityEngine.AudioClip>";
        if (InspectMetadataClass(progressClass).TypeName != "InLevel.ProgressControl"
            || Utf8(_className(progressClass)) != "ProgressControl" || Utf8(_classNamespace(progressClass)) != "InLevel"
            || _isValueType(progressClass))
            throw new UnsupportedSignatureException("The resource field requires the exact Progress declaring reference class.");
        var field = InspectExactDeclaredReadField(progressClass, "<Clip>k__BackingField", name, false, 0x30);
        var cls = field.FieldClass; var classType = _classType(cls);
        if (field.Size != IntPtr.Size || _typeCode(field.Type) != 21 || _byRef(field.Type)
            || classType == IntPtr.Zero || _typeCode(classType) != 21 || _byRef(classType)
            || _typeClass(classType) != cls || _isValueType(cls) || MetadataTypeName(classType) != name
            || Api<Predicate>("il2cpp_class_is_generic")(cls) || !Api<Predicate>("il2cpp_class_is_inflated")(cls)
            || Utf8(_className(cls)) != "IResourceReference`1" || Utf8(_classNamespace(cls)) != ""
            || Api<Ptr1>("il2cpp_class_get_image")(cls) == IntPtr.Zero)
            throw new UnsupportedSignatureException("The exact Progress field is not a closed constructed AudioClip resource reference.");
        return field;
    }

    internal sealed class NativePlaybackStockTargets
    {
        private readonly NativeRuntime _api;
        private readonly NativeMetadataMethod[] _metadata;
        private readonly NativeExactReadField _progressResourceField;
        internal IntPtr Module { get; }
        internal VerifiedHookTarget Pause { get; }
        internal VerifiedHookTarget ReplayScheduled { get; }
        internal VerifiedHookTarget ResumeScheduled { get; }
        internal VerifiedHookTarget ProgressPlay { get; }
        internal VerifiedHookTarget AudioPause { get; }
        internal VerifiedHookTarget PlayScheduled { get; }
        internal VerifiedHookTarget PitchInstance { get; }
        internal VerifiedHookTarget PitchStatic { get; }
        internal VerifiedHookTarget SetClip { get; }
        internal VerifiedHookTarget GetTime { get; }
        internal VerifiedHookTarget ProgressMusicPlayer { get; }
        internal VerifiedHookTarget SetTime { get; }
        internal VerifiedHookTarget ProgressStartMoveNext { get; }
        internal VerifiedHookTarget PlayerConstructor { get; }
        internal VerifiedHookTarget AudioPlayHelper { get; }
        internal VerifiedHookTarget AudioPlayDelayed { get; }
        internal VerifiedHookTarget AudioPlay { get; }
        internal VerifiedHookTarget AudioStop { get; }
        internal VerifiedHookTarget AudioStopBoolean { get; }
        internal VerifiedHookTarget AudioSetLoop { get; }
        internal VerifiedHookTarget ProgressClip { get; }

        private NativePlaybackStockTargets(NativeRuntime api, IntPtr module, NativeMetadataMethod[] metadata)
        {
            _api = api; Module = module; _metadata = metadata;
            Pause = Target(0, "pause", ExactHookAbi.PlaybackVoid0);
            ReplayScheduled = Target(1, "replay-scheduled", ExactHookAbi.PlaybackSingle1Void);
            ResumeScheduled = Target(2, "resume-scheduled", ExactHookAbi.PlaybackSingle1Void);
            ProgressPlay = Target(3, "progress-play", ExactHookAbi.PlaybackBoolean1Void);
            AudioPause = Target(4, "audio-pause", ExactHookAbi.PlaybackVoid0);
            PlayScheduled = Target(5, "play-scheduled", ExactHookAbi.PlaybackDouble1Void);
            PitchInstance = Target(6, "pitch-instance", ExactHookAbi.PlaybackPitchSingle1Void);
            PitchStatic = Target(7, "pitch-static", ExactHookAbi.PlaybackPitchSingle1Void);
            SetClip = Target(8, "set-clip", ExactHookAbi.PlaybackReference1Void);
            GetTime = Target(9, "get-time", ExactHookAbi.InstanceSingleReturn0);
            ProgressMusicPlayer = Target(10, "progress-music-player", ExactHookAbi.PlaybackReference1Void);
            SetTime = Target(11, "set-time", ExactHookAbi.PlaybackSingle1Void);
            ProgressStartMoveNext = Target(12, "progress-start-movenext", ExactHookAbi.PlaybackBooleanReturn0);
            PlayerConstructor = Target(13, "player-constructor", ExactHookAbi.PlaybackReference1Void);
            AudioPlayHelper = Target(14, "AudioPlayHelper", ExactHookAbi.PlaybackUInt64_1Void);
            AudioPlayDelayed = Target(15, "AudioPlayDelayed", ExactHookAbi.PlaybackDouble1Void);
            AudioPlay = Target(16, "AudioPlay", ExactHookAbi.PlaybackVoid0);
            AudioStop = Target(17, "AudioStop", ExactHookAbi.PlaybackVoid0);
            AudioStopBoolean = Target(18, "AudioStopBoolean", ExactHookAbi.PlaybackBoolean1Void);
            AudioSetLoop = Target(19, "AudioSetLoop", ExactHookAbi.PlaybackBoolean1Void);
            ProgressClip = Target(20, "progress-clip", ExactHookAbi.PlaybackReference1Void);
            _progressResourceField = RequireProgressResource(api, metadata[20]);
        }

        private VerifiedHookTarget Target(int index, string id, ExactHookAbi abi)
        {
            var method = _metadata[index];
            return new("gameplay.stock." + id, method.Entry!.Address, method.MethodInfo, method.DeclaringClass.Class, abi);
        }

        // The only issuing path takes a runtime and module, never caller-created metadata/targets.
        internal static NativePlaybackStockTargets Resolve(NativeRuntime api, IntPtr module)
        {
            api.VerifyAccess();
            if (module == IntPtr.Zero || !OperatingSystem.IsWindows() || IntPtr.Size != 8)
                throw new UnsupportedSignatureException("Stock playback declarations require a mapped Windows x64 module.");
            return new(api, module, Inspect(api, module));
        }

        internal void VerifyCurrent()
        {
            _api.VerifyAccess();
            var current = Inspect(_api, Module);
            if (RequireProgressResource(_api, current[20]) != _progressResourceField)
                throw new UnsupportedSignatureException("The resolved Progress resource field/type identity changed.");
            for (var index = 0; index < current.Length; ++index)
            {
                var before = _metadata[index]; var now = current[index];
                if (before.MethodInfo != now.MethodInfo || before.DeclaringClass != now.DeclaringClass
                    || before.Name != now.Name || before.ReturnType != now.ReturnType
                    || !before.ParameterTypes.SequenceEqual(now.ParameterTypes) || before.Flags != now.Flags
                    || before.ImplementationFlags != now.ImplementationFlags || before.Entry != now.Entry)
                    throw new UnsupportedSignatureException("The resolved stock playback metadata identity changed.");
            }
        }

        private static NativeMetadataMethod[] Inspect(NativeRuntime api, IntPtr module)
        {
            var player = RequireClass(api, "Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core", "MUGMusicPlayer");
            var progress = RequireClass(api, "Assembly-CSharp.dll", "InLevel", "ProgressControl");
            var source = RequireClass(api, "UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource");
            var clip = RequireClass(api, "UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
            var start = api.NestedClass(progress, "<Start>d__25");
            if (Utf8(api._className(start)) != "<Start>d__25" || Utf8(api._classNamespace(start)) != "" || api._isValueType(start))
                throw new UnsupportedSignatureException("The exact Progress.Start nested reference class differs.");
            api.RequireInstanceFieldLayout(start, "<>1__state", "System.Int32", 0x10);
            api.RequireInstanceFieldLayout(start, "<>2__current", "System.Object", 0x18);
            api.RequireInstanceFieldLayout(start, "<>4__this", "InLevel.ProgressControl", 0x20);
            var startName = api.InspectMetadataClass(start).TypeName;
            // Both issuance and revalidation build their own index from the current exports.
            var declarations = new Dictionary<IntPtr, ILookup<string, IntPtr>>();
            NativeMetadataMethod RequireFromClass(IntPtr cls, string typeName, string name, int rva, bool isStatic,
                string returns, params string[] parameters)
            {
                if (!declarations.TryGetValue(cls, out var methodsByName))
                {
                    methodsByName = api.EnumerateDeclaredMethods(cls).ToLookup(
                        method => Utf8(api._methodName(method)), StringComparer.Ordinal);
                    declarations.Add(cls, methodsByName);
                }
                return Require(api, module, cls, typeName, name, rva, isStatic, returns, parameters, methodsByName[name]);
            }
            var methods = new[]
            {
                RequireFromClass(player, "Pigeon.MUGToolkit.Core.MUGMusicPlayer", "Pause", 0x3fbd90, false, "System.Void"),
                RequireFromClass(player, "Pigeon.MUGToolkit.Core.MUGMusicPlayer", "ReplayScheduled", 0x3fbe30, false, "System.Void", "System.Single"),
                RequireFromClass(player, "Pigeon.MUGToolkit.Core.MUGMusicPlayer", "ResumeScheduled", 0x3fc060, false, "System.Void", "System.Single"),
                // Play(false) includes a direct AudioSource.Pause call, bypassing MUGMusicPlayer.Pause.
                RequireFromClass(progress, "InLevel.ProgressControl", "Play", 0x400700, false, "System.Void", "System.Boolean"),
                RequireFromClass(source, "UnityEngine.AudioSource", "Pause", 0x1511640, false, "System.Void"),
                RequireFromClass(source, "UnityEngine.AudioSource", "PlayScheduled", 0x15116d0, false, "System.Void", "System.Double"),
                RequireFromClass(source, "UnityEngine.AudioSource", "set_pitch", 0x15117c0, false, "System.Void", "System.Single"),
                RequireFromClass(source, "UnityEngine.AudioSource", "SetPitch", 0x15117c0, true, "System.Void", "UnityEngine.AudioSource", "System.Single"),
                RequireFromClass(source, "UnityEngine.AudioSource", "set_clip", 0x1511a20, false, "System.Void", "UnityEngine.AudioClip"),
                RequireFromClass(player, "Pigeon.MUGToolkit.Core.MUGMusicPlayer", "GetTime", 0x3fbb10, false, "System.Single"),
                RequireFromClass(progress, "InLevel.ProgressControl", "set_MusicPlayer", 0x401700, false, "System.Void", "Pigeon.MUGToolkit.Core.MUGMusicPlayer"),
                RequireFromClass(source, "UnityEngine.AudioSource", "set_time", 0x1511c00, false, "System.Void", "System.Single"),
                RequireFromClass(start, startName, "MoveNext", 0x404990, false, "System.Boolean"),
                RequireFromClass(player, "Pigeon.MUGToolkit.Core.MUGMusicPlayer", ".ctor", 0x36c3e0, false, "System.Void", "UnityEngine.AudioSource"),
                RequireFromClass(source, "UnityEngine.AudioSource", "PlayHelper", 0x1511680, true, "System.Void", "UnityEngine.AudioSource", "System.UInt64"),
                RequireFromClass(source, "UnityEngine.AudioSource", "Play", 0x1511730, false, "System.Void", "System.Double"),
                RequireFromClass(source, "UnityEngine.AudioSource", "Play", 0x1511780, false, "System.Void"),
                RequireFromClass(source, "UnityEngine.AudioSource", "Stop", 0x1511810, false, "System.Void"),
                RequireFromClass(source, "UnityEngine.AudioSource", "Stop", 0x1511850, false, "System.Void", "System.Boolean"),
                RequireFromClass(source, "UnityEngine.AudioSource", "set_loop", 0x1511a70, false, "System.Void", "System.Boolean"),
                RequireFromClass(progress, "InLevel.ProgressControl", "set_Clip", 0x4016f0, false, "System.Void", "IResourceReference<UnityEngine.AudioClip>")
            };
            RequireReference(api, methods[7].MethodInfo, 0, source, "UnityEngine.AudioSource");
            RequireReference(api, methods[8].MethodInfo, 0, clip, "UnityEngine.AudioClip");
            RequireReference(api, methods[13].MethodInfo, 0, source, "UnityEngine.AudioSource");
            RequireReference(api, methods[14].MethodInfo, 0, source, "UnityEngine.AudioSource");
            RequireReference(api, methods[10].MethodInfo, 0, player, "Pigeon.MUGToolkit.Core.MUGMusicPlayer");
            _ = RequireProgressResource(api, methods[20]);
            if (methods[6].MethodInfo == methods[7].MethodInfo || methods[6].Entry!.Address != methods[7].Entry!.Address)
                throw new UnsupportedSignatureException("SetPitch requires distinct instance/static declarations sharing one physical Single1 entry.");
            return methods;
        }

        private static IntPtr RequireClass(NativeRuntime api, string image, string ns, string name)
        {
            var cls = api.Class(image, ns, name);
            if (api.InspectMetadataClass(cls).TypeName != ns + "." + name || Utf8(api._className(cls)) != name
                || Utf8(api._classNamespace(cls)) != ns || api._isValueType(cls))
                throw new UnsupportedSignatureException("A stock playback declaring/reference class differs from its exact declaration.");
            return cls;
        }

        private static NativeMetadataMethod Require(NativeRuntime api, IntPtr module, IntPtr cls, string typeName,
            string name, int rva, bool isStatic, string returns, string[] parameters, IEnumerable<IntPtr> candidates)
        {
            var method = api.InspectCurrentDeclaredMethod(cls, name, parameters, returns, module, isStatic, candidates);
            if (method.DeclaringClass.Class != cls || method.DeclaringClass.TypeName != typeName || method.Name != name
                || method.ReturnType != returns || !method.ParameterTypes.SequenceEqual(parameters)
                || ((method.Flags & 0x10) != 0) != isStatic || (method.Flags & 0x400) != 0
                || method.GenericMethod || method.InflatedMethod || method.Entry?.Rva != rva
                || api._parameterCount(method.MethodInfo) != parameters.Length)
                throw new UnsupportedSignatureException("A stock playback method differs from its exact declared signature/RVA: " + name);
            RequireScalar(api, api._returnType(method.MethodInfo), returns, returns == "System.Single" ? 12 : returns == "System.Boolean" ? 2 : 1, returns == "System.Single" ? 4 : returns == "System.Boolean" ? 1 : 0);
            for (uint index = 0; index < parameters.Length; ++index)
            {
                var parameter = parameters[index];
                if (parameter == "System.Single") RequireScalar(api, api._parameterType(method.MethodInfo, index), parameter, 12, 4);
                else if (parameter == "System.Double") RequireScalar(api, api._parameterType(method.MethodInfo, index), parameter, 13, 8);
                else if (parameter == "System.UInt64") RequireScalar(api, api._parameterType(method.MethodInfo, index), parameter, 11, 8);
                else if (parameter == "System.Boolean") RequireScalar(api, api._parameterType(method.MethodInfo, index), parameter, 2, 1);
            }
            var address = IntPtr.Add(module, rva);
            HookTargetValidation.VerifyReadable(method.MethodInfo, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method.MethodInfo);
            HookTargetValidation.ValidateMetadata(layout, api._methodName(method.MethodInfo), cls, api._returnType(method.MethodInfo),
                method.Flags, (uint)parameters.Length, address);
            if (layout.ImplementationFlags != (ushort)method.ImplementationFlags || (layout.Bitfield & 3) != 0)
                throw new UnsupportedSignatureException("Stock playback MethodInfo implementation/generic layout differs.");
            HookTargetValidation.VerifyExecutable(address, module);
            return method;
        }

        private static void RequireScalar(NativeRuntime api, IntPtr type, string name, int code, int size)
        {
            // Read fresh exports, rather than accepting Describe's cached type layout after issuance.
            if (api.MetadataTypeName(type) != name || api._byRef(type) || api._typeCode(type) != code)
                throw new UnsupportedSignatureException("A stock playback scalar is not its exact non-byref native type.");
            if (code != 1)
            {
                var cls = api._typeClass(type);
                if (cls == IntPtr.Zero || !api._isValueType(cls) || api._valueSize(cls, out _) != size
                    || api.MetadataTypeName(api._classType(cls)) != name)
                    throw new UnsupportedSignatureException("A stock playback scalar has incompatible native storage.");
            }
        }

        private static void RequireReference(NativeRuntime api, IntPtr method, uint index, IntPtr cls, string name)
        {
            var type = api._parameterType(method, index);
            if (api.MetadataTypeName(type) != name || api._byRef(type) || api._typeClass(type) != cls
                || api._typeCode(type) != 18 || api._isValueType(cls)
                || api.MetadataTypeName(api._classType(cls)) != name)
                throw new UnsupportedSignatureException("A stock playback reference argument differs from its independently resolved exact class.");
        }

        private static NativeExactReadField RequireProgressResource(NativeRuntime api, NativeMetadataMethod method)
        {
            const string name = "IResourceReference<UnityEngine.AudioClip>";
            var progress = method.DeclaringClass.Class;
            var field = api.InspectProgressClipResourceField(progress);
            var parameter = api._parameterType(method.MethodInfo, 0);
            var cls = api._typeClass(parameter);
            var classType = cls == IntPtr.Zero ? IntPtr.Zero : api._classType(cls);
            // A GENERICINST reference is its actual closed interface class. The
            // AudioClip argument class itself is never the setter parameter.
            if (parameter == IntPtr.Zero || cls == IntPtr.Zero || classType == IntPtr.Zero
                || field.DeclaringClass != progress || field.FieldClass != cls || field.Size != IntPtr.Size
                || api._typeCode(field.Type) != 21 || api._byRef(field.Type)
                || api._typeCode(parameter) != 21 || api._byRef(parameter) || api._isValueType(cls)
                || api._typeCode(classType) != 21 || api._byRef(classType) || api._typeClass(classType) != cls
                || api.MetadataTypeName(parameter) != name || api.MetadataTypeName(classType) != name)
                throw new UnsupportedSignatureException("Progress.set_Clip requires the exact declared offset30 constructed AudioClip resource reference.");
            return field;
        }
    }
}
