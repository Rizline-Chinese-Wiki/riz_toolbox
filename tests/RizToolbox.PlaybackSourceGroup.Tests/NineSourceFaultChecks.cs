using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

internal static partial class SourceGroupChecks
{
    private static ulong DefaultBits(NativePlaybackSourceRole role) => role switch
    {
        NativePlaybackSourceRole.SetPitch => SingleBits(2), NativePlaybackSourceRole.Replay => SingleBits(0.125f),
        NativePlaybackSourceRole.Resume => SingleBits(0.25f), NativePlaybackSourceRole.PlayScheduled => DoubleBits(123.125),
        NativePlaybackSourceRole.SetClip => 0x301, NativePlaybackSourceRole.ProgressPlay => 1, _ => 0
    };
    private static void PreparePath(Fixture f, NativePlaybackSourceRole role)
    {
        Marshal.WriteInt32(f.Source, 48, role == NativePlaybackSourceRole.Pause ? 1 : 0);
        Marshal.WriteByte(f.Progress, 0x42, 0); Marshal.WriteByte(f.Player, 24, 0);
    }
    private static void RunFault(Fixture f)
    {
        var parts = f.Mode.Split('-');
        if (parts[0] == "tamper")
        {
            Check(parts.Length == 3 && (parts[1] == "gettime" || Roles.ContainsKey(parts[1])), "Unknown closed target tamper mode.");
            var getTime = parts[1] == "gettime"; var role = getTime ? NativePlaybackSourceRole.SetPitch : Roles[parts[1]];
            var target = getTime ? f.Targets.GetTime : f.Targets.SourceTarget(role);
            var source = f.Group.SourceLease(role); var length = getTime ? f.Targets.GetTimeBodyLength : source.OriginalBodyLength;
            var address = parts[2] switch
            {
                "patch" => target.Address,
                "tail" => IntPtr.Add(target.Address, length - 1),
                "original" => IntPtr.Add(getTime ? f.GetTimeLease.OriginalAddress : source.OriginalAddress, getTime ? 6 : source.RelocatedByteCount),
                "dobby" => IntPtr.Add(getTime ? f.GetTimeLease.DobbyOriginalAddress : source.DobbyOriginalAddress, getTime ? 6 : source.RelocatedByteCount),
                "unwind" => getTime ? f.GetTimeLease.TrampolineUnwindAddress : source.TrampolineUnwindAddress,
                "metadata" => target.MethodInfo,
                _ => throw new InvalidOperationException("Unknown exact member tamper span.")
            };
            var saved = Marshal.ReadByte(address); Write(address, (byte)(saved ^ 1));
            try { Reject(f.Group.VerifyCurrent, "A changed physical member did not revoke the complete group."); }
            finally { Write(address, saved); }
            f.AssertRevoked(); Reject(f.Group.VerifyCurrent, "Restored bytes revived a sticky faulted group.");
            Check(f.GetTime(f.Player, f.Targets.GetTime.MethodInfo) == 3, "Sticky revocation did not preserve the actual stock return after bytes were restored.");
            return;
        }
        if ((parts[0] is "seh" or "withdraw") && parts.Length == 2)
        {
            Check(parts[1] == "gettime" || Roles.ContainsKey(parts[1]), "Unknown closed target lifecycle mode.");
            if (parts[1] == "gettime")
            {
                Check(parts[0] == "seh", "Unknown GetTime lifecycle mode.");
                var before = f.GetTimeOwner.ReadTransportHealth().OriginalCalls;
                Marshal.WriteIntPtr(f.Player, 16, IntPtr.Zero);
                var result = Export<Invoke>(f.Module, "StockFixtureInvoke")(6, f.Targets.GetTime.Address, f.Player, 0, f.Targets.GetTime.MethodInfo, out var code);
                Marshal.WriteIntPtr(f.Player, 16, f.Source);
                Check(result == 2 && code == 0xe2400001u && f.GetTimeOwner.ReadTransportHealth().OriginalCalls == before + 1,
                    "Actual GetTime original SEH did not unwind exactly once through its retained bridge.");
                f.AssertRevoked(); return;
            }
            var role = Roles[parts[1]]; PreparePath(f, role); var originalCount = Count(f, role);
            if (parts[0] == "withdraw")
            {
                f.WithdrawInBefore = role; f.Call(role, DefaultBits(role));
                Check(f.WithdrawInFlight == 1 && Count(f, role) == originalCount + 1
                    && f.Group.ReadActivity() is { Known: true, InFlight: 0 } && !f.GetTimeOwner.ReadTransportHealth().AuthorityActive,
                    "Clean per-member withdrawal did not retain the once-only original and actual known drain.");
                f.Drain(true); Reject(f.Group.VerifyCurrent, "A cleanly withdrawn group revived."); return;
            }
            var (sourceFlags, playerFlags, progressFlags, expectedCode) = role switch
            {
                NativePlaybackSourceRole.SetPitch => (1, 0, 0, 0xe2400011u),
                NativePlaybackSourceRole.Pause => (0, 1, 0, 0xe2400015u),
                NativePlaybackSourceRole.Replay => (0, 2, 0, 0xe2400016u),
                NativePlaybackSourceRole.Resume => (0, 4, 0, 0xe2400017u),
                NativePlaybackSourceRole.PlayScheduled => (4, 0, 0, 0xe2400012u),
                NativePlaybackSourceRole.SetClip => (8, 0, 0, 0xe2400013u),
                NativePlaybackSourceRole.ProgressPlay => (0, 0, 8, 0xe2400018u),
                NativePlaybackSourceRole.PauseLeaf => (32, 0, 0, 0xe2400014u),
                _ => throw new InvalidOperationException("Unknown original exception role.")
            };
            Export<Configure>(f.Module, "StockFixtureConfigure")(f.Source, (uint)sourceFlags);
            Marshal.WriteInt32(f.Player, 44, playerFlags); Marshal.WriteInt32(f.Progress, 76, progressFlags);
            var resultCode = f.Invoke(role, DefaultBits(role), f.Targets.SourceTarget(role).MethodInfo, out var actualCode);
            Check(resultCode == 2 && actualCode == expectedCode && Count(f, role) == originalCount + 1,
                "An actual exact source original SEH did not unwind exactly once: " + role);
            Check(f.Observations.Any(value => value.Role == role && value.TryReadCompletion(out var receipt) && receipt is { PhysicalExited: true, Abnormal: true }),
                "The abnormal original has no paired actual native physical-exit receipt.");
            f.AssertRevoked(); f.Drain(false); return;
        }
        if (f.Mode == "native-thread-fault-nine")
        {
            Export<Configure>(f.Module, "StockFixtureConfigure")(f.Source, 16);
            var worker = Task.Run(() => f.Invoke(NativePlaybackSourceRole.SetPitch, SingleBits(2), f.Targets.PitchInstance.MethodInfo, out _));
            Check(Export<Wait>(f.Module, "StockFixtureWaitPitch")() == 0, "The actual foreign-thread original did not reach its blocked body.");
            try
            {
                Check(!f.Group.IsActive && !f.GetTimeOwner.ReadTransportHealth().AuthorityActive
                    && f.Group.ReadActivity() is { Known: false, InFlight: 1 } && Count(f, NativePlaybackSourceRole.SetPitch) == 1,
                    "Native source Unknown did not synchronously revoke the exact GetTime publication before its actual original returned.");
            }
            finally { Export<Release>(f.Module, "StockFixtureReleasePitch")(); }
            Check(worker.Wait(15000) && worker.GetAwaiter().GetResult() == 1, "The retained foreign-thread original did not complete exactly once.");
            f.AssertRevoked(); return;
        }
        if (f.Mode == "withdraw-blocked-nine")
        {
            PreparePath(f, NativePlaybackSourceRole.Replay);
            Export<Configure>(f.Module, "StockFixtureConfigure")(f.Source, 16);
            var worker = Task.Run(() =>
            {
                Check(Export<Wait>(f.Module, "StockFixtureWaitPitch")() == 0, "The nested actual pitch original did not block.");
                try
                {
                    Check(f.Group.ReadActivity() is { Known: true, InFlight: 2 }, "Replay and nested pitch were not counted exactly once in their shared native hub.");
                    f.Group.Dispose();
                    Check(f.Group.ReadActivity() is { Known: true, InFlight: 2 } && !f.GetTimeOwner.ReadTransportHealth().AuthorityActive,
                        "Normal withdrawal discarded nested actual responsibility or left the native policy published.");
                }
                finally { Export<Release>(f.Module, "StockFixtureReleasePitch")(); }
            });
            f.Call(NativePlaybackSourceRole.Replay, SingleBits(0.125f));
            Check(worker.Wait(15000), "Nested drain worker did not finish."); worker.GetAwaiter().GetResult();
            Check(f.Group.ReadActivity() is { Known: true, InFlight: 0 } && Count(f, NativePlaybackSourceRole.Replay) == 1
                && Count(f, NativePlaybackSourceRole.SetPitch) == 1 && Count(f, NativePlaybackSourceRole.PlayScheduled) == 1,
                "Logical withdrawal did not finish the actual outer, blocked child and dormant schedule originals.");
            f.Drain(true); return;
        }
        if (f.Mode is "callback-before-nine" or "callback-after-nine")
        {
            f.Call(NativePlaybackSourceRole.SetPitch, SingleBits(2));
            Check(Count(f, NativePlaybackSourceRole.SetPitch) == 1, "A subscriber exception changed actual original call count.");
            f.AssertRevoked(); f.Drain(false); return;
        }
        if (f.Mode == "reader-fault-nine")
        {
            f.ReaderFault = true;
            Check(f.GetTime(f.Player, f.Targets.GetTime.MethodInfo) == 3, "A failing exact reader did not preserve its actual native original.");
            f.AssertRevoked(); return;
        }
        throw new InvalidOperationException("Unknown nine-member source mode.");
    }
}
