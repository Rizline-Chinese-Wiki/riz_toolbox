using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Static unit/consumer facts belong to this exact image, layout and executable method bodies.
/// This authorizes read-only conversion; it does not establish runtime calibration or a value-control ABI.</summary>
internal sealed class GameplayClockNativeProfile : ISeekChartNativeProfile
{
    internal const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    internal const string Evidence = "Pinned GameAssembly " + Fingerprint + "; ProgressControl.Update writes progress seconds to nowRealTime/nowSyncedTime and maps nowTime through RealTimeToChartTime. Chart conversions use BPM/60 and bpmShift segments. Static consumer evidence; runtime calibration is unverified.";
    private readonly IntPtr _module;
    internal IntPtr ModuleBase => _module;
    internal IntPtr GetTimeMethodInfo { get; }
    internal VerifiedHookTarget GetTimeHookTarget { get; }
    private readonly GameplayClockCodeProfile _code;
    internal IntPtr InformationClass { get; }
    internal IntPtr ProgressClass { get; }
    internal IntPtr ChartClass { get; }
    IntPtr ISeekChartNativeProfile.ChartClass => ChartClass;
    IntPtr ISeekChartNativeProfile.ProgressClass => ProgressClass;
    void ISeekChartNativeProfile.VerifyCurrent()
    {
        VerifyModule();
        _code.VerifySeekChart();
        VerifyModule();
    }

    internal GameplayClockNativeProfile(NativeRuntime api, IntPtr information, NativePlaybackGetTimeHookLease? getTimeHook = null,
        NativePlaybackFilePreparation.PreparedFiles? preparedFiles = null)
    {
        _module = RequireMappedModule();
        InformationClass = information;
        ProgressClass = api.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl");
        ChartClass = api.Class("Assembly-CSharp.dll", "InLevel", "Chart");
        foreach (var (field, offset) in new[] { ("nowTime", 0xb0), ("nowRealTime", 0xb4), ("nowSyncedTime", 0xb8) })
            api.RequireInstanceFieldLayout(information, field, "System.Single", offset);
        api.RequireInstanceFieldLayout(information, "progressControl", "InLevel.ProgressControl", 0x28);
        api.RequireInstanceFieldLayout(information, "chart", "InLevel.Chart", 0x88);
        api.RequireInstanceFieldLayout(ProgressClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        api.RequireInstanceFieldLayout(ProgressClass, "prepareToPlay", "System.Boolean", 0x40);
        api.RequireInstanceFieldLayout(ProgressClass, "isPlaying", "System.Boolean", 0x42);
        api.RequireInstanceFieldLayout(ProgressClass, "_offset", "System.Double", 0x48);
        api.RequireInstanceFieldLayout(ProgressClass, "_nowTime", "System.Double", 0x50);
        api.RequireInstanceFieldLayout(ProgressClass, "timeReady", "System.Boolean", 0x65);
        api.RequireInstanceFieldLayout(ChartClass, "chartDelayMs", "System.Int32", 0x14);
        api.RequireInstanceFieldLayout(ChartClass, "bPM", "System.Single", 0x30);
        api.RequireInstanceFieldLayout(ChartClass, "bpmShifts", "System.Collections.Generic.List<InLevel.KeyPoint>", 0x38);
        RequireMethod(api, ProgressClass, "RealTimeToChartTime", new[] { "System.Double" }, "System.Single", 0x400c80);
        RequireMethod(api, ProgressClass, "ChartTimeToRealTime", new[] { "System.Double" }, "System.Single", 0x400400);
        RequireMethod(api, ProgressClass, "Update", Array.Empty<string>(), "System.Void", 0x400fe0);
        RequireMethod(api, ChartClass, "get_ChartDelay", Array.Empty<string>(), "System.Single", 0x3e5670);
        var player = api.Class("Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core", "MUGMusicPlayer");
        GetTimeMethodInfo = RequireMethod(api, player, "GetTime", Array.Empty<string>(), "System.Single", GameplayClockCodeProfile.GetTimeRva);
        GetTimeHookTarget = new("gameplay.music-player.GetTime", IntPtr.Add(_module, GameplayClockCodeProfile.GetTimeRva),
            GetTimeMethodInfo, player, ExactHookAbi.InstanceSingleReturn0);
        _code = new GameplayClockCodeProfile(_module, getTimeHook, GetTimeMethodInfo, preparedFiles);
        // Code profile construction has just completed the fixed body audit.
        VerifyModule();
    }

    private IntPtr RequireMethod(NativeRuntime api, IntPtr cls, string name, string[] parameters, string returns, int rva)
    {
        var method = api.InspectDeclaredMethod(cls, name, parameters, returns, _module);
        if (method.Entry?.Rva != rva || method.GenericMethod || method.InflatedMethod || (method.Flags & 0x10) != 0)
            throw new UnsupportedSignatureException("The exact clock method metadata/entry differs: " + name);
        return method.MethodInfo;
    }

    /// <summary>Only a broker-issued lease can account for the intentionally changed GetTime head.
    /// The full original body must match the pinned image. Withdrawal never falls back to the
    /// unhooked profile or revives an obsolete hook owner.</summary>
    internal void AttachGetTimeHook(NativePlaybackGetTimeHookLease lease)
    {
        VerifyModule();
        _code.AttachGetTimeHook(lease, GetTimeMethodInfo);
    }

    internal void VerifyCurrent()
    {
        VerifyModule();
        _code.VerifyCurrent();
    }

    internal void VerifyOperation()
    {
        VerifyModule();
        _code.VerifyOperation();
        VerifyModule();
    }

    internal void VerifyOperation(NativePlaybackGetTimeHookLease expectedHook)
    {
        VerifyModule();
        _code.VerifyOperation(expectedHook);
        VerifyModule();
    }

    internal void VerifyReadOnlyBodies() => _code.VerifyReadOnlyBodies();

    private void VerifyModule()
    {
        if (GetModuleHandle("GameAssembly.dll") != _module) throw new UnsupportedSignatureException("The clock semantics module changed.");
    }

    internal static IntPtr RequireMappedModule()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new UnsupportedSignatureException("Clock semantics require the pinned Windows x64 image.");
        var module = GetModuleHandle("GameAssembly.dll");
        return module != IntPtr.Zero ? module : throw new UnsupportedSignatureException("The actual GameAssembly image is absent.");
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
