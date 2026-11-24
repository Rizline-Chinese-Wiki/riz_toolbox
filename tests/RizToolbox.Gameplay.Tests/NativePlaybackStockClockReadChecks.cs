using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class NativePlaybackStockMetadataChecks
{
    private static int RunRetainedClockRead(MetadataFixture fixture, NativeRuntime api,
        NativeRuntime.NativePlaybackStockTargets targets, PlaybackSourceGroupLease group,
        NativePlaybackStockCodeProfile stock, GameplayClockNativeProfile retained)
    {
        var main = new GroupMain();
        var host = new NativePlaybackStockRateHost(api, main, new GroupLogger(), new GameEventState(), null!, "unused");
        using var backend = new GameplayNativeBackend(api, main, host);
        RetainedGroupEvidence.Add(host);
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        void SetHostField(string name, object value) => typeof(NativePlaybackStockRateHost).GetField(name, Members)!.SetValue(host, value);

        // Exercise the real backend selector from the public capture path.
        // The shipped adapter calls its legacy selector before the repair.
        var refresh = typeof(GameplayNativeBackend).GetMethod("RefreshReadClockProfile", Members)
            ?? typeof(GameplayNativeBackend).GetMethod("RefreshClockProfile", Members)!;
        (GameplayClockNativeProfile? Profile, string? Error) Read()
        {
            var arguments = new object?[] { null };
            var profile = (GameplayClockNativeProfile?)refresh.Invoke(backend, arguments);
            return (profile, (string?)arguments[0]);
        }

        var dormant = Read();
        Check(dormant.Profile is null && dormant.Error is not null, "A dormant host granted clock semantics.");
        // The test uses the mapped fixture's real registered group and exact
        // retained profiles, without constructing any logical game session.
        SetHostField("_group", group);
        SetHostField("_stock", targets);
        SetHostField("_sourceProfile", stock);
        SetHostField("_clockProfile", retained);
        var start = group.ReadOperationDiagnostics();
        var elapsed = Stopwatch.StartNew();
        var first = Read(); var second = Read();
        elapsed.Stop();
        var end = group.ReadOperationDiagnostics();
        var fullAudits = end.FullAudits - start.FullAudits;
        var clockChecks = end.ClockChecks - start.ClockChecks;
        var reusedProfile = ReferenceEquals(first.Profile, retained) && ReferenceEquals(second.Profile, retained);
        Console.WriteLine($"Read profile audits={fullAudits} operationChecks={clockChecks} reused={reusedProfile} milliseconds={elapsed.Elapsed.TotalMilliseconds:F1}.");
        Check(reusedProfile && first.Error is null && second.Error is null && fullAudits == 0 && clockChecks >= 2,
            $"Two independent field reads must reuse the registered profile and verify live ownership. {first.Error} {second.Error}");

        var address = IntPtr.Add(targets.GetTime.Address, 600);
        var saved = Marshal.ReadByte(address);
        Write(address, (byte)(saved ^ 1));
        (GameplayClockNativeProfile? Profile, string? Error) changed;
        try { changed = Read(); }
        finally { Write(address, saved); }
        var restored = Read();
        Check(changed.Profile is null && changed.Error is not null && restored.Profile is null && !group.GetTimeLease.IsActive,
            "A new read missed an actual GetTime mutation or restore revived the faulted owner.");
        Check(fixture.Base.Invocations == 0 && fixture.Base.ObjectAllocations == 0,
            "A read-only clock selector invoked or allocated a game object.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "mapped-retained-clock-read", passed = true,
            foundationEpoch = group.FoundationEpoch, fullAudits, clockChecks, reusedProfile, dormantRefused = true,
            mutationRefused = true, restorationRefused = true, nativeGameCodeExecuted = false }));
        return 0;
    }

    private sealed partial class MetadataFixture
    {
        private readonly IntPtr _clockReadCoreImage = new(0x7003);
        private IntPtr _clockReadCoreName;

        internal void ConfigureClockReadMetadata(IntPtr module)
        {
            ConfigureClockMetadata(module);
            _clockReadCoreName = String("UnityEngine.CoreModule.dll");
            Marshal.WriteIntPtr(_assemblies, 2 * IntPtr.Size, _clockReadCoreImage);
            Class(_clockReadCoreImage, "UnityEngine", "Resources", out _);
            Class(_clockReadCoreImage, "UnityEngine", "Object", out _);
            Class(_gameImage, "", "NormalModeInLevelController", out _);
        }
    }
}
