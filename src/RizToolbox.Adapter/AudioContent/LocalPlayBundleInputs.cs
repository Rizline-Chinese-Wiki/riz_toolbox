using System.Text;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>
/// Main-thread CPU preparation over one pinned request fork. No native resource, controller,
/// playback or game capability is created. Shared content leases/templates are never disposed here.
/// </summary>
internal sealed class LocalPlayBundleInputs : IDisposable
{
    internal const long MaximumNativeUploadBytes = 64L * 1024 * 1024;
    private const int MaximumProcessInputs = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<LocalPlayBundleInputs> ProcessInputs = new();
    private readonly IMainThreadDispatcher _main;
    private LocalPlayRequest? _request;
    private LocalSongSelectionSnapshot? _selection;
    private LocalNativeMetadataDescriptor? _descriptor;
    private PcmAudio? _audio;
    private PngDecoder.Image? _cover;
    private ValidatedChartJson? _chart;
    private long _estimated;
    private bool _ready, _disposeStarted, _cleanupUnknown;
    private Exception? _cleanupFailure;
    private LocalPlayBundleInputs(IMainThreadDispatcher main) => _main = main;
    internal static int RetainedProcessInputCount { get { lock (ProcessGate) return ProcessInputs.Count; } }
    internal bool IsDisposed => _disposeStarted;
    internal bool CleanupUnknown => _cleanupUnknown;
    internal LocalPlayRequest Request { get { Guard(); return _request!; } }
    internal LocalSongSelectionSnapshot Selection { get { Guard(); return _selection!; } }
    internal LocalNativeMetadataDescriptor Descriptor { get { Guard(); return _descriptor!; } }
    internal PcmAudio Audio { get { Guard(); return _audio!; } }
    internal PngDecoder.Image Cover { get { Guard(); return _cover!; } }
    internal ValidatedChartJson Chart { get { Guard(); return _chart!; } }
    internal long EstimatedNativeUploadBytes { get { Guard(); return _estimated; } }

