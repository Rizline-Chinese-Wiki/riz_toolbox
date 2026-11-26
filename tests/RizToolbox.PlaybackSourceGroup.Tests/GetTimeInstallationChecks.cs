using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Runtime.Hooks;

internal static partial class SourceGroupChecks
{
    private static int RunGetTimeCommit(string[] args)
    {
        var mode = args[0];
        Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero
            && Path.GetFileName(args[1]) == "RizToolbox.PlaybackStockFixture.dll", "Only the owned playback fixture may execute.");
        var targets = PlaybackSourceGroupTargets.ResolveOwnedFixture(NativeLibrary.Load(Path.GetFullPath(args[1])));
        var memberCount = targets.SourceRoleCount + 1;
        var broker = new ExactHookBroker(Path.GetFullPath(args[4]), new Main(), new Logger());
        using var pending = broker.BeginPlaybackSourceGroupPreparation(targets,
            Path.GetFullPath(args[2]), HashFile(args[2]), Path.GetFullPath(args[3]), HashFile(args[3]),
            () => throw new InvalidOperationException("A dormant playback audit cannot read its clock."), _ => { }, _ => { });
        while (!pending.IsPrepared) pending.PrepareNext();
        Check(broker.ReadActualPlaybackDobbyCallCounts() == (memberCount, 0), "Preparation did not retain each source uncommitted.");

        // Observe real traversal generations without introducing a test entry into the broker.
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var group = typeof(ExactHookBroker.PlaybackSourceInstallGroup.Preparation).GetField("_group", privateInstance)!.GetValue(pending);
        var generation = typeof(ExactHookBroker.PlaybackSourceInstallGroup).GetField("_installationGeneration", privateInstance)!;
        long ReadGeneration() => (long)generation.GetValue(group)!;
        var before = ReadGeneration();
        var imageAudits = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
        var clock = Stopwatch.StartNew();
        var changedMetadata = mode switch
        {
            "commit-gettime-audits" => IntPtr.Zero,
            "commit-gettime-own-metadata" => targets.GetTime.MethodInfo,
            "commit-gettime-sibling-metadata" => targets.Pause.MethodInfo,
            _ => throw new InvalidOperationException("Unknown GetTime installation check.")
        };
        if (changedMetadata == IntPtr.Zero)
            Check(pending.CommitNext() is null, "The first staged GetTime commit must leave the rest of the group dormant.");
        else
        {
            var saved = Marshal.ReadByte(changedMetadata);
            Write(changedMetadata, (byte)(saved ^ 1));
            try { Reject(() => pending.CommitNext(), "A changed actual method reached the GetTime commit."); }
            finally { Write(changedMetadata, saved); }
        }
        var firstCommitMilliseconds = clock.Elapsed.TotalMilliseconds;
        var firstCommitImageAudits = NativePlaybackExecutableSeal.ReadThreadFullImageAudits() - imageAudits;
        var firstCommitAudits = ReadGeneration() - before;
        var firstCommitCalls = broker.ReadActualPlaybackDobbyCallCounts().Committed;
        if (changedMetadata == IntPtr.Zero)
        {
            Check(firstCommitAudits == 2 && firstCommitCalls == 1 && pending.IsOpen,
                $"Expected one full audit on each side of the actual GetTime commit. Observed {firstCommitAudits} traversal generations, "
                + $"{firstCommitImageAudits} image audits, {firstCommitCalls} commits in {firstCommitMilliseconds:F1} ms.");
            PlaybackSourceGroupLease? lease = null;
            while (lease is null) lease = pending.CommitNext();
            using (lease)
            {
                Check(lease.IsActive && lease.GetTimeLease.IsActive, "The group did not arm after the verified GetTime commit.");
                lease.VerifyCurrent();
            }
        }
        else
        {
            Check(firstCommitAudits == 1 && firstCommitCalls == 0 && !pending.IsOpen,
                "A changed actual method must withdraw the preparation at its first audit without writing GetTime.");
            Reject(() => pending.CommitNext(), "Restoring the metadata revived the failed preparation.");
        }
        var counts = broker.ReadActualPlaybackDobbyCallCounts();
        Check(counts == (memberCount, changedMetadata == IntPtr.Zero ? memberCount : 0), "The staged audit changed the physical commit count.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned-playback-gettime-commit", mode, passed = true,
            checks = _checks, firstCommitAudits, firstCommitImageAudits, firstCommitCalls, firstCommitMilliseconds,
            physicalMemberCount = memberCount, sourceMemberCount = targets.SourceRoleCount,
            actualDobbyPrepareCount = counts.Prepared, actualDobbyCommitCount = counts.Committed,
            gameCodeExecuted = false, productionAdmission = false }));
        return 0;
    }
}
