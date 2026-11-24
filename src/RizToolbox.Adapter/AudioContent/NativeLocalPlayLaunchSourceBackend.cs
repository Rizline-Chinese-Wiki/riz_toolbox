using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Pinned native source selection from GameManager.Instance, its
/// inLevelControl and the initialized InLevelSettings.JudgeDataList[difficulty].</summary>
internal sealed class NativeLocalPlayLaunchSourceBackend : INativeLocalPlayLaunchSourceBackend
{
    private const string ListType = "System.Collections.Generic.List<JudgeData>";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void RuntimeClassInit(IntPtr declaringClass);
    private static readonly string[] JudgeFields = { "judgeTime", "normalPerfectJudgeTimeInChallengeTime", "maxPerfectJudgeTimeInChallengeTime", "badTime" };
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly NativePublicationArgumentRecords _arguments;
    private IntPtr _managerClass, _controlClass, _settingsClass, _judgeClass, _listClass, _alive, _getItem;
    private int _version, _count;
    private float[]? _judgeValues;
    private bool _attempted;
    public bool CleanupUnknown => _arguments.CleanupUnknown;
    internal NativeLocalPlayLaunchSourceBackend(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile, IntPtr module)
    { (_api, _main, _profile, _module) = (api, main, profile, module); _arguments = new(main); }

    public void Capture(int judgeIndex, Action<int, Func<NativeRuntime.RootHandle>> retain)
    {
        _main.VerifyAccess();
        if (_attempted || judgeIndex is < 0 or > 2) throw new InvalidOperationException("Native launch source capture cannot be repeated or use a foreign difficulty.");
        _attempted = true; RequireBuild();
        _managerClass = _api.Class("Assembly-CSharp.dll", "", "GameManager");
        _controlClass = _api.Class("Assembly-CSharp.dll", "", "InLevelControl");
        _settingsClass = _api.Class("Assembly-CSharp.dll", "", "InLevelSettings");
        _judgeClass = _api.Class("Assembly-CSharp.dll", "", "JudgeData");
        _api.RequireStaticFieldLayout(_managerClass, "Instance", "GameManager", 0);
        _api.RequireInstanceFieldLayout(_managerClass, "inLevelControl", "InLevelControl", 0x38);
        _api.RequireStaticFieldLayout(_settingsClass, "JudgeDataList", ListType, 0);
        for (var i = 0; i < JudgeFields.Length; ++i)
            _api.RequireInstanceFieldLayout(_judgeClass, JudgeFields[i], "System.Single", 0x10 + i * 4);
        var alive = _api.InspectDeclaredStaticMethod(_api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object"),
            "op_Implicit", new[] { "UnityEngine.Object" }, "System.Boolean", _module);
        if (alive.Entry?.Rva != 0x15409B0 || alive.GenericMethod || alive.InflatedMethod || (alive.Flags & 0x400) != 0)
            throw new UnsupportedSignatureException("The exact native Unity liveness method changed.");
        _alive = alive.MethodInfo;
        var manager = _api.ReadStatic<IntPtr>(_managerClass, "Instance"); RequirePresent(manager, "GameManager.Instance");
        NativeRuntime.RootHandle? managerRoot = null, controlRoot = null, listRoot = null;
        retain(0, () => managerRoot = _api.Root(manager));
        RequireClass(managerRoot!.Target, _managerClass);
        var control = _api.ReadWithPublication<IntPtr>(managerRoot.Target, "inLevelControl"); RequirePresent(control, "GameManager.inLevelControl");
        retain(1, () => controlRoot = _api.Root(control)); RequireClass(controlRoot!.Target, _controlClass);
        if (!Alive(managerRoot.Target) || !Alive(controlRoot.Target)) throw new NativeLocalPlaySourceNotReadyException("The current native manager/control is not alive.");
        var list = ReadInitializedJudgeList(_api, _arguments, _settingsClass, ResolveJudgeSettingsInitializer());
        retain(2, () => listRoot = _api.Root(list)); _listClass = _api.ObjectClass(listRoot!.Target);
        if (_api.InspectMetadataClass(_listClass).TypeName != ListType)
            throw new UnsupportedSignatureException("The rooted static judge list has a different closed class.");
        _api.RequireInstanceFieldLayout(_listClass, "_size", "System.Int32", 0x18);
        _api.RequireInstanceFieldLayout(_listClass, "_version", "System.Int32", 0x1C);
        var item = _api.InspectDeclaredMethod(_listClass, "get_Item", new[] { "System.Int32" }, "JudgeData", _module);
        if (item.Entry?.Rva != 0x9A40B0 || item.GenericMethod || (item.Flags & 0x10) != 0 || (item.Flags & 0x400) != 0)
            throw new UnsupportedSignatureException("The actual closed native JudgeDataList item method differs from the pinned call site.");
        _getItem = item.MethodInfo;
        _count = _api.ReadWithPublication<int>(listRoot.Target, "_size");
        _version = _api.ReadWithPublication<int>(listRoot.Target, "_version");
        if (_count != 3) throw new NativeLocalPlaySourceNotReadyException("The native judge settings list does not contain the exact three supported difficulty entries.");
        NativeRuntime.RootHandle? judgeRoot = null;
        var selectedJudge = ReadItem(listRoot.Target, judgeIndex,
            pointer => retain(3, () => judgeRoot = _api.Root(pointer)));
        // Expected null is a completed native read, not a throwing publisher.
        // Nonnull results were already independently rooted before cleanup.
        RequirePresent(selectedJudge, "selected native JudgeData");
        RequireClass(judgeRoot!.Target, _judgeClass);
        _judgeValues = ReadJudge(judgeRoot.Target);
    }

