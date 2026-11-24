using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Runtime.Hooks;

internal static partial class Checks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadWriter(IntPtr reservation, ulong nonce, ref NativePlaybackStockSourceHub.WriterInformation information);
    private static float Pitch(Fixture f) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(f.Source, 16));
    private static int WriterChecks(Fixture f, string mode)
    {
        Marshal.WriteInt32(f.Source, 48, 1); Marshal.WriteByte(f.Player, 24, 1);
        Check(f.Call(NativePlaybackSourceRole.Pause) == (1, 0u), "The actual stock Pause did not return.");
        var paused = f.Producer.Current ?? throw new InvalidOperationException("No private Pause receipt.");
        Check(Pitch(f) == 1, "Initial native source pitch differs.");
        Reject(() => f.Producer.WritePitch(Clone(paused), 2), "A copied stock receipt admitted a native writer reservation.");
        Check(Marshal.ReadInt32(f.Source, 20) == 0, "A copied root performed an actual pitch write.");
        if (mode == "writer-dispose")
        {
            var retained = false;
            f.WriterBefore = observation =>
            {
                if (observation.Role != NativePlaybackSourceRole.SetPitch) return;
                f.Producer.Dispose();
                retained = f.Group.ReadActivity().InFlight == 1 && !observation.IsReleased;
            };
            Reject(() => f.Producer.WritePitch(paused, 2), "An in-flight disposed writer issued a current continuation.");
            Check(retained && Pitch(f) == 2 && Marshal.ReadInt32(f.Source, 20) == 1,
                "Dispose lost actual in-flight responsibility or suppressed the original native setter.");
            f.Producer.PumpCompleted();
        }
        else if (mode == "writer-foreign")
        {
            var injected = false;
            f.WriterBefore = observation =>
            {
                if (observation.Role == NativePlaybackSourceRole.SetPitch && observation.IncomingMethod != IntPtr.Zero && !injected)
                {
                    injected = true;
                    Check(f.Call(NativePlaybackSourceRole.SetPitch, Fixture.SingleBits(2)) == (1, 0u),
                        "The actual same-value foreign nested setter failed.");
                }
            };
            Reject(() => f.Producer.WritePitch(paused, 2), "A callback-time same-value foreign setter obtained owned writer authority.");
            Check(injected && Pitch(f) == 2 && Marshal.ReadInt32(f.Source, 20) == 2,
                "The native original setters did not both execute exactly once.");
            Reject(paused.VerifyCurrent, "The old source ticket survived the contended writer.");
        }
        else if (mode == "writer-error")
        {
            Marshal.WriteInt32(f.Source, 68, 1);
            Reject(() => f.Producer.WritePitch(paused, 2), "A pitch setter failing after store issued a successful continuation.");
            Check(Pitch(f) == 2 && !f.Group.IsActive, "The native write-before-SEH responsibility or physical fault was lost.");
        }
        else
        {
            var first = f.Producer.WritePitch(paused, 2); first.VerifyCurrent();
            var actualReservation = first.ReservationFor(f.Producer);
            Reject(Clone(actualReservation).Dispose, "A copied writer object retired the genuine active reservation.");
            var workerRejected = Task.Run(() =>
            {
                try { actualReservation.Dispose(); return false; }
                catch (RizToolbox.Runtime.UnsupportedSignatureException) { return true; }
            }).GetAwaiter().GetResult();
            Check(workerRejected, "Worker Dispose changed owner-thread writer responsibility.");
            first.VerifyCurrent();
            Check(Pitch(f) == 2 && first.Pitch == 2 && Marshal.ReadInt32(f.Source, 20) == 1,
                "The reserved native pitch call did not execute and read back 2 exactly once.");
            Reject(paused.VerifyCurrent, "A writer changed the immutable old S2 fence instead of issuing its successor.");
            Reject(Clone(first).VerifyCurrent, "A copied writer continuation was accepted.");
            Reject(() => first.ReservationFor(f.Producer).Run(), "The native writer reservation ran twice.");
            if (mode == "writer-lifetime")
            {
                var current = first;
                var originalIdentity = actualReservation.Completed;
                for (var i = 0; i < 320; ++i)
                {
                    var previous = current;
                    current = current.WritePitch(i % 2 == 0 ? 3 : 2);
                    Reject(previous.VerifyCurrent, "A retired writer stayed current after its successor.");
                    current.VerifyCurrent();
                }
                Check(Pitch(f) == 2 && Marshal.ReadInt32(f.Source, 20) == 321,
                    "Recycled active writer slots stopped actual writes after the original 256 lifetime limit.");
                Check(f.Group.ReadSourceHealth().Known && f.Group.ReadSourceHealth().InFlight == 0,
                    "Continuous owned writes leaked physical source-journal responsibility.");
                var reader = Export<ReadWriter>(GetModuleHandle("RizToolbox.PlaybackSourcePrototype.dll"), "RtPlaybackWriterRead");
                var packet = new NativePlaybackStockSourceHub.WriterInformation { Version = 1, Size = 224 };
                Check(reader(new(unchecked((long)originalIdentity.Reservation)), originalIdentity.Nonce, ref packet) == 0,
                    "A retired old native pointer/nonce became current after reusing active slots.");
            }
            else if (mode == "writer-roots")
            {
                var current = first;
                for (var i = 0; i < 132; ++i)
                {
                    var previous = current;
                    Check(f.Call(NativePlaybackSourceRole.Replay, Fixture.SingleBits(.125f)) == (1, 0u),
                        "A later stock Replay could not reserve its own native writer.");
                    current = f.WriterResult ?? throw new InvalidOperationException("Missing actual Replay continuation.");
                    current.VerifyCurrent(); current.Schedule!.VerifyCurrent();
                    Reject(previous.VerifyCurrent, "An older stock root's writer survived the next actual occurrence.");
                    Check(f.Call(NativePlaybackSourceRole.Pause) == (1, 0u), "A later actual stock Pause failed.");
                    current = f.Producer.WritePitch(f.Producer.Current!, 2); current.VerifyCurrent();
                }
                Check(Pitch(f) == 2 && Marshal.ReadInt32(f.Source, 20) == 397,
                    "Active writer slots leaked across real stock roots after the original lifetime limit.");
            }
            else if (mode == "writer-aba")
            {
                Check(f.Call(NativePlaybackSourceRole.SetPitch, Fixture.SingleBits(1)) == (1, 0u)
                    && f.Call(NativePlaybackSourceRole.SetPitch, Fixture.SingleBits(2)) == (1, 0u), "Actual external ABA failed.");
                Check(Pitch(f) == 2, "The actual ABA did not return to the owned value.");
                Reject(first.VerifyCurrent, "Equal readback concealed foreign ABA from the writer ticket.");
            }
            else if (mode == "writer-alias")
            {
                var invoke = Export<Invoke>(f.Module, "StockFixtureInvoke");
                Check(invoke((uint)NativePlaybackSourceKind.Single1, f.Targets.PitchStatic.Address, f.Source,
                    Fixture.SingleBits(2), f.Targets.PitchStatic.MethodInfo, out var code) == 1 && code == 0,
                    "The actual static SetPitch alias did not execute.");
                Check(Pitch(f) == 2, "The actual equal-value static alias changed the expected readback.");
                Reject(first.VerifyCurrent, "The equal-value static alias evaded the shared physical writer fence.");
            }
            else if (mode == "writer-source")
            {
                var previousClip = unchecked((ulong)Marshal.ReadIntPtr(f.Source, 40).ToInt64());
                Check(f.Call(NativePlaybackSourceRole.SetClip, previousClip) == (1, 0u), "The actual equal-value external clip setter failed.");
                Reject(first.VerifyCurrent, "A clip/source replacement left the writer continuation current.");
            }
            else
            {
                var second = first.WritePitch(3); var third = second.WritePitch(2);
                Check(Pitch(f) == 2 && second.WriterSequence + 1 == third.WriterSequence && first.WriterSequence + 1 == second.WriterSequence,
                    "The native private predecessor chain did not produce increasing exact post fences.");
                Reject(first.VerifyCurrent, "An old writer predecessor remained current after its successor.");
                Reject(second.VerifyCurrent, "An intermediate writer remained current after its successor.");
                third.VerifyCurrent();
                Check(f.Call(NativePlaybackSourceRole.Replay, Fixture.SingleBits(.125f)) == (1, 0u), "Actual stock Replay failed.");
                var replay = f.WriterResult ?? throw new InvalidOperationException("Replay did not restore pitch in its actual Returned window.");
                replay.VerifyCurrent(); replay.Schedule!.VerifyCurrent();
                Check(Pitch(f) == 2 && replay.Schedule.RawDoubleBits == Fixture.DoubleBits(100.125)
                    && Marshal.ReadInt32(f.Source, 20) == 5, "Replay reset1 and private restore2/actual schedule were not both observed.");
                Reject(third.VerifyCurrent, "A previous Pause writer survived a new stock occurrence.");
                Check(f.Call(NativePlaybackSourceRole.Pause) == (1, 0u), "Pause before Resume failed.");
                Marshal.Copy(new[] { 4.25 }, 0, IntPtr.Add(f.Source, 24), 1);
                Check(f.Call(NativePlaybackSourceRole.Resume, Fixture.SingleBits(.25f)) == (1, 0u), "Actual stock Resume failed.");
                var resume = f.WriterResult ?? throw new InvalidOperationException("Resume did not restore selected pitch.");
                resume.VerifyCurrent(); resume.Schedule!.VerifyCurrent();
                Check(Pitch(f) == 2 && resume.Schedule.RawDoubleBits == Fixture.DoubleBits(100.25)
                    && BitConverter.Int64BitsToDouble(Marshal.ReadInt64(f.Player, 32)) == 96 && Marshal.ReadInt32(f.Source, 20) == 7,
                    "Resume reset1/restore2 or the independent actual schedule witness was lost.");
                Reject(Clone(resume.Schedule).VerifyCurrent, "A copied post-writer schedule witness became current.");
            }
        }
        f.Producer.Dispose(); f.Producer.PumpCompleted();
        Check(f.Broker.ReadActualPlaybackDobbyCallCounts() == (12, 12), "A writer reinstalled a physical hook.");
        if (mode != "writer-error") Check(f.Group.IsActive && f.Group.GetTimeLease.IsActive,
            "An ordinary writer logical boundary withdrew the healthy physical group.");
        Check(f.Group.ReadActivity().InFlight == 0, "The actual writer/root physical responsibilities did not drain.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "actual-stock-native-writer-checks", mode, passed = true,
            checks = _checks, actualDobbyPrepareCount = 12, actualDobbyCommitCount = 12,
            finalPitch = Pitch(f), actualPitchCalls = Marshal.ReadInt32(f.Source, 20), actualReadbackScope = "owned native fixture memory only",
            rootedNativeRuntimeReadbackVerified = false, providerIssued = false, clockIssued = false,
            productionAdmission = false, gameExecuted = false }));
        return 0;
    }
}
