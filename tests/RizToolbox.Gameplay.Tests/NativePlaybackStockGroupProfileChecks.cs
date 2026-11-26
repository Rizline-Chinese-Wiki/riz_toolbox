using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class NativePlaybackStockMetadataChecks
{
    // Successful installation owners intentionally retain module and metadata for this process.
    // Nothing here invokes an entry/original from the mapped game image.
    private static readonly List<object> RetainedGroupEvidence = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GroupBeforeInit(IntPtr init, [MarshalAs(UnmanagedType.LPWStr)] string absoluteDobbyPath);
    [DllImport("ntdll", EntryPoint = "RtlLookupFunctionEntry")]
    private static extern IntPtr GroupLookupFunction(ulong pc, out ulong imageBase, IntPtr history);

    internal static int RunGroupMapped(string path, string dobby, string getTimeHelper, string sourceHelper, string mode)
    {
        var checks = 0;
        void Assert(bool value, string reason) { ++checks; Check(value, reason); }
        void Refuse(Action action) { Reject(action); ++checks; }
        try
        {
            path = Path.GetFullPath(path);
            if (!path.StartsWith("D:\\riz-build-cache-next\\", StringComparison.OrdinalIgnoreCase)
                || mode is not ("normal" or "pitch-tail" or "body" or "consumer" or "foundation-normal" or "foundation-outside"
                    or "foundation-unwind" or "foundation-clock-read" or "clock-preparation"))
                throw new ArgumentException("Use a fresh isolated D-drive image and a known joint mode.");
            var module = LoadLibraryEx(path, IntPtr.Zero, 1); // DONT_RESOLVE_DLL_REFERENCES: no game DllMain/import initialization
            if (module == IntPtr.Zero) throw new InvalidOperationException("Cannot map the joint fixed evidence image: " + Marshal.GetLastWin32Error());
            if (mode is "foundation-clock-read" or "clock-preparation")
            {
                var unity = Path.GetFullPath(Environment.GetEnvironmentVariable("RIZ_CLOCK_READ_MAPPED_UNITY_PLAYER")
                    ?? throw new ArgumentException("An isolated pinned UnityPlayer image is required for the retained clock read."));
                if ((!unity.StartsWith("D:\\riz-m2\\", StringComparison.OrdinalIgnoreCase)
                    && !unity.StartsWith("D:\\riz-build-cache-next\\", StringComparison.OrdinalIgnoreCase))
                    || !Path.GetFileName(unity).Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase)
                    || GroupHashFile(unity) != NativePlaybackLowerPitchTarget.UnityPlayerSha256
                    || LoadLibraryEx(unity, IntPtr.Zero, 1) == IntPtr.Zero)
                    throw new InvalidOperationException("Cannot map the isolated pinned UnityPlayer without resolving imports.");
                // Fault in the untouched wrapper cache page before capturing
                // its memory identity. The mapped fixture has no game startup.
                Check(Marshal.ReadIntPtr(IntPtr.Add(module, 0x1fb0650)) == IntPtr.Zero,
                    "The offline wrapper cache is not initially zero.");
            }
            NativeFirstInstall.PlaybackMembership? foundation = null;
            NativeFirstInstall? foundationProducer = null;
            if (mode.StartsWith("foundation-", StringComparison.Ordinal))
            {
                var helperPath = Path.Combine(Path.GetDirectoryName(typeof(NativeFirstInstall).Assembly.Location)!, "RizToolbox.JudgeContext.dll");
                var helper = NativeLibrary.Load(helperPath);
                var before = Marshal.GetDelegateForFunctionPointer<GroupBeforeInit>(NativeLibrary.GetExport(helper, "RizFirstInstall_BeforeIl2CppInit"));
                Assert(before(NativeLibrary.GetExport(module, "il2cpp_init"), dobby) == 1,
                    "The actual mapped FirstInstall producer did not publish its private pre-Prepare evidence.");
                foundationProducer = NativeFirstInstall.RequireReady();
                foundation = foundationProducer.RequirePlaybackGroupAdoptionEvidence();
                Assert(foundation.Module == module && foundation.Epoch == 1 && foundation.ImageRanges().Length == 64,
                    "The real FirstInstall owner did not issue its exact private 64-range member.");
                RetainedGroupEvidence.Add(foundationProducer); RetainedGroupEvidence.Add(foundation);
            }
            var fixture = new MetadataFixture(module);
            if (mode == "foundation-clock-read") fixture.ConfigureClockReadMetadata(module);
            if (mode == "clock-preparation") fixture.ConfigureClockMetadata(module);
            RetainedGroupEvidence.Add(fixture);
            var api = fixture.CreateReadableRuntime();
            var targets = api.ResolvePlaybackStockTargets(module); targets.VerifyCurrent();
            RetainedGroupEvidence.Add(api); RetainedGroupEvidence.Add(targets);
            var stockAttached = new NativePlaybackStockCodeProfile(module);
            var clockAttached = new GameplayClockCodeProfile(module);
            var retainedClock = mode == "foundation-clock-read"
                ? new GameplayClockNativeProfile(api, fixture.ClockInformationClass) : null;
            var preparationProfile = mode == "clock-preparation"
                ? new GameplayClockNativeProfile(api, fixture.ClockInformationClass) : null;
            var preparationConsumer = preparationProfile is null ? null : new NativePlaybackGetTimeCallProfile(preparationProfile);
            var groupTargets = PlaybackSourceGroupTargets.FromStock(targets);
            var forbiddenCallbacks = 0; var sourceBefore = 0; var sourceAfter = 0;
            var getTimeFaults = 0; var sourceFaults = 0;
            var broker = new ExactHookBroker(dobby, new GroupMain(), new GroupLogger());
            var group = broker.BindPlaybackSourceGroup(groupTargets,
                getTimeHelper, GroupHashFile(getTimeHelper), sourceHelper, GroupHashFile(sourceHelper),
                () => { Interlocked.Increment(ref forbiddenCallbacks); throw new InvalidOperationException("Mapped game methods must never execute."); },
                _ => Interlocked.Increment(ref getTimeFaults), _ => Interlocked.Increment(ref sourceFaults),
                observation => { Interlocked.Increment(ref sourceBefore); observation.Dispose(); },
                _ => Interlocked.Increment(ref sourceAfter), foundation: foundation);
            RetainedGroupEvidence.Add(broker); RetainedGroupEvidence.Add(group);
            if (preparationProfile is not null)
            {
                preparationProfile.AttachGetTimeHook(group.GetTimeLease);
                var hook = group.GetTimeLease;
                var retainedOwner = hook.OwnerForGroupAdoption();
                var caller = module.ToInt64() + NativePlaybackGetTimeCallProfile.ProgressUpdateReturnRva;
                var beforeFull = group.ReadOperationDiagnostics();
                Assert(preparationConsumer!.AllowsNull(caller), "Initial admission refused its fixed null-MethodInfo return site.");
                var afterFull = group.ReadOperationDiagnostics();
                Assert(afterFull.FullAudits > beforeFull.FullAudits && afterFull.FullImageAudits > beforeFull.FullImageAudits,
                    "Initial admission skipped the full installed group and image audits.");
                void VerifyPreparation() => preparationConsumer.VerifyPreparedSource(hook);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (var index = 0; index < 2; ++index) VerifyPreparation();
                watch.Stop();
                var afterChecks = group.ReadOperationDiagnostics();
                Console.WriteLine($"Repeated clock preparation: group audits={afterChecks.FullAudits - afterFull.FullAudits}, image audits={afterChecks.FullImageAudits - afterFull.FullImageAudits}, GetTime checks={afterChecks.ClockChecks - afterFull.ClockChecks}, elapsedMs={watch.Elapsed.TotalMilliseconds:F2}.");
                Assert(afterChecks.FullAudits == afterFull.FullAudits && afterChecks.FullImageAudits == afterFull.FullImageAudits,
                    "Repeated clock preparation traversed the already admitted group or whole image.");
                Assert(afterChecks.ClockChecks - afterFull.ClockChecks == 4,
                    "Each preparation check must bracket the fixed consumers with two current native GetTime checks.");

                // The nested Seek profiles call the chart interface, not the source consumer.
                ISeekChartNativeProfile seekChart = preparationProfile;
                seekChart.VerifyCurrent();
                var afterSeek = group.ReadOperationDiagnostics();
                Assert(afterSeek.FullAudits == afterChecks.FullAudits && afterSeek.FullImageAudits == afterChecks.FullImageAudits
                    && afterSeek.ClockChecks - afterChecks.ClockChecks == 2,
                    "A nested Seek chart verification repeated the full installation audit or missed its live GetTime checks.");

                foreach (var rva in new[] { 0x400fe0, 0x400c80, 0x400400, 0x3e5670 })
                {
                    var address = IntPtr.Add(module, rva + 1);
                    var saved = Marshal.ReadByte(address);
                    Write(address, (byte)(saved ^ 1));
                    try { Refuse(VerifyPreparation); Refuse(seekChart.VerifyCurrent); }
                    finally { Write(address, saved); }
                    VerifyPreparation();
                }
                var getTimeBody = IntPtr.Add(targets.GetTime.Address, 20);
                var original = Marshal.ReadByte(getTimeBody);
                Write(getTimeBody, (byte)(original ^ 1));
                try { Refuse(seekChart.VerifyCurrent); }
                finally { Write(getTimeBody, original); }
                Refuse(VerifyPreparation);
                Refuse(seekChart.VerifyCurrent);
                var terminal = retainedOwner.ReadTransportHealth();
                Assert(!hook.IsActive && terminal.OriginalCalls == 0 && forbiddenCallbacks == 0
                    && sourceBefore == 0 && sourceAfter == 0 && fixture.Base.Invocations == 0,
                    "The repeated check revived the faulted GetTime owner or executed mapped game code.");
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "mapped-clock-preparation", mode, passed = true, checks,
                    groupAudits = afterChecks.FullAudits - afterFull.FullAudits,
                    imageAudits = afterChecks.FullImageAudits - afterFull.FullImageAudits,
                    getTimeChecks = afterChecks.ClockChecks - afterFull.ClockChecks,
                    seekGroupAudits = afterSeek.FullAudits - afterChecks.FullAudits,
                    seekImageAudits = afterSeek.FullImageAudits - afterChecks.FullImageAudits,
                    seekGetTimeChecks = afterSeek.ClockChecks - afterChecks.ClockChecks,
                    gameCodeExecuted = false, dllMainExecuted = false, productionAdmission = false }));
                return 0;
            }
            if (retainedClock is not null)
            {
                retainedClock.AttachGetTimeHook(group.GetTimeLease);
                stockAttached.AttachSourceGroup(group, targets);
                return RunRetainedClockRead(fixture, api, targets, group, stockAttached, retainedClock);
            }
            var getTime = group.GetTimeLease; var pitch = group.SetPitchLease;
            var getTimeOwner = getTime.OwnerForGroupAdoption(); // same retained owner; transport diagnostics remain readable after withdrawal
            Assert(broker.ReadActualPlaybackDobbyCallCounts() == (20, 20), "Fixed image did not perform twenty actual Dobby Prepare/Commit calls.");
            group.VerifyCurrent(targets);
            if (foundation is not null)
                Assert(group.FoundationEpoch == foundation.Epoch, "The twenty-member group lost its actual private foundation epoch.");
            Assert(group.IsActive && getTime.IsActive && pitch.IsActive && group.Epoch == pitch.GroupEpoch
                && !group.ProductionAdmission && !getTime.ProductionAdmission,
                "All twenty fixed-image members did not arm in the same private nonproduction group.");
            Assert(getTime.PatchedByteCount == 6 && pitch.PatchedByteCount == 6
                && getTime.OriginalAddress != getTime.DobbyOriginalAddress && pitch.OriginalAddress != pitch.DobbyOriginalAddress
                && getTime.TrampolineUnwindAddress != IntPtr.Zero && pitch.TrampolineUnwindAddress != IntPtr.Zero
                && getTime.TrampolineUnwindAddress != pitch.TrampolineUnwindAddress,
                "The independent GetTime/pitch original/thunk/unwind receipts are incomplete.");
            var sourceMembers = PlaybackSourceGroupTargets.SourceRoles(constructor: true, directAudio: true, progressResource: true).Select(group.SourceLease).ToArray();
            Assert(sourceMembers.Length == 19 && sourceMembers.All(value => value.IsActive && value.GroupEpoch == group.Epoch
                && value.OriginalAddress != value.DobbyOriginalAddress && value.TrampolineUnwindAddress != IntPtr.Zero
                && value.RelocatedByteCount == (value.Role is NativePlaybackSourceRole.ProgressMusicPlayer or NativePlaybackSourceRole.ProgressClip ? 8 : value.Role == NativePlaybackSourceRole.ProgressPlay ? 7 : 6))
                && sourceMembers.Select(value => value.TrampolineUnwindAddress).Append(getTime.TrampolineUnwindAddress).Distinct().Count() == 20,
                "Every independent physical source did not retain its own relocation/unwind in the shared group.");
            var resourceLease = group.SourceLease(NativePlaybackSourceRole.ProgressClip);
            Assert(group.HasProgressResource && resourceLease.Kind == NativePlaybackSourceKind.Reference1
                && resourceLease.RelocatedByteCount == 8 && resourceLease.EntryAddress != IntPtr.Zero
                && resourceLease.EntryAddress != targets.ProgressClip.Address && resourceLease.OriginalBodyLength == 13
                && resourceLease.OriginalBodySha256 == "bb8036f89480f121c1bd96fd6bcd7c739d4d45f2bb58f562a0fb49b974aab390"
                && resourceLease.OriginalAddress != resourceLease.DobbyOriginalAddress
                && resourceLease.TrampolineUnwindAddress != group.SourceLease(NativePlaybackSourceRole.ProgressMusicPlayer).TrampolineUnwindAddress,
                "Progress.set_Clip lost its independent offset30 resource leaf relocation/unwind.");
            var constructorLease = group.SourceLease(NativePlaybackSourceRole.PlayerConstructor);
            Assert(constructorLease.Kind == NativePlaybackSourceKind.Reference1
                && constructorLease.RelocatedByteCount == 6
                && constructorLease.OriginalAddress != constructorLease.DobbyOriginalAddress,
                "Shared constructor lost its independent exact reference-argument trampoline.");
            var originalBodies = NativePlaybackStockCodeProfile.HookBodies
                .Select(body => (Address: IntPtr.Add(module, body.Rva), Hash: body.Sha256, body.Length)).ToArray();
            Assert(group.MatchesOriginalBodies(originalBodies), "The closed group differs from the fixed twenty-body profile.");
            if (mode == "normal")
            {
                Assert(group.MatchesOriginalBodies(originalBodies.Reverse()
                    .Select(body => (body.Address, body.Hash.ToUpperInvariant(), body.Length)).ToArray()),
                    "A complete original-body query depends on request order or hash letter case.");
                var changed = originalBodies.ToArray();
                changed[0] = (changed[0].Address, new string('0', 64), changed[0].Length);
                Assert(!group.MatchesOriginalBodies(changed), "A batch accepted an incorrect complete original-body hash.");
                changed[0] = (originalBodies[0].Address, originalBodies[0].Hash, originalBodies[0].Length - 1);
                Assert(!group.MatchesOriginalBodies(changed), "A batch accepted a truncated original-body length.");
                changed[0] = (IntPtr.Add(originalBodies[0].Address, 1), originalBodies[0].Hash, originalBodies[0].Length);
                Assert(!group.MatchesOriginalBodies(changed), "A batch accepted an address outside its exact registered entries.");
                changed[0] = originalBodies[1];
                Assert(!group.MatchesOriginalBodies(changed), "A batch accepted duplicate physical entries.");
                Assert(!group.MatchesOriginalBodies(Array.Empty<(IntPtr, string, int)>())
                    && !group.MatchesOriginalBodies(Enumerable.Repeat(originalBodies[0], 64).ToArray()),
                    "An empty or oversized original-body query was accepted.");
                Assert(group.MatchesOriginalBodies(originalBodies), "Rejected query data poisoned or replaced the actual group evidence.");
            }
            Assert(group.MatchesOriginalBody(targets.GetTime.Address,
                "32ee6e9d92f72b234c4003acda0a03336795a3719cd0b7f83debdd0ff58399bc", 640)
                && group.MatchesOriginalBody(targets.PitchInstance.Address,
                "7f6d92b57a6420bd4fa11ecc508c0aaec5dca9f1dfd784cbd826e42ebbc2ea07", 80),
                "Joint leases lost a complete pinned original body.");

            stockAttached.AttachSourceGroup(group, targets);
            var stockStartup = new NativePlaybackStockCodeProfile(module, group, targets);
            clockAttached.AttachGetTimeHook(getTime, targets.GetTime.MethodInfo);
            var clockStartup = new GameplayClockCodeProfile(module, getTime, targets.GetTime.MethodInfo);
            void VerifyAll()
            {
                group.VerifyCurrent(); targets.VerifyCurrent();
                stockAttached.VerifyCurrent(); stockStartup.VerifyCurrent();
                clockAttached.VerifyCurrent(); clockStartup.VerifyCurrent();
            }
            VerifyAll();
            Assert(NativePlaybackStockCodeProfile.HookBodies.Count == 20 && NativePlaybackStockCodeProfile.ReadOnlyBodies.Count == 5,
                "The joint source path removed a fixed source or read-only body pin.");
            Assert(getTime.ReadHealth() is { Known: true, OriginalCalls: 0, AuthorityActive: false, InFlight: 0 }
                && pitch.ReadHealth() is { Known: true, Armed: true, Entered: 0, Exited: 0, InFlight: 0 },
                "Code/profile installation invoked a mapped game original or source entry.");

            // The graph is explicitly synthetic and confers no run/root authority. One final Clock
            // uses a direct immutable baseline and the actual broker-backed publication.
            var binding = new NativePlaybackBinding("fixed-stock-profile-" + mode, 1, 1, 1,
                0x201, 0x202, 0x203, 0x204, 0x205, 0x206, targets.GetTime.MethodInfo.ToInt64());
            var initial = new NativePlaybackClockState(binding, 1, NativePlaybackPhase.Paused, 100, 4, 1, 120);
            var publication = new NativePlaybackClockPublication(getTime, initial, IntPtr.Add(module, 0x40121d), VerifyAll);
            var clock = new NativePlaybackClock(binding, 100, 4, 1, 120, publication);
            var initialHealth = getTime.ReadHealth();
            long commitRevision = 0, restoredRevision = 0;
            Assert(publication.IsCurrent(clock.Capture()) && initialHealth is { AuthorityActive: true, Revision: 1, OriginalCalls: 0 }
                && !publication.ProductionAdmission, "The joint fixed profile did not admit its lease-backed initial publication.");
            using (var change = clock.PreparePausedRate(binding, 2))
            {
                change.Commit();
                var committedHealth = getTime.ReadHealth(); commitRevision = committedHealth.Revision;
                Assert(change.Confirm() && committedHealth is { AuthorityActive: true, Revision: 2, OriginalCalls: 0 }
                    && getTime.ReadCurrent().Authority is { Rate: 2 }, "Joint publication Commit did not read back revision/rate 2.");
                change.Restore();
                var restoredHealth = getTime.ReadHealth(); restoredRevision = restoredHealth.Revision;
                Assert(change.ConfirmRestored() && restoredHealth is { AuthorityActive: true, Revision: 3, OriginalCalls: 0 }
                    && getTime.ReadCurrent().Authority is { Rate: 1 }, "Joint publication Restore did not read back revision 3/rate 1.");
            }
            VerifyAll();
            Assert(publication.IsCurrent(clock.Capture()) && group.ReadActivity() is { Known: true, InFlight: 0 },
                "Complete profile/group authority did not survive clock Commit/Restore.");
            var sourceHealthBeforeTermination = pitch.ReadHealth();

            if (mode is "normal" or "foundation-normal")
            {
                publication.Withdraw("The offline joint publication completed.");
                Assert(!getTime.ReadCurrent().Active && group.IsActive && pitch.IsActive,
                    "Conditional policy withdrawal discarded the physical source group.");
                VerifyAll();
                group.Dispose();
                Assert(!group.IsActive && !getTime.IsActive && !pitch.IsActive && group.ReadActivity() is { Known: true, InFlight: 0 },
                    "Joint group withdrawal lost its retained zero-in-flight evidence.");
                Refuse(() => group.MatchesOriginalBodies(originalBodies));
                Refuse(stockAttached.VerifyCurrent); Refuse(stockStartup.VerifyCurrent);
                Refuse(clockAttached.VerifyCurrent); Refuse(clockStartup.VerifyCurrent);
                if (foundation is not null)
                {
                    foundation.VerifyCurrent();
                    Assert(foundation.ImageRanges().Length == 64 && foundationProducer!.OperationPublication.Listener.Common.WritesStarted == 64,
                        "Source group withdrawal discarded or reset the retained FirstInstall physical responsibility.");
                }
            }
            else
            {
                var address = mode switch
                {
                    "pitch-tail" => IntPtr.Add(targets.PitchInstance.Address, 40),
                    "body" => IntPtr.Add(targets.ReplayScheduled.Address, 32),
                    "consumer" => IntPtr.Add(module, 0x400fe0 + 20),
                    "foundation-outside" => IntPtr.Add(module, 0x2841c0 + 16),
                    "foundation-unwind" => FoundationOriginalUnwind(foundationProducer!),
                    _ => throw new InvalidOperationException("Unknown joint tamper mode.")
                };
                var saved = Marshal.ReadByte(address); Write(address, (byte)(saved ^ 1));
                try
                {
                    Refuse(() => group.MatchesOriginalBodies(originalBodies));
                    Refuse(stockAttached.VerifyCurrent);
                }
                finally { Write(address, saved); }
                Refuse(group.VerifyCurrent);
                Refuse(stockAttached.VerifyCurrent); Refuse(stockStartup.VerifyCurrent);
                Refuse(clockAttached.VerifyCurrent); Refuse(clockStartup.VerifyCurrent);
                Refuse(() => getTime.VerifyCurrent(module, targets.GetTime.Address, targets.GetTime.MethodInfo));
                Refuse(pitch.VerifyCurrent);
                var revokedHealth = getTimeOwner.ReadTransportHealth();
                Assert(!group.IsActive && !getTime.IsActive && !pitch.IsActive && !revokedHealth.AuthorityActive,
                    $"Restoring a changed fixed body revived a member or native policy: group={group.IsActive}, GetTime={getTime.IsActive}, SetPitch={pitch.IsActive}, nativeAuthority={revokedHealth.AuthorityActive}, revision={revokedHealth.Revision}.");
                Refuse(() => publication.IsCurrent(clock.Capture()));
            }
            var terminalHealth = getTimeOwner.ReadTransportHealth();
            Assert(terminalHealth.OriginalCalls == 0 && sourceHealthBeforeTermination is { Entered: 0, Exited: 0, InFlight: 0 }
                && forbiddenCallbacks == 0 && sourceBefore == 0 && sourceAfter == 0
                && fixture.Base.Invocations == 0 && fixture.Base.ObjectAllocations == 0,
                "The fixed-image joint check executed mapped game code or allocated an IL2CPP object.");
            Assert(broker.ReadActualPlaybackDobbyCallCounts() == (20, 20), "Verification or publication performed an extra physical installation.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "mapped-playback-stock-joint-profile", mode, passed = true, checks,
                actualDobbyPrepareCount = 20, actualDobbyCommitCount = 20, physicalMemberCount = 20, sourceMemberCount = 19,
                resourceRelocatedBytes = resourceLease.RelocatedByteCount, epoch = group.Epoch,
                foundationEpoch = group.FoundationEpoch, foundationOriginalCount = foundation is null ? 0 : 28,
                foundationWriteCount = foundation is null ? 0 : 64,
                getTimePatchBytes = getTime.PatchedByteCount, pitchPatchBytes = pitch.PatchedByteCount,
                getTimeEntry = getTime.EntryAddress.ToInt64(), pitchEntry = pitch.EntryAddress.ToInt64(),
                sourceBefore, sourceAfter, getTimeOriginalCalls = terminalHealth.OriginalCalls,
                initialRevision = initialHealth.Revision, commitRevision, restoredRevision,
                nativeAuthorityActiveAfterTermination = terminalHealth.AuthorityActive,
                groupActiveAfterTermination = group.IsActive, getTimeActiveAfterTermination = getTime.IsActive,
                pitchActiveAfterTermination = pitch.IsActive,
                sourceEntered = sourceHealthBeforeTermination.Entered, sourceExited = sourceHealthBeforeTermination.Exited,
                sourceHealthSampleBeforeTermination = true,
                getTimeFaults, sourceFaults, metadataSynthetic = true, syntheticGraph = true,
                gameCodeExecuted = false, dllMainExecuted = false, gameLaunched = false, productionAdmission = false
            }));
            GC.KeepAlive(RetainedGroupEvidence);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "mapped-playback-stock-joint-profile", mode, passed = false, checks, error = error.Message }));
            return 1;
        }
    }

    private static string GroupHashFile(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static IntPtr FoundationOriginalUnwind(NativeFirstInstall producer)
    {
        var function = GroupLookupFunction(unchecked((ulong)producer.RuntimeOriginal.ToInt64()), out var imageBase, IntPtr.Zero);
        if (function == IntPtr.Zero || imageBase == 0 || imageBase > long.MaxValue)
            throw new InvalidOperationException("The actual foundation parent original has no retained OS unwind function.");
        return new IntPtr(checked((long)imageBase + (uint)Marshal.ReadInt32(function, 8)));
    }
    private sealed class GroupMain : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Wrong fixed-image installation thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        { VerifyAccess(); cancellationToken.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; }
    }
    private sealed class GroupLogger : ILogger
    { public void Log(LogLevel level, string message, Exception? exception = null) { } }

    private sealed partial class MetadataFixture
    {
        private readonly Dictionary<IntPtr, IntPtr> _nativeTypeOriginals = new(), _nativeClassTypes = new();

        internal NativeRuntime CreateReadableRuntime()
        {
            if (_nativeTypeOriginals.Count != 0) throw new InvalidOperationException("Readable metadata types are once-only per joint fixture.");
            var relocated = new Dictionary<IntPtr, IntPtr>();
            foreach (var pair in Types)
            {
                var cell = Allocate(16); Marshal.Copy(new byte[16], 0, cell, 16);
                var cls = Base.ClassOf(pair.Key); Marshal.WriteIntPtr(cell, cls);
                Marshal.WriteInt32(cell, 8, (pair.Value.Code << 16) | (pair.Value.ByRef ? 0x40000000 : 0));
                relocated.Add(pair.Key, cell); _nativeTypeOriginals.Add(cell, pair.Key); _nativeClassTypes.Add(cls, cell);
            }
            foreach (var pair in Rows)
            {
                var row = pair.Value; Update(pair.Key, value => value.ReturnType = relocated[value.ReturnType]);
                var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(row.Method);
                for (var index = 0; index < row.Parameters.Length; ++index)
                { row.Parameters[index] = relocated[row.Parameters[index]]; Marshal.WriteIntPtr(layout.Parameters, index * IntPtr.Size, row.Parameters[index]); }
            }
            Export("il2cpp_class_get_type", "Ptr1", nameof(NativeClassType));
            Export("il2cpp_class_from_type", "Ptr1", nameof(NativeTypeClass));
            Export("il2cpp_type_get_name", "Ptr1", nameof(NativeTypeName));
            Export("il2cpp_type_is_byref", "Predicate", nameof(NativeByRef));
            Export("il2cpp_class_is_valuetype", "Predicate", nameof(NativeValueType));
            Export("il2cpp_class_value_size", "ValueSize", nameof(NativeValueSize));
            Export("il2cpp_type_get_type", "TypeCode", nameof(NativeTypeCode));
            var runtime = new NativeRuntime(name => Marshal.GetFunctionPointerForDelegate(_exports[name])); runtime.BindCurrentThread(); return runtime;
        }
        private FakeNative.TypeInfo NativeTypeInfo(IntPtr type) => Types[_nativeTypeOriginals[type]];
        private IntPtr NativeClassType(IntPtr cls) { Reads++; return _nativeClassTypes[cls]; }
        private IntPtr NativeTypeClass(IntPtr type) { Reads++; return Base.ClassOf(_nativeTypeOriginals[type]); }
        private IntPtr NativeTypeName(IntPtr type) { Reads++; return Marshal.StringToCoTaskMemUTF8(NativeTypeInfo(type).Name); }
        private bool NativeByRef(IntPtr type) { Reads++; return NativeTypeInfo(type).ByRef; }
        private bool NativeValueType(IntPtr cls) { Reads++; return NativeTypeInfo(_nativeClassTypes[cls]).ValueType; }
        private int NativeValueSize(IntPtr cls, out uint alignment) { Reads++; alignment = 1; return NativeTypeInfo(_nativeClassTypes[cls]).Size; }
        private int NativeTypeCode(IntPtr type) { Reads++; return NativeTypeInfo(type).Code; }
    }
}
