using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

// Real source owner/root lifecycle algorithm with a controlled read-only backend.
// This supplies no production startup, native metadata or launch permission.
internal static class NativeLocalPlayLaunchSourceRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Launch source retains four exact inputs and rechecks stability before borrowed access", ExactSource),
        ("Source capture failure releases only acquired roots before any launch can occur", PartialSource),
        ("Changed native source generation cannot pass the final pre-Begin recheck", SourceDrift),
        ("Final LOCAL launch source drift releases preparation and allows a fresh attempt", RetryPreparedSourceDrift),
        ("Final LOCAL launch source drift propagates an unconfirmed prefab release", PreparedSourceDriftCleanupFailure),
        ("Unknown source free attempts independent roots once and retains failed responsibility", SourceFreeFailure),
        ("Wrong thread and capture reentry cannot release or replace adopted source roots", SourceAdmission)
    };
    internal static readonly (string Name, Action Test)[] PublicationCases = {
        ("LOCAL initializes the native judge settings class before reading the static list", InitializesJudgeList),
        ("A completed judge settings initializer with no list stays NotReady", MissingInitializedJudgeList),
        ("An unknown judge settings initialization is not invoked again", UnknownJudgeSettingsInitialization),
        ("Actual judge publication returns null and changed identities as known completed reads", KnownPublishedJudgeRead),
        ("Actual nonnull judge publication roots the result before native temporary cleanup", PublishedJudgeRootOrder),
        ("Actual judge root adoption failure remains Unknown rather than a retryable null read", PublishedJudgeRootFailure)
    };
    private static void InitializesJudgeList()
    {
        using var main = new MainThreadDispatcher(); using var native = CreateJudgeListFixture();
        var records = new NativePublicationArgumentRecords(main);
        var settingsClass = native.Api.ObjectClass(native.Instance);
        var count = 0;
        var list = NativeLocalPlayLaunchSourceBackend.ReadInitializedJudgeList(native.Api, records, settingsClass, cls => {
            Equal(settingsClass, cls); Equal(0, native.FieldReads); ++count;
            native.FieldBytes = IntPtr.Size == 8 ? BitConverter.GetBytes(native.OtherObject.ToInt64()) : BitConverter.GetBytes(native.OtherObject.ToInt32());
        });
        Equal(native.OtherObject, list); Equal(1, count); Equal(1, native.FieldReads);
        Equal(0, native.Invocations); Equal(0, native.ActiveRoots); Check(!records.CleanupUnknown);
    }

    private static void MissingInitializedJudgeList()
    {
        using var main = new MainThreadDispatcher(); using var native = CreateJudgeListFixture();
        var records = new NativePublicationArgumentRecords(main);
        var count = 0;
        Throws<NativeLocalPlaySourceNotReadyException>(() => NativeLocalPlayLaunchSourceBackend.ReadInitializedJudgeList(
            native.Api, records, native.Api.ObjectClass(native.Instance), _ => ++count));
        Equal(1, count); Equal(1, native.FieldReads); Check(!records.CleanupUnknown);
    }

    private static void UnknownJudgeSettingsInitialization()
    {
        using var main = new MainThreadDispatcher(); using var native = CreateJudgeListFixture();
        var records = new NativePublicationArgumentRecords(main);
        var settingsClass = native.Api.ObjectClass(native.Instance);
        var count = 0;
        Throws<IOException>(() => NativeLocalPlayLaunchSourceBackend.ReadInitializedJudgeList(native.Api, records, settingsClass, _ => {
            ++count; throw new IOException("Controlled native class initialization failure.");
        }));
        Check(records.CleanupUnknown);
        Throws<InvalidOperationException>(() => NativeLocalPlayLaunchSourceBackend.ReadInitializedJudgeList(
            native.Api, records, settingsClass, _ => ++count));
        Equal(1, count); Equal(0, native.FieldReads);
    }

    private static FakeNative CreateJudgeListFixture()
    {
        var native = new FakeNative { StaticField = true, FieldBytes = new byte[IntPtr.Size] };
        native.FieldType = native.Object;
        return native;
    }

    private static void KnownPublishedJudgeRead()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        native.Parameters = new[] { native.Int32 }; native.ReturnType = native.Object;
        var records = new NativePublicationArgumentRecords(main); var retains = 0;
        native.Result = IntPtr.Zero;
        Equal(IntPtr.Zero, NativeLocalPlayLaunchSourceBackend.ReadPublishedJudge(native.Api, records,
            native.Method, native.Instance, 2, _ => retains++));
        Equal(0, retains); Equal(0, records.Count); Check(!records.CleanupUnknown); Equal(0, native.ActiveRoots);
        native.Result = native.OtherObject;
        Equal(native.OtherObject, NativeLocalPlayLaunchSourceBackend.ReadPublishedJudge(native.Api, records,
            native.Method, native.Instance, 2, null));
        Check(!records.CleanupUnknown); Equal(0, records.Count); Equal(0, native.ActiveRoots); Equal(2, native.Invocations);
    }
    private static void PublishedJudgeRootOrder()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        native.Parameters = new[] { native.Int32 }; native.ReturnType = native.Object; native.Result = native.OtherObject;
        var records = new NativePublicationArgumentRecords(main); NativeRuntime.RootHandle? root = null;
        var observed = NativeLocalPlayLaunchSourceBackend.ReadPublishedJudge(native.Api, records, native.Method, native.Instance, 1,
            pointer => { Equal(0, native.FreedRoots); root = native.Api.Root(pointer); Equal(2, native.ActiveRoots); });
        Equal(native.OtherObject, observed); Equal(1, native.ActiveRoots); Equal(native.OtherObject, root!.Target);
        Equal(0, records.Count); Check(!records.CleanupUnknown); root.Dispose(); Equal(0, native.ActiveRoots);
    }
    private static void PublishedJudgeRootFailure()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        native.Parameters = new[] { native.Int32 }; native.ReturnType = native.Object; native.Result = native.OtherObject;
        var records = new NativePublicationArgumentRecords(main);
        Throws<IOException>(() => NativeLocalPlayLaunchSourceBackend.ReadPublishedJudge(native.Api, records,
            native.Method, native.Instance, 0, _ => throw new IOException("Controlled result-root creation failure.")));
        Check(records.CleanupUnknown); Equal(1, records.Count); Equal(1, native.Invocations);
        Throws<InvalidOperationException>(() => NativeLocalPlayLaunchSourceBackend.ReadPublishedJudge(native.Api, records,
            native.Method, native.Instance, 0, null));
        Equal(1, native.Invocations);
    }
    private static void ExactSource()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main);
        var owner = NativeLocalPlayLaunchSource.CreateControlled(main, backend);
        owner.Capture(2); owner.VerifyStable(); Equal(2, backend.Verifications); Equal(2, backend.JudgeIndex);
        Equal(new IntPtr(101), owner.Manager.Target); Equal(new IntPtr(102), owner.InLevelControl.Target); Equal(new IntPtr(104), owner.Judge.Target);
        Equal(4, owner.RootRecords); owner.Dispose(); Check(owner.Released); Equal("3,2,1,0", string.Join(",", backend.Frees));
        owner.Dispose(); Equal(4, backend.Frees.Count);
        Throws<InvalidOperationException>(() => _ = owner.Judge);
    }
    private static void PartialSource()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main) { FailAfter = 2 };
        var owner = NativeLocalPlayLaunchSource.CreateControlled(main, backend);
        Throws<NativeLocalPlaySourceNotReadyException>(() => owner.Capture(1)); Equal(2, owner.RootRecords);
        owner.Dispose(); Check(owner.Released); Equal("1,0", string.Join(",", backend.Frees));
        Throws<InvalidOperationException>(() => owner.Capture(1));
    }
    private static void SourceDrift()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main);
        var owner = NativeLocalPlayLaunchSource.CreateControlled(main, backend); owner.Capture(0); backend.Drift = true;
        Throws<NativeLocalPlaySourceNotReadyException>(() => owner.VerifyStable());
        Equal(4, owner.RootRecords); owner.Dispose(); Check(owner.Released);
    }
    private static void RetryPreparedSourceDrift()
    {
        using var main = new MainThreadDispatcher();
        var backend = new Backend(main);
        using var source = NativeLocalPlayLaunchSource.CreateControlled(main, backend);
        source.Capture(0);
        backend.Drift = true;
        var releases = 0;

        var refusal = NativeLocalAudioContentRuntime.RecheckPreparedSource(source, () => ++releases);
        Check(refusal is { Code: ErrorCode.NotReady, CapabilityId: AudioContentCapabilityIds.LocalCatalogSelection, Retryable: true });
        Equal("Controlled generation changed.", refusal!.Message);
        Equal(1, releases);
        Equal(4, source.RootRecords);
        source.Dispose();
        Check(source.Released);

        var retryBackend = new Backend(main);
        using var retry = NativeLocalPlayLaunchSource.CreateControlled(main, retryBackend);
        retry.Capture(0);
        Check(NativeLocalAudioContentRuntime.RecheckPreparedSource(retry, () => ++releases) is null);
        Equal(1, releases);
        Equal(2, retryBackend.Verifications);
    }
    private static void PreparedSourceDriftCleanupFailure()
    {
        using var main = new MainThreadDispatcher();
        var backend = new Backend(main);
        using var source = NativeLocalPlayLaunchSource.CreateControlled(main, backend);
        source.Capture(0);
        backend.Drift = true;
        var releases = 0;

        Throws<IOException>(() => NativeLocalAudioContentRuntime.RecheckPreparedSource(source, () => {
            ++releases;
            throw new IOException("Controlled prefab release failure.");
        }));
        Equal(1, releases);
        Equal(4, source.RootRecords);
    }
    private static void SourceFreeFailure()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main) { FailFree = 2 };
        var owner = NativeLocalPlayLaunchSource.CreateControlled(main, backend); owner.Capture(0);
        Throws<AggregateException>(() => owner.Dispose()); Equal(1, owner.RootRecords); Check(owner.CleanupUnknown && !owner.Released);
        Equal("3,2,1,0", string.Join(",", backend.Frees));
        Throws<InvalidOperationException>(() => owner.Dispose()); Equal(4, backend.Frees.Count);
    }
    private static void SourceAdmission()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main);
        var owner = NativeLocalPlayLaunchSource.CreateControlled(main, backend);
        Task.Run(() => Throws<InvalidOperationException>(() => owner.Capture(0))).GetAwaiter().GetResult();
        Equal(0, backend.Captures);
        backend.OnRetain = () => Throws<InvalidOperationException>(() => owner.Dispose()); owner.Capture(1);
        Equal(4, owner.RootRecords); Equal(0, backend.Frees.Count); owner.Dispose(); Equal(4, backend.Frees.Count);
    }
    private sealed class Backend : INativeLocalPlayLaunchSourceBackend
    {
        private readonly MainThreadDispatcher _main;
        internal int Captures, Verifications, JudgeIndex, FailAfter = -1, FailFree = -1;
        internal bool Drift;
        internal Action? OnRetain;
        internal readonly List<int> Frees = new();
        internal Backend(MainThreadDispatcher main) => _main = main;
        public bool CleanupUnknown => false;
        public void Capture(int index, Action<int, Func<NativeRuntime.RootHandle>> retain)
        {
            _main.VerifyAccess(); Captures++; JudgeIndex = index;
            for (var slot = 0; slot < 4; slot++)
            {
                if (slot == FailAfter) throw new NativeLocalPlaySourceNotReadyException("Controlled absent source.");
                var captured = slot;
                retain(slot, () => new NativeRuntime.RootHandle((uint)captured + 1,
                    _ => { Frees.Add(captured); if (captured == FailFree) throw new IOException("Controlled Unknown free."); },
                    _ => new IntPtr(101 + captured), _main.VerifyAccess));
                OnRetain?.Invoke();
            }
        }
        public void VerifyStable(int index, IReadOnlyList<NativeRuntime.RootHandle?> roots)
        {
            _main.VerifyAccess(); Verifications++; Equal(JudgeIndex, index); Equal(4, roots.Count);
            if (Drift) throw new NativeLocalPlaySourceNotReadyException("Controlled generation changed.");
        }
    }
}
