using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RizToolbox.Adapter.Settings;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class DataSettingsProjectionRegression
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("data settings read live scalar values in order without searching for sliders", NativeProjection);
        yield return ("data settings preserve atomic failure, thread access and reentrant withdrawal", AtomicFailure);
    }

    private static void NativeProjection()
    {
        using var main = new MainThreadDispatcher();
        using var native = new SettingsNativeFixture();
        using var service = new GameSettingsService(main, new TestLogger(), native.Backend(main));
        Check(service.Refresh().Success, "The settings service should initialize with verified ranges.");
        native.UiActive = false;
        var scans = native.SliderSearches;
        var reads = native.SettingReads;
        var definitions = NativeGameSettingsBackend.Definitions.OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray();

        var first = Capture(service);
        Check(first.Select(row => row.Id).SequenceEqual(definitions.Select(definition => definition.Id)),
            "The scalar projection should preserve ordinal setting IDs.");
        for (int i = 0; i < definitions.Length; i++)
        {
            var definition = definitions[i];
            var row = first[i];
            Check(row.JsonValue == native.Values[definition.Property].ToString("R", CultureInfo.InvariantCulture) &&
                row.Source.Contains("SettingsControl.get_" + definition.Property, StringComparison.Ordinal) &&
                row.Unit?.Contains("normalized volume scalar", StringComparison.Ordinal) == true,
                "The scalar projection should preserve the exact value, getter source and setter unit.");
        }

        var musicId = definitions.Single(definition => definition.Property == "MusicVolume").Id;
        var previous = first.Single(row => row.Id == musicId).JsonValue;
        var changed = MathF.BitDecrement(.995f);
        native.Values["MusicVolume"] = changed;
        var second = Capture(service);
        Check(second.Single(row => row.Id == musicId).JsonValue == changed.ToString("R", CultureInfo.InvariantCulture) &&
            first.Single(row => row.Id == musicId).JsonValue == previous && previous != second.Single(row => row.Id == musicId).JsonValue,
            "A new capture should read the current game-owned Single and leave the prior snapshot intact.");
        Check(native.SliderSearches == scans && native.SettingReads - reads == 2 * definitions.Length &&
            native.ActiveRoots == 0 && native.Writes.Count == 0,
            "Two data snapshots should reach each real getter twice with zero slider searches, writes or remaining roots.");
    }

    private static void AtomicFailure()
    {
        using var fixture = new Fixture();
        var backend = fixture.Backend;
        var earlier = Capture(fixture.Service);
        backend.Values["music"] = .25f;
        backend.Values["sfx"] = float.NaN;
        CheckFailure(fixture.Service.CaptureDataSettings(), ErrorCode.CapabilityUnavailable, "sfx", "non-finite");

        var sfxIndex = backend.List.FindIndex(binding => binding.Id == "sfx");
        backend.List[sfxIndex] = backend.List[sfxIndex] with { Read = () => throw new IOException("controlled getter failure") };
        CheckFailure(fixture.Service.CaptureDataSettings(), ErrorCode.CapabilityUnavailable, "sfx", "controlled getter failure");
        Check(earlier.Single(row => row.Id == "music").JsonValue == "0.8",
            "A failed capture must leave earlier successful snapshots intact.");

        var reads = backend.Reads;
        var scans = backend.RangeProbes;
        backend.Initialized = false;
        CheckFailure(fixture.Service.CaptureDataSettings(), ErrorCode.NotReady);
        CheckFailure(Task.Run(fixture.Service.CaptureDataSettings).GetAwaiter().GetResult(), ErrorCode.WrongThread);
        Check(backend.Reads == reads && backend.RangeProbes == scans, "Rejected captures must not call any getter or range probe.");
        backend.Initialized = true;

        var musicIndex = backend.List.FindIndex(binding => binding.Id == "music");
        backend.List[musicIndex] = backend.List[musicIndex] with { Read = () =>
        {
            fixture.Service.Dispose();
            Check(backend.DisposeCount == 0, "Backend cleanup must wait for the getter and capture lease to return.");
            return .25f;
        } };
        CheckFailure(fixture.Service.CaptureDataSettings(), ErrorCode.ScopeExpired);
        Check(backend.DisposeCount == 1, "Withdrawal during a getter must discard the capture and dispose the backend once.");
        CheckFailure(fixture.Service.CaptureDataSettings(), ErrorCode.ScopeExpired);
    }

    internal static void Measure()
    {
        const int samples = 500;
        using var main = new MainThreadDispatcher();
        using var native = new SettingsNativeFixture();
        using var service = new GameSettingsService(main, new TestLogger(), native.Backend(main));
        Check(service.Refresh().Success, "The measurement fixture should initialize.");
        native.UiActive = false;
        for (int i = 0; i < 150; i++) Capture(service);
        var batches = new List<object>();
        for (int batch = 0; batch < 5; batch++)
        {
            var ranges = native.SliderSearches;
            var getters = native.SettingReads;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < samples; i++) Capture(service);
            var elapsed = Stopwatch.GetElapsedTime(start);
            var totalAllocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
            batches.Add(new
            {
                samples,
                sliderSearches = native.SliderSearches - ranges,
                getterCalls = native.SettingReads - getters,
                allocatedBytesPerCapture = totalAllocation / (double)samples,
                meanMicroseconds = elapsed.TotalMilliseconds * 1000 / samples
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(batches));
    }

    private static IReadOnlyList<GameSettingSnapshot> Capture(GameSettingsService service)
    {
        var result = service.CaptureDataSettings();
        return result.Value ?? throw new InvalidOperationException(result.Error?.Message ?? "No captured values.");
    }

    private static void CheckFailure(Result<IReadOnlyList<GameSettingSnapshot>> result, ErrorCode code,
        string? target = null, string? detail = null)
        => Check(!result.Success && result.Value is null && result.Error?.Code == code &&
            (target is null || result.Error.TargetId == target) &&
            (detail is null || result.Error.Message.Contains(detail, StringComparison.Ordinal)),
            "Capture should fail atomically with " + code + " for " + target + ".");

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
