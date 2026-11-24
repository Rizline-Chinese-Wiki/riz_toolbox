using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Exact source identity for target-state reads; a chart conversion profile alone is insufficient.</summary>
internal interface ISeekTargetNativeProfile : ISeekChartNativeProfile
{
    IntPtr InformationClass { get; }
    IntPtr NoteClass { get; }
    IntPtr JudgeClass { get; }
    IntPtr HoldClass { get; }
    IntPtr ChallengeTimeClass { get; }
}

/// <summary>Read-only source contract for a new practice epoch. Complete native consumers are
/// pinned, but this does not replay input/history, initialize Judge, or restore Hold geometry.</summary>
internal sealed class NativeSeekTargetNativeProfile : ISeekTargetNativeProfile
{
    internal const string Fingerprint = GameplayClockNativeProfile.Fingerprint;
    internal const bool RuntimeTargetStateVerified = false;
    internal const bool HoldContinuationClosureKnown = false;
    internal const bool ProductionAdmissionKnown = false;
    internal const string Evidence = "Pinned GameAssembly " + Fingerprint
        + "; complete LevelControl.Start, NoteJudgeControl.Update, HoldControl.Update and both time-converter bodies and exact metadata."
        + " Judge seed ordering uses chart note.time and the original nested swap algorithm; windows use float32 progress seconds."
        + " Progress seconds mean ProgressControl._nowTime (GetTime minus initialized _offset), not media/GetTime seconds."
        + " The wrapped chart profile pins conversions and the nowSyncedTime producer; chart delay is not added again."
        + " Projection stops before input/AutoPlay consumption; no history, Judge initialization, Hold geometry or engine closure is implied.";
    private readonly NativeRuntime _api;
    private readonly ISeekChartNativeProfile _chart;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IntPtr _module;
    private readonly string _path;
    private readonly List<NativeMetadataClass> _classes = new();
    private readonly List<NativeMetadataMethod> _methods = new();
    private readonly List<NativeExactReadField> _fields = new();
    private int _checking, _faulted;
    public IntPtr ChartClass => _chart.ChartClass;
    public IntPtr ProgressClass => _chart.ProgressClass;
    public IntPtr InformationClass { get; }
    public IntPtr NoteClass { get; }
    public IntPtr JudgeClass { get; }
    public IntPtr HoldClass { get; }
    public IntPtr ChallengeTimeClass { get; }

