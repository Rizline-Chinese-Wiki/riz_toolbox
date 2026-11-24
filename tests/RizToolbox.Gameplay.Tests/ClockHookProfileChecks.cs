using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class ClockCodeProfileChecks
{
    internal static int RunHooked(string path, string dobby, string helper, string mode)
    {
        path = Path.GetFullPath(path);
        if (!path.StartsWith("D:\\riz-build-cache-next\\", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("An isolated evidence copy is required.");
        var module = LoadLibraryEx(path, IntPtr.Zero, 1);
        if (module == IntPtr.Zero) throw new InvalidOperationException("Cannot map the evidence image.");
        var profile = new GameplayClockCodeProfile(module);
        var method = Marshal.AllocHGlobal(88); var returnType = Marshal.AllocHGlobal(16);
        var name = Marshal.StringToHGlobalAnsi("GetTime");
        Marshal.Copy(new byte[88], 0, method, 88); Marshal.Copy(new byte[16], 0, returnType, 16);
        Marshal.WriteInt32(returnType, 8, 0x0c0000);
        var targetAddress = IntPtr.Add(module, GameplayClockCodeProfile.GetTimeRva); var declaring = new IntPtr(0x12345000);
        Marshal.StructureToPtr(new HookTargetValidation.MethodInfo2022 { MethodPointer = targetAddress,
            VirtualMethodPointer = targetAddress, Class = declaring, Name = name, ReturnType = returnType }, method, false);
        var target = new VerifiedHookTarget("owned-mapped-gettime-code", targetAddress, method, declaring, ExactHookAbi.InstanceSingleReturn0);
        var broker = new ExactHookBroker(dobby, new Main(), new TestLog()); var faults = new ManualResetEventSlim();
        var lease = broker.BindPlaybackGetTime(target, module, GameplayClockNativeProfile.Fingerprint, 640,
            "32ee6e9d92f72b234c4003acda0a03336795a3719cd0b7f83debdd0ff58399bc", helper,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(helper))).ToLowerInvariant(),
            () => throw new InvalidOperationException("Game method execution is forbidden in this code test."), _ => faults.Set());
        profile.AttachGetTimeHook(lease, method); profile.VerifyCurrent();
        var startup = new GameplayClockCodeProfile(module, lease, method); startup.VerifyCurrent();
        profile.VerifyOperation(lease); startup.VerifyOperation(lease);
        var copiedProfile = (GameplayClockCodeProfile)typeof(object)
            .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(profile, null)!;
        var copiedLease = (NativePlaybackGetTimeHookLease)typeof(object)
            .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(lease, null)!;
        Reject(() => copiedProfile.VerifyOperation(lease));
        Reject(() => profile.VerifyOperation(copiedLease));
        var workerRejected = Task.Run(() =>
        {
            try { profile.VerifyOperation(lease); return false; }
            catch (RizToolbox.Runtime.UnsupportedSignatureException) { return true; }
        }).GetAwaiter().GetResult();
        if (!workerRejected) throw new InvalidOperationException("A worker borrowed the fixed code profile.");
        Reject(() => new GameplayClockCodeProfile(module));
        if (lease.ReadHealth().OriginalCalls != 0) throw new InvalidOperationException("A game original was called.");
        var checks = mode == "normal" ? NormalHook(profile, startup, lease, module, method) : TamperedHook(profile, lease, module, targetAddress, mode, faults);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { kind = "mapped-clock-hook-profile", mode,
            passed = true, checks = checks + 8, originalCalls = 0, gameCodeExecuted = false, gameLaunched = false,
            dllMainExecuted = false, productionAdmission = false, patchBytes = lease.PatchedByteCount }));
        // The broker retains this loader reference and metadata for native process lifetime.
        return 0;
    }
    private static int NormalHook(GameplayClockCodeProfile profile, GameplayClockCodeProfile startup,
        NativePlaybackGetTimeHookLease lease, IntPtr module, IntPtr method)
    {
        var binding = new NativePlaybackBinding("mapped-code", 1, 1, 1, 11, 12, 13, 14, 15, 16, method.ToInt64());
        var initial = new NativePlaybackClock(binding, 100, 4, 1, 120);
        var publication = new NativePlaybackClockPublication(lease, initial.Capture(), IntPtr.Add(module, 0x40121d), profile.VerifyCurrent);
        var clock = new NativePlaybackClock(binding, 100, 4, 1, 120, publication);
        using (var change = clock.PreparePausedRate(binding, 2))
        {
            change.Commit(); if (!change.Confirm() || lease.ReadHealth().Revision != 2) throw new InvalidOperationException("Broker-backed clock publication failed.");
            change.Restore(); if (!change.ConfirmRestored() || lease.ReadHealth().Revision != 3) throw new InvalidOperationException("Broker-backed clock restoration failed.");
        }
        profile.VerifyCurrent();
        if (lease.ReadHealth().OriginalCalls != 0) throw new InvalidOperationException("Code verification called the game original.");
        lease.Dispose(); Reject(profile.VerifyCurrent); Reject(startup.VerifyCurrent);
        Reject(() => profile.VerifyOperation(lease)); Reject(() => startup.VerifyOperation(lease));
        return 6;
    }
    private static int TamperedHook(GameplayClockCodeProfile profile, NativePlaybackGetTimeHookLease lease,
        IntPtr module, IntPtr targetAddress, string mode, ManualResetEventSlim faults)
    {
        var address = mode switch { "head" => targetAddress, "tail" => IntPtr.Add(targetAddress, 600),
            "consumer" => IntPtr.Add(module, 0x400fe0 + 20), "original" => lease.OriginalAddress,
            _ => throw new ArgumentException("Unknown mapped-code mode.") };
        var original = Marshal.ReadByte(address); Write(address, (byte)(original ^ 1));
        try { Reject(profile.VerifyCurrent); }
        finally { Write(address, original); }
        Reject(profile.VerifyCurrent);
        if (!faults.Wait(2000)) throw new InvalidOperationException("Fault owner was not notified.");
        return 2;
    }
}
