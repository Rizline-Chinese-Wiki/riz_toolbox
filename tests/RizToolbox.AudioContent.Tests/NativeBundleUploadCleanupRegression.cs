using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;

internal static class NativeBundleUploadCleanupRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("LOCAL failed SetData preparation releases its still-owned pinned upload before returning the budget", RejectedUpload),
        ("LOCAL wrong-thread upload adoption never consumes the callback or cleanup opportunity", WrongThread),
        ("LOCAL failed early upload free retains its record and never retries the ambiguous native outcome", EarlyUnknown),
        ("LOCAL failed retirement upload free keeps process ownership and refuses an apparent second completion", RetirementUnknown),
        ("LOCAL upload publication keeps the output record when allocation temporary cleanup fails", PublicationUnknown)
    };
    private static void RejectedUpload()
    {
        using var main = new MainThreadDispatcher(); using var fake = new FakeNative();
        var owner = new OwnedNativeLocalPlayBundle(fake.Api, main); owner.ReserveUploads(64 * 1024 * 1024);
        var frees = 0;
        owner.RetainUpload(publish => publish(new NativeRuntime.RootHandle(1, _ => frees++, _ => new(77), main.VerifyAccess)));
        Equal(1, owner.DirectRootRecords); owner.MarkCreationFailed("SetData returned false before upload cleanup.");
        owner.Dispose(); Equal(0, frees); Check(owner.TryCompleteDestroy());
        Equal(1, frees); Equal(0, owner.DirectRootRecords); Check(owner.IsClosed); owner.Dispose(); Check(owner.TryCompleteDestroy()); Equal(1, frees);
        using var next = new OwnedNativeLocalPlayBundle(fake.Api, main); next.ReserveUploads(64 * 1024 * 1024);
        next.Dispose(); Check(next.TryCompleteDestroy()); Equal(0, fake.Invocations);
    }
    private static void WrongThread()
    {
        using var main = new MainThreadDispatcher(); using var fake = new FakeNative();
        var owner = new OwnedNativeLocalPlayBundle(fake.Api, main); var allocated = 0; var freed = 0;
        Task.Run(() => Throws<InvalidOperationException>(() => owner.RetainUpload(_ => { allocated++; throw new Exception(); }))).GetAwaiter().GetResult();
        owner.RetainUpload(publish => { allocated++; publish(new(1, _ => freed++, _ => new(77), main.VerifyAccess)); });
        Task.Run(() => Throws<InvalidOperationException>(() => owner.ReleaseUpload())).GetAwaiter().GetResult();
        Equal(1, allocated); Equal(0, freed); owner.ReleaseUpload(); Equal(1, freed); owner.Dispose(); Check(owner.TryCompleteDestroy());
    }
    private static void EarlyUnknown()
    {
        using var main = new MainThreadDispatcher(); using var fake = new FakeNative();
        var owner = new OwnedNativeLocalPlayBundle(fake.Api, main); owner.ReserveUploads(1024); var frees = 0;
        owner.RetainUpload(publish => publish(new(1, _ => { frees++; throw new IOException("Unknown upload free."); }, _ => new(77), main.VerifyAccess)));
        Throws<IOException>(() => owner.ReleaseUpload()); Check(owner.CleanupUnknown);
        owner.Dispose(); Check(!owner.TryCompleteDestroy()); Throws<InvalidOperationException>(() => owner.ReleaseUpload());
        Equal(1, frees); Equal(1, owner.DirectRootRecords); Check(!owner.IsClosed);
    }
    private static void RetirementUnknown()
    {
        using var main = new MainThreadDispatcher(); using var fake = new FakeNative();
        var owner = new OwnedNativeLocalPlayBundle(fake.Api, main); owner.ReserveUploads(1024); var frees = 0;
        owner.RetainUpload(publish => publish(new(1, _ => { frees++; throw new IOException("Unknown pending upload free."); }, _ => new(77), main.VerifyAccess)));
        owner.Dispose(); Throws<AggregateException>(() => owner.TryCompleteDestroy());
        Check(owner.CleanupUnknown && !owner.IsClosed); Check(!owner.TryCompleteDestroy()); owner.Dispose();
        Equal(1, frees); Equal(1, owner.DirectRootRecords);
    }
    private static void PublicationUnknown()
    {
        using var main = new MainThreadDispatcher(); using var fake = new FakeNative();
        var owner = new OwnedNativeLocalPlayBundle(fake.Api, main); owner.ReserveUploads(1024); var frees = 0;
        var root = new NativeRuntime.RootHandle(1, _ => frees++, _ => new(77), main.VerifyAccess);
        Throws<IOException>(() => owner.RetainUpload(publish => { publish(root); throw new IOException("Unknown temporary allocation cleanup after publication."); }));
        Equal(1, owner.DirectRootRecords); Check(owner.CleanupUnknown); Equal(new IntPtr(77), root.Target);
        owner.Dispose(); Check(!owner.TryCompleteDestroy()); Equal(0, frees); Equal(1, owner.DirectRootRecords);
    }
}