    internal NativeSeekTargetNativeProfile(NativeRuntime api, ISeekChartNativeProfile chartProfile)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(chartProfile);
        _api = api; _chart = chartProfile;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new UnsupportedSignatureException("Target-state source evidence requires the pinned Windows x64 image.");
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero) throw new UnsupportedSignatureException("The actual target-state image is absent.");
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(_module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity)
            throw new UnsupportedSignatureException("The mapped target-state image path is unavailable.");
        _path = path.ToString();
        using (var file = File.OpenRead(_path))
        using (var hash = SHA256.Create())
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The target-state image differs from the pinned source evidence.");
        _chart.VerifyCurrent();
        InformationClass = Class("LevelInformation");
        NoteClass = Class("NoteInformation");
        JudgeClass = Class("NoteJudgeControl");
        HoldClass = Class("HoldControl");
        ChallengeTimeClass = Class("ChallengeTime");
        var level = Class("LevelControl");
        var noteData = Class("Note");
        var noteControl = Class("NoteControl");
        RequireClass(ChartClass, "InLevel.Chart"); RequireClass(ProgressClass, "InLevel.ProgressControl");
        CaptureFields(level, noteData, noteControl);
        Bind(level, "Start", 0x3ea580);
        Bind(JudgeClass, "Update", 0x3fd9e0);
        Bind(HoldClass, "Update", 0x3e80e0);
        Bind(ProgressClass, "ChartTimeToRealTime", "System.Single", 0x400400, "System.Double");
        Bind(ProgressClass, "RealTimeToChartTime", "System.Single", 0x400c80, "System.Double");
        VerifyCurrent();
    }

    private void CaptureFields(IntPtr level, IntPtr noteData, IntPtr noteControl)
    {
        Field(level, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(InformationClass, "levelControl", "InLevel.LevelControl", 0x20);
        Field(InformationClass, "progressControl", "InLevel.ProgressControl", 0x28);
        Field(InformationClass, "noteControl", "InLevel.NoteControl", 0x40);
        Field(InformationClass, "holdControl", "InLevel.HoldControl", 0x48);
        Field(InformationClass, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58);
        Field(InformationClass, "chart", "InLevel.Chart", 0x88);
        Field(InformationClass, "allComboNum", "System.Int32", 0x90);
        Field(InformationClass, "challengeTimeComboNum", "System.Int32", 0x94);
        Field(InformationClass, "speed", "System.Single", 0x98);
        Field(InformationClass, "offset", "System.Single", 0x9c);
        Field(InformationClass, "judgeTime", "System.Single", 0xa0);
        Field(InformationClass, "maxPerfectJudgeTimeInChallengeTime", "System.Single", 0xa4);
        Field(InformationClass, "normalPerfectJudgeTimeInChallengeTime", "System.Single", 0xa8);
        Field(InformationClass, "badTime", "System.Single", 0xac);
        Field(InformationClass, "nowTime", "System.Single", 0xb0);
        Field(InformationClass, "nowRealTime", "System.Single", 0xb4);
        Field(InformationClass, "nowSyncedTime", "System.Single", 0xb8);
        Field(InformationClass, "challengeCompletely", "System.Boolean", 0xbc);
        Field(InformationClass, "inChallengeTime", "System.Boolean", 0xbd);
        Field(InformationClass, "levelOver", "System.Boolean", 0xc0);
        Field(InformationClass, "sceneWidth", "System.Single", 0xc4);
        Field(ProgressClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(ProgressClass, "prepared", "System.Boolean", 0x41);
        Field(ProgressClass, "isPlaying", "System.Boolean", 0x42);
        Field(ProgressClass, "_offset", "System.Double", 0x48);
        Field(ProgressClass, "_nowTime", "System.Double", 0x50);
        Field(ProgressClass, "timeReady", "System.Boolean", 0x65);
        Field(ChartClass, "chartDelayMs", "System.Int32", 0x14);
        Field(ChartClass, "challengeTimes", "System.Collections.Generic.List<InLevel.ChallengeTime>", 0x28);
        Field(ChallengeTimeClass, "checkPoint", "System.Single", 0x10);
        Field(ChallengeTimeClass, "start", "System.Single", 0x14);
        Field(ChallengeTimeClass, "end", "System.Single", 0x18);
        Field(ChallengeTimeClass, "transTime", "System.Single", 0x1c);
        Field(noteControl, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(noteControl, "notes", "InLevel.NoteInformation[]", 0x28);
        Field(noteControl, "allNotes", "InLevel.NoteInformation[]", 0x30);
        Field(noteData, "type", "System.Int32", 0x10);
        Field(noteData, "time", "System.Single", 0x14);
        Field(noteData, "otherInformations", "System.Collections.Generic.List<System.Single>", 0x20);
        Field(NoteClass, "lineInformation", "InLevel.LineInformation", 0x20);
        Field(NoteClass, "noteData", "InLevel.Note", 0x28);
        Field(NoteClass, "isActive", "System.Boolean", 0x30);
        Field(NoteClass, "destroyed", "System.Boolean", 0x31);
        Field(NoteClass, "realTime", "System.Single", 0x34);
        Field(NoteClass, "holdEndRealTime", "System.Single", 0x38);
        Field(NoteClass, "holdEndFlag", "UnityEngine.Transform", 0x40);
        Field(NoteClass, "holdLine", "UnityEngine.Transform", 0x48);
        Field(NoteClass, "lastFrameHoldLineLength", "System.Single", 0x50);
        Field(NoteClass, "isStaticNote", "System.Boolean", 0x54);
        Field(NoteClass, "missed", "System.Boolean", 0x55);
        Field(NoteClass, "inChallengeTime", "System.Boolean", 0x56);
        Field(NoteClass, "fingerIds", "System.Collections.Generic.List<System.Int32>", 0x58);
        Field(JudgeClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(JudgeClass, "notes", "System.Collections.Generic.List<InLevel.NoteInformation>", 0x38);
        Field(JudgeClass, "tapNotesWaitingForJudge", "System.Collections.Generic.List<InLevel.NoteInformation>", 0x40);
        Field(JudgeClass, "dragNotesWaitingForJudge", "System.Collections.Generic.List<InLevel.NoteInformation>", 0x48);
        Field(JudgeClass, "holdOnJudgeCount", "System.Int32", 0x50);
        Field(JudgeClass, "_judgeTime", "System.Single", 0x54);
        Field(JudgeClass, "_maxPerfectJudgeTimeInChallengeTime", "System.Single", 0x58);
        Field(JudgeClass, "_normalPerfectJudgeTimeInChallengeTime", "System.Single", 0x5c);
        Field(JudgeClass, "badTime", "System.Single", 0x60);
        Field(JudgeClass, "_nowTime", "System.Single", 0x64);
        Field(JudgeClass, "_sceneWidth", "System.Single", 0x68);
        Field(JudgeClass, "_instantiateReady", "System.Boolean", 0x6c);
        Field(JudgeClass, "<InstantiateBadHit>k__BackingField", "System.Boolean", 0x78);
        Field(JudgeClass, "<AutoPlay>k__BackingField", "System.Boolean", 0x79);
        Field(JudgeClass, "<FreeHold>k__BackingField", "System.Boolean", 0x7a);
        Field(HoldClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(HoldClass, "holdNotes", "System.Collections.Generic.List<InLevel.NoteInformation>", 0x28);
        Field(HoldClass, "runningHoldNotes", "System.Collections.Generic.List<InLevel.NoteInformation>", 0x30);
        Field(HoldClass, "_nowTime", "System.Single", 0x38);
        Field(HoldClass, "_speed", "System.Single", 0x3c);
        Field(HoldClass, "_holdLineLength", "System.Single", 0x40);
    }

    private IntPtr Class(string name)
    {
        var cls = _api.Class("Assembly-CSharp.dll", "InLevel", name);
        RequireClass(cls, "InLevel." + name);
        return cls;
    }
    private void RequireClass(IntPtr cls, string name)
    {
        var description = _api.InspectMetadataClass(cls);
        if (description.TypeName != name)
            throw new UnsupportedSignatureException("Target-state metadata has another exact class: " + name);
        _classes.Add(description);
    }
    private void Field(IntPtr cls, string name, string type, int offset)
        => _fields.Add(_api.InspectExactDeclaredReadField(cls, name, type, false, offset));
    private void Bind(IntPtr cls, string name, int rva)
        => Bind(cls, name, "System.Void", rva);
    private void Bind(IntPtr cls, string name, string returns, int rva, params string[] parameters)
    {
        var method = _api.InspectDeclaredMethod(cls, name, parameters, returns, _module);
        if (method.Entry?.Rva != rva || method.GenericMethod || method.InflatedMethod
            || (method.Flags & 0x10) != 0 || !Bodies.Any(body => body.Rva == rva))
            throw new UnsupportedSignatureException("Target-state method metadata/entry differs: " + name);
        _methods.Add(method);
    }

    public void VerifyCurrent()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Target-state profile must be verified on its owner thread.");
        if (Volatile.Read(ref _faulted) != 0)
            throw new UnsupportedSignatureException("The target-state source profile has been withdrawn.");
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            throw new InvalidOperationException("Target-state source verification cannot reenter.");
        try
        {
            _chart.VerifyCurrent(); VerifyModule();
            foreach (var expected in _classes)
                if (_api.InspectMetadataClass(expected.Class) != expected)
                    throw new UnsupportedSignatureException("Target-state class metadata changed: " + expected.TypeName);
            foreach (var field in _fields)
                if (_api.InspectExactDeclaredReadField(field.DeclaringClass, field.Name, field.TypeName, false, field.Offset) != field)
                    throw new UnsupportedSignatureException("Target-state field metadata changed: " + field.Name);
            foreach (var expected in _methods)
            {
                var current = _api.InspectDeclaredMethod(expected.DeclaringClass.Class, expected.Name,
                    expected.ParameterTypes.ToArray(), expected.ReturnType, _module);
                if (current.MethodInfo != expected.MethodInfo || current.DeclaringClass != expected.DeclaringClass
                    || current.Entry != expected.Entry || current.Flags != expected.Flags
                    || current.ImplementationFlags != expected.ImplementationFlags || current.GenericMethod || current.InflatedMethod)
                    throw new UnsupportedSignatureException("Target-state method metadata changed: " + expected.Name);
            }
            foreach (var body in Bodies)
            {
                var address = IntPtr.Add(_module, body.Rva);
                HookTargetValidation.VerifyExecutable(address, _module);
                HookTargetValidation.VerifyReadable(address, (nuint)body.Length);
                var bytes = new byte[body.Length]; Marshal.Copy(address, bytes, 0, bytes.Length);
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(body.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("A complete target-state source body changed: " + body.Name);
            }
            foreach (var constant in Constants)
            {
                var expected = Convert.FromHexString(constant.HexBytes);
                var address = IntPtr.Add(_module, constant.Rva);
                HookTargetValidation.VerifyReadable(address, (nuint)expected.Length);
                var actual = new byte[expected.Length]; Marshal.Copy(address, actual, 0, actual.Length);
                if (!actual.AsSpan().SequenceEqual(expected))
                    throw new UnsupportedSignatureException("A target-state source constant changed: " + constant.Name);
            }
            VerifyModule(); _chart.VerifyCurrent();
        }
        catch { Interlocked.Exchange(ref _faulted, 1); throw; }
        finally { Volatile.Write(ref _checking, 0); }
    }
    private void VerifyModule()
    {
        if (GetModuleHandle("GameAssembly.dll") != _module || GetModuleHandle(_path) != _module)
            throw new UnsupportedSignatureException("The target-state source module changed or was unloaded.");
    }
    internal sealed record Body(string Name, int Rva, int Length, string Sha256);
    internal sealed record DataConstant(string Name, int Rva, string HexBytes);
    internal static IReadOnlyList<DataConstant> Constants { get; } = Array.AsReadOnly(new DataConstant[]
    {
        new("SortAndHold.DoubleEpsilon", 0x17c4bc0, "8dedb5a0f7c6b03e"),
        new("SortAndHold.FloatAbsMask", 0x17c4c70, "ffffff7fffffff7fffffff7fffffff7f"),
    });
    // Reproduction: work/seek-target-policy-astra-20261108/verify-target-profile.py.
    // Full next-metadata spans include each complete image runtime-function body.
    // The producer remains the wrapped clock profile's responsibility. Converter
    // metadata is rechecked here too, so a redirected MethodInfo cannot bypass it.
    internal static IReadOnlyList<Body> Bodies { get; } = Array.AsReadOnly(new Body[]
    {
        new("LevelControl.Start", 0x3ea580, 10656, "5d707459325a512d56a5fbca0f9ca20f5755ee253b9dc75945b903ec5508bc80"),
        new("NoteJudgeControl.Update", 0x3fd9e0, 8416, "8d9b7cf9dc1995d2a304f8532c4bc935e3291b9af3b724d7c6449b26d9ac7e98"),
        new("HoldControl.Update", 0x3e80e0, 3616, "127370acc502e038ad3a37a9409856303698b3da8b5d3b37b2a913292b5089fe"),
        new("ProgressControl.ChartTimeToRealTime", 0x400400, 624, "0a9e0327261de3d598c27d21a77f72adfbc5e9d587644b506a198008124efef2"),
        new("ProgressControl.RealTimeToChartTime", 0x400c80, 624, "198a612876421ca443fd64d8248f196f731b81764dcbbcd3dbc48021331b8d2c"),
    });
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