    private RuntimeClassInit ResolveJudgeSettingsInitializer()
    {
        const uint requiredFlags = (uint)(MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
        var constructor = _api.InspectDeclaredStaticMethod(_settingsClass, ".cctor", Array.Empty<string>(), "System.Void", _module);
        if (constructor.Entry?.Rva != 0x371E20 || (constructor.Flags & (requiredFlags | (uint)MethodAttributes.Abstract)) != requiredFlags)
            throw new UnsupportedSignatureException("The native InLevelSettings type initializer differs from the pinned declaration.");
        if (!NativeLibrary.TryGetExport(_module, "il2cpp_runtime_class_init", out var address) || address != IntPtr.Add(_module, 0x2650A0))
            throw new UnsupportedSignatureException("The native class initialization export differs from the pinned workshop launch entry.");
        HookTargetValidation.VerifyExecutable(address, _module);
        return Marshal.GetDelegateForFunctionPointer<RuntimeClassInit>(address);
    }

    // LoadWorkshopLevel initializes this class before reading the static list.
    // The runtime owns the once-only constructor state, including concurrent access.
    internal static IntPtr ReadInitializedJudgeList(NativeRuntime api, NativePublicationArgumentRecords records,
        IntPtr settingsClass, RuntimeClassInit initialize)
    {
        records.Begin(Array.Empty<A>());
        try { initialize(settingsClass); }
        catch { records.Fail(); throw; }
        records.Complete();
        var list = api.ReadStatic<IntPtr>(settingsClass, "JudgeDataList");
        RequirePresent(list, "InLevelSettings.JudgeDataList");
        return list;
    }

    public void VerifyStable(int judgeIndex, IReadOnlyList<NativeRuntime.RootHandle?> roots)
    {
        _main.VerifyAccess();
        if (!_attempted || CleanupUnknown || roots.Count != 4 || roots.Any(root => root is null) || _judgeValues is null
            || judgeIndex is < 0 or > 2) throw new InvalidOperationException("The exact native source was not fully captured.");
        var manager = roots[0]!.Target; var control = roots[1]!.Target;
        var list = roots[2]!.Target; var judge = roots[3]!.Target;
        RequireClass(manager, _managerClass); RequireClass(control, _controlClass); RequireClass(list, _listClass); RequireClass(judge, _judgeClass);
        void VerifyFields()
        {
            if (_api.ReadStatic<IntPtr>(_managerClass, "Instance") != manager
                || _api.ReadWithPublication<IntPtr>(manager, "inLevelControl") != control
                || _api.ReadStatic<IntPtr>(_settingsClass, "JudgeDataList") != list
                || _api.ReadWithPublication<int>(list, "_size") != _count
                || _api.ReadWithPublication<int>(list, "_version") != _version
                || !Alive(manager) || !Alive(control))
                throw new NativeLocalPlaySourceNotReadyException("The actual manager/control or judge-list generation changed during launch source capture.");
            var values = ReadJudge(judge);
            if (values.Where((value, index) => BitConverter.SingleToInt32Bits(value) != BitConverter.SingleToInt32Bits(_judgeValues[index])).Any())
                throw new NativeLocalPlaySourceNotReadyException("The selected native judging windows changed during source capture.");
        }
        VerifyFields();
        var currentJudge = ReadItem(list, judgeIndex, null);
        if (currentJudge != judge)
            throw new NativeLocalPlaySourceNotReadyException("The selected native judge-list entry changed identity.");
        VerifyFields();
    }
    private float[] ReadJudge(IntPtr judge)
    {
        var values = JudgeFields.Select(field => _api.ReadWithPublication<float>(judge, field)).ToArray();
        if (values.Any(value => !float.IsFinite(value) || value <= 0))
            throw new NativeLocalPlaySourceNotReadyException("The selected native judging windows are absent, nonpositive or nonfinite.");
        return values;
    }
    private IntPtr ReadItem(IntPtr list, int index, Action<IntPtr>? retainNonNull)
        => ReadPublishedJudge(_api, _arguments, _getItem, list, index, retainNonNull);

    // The same actual publication wrapper is exercised by regression. Only
    // ownership adoption may throw inside its callback; null/drift checks occur
    // after normal native return and temporary-input cleanup have completed.
    internal static IntPtr ReadPublishedJudge(NativeRuntime api, NativePublicationArgumentRecords records,
        IntPtr method, IntPtr list, int index, Action<IntPtr>? retainNonNull)
    {
        if (records.CleanupUnknown || records.Count != 0)
            throw new InvalidOperationException("The prior judge publication remains in-flight or Unknown.");
        var arguments = new[] { A.Value(index) }; records.Begin(arguments); var observed = IntPtr.Zero;
        try { api.InvokeReferenceWithPublication(method, list, pointer => {
            observed = pointer;
            if (pointer != IntPtr.Zero) retainNonNull?.Invoke(pointer);
        }, arguments); }
        catch { records.Fail(); throw; }
        records.Complete(); return observed;
    }
    private bool Alive(IntPtr value)
    {
        var arguments = new[] { A.Ref(value) }; _arguments.Begin(arguments);
        bool result;
        try { result = _api.InvokeValueWithPublication<bool>(_alive, IntPtr.Zero, arguments); }
        catch { _arguments.Fail(); throw; }
        _arguments.Complete(); return result;
    }
    private void RequireClass(IntPtr value, IntPtr expected)
    { if (_api.ObjectClass(value) != expected) throw new UnsupportedSignatureException("The rooted native launch input differs from its exact class."); }
    private static void RequirePresent(IntPtr value, string source)
    { if (value == IntPtr.Zero) throw new NativeLocalPlaySourceNotReadyException(source + " is not ready."); }
    private void RequireBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Native launch sources require the pinned Windows x64 Unity build.");
        using var file = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Native launch source disk image differs from the pinned GameAssembly.");
    }
}
