using System.Collections;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct ProgressWatchProjection
    {
        internal uint Version, Size, Phase, Active;
        internal ulong Owner, OwnerEpoch, Receipt, Ordinal, Progress, Class, Expected, PlayerWatch, Player, AudioSource;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadProgressWatch(IntPtr owner, IntPtr receipt, ref ProgressWatchProjection value);
    private static NativePlaybackStockSourceHub ProgressHub(PlaybackSourceGroupLease group)
        => Private<NativePlaybackStockSourceHub>(Private<ExactHookBroker.PlaybackSourceOwner>(group.SourceLease(NativePlaybackSourceRole.ProgressPlay), "_owner"), "_hub");

    private static void VerifyActualProgressWatch(FakeNative api, PlaybackSourceGroupTargets targets, PlaybackSourceGroupLease group,
        object owner, NativeRuntime.RootHandle root, bool hostOnly)
    {
        var hub = ProgressHub(group);
        var watches = Private<IList>(hub, "_progressWatches");
        Check(watches.Count == 1 && Marshal.SizeOf<ProgressWatchProjection>() == 96,
            "The actual route did not register one private 96-byte Progress-watch record.");
        var watch = watches[0]!;
        Check(ReferenceEquals(Private<object>(watch, "<RootOwner>k__BackingField"), owner)
            && ReferenceEquals(Private<NativeRuntime.RootHandle>(watch, "_root"), root)
            && root.Target == api.GameplayProgress,
            "Progress watch did not borrow the exact independent host/source RootHandle.");
        var value = new ProgressWatchProjection { Version = 1, Size = 96 };
        Check(Export<ReadProgressWatch>(hub.NativeLibraryHandle, "RtPlaybackSourceReadProgressWatch")(
                hub.NativeOwner, Private<IntPtr>(watch, "_receipt"), ref value) == 1
            && value.Phase == 2 && value.Active == 1 && value.Progress == unchecked((ulong)root.Target.ToInt64())
            && value.Class == unchecked((ulong)targets.ProgressPlay.DeclaringClass.ToInt64())
            && (hostOnly ? value.PlayerWatch == 0 && value.Player == 0 && value.AudioSource == 0
                : value.PlayerWatch != 0 && value.Player == unchecked((ulong)api.GameplayPlayer.ToInt64())
                    && value.AudioSource == unchecked((ulong)api.GameplaySource.ToInt64())),
            "Actual native Progress watch did not match its independently retained graph and phase.");
    }

    private static void CheckActualProgressForeign(FakeNative api, IntPtr module, PlaybackSourceGroupTargets targets,
        PlaybackSourceGroupLease group, Func<int> observations, Func<int> clockReads)
    {
        var source = api.CreateSharedConstructorSource();
        var player = Export<CreateChild>(module, "StockFixtureCreatePlayer")(source);
        Marshal.WriteIntPtr(player, Marshal.ReadIntPtr(api.GameplayPlayer));
        var progress = Export<CreateChild>(module, "StockFixtureCreateProgress")(player);
        Marshal.WriteIntPtr(progress, targets.ProgressPlay.DeclaringClass);
        var enumerator = Export<CreateStart>(module, "StockFixtureCreateStart")(progress, api.GameplayClip);
        Marshal.WriteIntPtr(enumerator, targets.ProgressStartMoveNext.DeclaringClass);
        Check(progress != api.GameplayProgress && source != api.GameplaySource && player != api.GameplayPlayer,
            "Progress isolation requires a distinct actual Progress/player/source graph.");
        var invoke = Export<InvokeStock>(module, "StockFixtureInvoke");
        var invokeStart = Export<InvokeStart>(module, "StockFixtureInvokeStart");
        var before = group.ReadSourceHealth(); var initialization = group.ReadInitialization();
        var observed = observations(); var reads = clockReads(); var roots = api.ActiveRoots;
        var raw = new byte[] { 0, 1, 0x80, 0xFF };
        for (var index = 0; index < raw.Length; ++index)
        {
            // Make the real foreign Progress.Play take its inline Pause branch.
            Marshal.WriteByte(progress, 0x42, (byte)~raw[index]);
            Check(invoke(5, targets.ProgressPlay.Address, progress, raw[index], index % 2 == 0 ? targets.ProgressPlay.MethodInfo : IntPtr.Zero, out var code) == 1 && code == 0
                && Marshal.ReadByte(progress, 0x42) == raw[index] && Marshal.ReadInt64(progress, 0x50) == raw[index],
                "Foreign Progress.Play lost its real raw Boolean original/inline branch.");
            Check(invoke(4, targets.ProgressMusicPlayer.Address, progress, unchecked((ulong)player.ToInt64()), IntPtr.Zero, out code) == 1 && code == 0
                && Marshal.ReadIntPtr(progress, 0x38) == player,
                "Foreign Progress.set_MusicPlayer did not reach its real reference store/tail.");
        }
        Check(invokeStart(targets.ProgressStartMoveNext.Address, enumerator, IntPtr.Zero, out var first, out var error) == 1 && error == 0 && first == 1
            && invokeStart(targets.ProgressStartMoveNext.Address, enumerator, targets.ProgressStartMoveNext.MethodInfo, out var second, out error) == 1 && error == 0 && second == 0
            && Marshal.ReadIntPtr(progress, 0x38) != player && Marshal.ReadIntPtr(source, 40) == api.GameplayClip,
            "Foreign Start did not execute its real AL1/AL0, SetClip and independent player allocation.");
        var worker = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 80; ++iteration)
            {
                Marshal.WriteByte(progress, 0x42, 0x7F);
                if (invoke(5, targets.ProgressPlay.Address, progress, 0x80, IntPtr.Zero, out var code) != 1 || code != 0
                    || invoke(4, targets.ProgressMusicPlayer.Address, progress, unchecked((ulong)player.ToInt64()), IntPtr.Zero, out code) != 1 || code != 0
                    || invokeStart(targets.ProgressStartMoveNext.Address, enumerator, IntPtr.Zero, out _, out code) != 1 || code != 0)
                    throw new InvalidOperationException("Foreign Progress/Start worker did not return through its actual original.");
            }
        });
        Check(worker.Wait(10000) && worker.IsCompletedSuccessfully, "Foreign Progress/Start flood failed to return.");
        Check(group.ReadSourceHealth() == before && group.ReadInitialization().Equals(initialization)
            && observations() == observed && clockReads() == reads && api.ActiveRoots == roots,
            "Foreign Progress/Start or actual foreign children changed the current journal, fences, initialization hold, roots or Clock reader.");
        group.RequireFreshAdmission();
    }

    private static int RunProgressForeign(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && Hash(arguments[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256,
                "Progress identity checks require the exact nineteen-member owned fixture.");
            using var native = new ConstructorContext(arguments);
            var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
            var actual = Private<NativePlaybackActualSource>(rooted, "_actual");
            VerifyActualProgressWatch(native.Api, native.Targets, native.Group, rooted, actual.ProgressRoot, hostOnly: false);
            CheckActualProgressForeign(native.Api, native.Module, native.Targets, native.Group,
                () => native.Observations.Count, () => native.ClockReads);
            native.Router.Dispose(); native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.CallbackFailure is null
                && Private<IList>(ProgressHub(native.Group), "_progressWatches").Count == 0,
                "Known Progress identity isolation did not retire the exact private watch and actual roots.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 19, actualCommit = 19, actualProgressRootBorrowed = true, progressWatchVersion = 1,
                actualForeignStartAl1Al0 = true, retainedRoots = native.Api.ActiveRoots,
                sdkExecuted = false, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }
}