    internal static LocalPlayBundleInputs CaptureBorrowing(IMainThreadDispatcher main, LocalPlayRequest request,
        LocalNativeMetadataDescriptor descriptor)
    {
        // Even request.Selection/root-free metadata access must not precede admission.
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(descriptor);
        var owner = new LocalPlayBundleInputs(main);
        lock (ProcessGate)
        {
            if (ProcessInputs.Count >= MaximumProcessInputs)
                throw new InvalidOperationException("Prepared LOCAL bundle input responsibility capacity is exhausted.");
            ProcessInputs.Add(owner);
        }
        try
        {
            // Adopt the fork immediately; every later rejection releases this fork only.
            owner._request = request.Retain();
            owner._selection = owner._request.Selection;
            owner._descriptor = NativeLocalMetadataFactory.ValidateAndCopy(descriptor);
            var selection = owner._selection;
            if (selection.Song is null || selection.Chart is null || selection.Generation <= 0
                || selection.Song.Song != owner._descriptor.LocalId || selection.Song.Revision != owner._descriptor.Revision
                || selection.Chart.Id != owner._descriptor.ChartId || selection.Song.Title != owner._descriptor.Title
                || selection.Song.Artist != owner._descriptor.Artist)
                throw new InvalidDataException("LOCAL metadata descriptor does not match the exact selected song/revision/chart/title/artist.");
            if (selection.Song.Charts is null || selection.Song.Charts.Count is < 1 or > LocalCatalogService.MaximumCharts
                || selection.Song.Charts.Count(chart => ReferenceEquals(chart, selection.Chart)) != 1)
                throw new InvalidDataException("Selected chart is not the exact single member of this song snapshot.");
            var resources = owner._request.Resources;
            if (resources.Count is < 3 or > LocalCatalogService.MaximumCharts + 2)
                throw new InvalidDataException("Prepared song resources exceed their bounded catalog shape.");
            object Payload(LocalContentSnapshot expected, LocalContentKind kind)
            {
                if (expected is null || expected.Kind != kind)
                    throw new InvalidDataException("Selected content snapshot has the wrong kind.");
                ValidatedContentLease? selected = null; var matches = 0;
                for (var i = 0; i < resources.Count; i++)
                {
                    var lease = resources[i];
                    if (lease is null) throw new InvalidDataException("Prepared song contains a null content lease.");
                    if (!ReferenceEquals(lease.Snapshot, expected)) continue;
                    matches++; selected = lease;
                }
                if (matches != 1 || selected!.Snapshot.Kind != kind)
                    throw new InvalidDataException("The exact selected content snapshot must have one retained lease; same-ID replacements are refused.");
                return selected.Payload;
            }
            owner._audio = Payload(selection.Song.Audio, LocalContentKind.Audio) as PcmAudio
                ?? throw new InvalidDataException("Selected audio is not decoded PCM.");
            owner._cover = Payload(selection.Song.Cover, LocalContentKind.Cover) as PngDecoder.Image
                ?? throw new InvalidDataException("Selected cover is not decoded RGBA pixels.");
            var template = Payload(selection.Chart.Content, LocalContentKind.Chart) as IParsedLocalChart
                ?? throw new InvalidDataException("Selected chart is not a parsed chart template.");
            ValidateAudio(owner._audio); ValidateCover(owner._cover);
            var source = template.Source ?? throw new InvalidDataException("Selected chart template has no source.");
            if (source.Json is null || source.Json.Length > ChartJsonValidator.MaximumBytes)
                throw new InvalidDataException("Chart source exceeds its bounded JSON input.");
            var encoding = new UTF8Encoding(false, true);
            if (encoding.GetByteCount(source.Json) > ChartJsonValidator.MaximumBytes)
                throw new InvalidDataException("Chart source exceeds its UTF-8 byte budget.");
            owner._chart = ChartJsonValidator.Validate(encoding.GetBytes(source.Json));
            if (owner._chart != source)
                throw new InvalidDataException("Chart source JSON differs from the retained validated line/note/BPM summary.");
            owner._estimated = checked((long)owner._audio.Samples.Length * sizeof(float)
                + (long)owner._cover.Pixels.Length * 4 + 1024);
            if (owner._estimated > MaximumNativeUploadBytes)
                throw new InvalidDataException("LOCAL native upload estimate exceeds 64 MiB.");
            owner._ready = true; return owner;
        }
        catch (Exception failure)
        {
            try { owner.Dispose(); }
            catch (Exception cleanup)
            { throw new AggregateException("LOCAL CPU input capture failed and its request fork cleanup is unknown.", failure, cleanup); }
            throw;
        }
    }
    private static void ValidateAudio(PcmAudio audio)
    {
        if (audio.SampleFrames <= 0 || audio.Channels is < 1 or > 2 || audio.Frequency is < 8000 or > 192000
            || audio.Samples is null || (long)audio.SampleFrames * audio.Channels != audio.Samples.Length
            || (long)audio.Samples.Length * sizeof(float) > WavDecoder.MaximumDecodedBytes
            || audio.Samples.Any(sample => !float.IsFinite(sample) || sample is < -1 or > 1))
            throw new InvalidDataException("PCM shape, finite normalized samples or decoded-byte budget is invalid.");
    }
    private static void ValidateCover(PngDecoder.Image image)
    {
        if (image.Width is < 1 or > 2048 || image.Height is < 1 or > 2048 || image.Pixels is null
            || (long)image.Width * image.Height != image.Pixels.Length || image.Pixels.Any(pixel =>
                !float.IsFinite(pixel.R) || !float.IsFinite(pixel.G) || !float.IsFinite(pixel.B) || !float.IsFinite(pixel.Alpha)
                || pixel.R is < 0 or > 1 || pixel.G is < 0 or > 1 || pixel.B is < 0 or > 1 || pixel.Alpha is < 0 or > 1))
            throw new InvalidDataException("Cover shape or finite normalized RGBA pixels are invalid.");
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (!_ready || _disposeStarted || _cleanupUnknown) throw new ObjectDisposedException(nameof(LocalPlayBundleInputs));
        _ = _request!.Resources; // Caller disposing the exposed fork cannot leave a seemingly valid CPU view.
    }
    public void Dispose()
    {
        _main.VerifyAccess();
        if (_cleanupUnknown) throw new InvalidOperationException("Unknown LOCAL input request free will not be retried.", _cleanupFailure);
        if (_disposeStarted) return;
        _disposeStarted = true; _ready = false;
        try { _request?.Dispose(); }
        catch (Exception error)
        { _cleanupUnknown = true; _cleanupFailure = error; throw; } // Keep this owner, payloads and process slot.
        _request = null; _selection = null; _descriptor = null; _audio = null; _cover = null; _chart = null;
        lock (ProcessGate) ProcessInputs.Remove(this);
    }
}
