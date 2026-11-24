using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

internal static partial class SourceGroupChecks
{
    private static int Count(Fixture f, NativePlaybackSourceRole role) => role switch
    {
        NativePlaybackSourceRole.SetPitch => Marshal.ReadInt32(f.Source, 20),
        NativePlaybackSourceRole.Pause => Marshal.ReadInt32(f.Player, 64),
        NativePlaybackSourceRole.Replay => Marshal.ReadInt32(f.Player, 56),
        NativePlaybackSourceRole.Resume => Marshal.ReadInt32(f.Player, 60),
        NativePlaybackSourceRole.PlayScheduled => Marshal.ReadInt32(f.Source, 60),
        NativePlaybackSourceRole.SetClip => Marshal.ReadInt32(f.Source, 64),
        NativePlaybackSourceRole.ProgressPlay => Marshal.ReadInt32(f.Progress, 68),
        NativePlaybackSourceRole.PauseLeaf => Marshal.ReadInt32(f.Source, 56),
        _ => throw new InvalidOperationException("Unknown role counter.")
    };
    private static ulong SingleBits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    private static ulong DoubleBits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
    private static NativePlaybackSourceObservation[] CallChecked(Fixture f, NativePlaybackSourceRole role,
        ulong bits = 0, IntPtr? method = null)
    {
        var count = Count(f, role); var start = f.Observations.Count;
        var actualMethod = method ?? f.Targets.SourceTarget(role).MethodInfo;
        f.Call(role, bits, actualMethod);
        var rows = f.Observations.Skip(start).ToArray();
        Check(Count(f, role) == count + 1, "The actual original did not run exactly once: " + role);
        var root = rows.Single(value => value.ParentSequence == 0);
        Check(root.Role == role && root.Self == f.Self(role) && root.IncomingMethod == actualMethod && root.ArgBits == bits,
            "The actual typed root entry changed raw arguments, self or incoming MethodInfo: " + role);
        Check(f.Returned.Count(value => ReferenceEquals(value.Observation, root)) == 1, "A root original has no once-only returned pair: " + role);
        return rows;
    }
    private static void CheckNested(NativePlaybackSourceObservation[] rows, params NativePlaybackSourceRole[] expected)
    {
        Check(rows.Select(value => value.Role).SequenceEqual(expected), "The actual original call tree differs from the expected physical roles.");
        var parent = rows[0];
        Check(parent.ParentSequence == 0 && rows.Skip(1).All(value => value.ParentSequence == parent.Sequence && value.OwnerEpoch == parent.OwnerEpoch
            && value.IncomingMethod == IntPtr.Zero), "The shared native TLS did not issue the actual outer source parent to its nested leaves.");
    }
    private static void RunNormal(Fixture f)
    {
        CallChecked(f, NativePlaybackSourceRole.SetPitch, SingleBits(2));
        CallChecked(f, NativePlaybackSourceRole.SetPitch, SingleBits(2), f.Targets.PitchStatic.MethodInfo);
        CallChecked(f, NativePlaybackSourceRole.SetPitch, SingleBits(7), IntPtr.Zero);
        CallChecked(f, NativePlaybackSourceRole.SetPitch, SingleBits(2));
        Check(Marshal.ReadInt32(f.Source, 20) == 4 && Marshal.ReadIntPtr(f.Source, 72) == f.Targets.PitchInstance.MethodInfo
            && Marshal.ReadInt64(f.Source, 104) == (long)SingleBits(2), "Physical pitch aliases, same-value and ABA stores lost their raw arguments.");
        foreach (var clip in new[] { 0x301L, 0x301L, 0L, 0x301L })
        {
            CallChecked(f, NativePlaybackSourceRole.SetClip, unchecked((ulong)clip));
            Check(Marshal.ReadIntPtr(f.Source, 40).ToInt64() == clip && Marshal.ReadIntPtr(f.Source, 96) == f.Targets.SetClip.MethodInfo,
                "The exact Ref1 original did not preserve reference/null/ABA or incoming MI.");
        }
        const double scheduled = 123.125;
        CallChecked(f, NativePlaybackSourceRole.PlayScheduled, DoubleBits(scheduled));
        Check(Marshal.ReadInt64(f.Source, 112) == (long)DoubleBits(scheduled) && Marshal.ReadIntPtr(f.Source, 80) == f.Targets.PlayScheduled.MethodInfo,
            "The independent Double1 original changed raw64 arguments or MethodInfo.");
        CallChecked(f, NativePlaybackSourceRole.PauseLeaf);
        Marshal.WriteInt32(f.Source, 48, 1); Marshal.WriteByte(f.Player, 24, 1);
        CheckNested(CallChecked(f, NativePlaybackSourceRole.Pause), NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf);
        Check(Marshal.ReadByte(f.Player, 24) == 0, "The actual Pause original did not clear its own nullable origin.");
        Marshal.WriteInt32(f.Source, 48, 0);
        CheckNested(CallChecked(f, NativePlaybackSourceRole.Replay, SingleBits(0.125f)), NativePlaybackSourceRole.Replay,
            NativePlaybackSourceRole.SetPitch, NativePlaybackSourceRole.PlayScheduled);
        Check(Marshal.ReadInt64(f.Player, 72) == (long)SingleBits(0.125f) && Marshal.ReadIntPtr(f.Player, 88) == f.Targets.ReplayScheduled.MethodInfo
            && Marshal.ReadInt64(f.Source, 112) == (long)DoubleBits(100.125) && Marshal.ReadByte(f.Player, 24) == 1,
            "Actual Replay did not preserve delay bits/MI and issue its exact nested Double schedule.");
        CheckNested(CallChecked(f, NativePlaybackSourceRole.Replay, SingleBits(0.125f)), NativePlaybackSourceRole.Replay);
        Marshal.WriteInt32(f.Source, 48, 0); Marshal.Copy(new[] { 4.25 }, 0, IntPtr.Add(f.Source, 24), 1);
        CheckNested(CallChecked(f, NativePlaybackSourceRole.Resume, SingleBits(0.25f)), NativePlaybackSourceRole.Resume,
            NativePlaybackSourceRole.SetTime, NativePlaybackSourceRole.SetPitch, NativePlaybackSourceRole.PlayScheduled);
        Check(Marshal.ReadInt64(f.Player, 72) == (long)SingleBits(0.25f) && Marshal.ReadIntPtr(f.Player, 88) == f.Targets.ResumeScheduled.MethodInfo
            && Marshal.ReadInt64(f.Source, 112) == (long)DoubleBits(100.25) && BitConverter.Int64BitsToDouble(Marshal.ReadInt64(f.Source, 24)) == 4.25
            && BitConverter.Int64BitsToDouble(Marshal.ReadInt64(f.Player, 32)) == 96,
            "Actual Resume lost saved media time, incoming delay/MI or nested schedule/origin math.");
        CheckNested(CallChecked(f, NativePlaybackSourceRole.Resume, SingleBits(0.25f)), NativePlaybackSourceRole.Resume);
        Marshal.WriteByte(f.Progress, 0x42, 0);
        var pauses = Count(f, NativePlaybackSourceRole.PauseLeaf);
        CheckNested(CallChecked(f, NativePlaybackSourceRole.ProgressPlay, 0), NativePlaybackSourceRole.ProgressPlay);
        Check(Marshal.ReadByte(f.Progress, 0x43) == 1 && Count(f, NativePlaybackSourceRole.PauseLeaf) == pauses,
            "The seven-byte CMP relocation did not preserve flags through the actual target+7 JE no-op.");
        CheckNested(CallChecked(f, NativePlaybackSourceRole.ProgressPlay, 1), NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf);
        Check(Marshal.ReadByte(f.Progress, 0x43) == 0 && Marshal.ReadInt64(f.Progress, 80) == 1
            && Marshal.ReadIntPtr(f.Progress, 88) == f.Targets.ProgressPlay.MethodInfo && Marshal.ReadByte(f.Player, 24) == 0,
            "The distinct Progress object, Bool argument or inline AudioPause path lost its actual branch facts.");
        var health = f.Group.ReadSourceHealth();
        Check(health is { Known: true, Armed: true, InFlight: 0 } && health.Entered == (ulong)f.Observations.Count && health.Exited == health.Entered
            && health.WriterSequence == 1 + (ulong)Count(f, NativePlaybackSourceRole.SetPitch)
            && health.SourceSequence == 1 + (ulong)Count(f, NativePlaybackSourceRole.SetClip)
            && health.SchedulingSequence == 1 + health.Entered - (ulong)Count(f, NativePlaybackSourceRole.SetPitch) - (ulong)Count(f, NativePlaybackSourceRole.SetClip),
            $"The shared owner counters differ: known={health.Known}, armed={health.Armed}, inFlight={health.InFlight}, entered={health.Entered}, exited={health.Exited}, observations={f.Observations.Count}, writer={health.WriterSequence}/{Count(f, NativePlaybackSourceRole.SetPitch)}, source={health.SourceSequence}/{Count(f, NativePlaybackSourceRole.SetClip)}.");
        Check(f.Observations.Select(value => value.OwnerEpoch).Distinct().Count() == 1
            && f.Observations.Select(value => value.Sequence).Distinct().Count() == f.Observations.Count,
            "Eight roles did not share one actual native epoch and unique sequence stream.");
        f.Drain(true);
        if (f.Mode == "stream-nine")
        {
            f.Deferred = true; var initialPitch = Count(f, NativePlaybackSourceRole.SetPitch); var initialEntered = health.Entered;
            for (var index = 0; index < 320; ++index) f.Call(NativePlaybackSourceRole.SetPitch, SingleBits(2));
            f.Group.PumpCompleted(); health = f.Group.ReadSourceHealth();
            Check(f.DeferredObserved == 320 && Count(f, NativePlaybackSourceRole.SetPitch) == initialPitch + 320
                && health.Known && health.Armed && health.InFlight == 0 && health.Entered == initialEntered + 320 && health.Exited == health.Entered,
                "Actual callback disposal did not drain a shared stream larger than the native journal.");
        }
        Check(f.Group.ReadActivity() is { Known: true, InFlight: 0 } && f.GetTime(f.Player, f.Targets.GetTime.MethodInfo) == 6,
            "The complete eleven-member group lost its actual final clock after all originals executed.");
        Reject(() => f.Broker.Bind(f.Targets.PitchInstance, _ => true, (_, _, _, _) => { }), "Generic binding admitted a private source ABI.");
        f.Group.Dispose();
        Check(f.Group.ReadActivity() is { Known: true, InFlight: 0 } && !f.GetTimeOwner.ReadTransportHealth().AuthorityActive,
            "Clean eleven-member withdrawal lost actual drain evidence or retained the native clock policy.");
    }
}
