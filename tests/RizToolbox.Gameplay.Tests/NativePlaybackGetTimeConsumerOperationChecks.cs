using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class NativePlaybackStockMetadataChecks
{
    internal static int RunGetTimeConsumerOperation(string path, string dobby, string getTimeHelper, string sourceHelper)
    {
        var checks = 0;
        void Assert(bool value, string reason) { ++checks; Check(value, reason); }
        void Refuse(Action action) { Reject(action); ++checks; }
        T Copy<T>(T value) where T : class => (T)typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;

        path = Path.GetFullPath(path);
        if (!path.StartsWith("D:\\riz-build-cache-next\\", StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(path).Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use the isolated pinned GameAssembly evidence copy.");
        var module = LoadLibraryEx(path, IntPtr.Zero, 1); // no imports or DllMain
        if (module == IntPtr.Zero) throw new InvalidOperationException("Cannot map the fixed consumer evidence image.");
        var fixture = new MetadataFixture(module); fixture.ConfigureClockMetadata(module);
        var api = fixture.CreateReadableRuntime();
        var nativeProfile = new GameplayClockNativeProfile(api, fixture.ClockInformationClass);
        var consumer = new NativePlaybackGetTimeCallProfile(nativeProfile);
        var unattached = new GameplayClockCodeProfile(module);
        var target = nativeProfile.GetTimeHookTarget;
        var forbiddenCallbacks = 0;
        var broker = new ExactHookBroker(dobby, new GroupMain(), new GroupLogger());
        // The formal source group also requires the exact UnityPlayer image
        // and retained foundation. This offline code test installs only its
        // real standalone GetTime owner; it claims no formal Fresh admission.
        var hook = broker.BindPlaybackGetTime(target, module, GameplayClockNativeProfile.Fingerprint, 640,
            "32ee6e9d92f72b234c4003acda0a03336795a3719cd0b7f83debdd0ff58399bc",
            getTimeHelper, GroupHashFile(getTimeHelper),
            () => { ++forbiddenCallbacks; throw new InvalidOperationException("Mapped game execution is forbidden."); },
            _ => { });
        RetainedGroupEvidence.Add(fixture); RetainedGroupEvidence.Add(api);
        RetainedGroupEvidence.Add(broker); RetainedGroupEvidence.Add(hook);
        var retainedOwner = hook.OwnerForGroupAdoption();
        var auditStart = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
        hook.VerifyCurrent(module, target.Address, target.MethodInfo);
        var singleFullAudit = NativePlaybackExecutableSeal.ReadThreadFullImageAudits() - auditStart;
        auditStart = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
        nativeProfile.AttachGetTimeHook(hook);
        var attachAudits = NativePlaybackExecutableSeal.ReadThreadFullImageAudits() - auditStart;
        auditStart = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
        _ = new GameplayClockNativeProfile(api, fixture.ClockInformationClass, hook);
        var newProfileAudits = NativePlaybackExecutableSeal.ReadThreadFullImageAudits() - auditStart;
        Console.WriteLine($"Clock code audits single={singleFullAudit} attach={attachAudits} construction={newProfileAudits}.");
        Assert(singleFullAudit > 0 && attachAudits == 2 * singleFullAudit && newProfileAudits == 2 * singleFullAudit,
            "Clock attachment and construction must verify exactly once before and once after the fixed consumer body reads.");
        var code = new GameplayClockCodeProfile(module, hook, target.MethodInfo);
        var fixedReturn = module.ToInt64() + NativePlaybackGetTimeCallProfile.ProgressUpdateReturnRva;
        Assert(consumer.AllowsCall(target.MethodInfo.ToInt64(), 0) && consumer.AllowsNull(fixedReturn),
            "The actual full consumer profile rejected metadata or its fixed null return.");
        Assert(!consumer.AllowsNull(fixedReturn - 1) && !consumer.AllowsNull(fixedReturn + 1)
            && !consumer.AllowsCall(target.MethodInfo.ToInt64() + 1, fixedReturn),
            "The fixed consumer accepted an adjacent null return or foreign metadata.");

        var binding = new NativePlaybackBinding("mapped-consumer-operation", 1, 1, 1,
            0x201, 0x202, 0x203, 0x204, 0x205, 0x206, target.MethodInfo.ToInt64());
        var initial = new NativePlaybackClockState(binding, 1, NativePlaybackPhase.Paused, 100, 4, 1, 120);
        var publication = new NativePlaybackClockPublication(hook, initial, new IntPtr(fixedReturn), nativeProfile.VerifyCurrent);
        Assert(hook.ReadHealth() is { Known: true, AuthorityActive: true, Revision: 1, OriginalCalls: 0 },
            "The mapped operation lacks its actual native Clock policy.");
        var before = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
        for (var index = 0; index < 3; ++index) consumer.VerifyOperation(hook);
        var after = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
        Assert(after > before, "A standalone consumer without a group skipped its required complete installation audit.");

        var alternate = NativePlaybackGetTimeHookLease.Issue(retainedOwner);
        Refuse(() => consumer.VerifyOperation(Copy(hook)));
        Refuse(() => consumer.VerifyOperation(alternate));
        Refuse(() => Copy(consumer).VerifyOperation(hook));
        Refuse(() => Copy(consumer).AllowsNull(fixedReturn));
        Refuse(() => Copy(code).VerifyOperation(hook));
        Refuse(() => unattached.VerifyOperation(hook));
        Refuse(() => code.VerifyOperation(alternate));
        var workerRefused = Task.Run(() =>
        {
            try { consumer.VerifyOperation(hook); return false; }
            catch (RizToolbox.Runtime.UnsupportedSignatureException) { return true; }
        }).GetAwaiter().GetResult();
        Assert(workerRefused, "A worker borrowed the owner thread's fixed consumer.");
        var rejected = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
        Assert(rejected == after,
            "Foreign consumer identities reached the actual Clock channel.");
        consumer.VerifyOperation(hook);
        Assert(hook.ReadHealth() is { Known: true, AuthorityActive: true, Revision: 1, OriginalCalls: 0 },
            "Consumer refusals revoked the legitimate owner's unchanged Clock policy.");

        // Standalone consumers retain the complete code audit. Grouped narrow
        // channels are covered separately by the real owned operation tests.
        var address = IntPtr.Add(module, 0x400fe0 + 20);
        var saved = Marshal.ReadByte(address);
        Write(address, (byte)(saved ^ 1));
        try
        {
            Refuse(() => consumer.VerifyOperation(hook));
        }
        finally { Write(address, saved); }
        Refuse(() => consumer.VerifyOperation(hook));
        Refuse(() => consumer.AllowsNull(fixedReturn));
        var terminal = retainedOwner.ReadTransportHealth();
        Assert(!hook.IsActive && !terminal.AuthorityActive && terminal.OriginalCalls == 0,
            "Restored code revived the faulted consumer or executed a mapped game original.");
        Assert(forbiddenCallbacks == 0 && fixture.Base.Invocations == 0 && fixture.Base.ObjectAllocations == 0,
            "Offline fixed-consumer verification executed a game method or allocated a game object.");
        Assert(broker.ReadActualPlaybackDobbyCallCounts() == (1, 1),
            "Consumer operation replaced an actual installed physical member.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "mapped-gettime-consumer-operation", mode = "consumer-operation",
            passed = true, checks, before, after, rejected, originalCalls = terminal.OriginalCalls,
            physicalMemberCount = 1, fixedReturnRva = NativePlaybackGetTimeCallProfile.ProgressUpdateReturnRva,
            callProfileConstructed = true, standaloneConsumerExecuted = true, groupedConsumerExecuted = false, syntheticMetadata = true,
            fixedGameConsumerExecuted = false, formalFreshSourceScopeCovered = false,
            gameCodeExecuted = false, dllMainExecuted = false, installed = false, productionAdmission = false }));
        GC.KeepAlive(publication);
        return 0;
    }
}
