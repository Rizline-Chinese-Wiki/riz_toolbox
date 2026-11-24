using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class ClockProfileReadinessChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("clock profile retries a dormant transport and publishes its later verified profile", LateTransport),
        ("clock profile retries incomplete construction without caching a partial profile", LateProfile),
        ("unhooked clock metadata adopts its later exact hook once", LateHook),
        ("clock profile never rebuilds to forget a withdrawn native owner", WithdrawnOwner),
        ("clock hook replacement remains rejected after the old lease is returned", ReplacedOwner),
        ("temporarily missing clock transport does not fall back to unhooked metadata", MissingTransport),
        ("failed late hook attachment cannot publish an unverified clock profile", FailedAttachment),
        ("worker probing cannot clear the owner thread clock profile", WrongThread),
        ("cold native metadata DSP and controller capture never request GetTime authority", ColdBackend),
        ("formal native backend retries a late hook provider without granting fixture clock semantics", BackendRetry),
        ("clock consumers select once per read and release failed or nested scopes", ConsumerReadScopes),
        ("optional mapping failure does not block an independent native rate provider", IndependentRateProvider),
        ("stock lower-time preflight accepts its retained complete wrapper with REX.W tail jump", StockLowerTimeWrapper),
        ("stock lower-time preflight rejects a changed wrapper even with its exact tail jump", ChangedStockLowerTimeWrapper)
    };
    private static void Check(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }

    // Retained bytes from playback-progress-player-writer-20261101 /
    // next-candidate-profile.json, AudioSource.set_time @ 0x1511c00.
    private static byte[] StockTimeWrapper() => Convert.FromHexString(
        "40534883ec30488b057beaa900488bd90f297424200f28f14885c07513488d0d94353400"
        + "e83734d5fe48890558eaa9000f28ce488bcb0f287424204883c4305b48ffe0cccccccccccccccccccccccccc");
    private static void StockLowerTimeWrapper()
        => NativePlaybackLowerPitchTarget.LowerTimePreflight.VerifyWrapperPin(StockTimeWrapper(), owned: false);
    private static void ChangedStockLowerTimeWrapper()
    {
        var wrapper = StockTimeWrapper(); wrapper[9] ^= 1; // Change its cache displacement, leaving the JMP intact.
        try { NativePlaybackLowerPitchTarget.LowerTimePreflight.VerifyWrapperPin(wrapper, owned: false); }
        catch (UnsupportedSignatureException) { return; }
        throw new InvalidOperationException("The stock lower-time preflight accepted a changed complete wrapper.");
    }

    // These are availability state tests. Fake metadata does not issue native
    // permission; BackendRetry separately exercises the actual backend and its
    // unchanged fixed-image refusal with the managed native API fixture.
    private sealed class Hook { internal bool Admitted = true; }
    private sealed class Profile
    {
        internal Hook? Hook;
        internal bool Withdrawn;
    }
    private sealed class Fixture
    {
        internal Hook? CurrentHook;
        internal Exception? TransportError, ConstructionError;
        internal int Captures, Creations, Attachments, Verifications;
        internal Profile? Created;
        internal readonly GameplayClockProfileCache<Profile, Hook> Cache;
        internal Fixture()
        {
            Cache = new(() => {
                Captures++; if (TransportError is not null) throw TransportError; return CurrentHook;
            }, hook => {
                Creations++; if (ConstructionError is not null) throw ConstructionError;
                return Created = new() { Hook = hook };
            }, (profile, hook) => {
                Attachments++;
                if (!hook.Admitted) throw new UnsupportedSignatureException("Exact native owner rejected.");
                profile.Hook = hook;
            }, profile => {
                Verifications++;
                if (profile.Withdrawn) throw new UnsupportedSignatureException("Native owner remains withdrawn.");
                Check(profile.Hook is null || profile.Hook.Admitted, "Unverified hook was accepted.");
            });
        }
        internal Profile Ready()
        {
            Check(Cache.Refresh() is null && Cache.Current is not null, "Clock profile did not become ready.");
            return Cache.Current!;
        }
    }
    private static void LateTransport()
    {
        var f = new Fixture { TransportError = new GameplayStateException(ErrorCode.NotReady, "Dormant physical transport.") };
        Check(f.Cache.Refresh() == "Dormant physical transport." && f.Cache.Current is null && f.Creations == 0, "Dormant transport escaped.");
        f.TransportError = null; f.CurrentHook = new();
        var profile = f.Ready();
        Check(ReferenceEquals(profile, f.Ready()) && f.Captures == 3 && f.Creations == 1 && f.Verifications == 2,
            "Late readiness was cached forever, or an established profile was rebuilt.");
    }
    private static void LateProfile()
    {
        var f = new Fixture { CurrentHook = new(), ConstructionError = new GameplayStateException(ErrorCode.NotReady, "Metadata not ready.") };
        Check(f.Cache.Refresh() == "Metadata not ready." && f.Cache.Current is null, "A partial profile was published.");
        f.ConstructionError = null;
        var profile = f.Ready();
        Check(ReferenceEquals(profile, f.Ready()) && f.Creations == 2, "A failed constructor poisoned or replaced the later profile.");
    }
    private static void LateHook()
    {
        var f = new Fixture(); var original = f.Ready();
        f.CurrentHook = new();
        Check(ReferenceEquals(original, f.Ready()) && ReferenceEquals(original.Hook, f.CurrentHook), "Late hook discarded the verified metadata profile.");
        f.Ready();
        Check(f.Creations == 1 && f.Attachments == 1 && f.Verifications == 3, "Hook attachment was repeated or current verification was skipped.");
    }
    private static void WithdrawnOwner()
    {
        var f = new Fixture { CurrentHook = new() }; var profile = f.Ready(); profile.Withdrawn = true;
        Check(f.Cache.Refresh() is not null && f.Cache.Current is null, "A withdrawn native profile remained available.");
        Check(f.Cache.Refresh() is not null && f.Cache.Current is null && f.Creations == 1 && f.Verifications == 3,
            "Cache recreation forgot the native owner's withdrawal.");
    }
    private static void ReplacedOwner()
    {
        var first = new Hook(); var f = new Fixture { CurrentHook = first }; f.Ready();
        f.CurrentHook = new();
        Check(f.Cache.Refresh() is not null && f.Cache.Current is null, "Another hook borrowed the old profile.");
        f.CurrentHook = first;
        Check(f.Cache.Refresh() is not null && f.Cache.Current is null && f.Creations == 1 && f.Attachments == 0,
            "A switched physical owner revived its previous authority.");
    }
    private static void MissingTransport()
    {
        var hook = new Hook(); var f = new Fixture { CurrentHook = hook }; var profile = f.Ready();
        f.CurrentHook = null;
        Check(f.Cache.Refresh() is not null && f.Cache.Current is null && f.Creations == 1, "Missing transport fell back to an unhooked profile.");
        f.CurrentHook = hook;
        Check(ReferenceEquals(f.Ready(), profile) && f.Attachments == 0 && f.Verifications == 2,
            "Returning the same still-valid physical transport replaced its owner.");
    }
    private static void FailedAttachment()
    {
        var f = new Fixture(); var profile = f.Ready(); f.CurrentHook = new() { Admitted = false };
        Check(f.Cache.Refresh() is not null && f.Cache.Current is null && profile.Hook is null, "Failed attachment published a profile.");
        f.CurrentHook = new();
        Check(ReferenceEquals(f.Ready(), profile) && f.Creations == 1 && f.Attachments == 2,
            "Failed attachment forgot the existing fixed metadata or prevented a later verified attachment.");
    }
    private static void WrongThread()
    {
        var f = new Fixture(); var profile = f.Ready();
        var refused = Task.Run(() => {
            try { f.Cache.Refresh(); return false; }
            catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult();
        Check(refused && ReferenceEquals(profile, f.Cache.Current) && f.Captures == 1,
            "A worker consumed transport or cleared the actual owner's profile.");
    }
    private sealed class DelayedProvider : INativePlaybackRateProvider, INativePlaybackGetTimeHookProvider
    {
        internal bool Ready, RateSupported;
        internal int HookCaptures, Probes, RateCaptures;
        public NativePlaybackRateProviderProbe Probe()
        { Probes++; return new(RateSupported, "Fixture provider; no game production authority."); }
        public INativePlaybackRateSource Capture(NativePlaybackRateOrigin origin)
        {
            RateCaptures++;
            throw new GameplayStateException(ErrorCode.NotReady, "Independent rate source is not ready.");
        }
        public NativePlaybackGetTimeHookLease? CaptureGetTimeHook()
        {
            HookCaptures++;
            if (!Ready) throw new GameplayStateException(ErrorCode.NotReady, "Initial physical transport is dormant.");
            return null;
        }
    }
    private static void ColdBackend()
    {
        using var native = new FakeNative(); native.ConfigureGameplay(); native.GameplayActive = false;
        using var main = new MainThreadDispatcher(); var provider = new DelayedProvider();
        using (var backend = new GameplayNativeBackend(native.GameplayApi, main, provider))
        {
            var first = backend.Probe(); var second = backend.Probe();
            Check(first.Sources["clockMapping"] is not null && second.Sources["clockMapping"] == first.Sources["clockMapping"],
                "Metadata probing claimed current clock mapping authority.");
            var probes = provider.Probes;
            Check(backend.ReadDsp().Available, "Dormant GetTime transport disabled independent DSP.");
            try { using var missing = backend.Capture(); throw new InvalidOperationException("An absent controller was captured."); }
            catch (GameplayStateException error) when (error.Code == ErrorCode.TargetMissing) { }
            native.GameplayActive = true;
            using var capture = backend.Capture();
            Check(provider.HookCaptures == 0 && provider.Probes == probes,
                "DSP or controller discovery consumed playback capability/GetTime authority.");
        }
        Check(native.ActiveRoots == 0, "Cold backend discovery leaked native roots.");
    }
    private static void BackendRetry()
    {
        using var native = new FakeNative(); native.ConfigureGameplay();
        using var main = new MainThreadDispatcher(); var provider = new DelayedProvider();
        using (var backend = new GameplayNativeBackend(native.GameplayApi, main, provider))
        {
            var first = backend.Probe();
            using var capture = backend.Capture();
            var raw = capture.ReadClock("nowTime");
            Check(provider.HookCaptures == 1 && raw.Available && raw.Unit == GameplayClockUnit.RawGameUnits
                && raw.UnitEvidence == "Initial physical transport is dormant.", "The actual clock read did not verify its current transport.");
            provider.Ready = true; var second = backend.Probe();
            Check(provider.HookCaptures == 1 && second.Sources["clockMapping"] == first.Sources["clockMapping"],
                "Metadata probing requested GetTime authority or mutated its earlier diagnostic snapshot.");
            raw = capture.ReadClock("nowRealTime");
            Check(provider.HookCaptures == 2 && raw.Available && raw.Unit == GameplayClockUnit.RawGameUnits
                && raw.UnitEvidence is not null && raw.UnitEvidence != "Initial physical transport is dormant."
                && native.GameplayPitchWrites == 0, "An existing capture failed to retry late readiness or acquired fixed-image semantics.");
            Check(second.Sources["nowTime"] is null && backend.ReadDsp().Available && provider.HookCaptures == 2,
                "Optional profile failure disabled independent raw/DSP sources or DSP requested GetTime authority.");
            provider.Ready = false;
            var before = provider.HookCaptures;
            try { _ = capture.ClockSourceIdentity; throw new InvalidOperationException("Dormant source identity was accepted."); }
            catch (GameplayStateException error)
            {
                Check(error.Code == ErrorCode.OperationUnsupported && error.Message == "Initial physical transport is dormant."
                    && provider.HookCaptures == before + 1, "An existing capture did not recheck the current physical transport.");
            }
            provider.Ready = true; before = provider.HookCaptures;
            try { _ = capture.ClockSourceIdentity; throw new InvalidOperationException("Fixture source acquired native mapping permission."); }
            catch (GameplayStateException error)
            {
                Check(error.Code == ErrorCode.OperationUnsupported && error.Message != "Initial physical transport is dormant."
                    && provider.HookCaptures == before + 1, "Source mapping selected its error before retrying a now-ready provider.");
            }
        }
        Check(native.ActiveRoots == 0, "Late backend probing leaked native capture roots.");
    }
    private static void IndependentRateProvider()
    {
        using var native = new FakeNative(); native.ConfigureGameplay();
        using var main = new MainThreadDispatcher(); var provider = new DelayedProvider { RateSupported = true };
        using (var backend = new GameplayNativeBackend(native.GameplayApi, main, provider))
        {
            using var capture = backend.Capture();
            try
            {
                using var prepared = capture.Prepare(new(GameplayControlAction.SetSpeed, 1, "fixture-run", 2,
                    GameplayControlValueUnit.PlaybackRateMultiplier));
                throw new InvalidOperationException("The fixture unexpectedly issued a rate source.");
            }
            catch (GameplayStateException error)
            {
                Check(error.Code == ErrorCode.NotReady && error.Message == "Independent rate source is not ready."
                    && provider.HookCaptures == 1 && provider.RateCaptures == 1 && native.GameplayPitchWrites == 0,
                    "Mapping failure blocked independent rate authority or rate preparation skipped current hook verification.");
            }
        }
        Check(native.ActiveRoots == 0, "Independent rate preparation leaked native roots.");
    }
    private static void ConsumerReadScopes()
    {
        // Exercise the formal service and actual native backend without giving
        // fixture metadata a stock image or native source permission.
        using (var native = new FakeNative())
        {
            native.ConfigureGameplay();
            using var main = new MainThreadDispatcher();
            var provider = new DelayedProvider();
            using var backend = new GameplayNativeBackend(native.GameplayApi, main, provider);
            using var service = new GameplayService(backend, main);
            using var context = new Context("clock-consumer", main);
            var clock = service.ClockForPlugin(context);
            Check(clock.Read().Success && provider.HookCaptures == 1,
                "The same service Read refreshed separately for its clock fields and mapping identity.");
            Check(clock.Read().Success && provider.HookCaptures == 2,
                "A second service Read reused the earlier consumer's profile selection.");
        }

        // Scope mechanics use inert objects, not fake native issuers. A nested
        // consumer must make its own selection even while its caller is live.
        var refreshes = 0;
        var reads = new GameplayClockProfileReads<object>(() =>
        {
            refreshes++; return (new object(), null);
        });
        object? outer;
        using (reads.Begin())
        {
            outer = reads.Read(out _);
            Check(ReferenceEquals(outer, reads.Read(out _)) && refreshes == 1,
                "Auxiliary reads did not share their explicit consumer selection.");
            using (reads.Begin())
            {
                var nested = reads.Read(out _);
                Check(!ReferenceEquals(outer, nested) && ReferenceEquals(nested, reads.Read(out _)) && refreshes == 2,
                    "A nested consumer borrowed the outer profile or refreshed twice.");
            }
            Check(ReferenceEquals(outer, reads.Read(out _)) && refreshes == 2,
                "Ending a nested consumer did not restore its caller's scope.");
        }
        Check(!ReferenceEquals(outer, reads.Read(out _)) && refreshes == 3,
            "An ended scope retained its profile for an outside consumer.");

        try
        {
            using var scope = reads.Begin();
            _ = reads.Read(out _);
            throw new IOException("Consumer ended abnormally.");
        }
        catch (IOException error) when (error.Message == "Consumer ended abnormally.") { }
        _ = reads.Read(out _);
        Check(refreshes == 5, "An exception left a live scope authorizing later reads.");

        var attempts = 0; var fail = true;
        var failing = new GameplayClockProfileReads<object>(() =>
        {
            attempts++;
            if (fail) throw new IOException("Selection failed.");
            return (new object(), null);
        });
        using (failing.Begin())
        {
            for (var index = 0; index < 2; index++)
            {
                try { _ = failing.Read(out _); throw new InvalidOperationException("Failed selection became available."); }
                catch (IOException error) when (error.Message == "Selection failed.") { }
            }
            Check(attempts == 1, "A failed selection was retried inside the same consumer.");
        }
        fail = false;
        using (failing.Begin())
            Check(failing.Read(out _) is not null && attempts == 2,
                "An ended failed scope prevented a later fresh consumer from becoming ready.");
    }
}
