using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

internal static class LocalPlayBundleInputsRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("LOCAL CPU bundle inputs fork an exact real catalog revision and estimate only declared upload bytes", RealCatalogCapture),
        ("LOCAL CPU bundle inputs retain real payloads after provider content and catalog withdrawal", Withdrawal),
        ("LOCAL CPU bundle inputs never resolve reconstructed same-ID song or chart resources", Reconstruction),
        ("LOCAL CPU bundle input descriptor identity rejects mismatch without disposing the caller request", DescriptorIdentity),
        ("LOCAL CPU bundle inputs reuse complete native-metadata descriptor bounds before publication", DescriptorBounds),
        ("LOCAL CPU bundle input matching rejects equal-but-different snapshots duplicate matches and wrong payload kinds", ExactResources),
        ("LOCAL CPU bundle input PCM geometry normalization and finite sample checks reject malformed payloads", AudioValidation),
        ("LOCAL CPU bundle input cover dimensions normalization and finite RGBA checks reject malformed payloads", CoverValidation),
        ("LOCAL CPU bundle input chart JSON is revalidated against its retained source summary", ChartValidation),
        ("LOCAL CPU bundle input wrong-thread capture read and disposal preserve main-thread responsibility", WrongThread),
        ("LOCAL CPU bundle inputs dispose only their own fork and never shared leases or chart templates", Ownership),
        ("LOCAL CPU bundle input process reservations reject overflow before retaining another request", ProcessQuota),
        // Last: intentionally preserves one failed process slot without retry/reset.
        ("LOCAL CPU bundle input capture failure retains unknown fork cleanup once in bounded process responsibility", UnknownCleanup)
    };

    private static void RealCatalogCapture()
    {
        using var f = new RealCatalog(); var before = f.Catalog.PreparedRequestCount;
        using var inputs = LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor);
        Equal(before + 1, f.Catalog.PreparedRequestCount); Check(!ReferenceEquals(f.Request, inputs.Request));
        Equal(f.Request.Selection, inputs.Selection); Equal(f.Descriptor, inputs.Descriptor);
        Check(!ReferenceEquals(f.Descriptor, inputs.Descriptor));
        Check(ReferenceEquals(f.Audio, inputs.Audio)); Check(ReferenceEquals(f.Cover, inputs.Cover));
        Equal(f.Template.Source, inputs.Chart);
        Equal((long)f.Audio.Samples.Length * 4 + (long)f.Cover.Pixels.Length * 4 + 1024, inputs.EstimatedNativeUploadBytes);
        Check(inputs.EstimatedNativeUploadBytes <= LocalPlayBundleInputs.MaximumNativeUploadBytes);
    }
    private static void Withdrawal()
    {
        using var f = new RealCatalog(); var old = f.Template;
        f.Provider.Dispose(); f.Selection.Dispose(); f.Catalog.Dispose(); f.Content.Dispose();
        using var inputs = LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor);
        f.Request.Dispose(); Check(!old.Disposed); Equal(1, f.Catalog.PreparedRequestCount);
        Check(ReferenceEquals(f.Audio, inputs.Audio) && ReferenceEquals(f.Cover, inputs.Cover));
        Equal(f.Descriptor.Revision, inputs.Selection.Song.Revision); Equal(old.Source, inputs.Chart);
        inputs.Dispose(); Check(old.Disposed); Equal(0, f.Catalog.PreparedRequestCount);
    }
    private static void Reconstruction()
    {
        using var f = new RealCatalog(); var oldTemplate = f.Template; var oldAudio = f.Audio;
        f.Batch.Dispose(); f.Song.Dispose(); f.Selection.Dispose(); f.RegisterResources();
        var next = Need(f.Catalog.ForPlugin(f.Provider).RegisterSong(f.SongRequest with { Title = "Reconstructed" }));
        var newTemplate = f.LastTemplate;
        using var inputs = LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor);
        Check(next.Snapshot.Revision != inputs.Selection.Song.Revision); Equal("Same title", inputs.Selection.Song.Title);
        Check(ReferenceEquals(oldAudio, inputs.Audio)); Equal(oldTemplate.Source, inputs.Chart);
        Check(!ReferenceEquals(oldTemplate, newTemplate)); f.Request.Dispose(); Check(!oldTemplate.Disposed);
        inputs.Dispose(); Check(oldTemplate.Disposed && !newTemplate.Disposed);
    }
    private static void DescriptorIdentity()
    {
        using var f = new Synthetic();
        foreach (var descriptor in new[] {
            f.Descriptor with { LocalId = new("other", "song") }, f.Descriptor with { Revision = 2 },
            f.Descriptor with { ChartId = "other" }, f.Descriptor with { Title = "other" }, f.Descriptor with { Artist = "other" }
        }) Throws<InvalidDataException>(() => LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, descriptor));
        Equal(5, f.Forks); Equal(5, f.ForkFrees); Equal(0, f.SharedLeaseFrees); Check(!f.Request.IsDisposed);
    }
    private static void DescriptorBounds()
    {
        using var f = new Synthetic();
        foreach (var descriptor in new[] {
            f.Descriptor with { ChartName = "" }, f.Descriptor with { DifficultyText = "\n" },
            f.Descriptor with { Designer = "" }, f.Descriptor with { MaxHit = -1 }, f.Descriptor with { MaxScore = -1 },
            f.Descriptor with { Difficulty = float.NaN }, f.Descriptor with { Illustrator = null! },
            f.Descriptor with { ThemeColor = new(float.PositiveInfinity, 0, 0, 1) }
        }) Throws<ArgumentException>(() => LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, descriptor));
        Equal(8, f.Forks); Equal(8, f.ForkFrees); Check(!f.Request.IsDisposed); Equal(0, f.SharedLeaseFrees);
    }
    private static void ExactResources()
    {
        using (var f = new Synthetic())
        { f.Resources[0] = f.Lease(f.AudioSnapshot with { }, f.Audio); f.Reject(); }
        using (var f = new Synthetic())
        { f.Resources[1] = f.Resources[0]; f.Reject(); }
        using (var f = new Synthetic())
        { f.Resources[0] = f.Lease(f.AudioSnapshot, new object()); f.Reject(); }
        using (var f = new Synthetic())
        { f.Resources[2] = f.Lease(f.ChartSnapshot, new object()); f.Reject(); }
        using (var f = new Synthetic())
        { f.Resources[1] = f.Lease(f.CoverSnapshot, f.Audio); f.Reject(); }
    }
    private static void AudioValidation()
    {
        var bad = new[] {
            new PcmAudio(0, 1, 8000, Array.Empty<float>()), new PcmAudio(2, 3, 8000, new float[6]),
            new PcmAudio(2, 1, 7999, new float[2]), new PcmAudio(2, 1, 192001, new float[2]),
            new PcmAudio(2, 2, 8000, new float[2]), new PcmAudio(2, 1, 8000, null!),
            new PcmAudio(2, 1, 8000, new[] { float.NaN, 0f }), new PcmAudio(2, 1, 8000, new[] { 2f, 0f })
        };
        foreach (var audio in bad)
        { using var f = new Synthetic(); f.Resources[0] = f.Lease(f.AudioSnapshot, audio); f.Reject(); }
    }
    private static void CoverValidation()
    {
        var bad = new[] {
            new PngDecoder.Image(0, 1, Array.Empty<Rgba>()), new PngDecoder.Image(2049, 1, new Rgba[2049]),
            new PngDecoder.Image(2, 1, new Rgba[1]), new PngDecoder.Image(1, 1, null!),
            new PngDecoder.Image(1, 1, new[] { new Rgba(0, float.NaN, 0, 1) }),
            new PngDecoder.Image(1, 1, new[] { new Rgba(0, 0, 0, 1.1f) })
        };
        foreach (var cover in bad)
        { using var f = new Synthetic(); f.Resources[1] = f.Lease(f.CoverSnapshot, cover); f.Reject(); }
    }
    private static void ChartValidation()
    {
        using (var f = new Synthetic())
        { f.Template.Value = f.Template.Value with { Notes = 5 }; f.Reject(); }
        using (var f = new Synthetic())
        { f.Template.Value = f.Template.Value with { Json = ChartJson().Replace("\"fileVersion\":0", "\"fileVersion\":1") }; f.Reject(); }
        using (var f = new Synthetic())
        { f.Template.Value = f.Template.Value with { Bpm = float.NaN }; f.Reject(); }
        using (var f = new Synthetic())
        { f.Template.Value = f.Template.Value with { Json = new string('x', ChartJsonValidator.MaximumBytes + 1) }; f.Reject(); }
    }
    private static void WrongThread()
    {
        using var f = new Synthetic();
        Task.Run(() => {
            Throws<InvalidOperationException>(() => LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor));
            Throws<InvalidOperationException>(() => LocalPlayBundleInputs.CaptureBorrowing(f.Main, null!, null!));
        }).GetAwaiter().GetResult(); Equal(0, f.Forks);
        using var inputs = LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor);
        Task.Run(() => {
            Throws<InvalidOperationException>(() => inputs.Dispose());
            Throws<InvalidOperationException>(() => { _ = inputs.Audio; });
            Throws<InvalidOperationException>(() => { _ = inputs.Selection; });
            Throws<InvalidOperationException>(() => { _ = inputs.EstimatedNativeUploadBytes; });
        }).GetAwaiter().GetResult();
        Equal(0, f.ForkFrees); Check(!inputs.IsDisposed); Equal(2, inputs.Audio.SampleFrames);
        inputs.Dispose(); Equal(1, f.ForkFrees);
    }
    private static void Ownership()
    {
        using var f = new Synthetic(); var inputs = LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor);
        inputs.Dispose(); inputs.Dispose(); Equal(1, f.ForkFrees); Equal(0, f.ParentFrees);
        Equal(0, f.SharedLeaseFrees); Equal(0, f.Template.Frees); Check(!f.Request.IsDisposed);
        Throws<ObjectDisposedException>(() => { _ = inputs.Request; });
        Throws<ObjectDisposedException>(() => { _ = inputs.Chart; });
        using var again = LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor);
        Equal(f.Template.Source, again.Chart);
    }
    private static void ProcessQuota()
    {
        using var f = new Synthetic(); var inputs = new List<LocalPlayBundleInputs>();
        var before = LocalPlayBundleInputs.RetainedProcessInputCount;
        try
        {
            for (var i = before; i < 32; i++) inputs.Add(LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor));
            var forks = f.Forks;
            Throws<InvalidOperationException>(() => LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor));
            Equal(forks, f.Forks); inputs[0].Dispose();
            inputs.Add(LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor));
        }
        finally { foreach (var input in inputs) input.Dispose(); }
        Equal(before, LocalPlayBundleInputs.RetainedProcessInputCount); Equal(f.Forks, f.ForkFrees); Equal(0, f.SharedLeaseFrees);
    }
    private static void UnknownCleanup()
    {
        using var f = new Synthetic(); var before = LocalPlayBundleInputs.RetainedProcessInputCount;
        f.FailForkFree = true;
        Throws<AggregateException>(() => LocalPlayBundleInputs.CaptureBorrowing(f.Main, f.Request, f.Descriptor with { Title = "mismatch" }));
        Equal(1, f.ForkFrees); Equal(before + 1, LocalPlayBundleInputs.RetainedProcessInputCount);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Equal(1, f.ForkFrees); Equal(before + 1, LocalPlayBundleInputs.RetainedProcessInputCount);
        Check(!f.Request.IsDisposed); Equal(0, f.SharedLeaseFrees); Equal(0, f.Template.Frees);
    }

    private static LocalNativeMetadataDescriptor Descriptor(LocalSongSelectionSnapshot selection) => new(selection.Song.Song,
        selection.Song.Revision, selection.Chart.Id, selection.Song.Title, selection.Song.Artist, selection.Chart.Name,
        "3", "Designer", new(.2f, .3f, .4f, 1), 1, 1_000_000, 3);
    private sealed class Template : IParsedLocalChart
    {
        internal ValidatedChartJson Value = ChartJsonValidator.Validate(Encoding.UTF8.GetBytes(ChartJson()));
        internal int Frees; public ValidatedChartJson Source => Value;
        public void Dispose() => Frees++;
    }
    private sealed class Synthetic : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly PcmAudio Audio = new(2, 1, 8000, new[] { 0f, .25f });
        internal readonly PngDecoder.Image Cover = new(1, 1, new[] { new Rgba(1, 1, 1, 1) });
        internal readonly Template Template = new();
        internal readonly LocalContentSnapshot AudioSnapshot, CoverSnapshot, ChartSnapshot;
        internal readonly ValidatedContentLease[] Resources;
        internal readonly LocalSongSelectionSnapshot Selection;
        internal readonly LocalNativeMetadataDescriptor Descriptor;
        internal readonly LocalPlayRequest Request;
        internal int Forks, ForkFrees, ParentFrees, SharedLeaseFrees;
        internal bool FailForkFree;
        internal Synthetic()
        {
            LocalContentSnapshot Snapshot(string id, LocalContentKind kind, string format, int version)
                => new(new("provider", id), kind, format, version, 1, kind == LocalContentKind.Chart, Array.Empty<LocalContentId>());
            AudioSnapshot = Snapshot("audio", LocalContentKind.Audio, "wav.pcm16", 1);
            CoverSnapshot = Snapshot("cover", LocalContentKind.Cover, "png.rgba8", 1);
            ChartSnapshot = Snapshot("chart", LocalContentKind.Chart, "rizline.chart.json", 0);
            var chart = new LocalSongChartSnapshot("easy", "Easy", ChartSnapshot);
            var song = new LocalSongSnapshot(new("provider", "song"), "Same title", "Artist", 1, AudioSnapshot, CoverSnapshot, new[] { chart }, false);
            Selection = new(song, chart, 1); Descriptor = LocalPlayBundleInputsRegression.Descriptor(Selection);
            Resources = new[] { Lease(AudioSnapshot, Audio), Lease(CoverSnapshot, Cover), Lease(ChartSnapshot, Template) };
            Request = Make(false);
        }
        internal ValidatedContentLease Lease(LocalContentSnapshot snapshot, object payload)
            => new(snapshot, payload, "bundle-input-test", new ActionLease(() => SharedLeaseFrees++), Main.VerifyAccess);
        private LocalPlayRequest Make(bool fork) => new(Selection, Resources,
            () => { Forks++; return Make(true); },
            () => { if (!fork) { ParentFrees++; return; } ForkFrees++; if (FailForkFree) throw new IOException("Unknown request-fork release."); }, Main.VerifyAccess);
        internal void Reject()
        {
            Throws<InvalidDataException>(() => LocalPlayBundleInputs.CaptureBorrowing(Main, Request, Descriptor));
            Equal(Forks, ForkFrees); Equal(0, SharedLeaseFrees); Equal(0, Template.Frees); Check(!Request.IsDisposed);
        }
        public void Dispose() { Request.Dispose(); Main.Dispose(); }
    }
    private sealed class RealCatalog : IDisposable
    {
        private readonly ChartFixture _f = new();
        internal MainThreadDispatcher Main => _f.Main;
        internal LocalContentService Content => _f.Content;
        internal FakeParsedChart LastTemplate => _f.Parser.Created.Last();
        internal readonly LocalCatalogService Catalog;
        internal readonly Context Provider, Consumer;
        internal ILocalContentBatchRegistration Batch = null!;
        internal LocalSongRequest SongRequest = null!;
        internal readonly ILocalSongRegistration Song;
        internal readonly ILocalCatalogSelection Selection;
        internal readonly LocalPlayRequest Request;
        internal readonly LocalNativeMetadataDescriptor Descriptor;
        internal readonly PcmAudio Audio;
        internal readonly PngDecoder.Image Cover;
        internal readonly FakeParsedChart Template;
        internal RealCatalog()
        {
            File.WriteAllBytes(Path.Combine(_f.Directory, "tone.wav"), Wav());
            File.WriteAllBytes(Path.Combine(_f.Directory, "cover.png"), Png());
            Catalog = new(Main, Content, new Logger()); Provider = _f.Context("provider"); Consumer = _f.Context("consumer");
            RegisterResources(); Song = Need(Catalog.ForPlugin(Provider).RegisterSong(SongRequest));
            Selection = Need(Catalog.SelectionForPlugin(Consumer).Select(Song.Snapshot.Song, "easy"));
            Request = Need(Catalog.Prepare(Selection)); Descriptor = LocalPlayBundleInputsRegression.Descriptor(Request.Selection);
            Audio = (PcmAudio)Request.Resources.Single(x => ReferenceEquals(x.Snapshot, Request.Selection.Song.Audio)).Payload;
            Cover = (PngDecoder.Image)Request.Resources.Single(x => ReferenceEquals(x.Snapshot, Request.Selection.Song.Cover)).Payload;
            Template = LastTemplate;
        }
        internal void RegisterResources()
        {
            Batch = Need(Content.BatchForPlugin(Provider).RegisterBatch(new[] {
                new LocalContentRequest("audio", LocalContentKind.Audio, "wav.pcm16", 1, new(ContentFileArea.Package, "tone.wav")),
                new LocalContentRequest("cover", LocalContentKind.Cover, "png.rgba8", 1, new(ContentFileArea.Package, "cover.png")),
                new LocalContentRequest("chart", LocalContentKind.Chart, "rizline.chart.json", 0, new(ContentFileArea.Package, "chart.json")) }));
            SongRequest = new("song", "Same title", "Artist", new("provider", "audio"), new("provider", "cover"),
                new[] { new LocalSongChartRequest("easy", "Easy", new("provider", "chart")) });
        }
        public void Dispose()
        {
            Request.Dispose(); Selection.Dispose(); Catalog.Dispose(); Provider.Dispose(); Consumer.Dispose();
            File.Delete(Path.Combine(_f.Directory, "tone.wav")); File.Delete(Path.Combine(_f.Directory, "cover.png")); _f.Dispose();
        }
        private static byte[] Png()
        {
            using var output = new MemoryStream(); output.Write(new byte[] { 137,80,78,71,13,10,26,10 });
            var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, 1); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1); header[8] = 8; header[9] = 6;
            Chunk(output, "IHDR", header); using var data = new MemoryStream(); using (var z = new ZLibStream(data, CompressionLevel.Fastest, true)) z.Write(new byte[] { 0,255,255,255,255 });
            Chunk(output, "IDAT", data.ToArray()); Chunk(output, "IEND", Array.Empty<byte>()); return output.ToArray();
        }
        private static void Chunk(Stream output, string kind, byte[] data)
        {
            var length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, data.Length); output.Write(length);
            var bytes = Encoding.ASCII.GetBytes(kind).Concat(data).ToArray(); output.Write(bytes); var crc = uint.MaxValue;
            foreach (var value in bytes) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); output.Write(length);
        }
    }
}
