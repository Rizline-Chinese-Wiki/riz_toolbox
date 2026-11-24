using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

/// <summary>Explicit opt-in, original one-note validation data. It never claims native play support.</summary>
internal static class LocalCatalogValidationProvider
{
    internal static void Start(IPluginContext context)
    {
        bool attempted = false; double next = 0;
        context.Events.Subscribe<FrameUpdate>(frame =>
        {
            if (attempted || frame.ElapsedSeconds < next) return; next = frame.ElapsedSeconds + 1;
            var batch = context.GetService<ILocalContentBatchProviderService>(AudioContentCapabilityIds.LocalContentBatch).Service;
            var catalog = context.GetService<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog).Service;
            var storage = context.GetService<IPluginStorageService>(StorageCapabilityIds.Storage).Service;
            if (batch is null || catalog is null || storage is null) return;
            attempted = true; ILocalContentBatchRegistration? group = null;
            var songs = new List<ILocalSongRegistration>();
            try
            {
                Ensure("tone.wav", Wav()); Ensure("cover.png", Png()); Ensure("chart.json", Encoding.UTF8.GetBytes(Chart));
                var published = batch.RegisterBatch(new[] {
                    new LocalContentRequest("validation-audio", LocalContentKind.Audio, "wav.pcm16", 1, new(ContentFileArea.PluginData,"catalog-validation/tone.wav")),
                    new LocalContentRequest("validation-cover", LocalContentKind.Cover, "png.rgba8", 1, new(ContentFileArea.PluginData,"catalog-validation/cover.png")),
                    new LocalContentRequest("validation-chart", LocalContentKind.Chart, "rizline.chart.json", 0, new(ContentFileArea.PluginData,"catalog-validation/chart.json")) });
                // Parser discovery may finish after the content registry. A refused transaction publishes nothing.
                if (!published.Success) { attempted = published.Error?.KnownCode != ErrorCode.OperationUnsupported; throw new InvalidOperationException(published.Error?.Message); }
                group = published.Value!;
                for (var i = 0; i < 2; i++)
                {
                    var request = new LocalSongRequest("validation-song-" + i, "LOCAL validation fixture", "RizToolbox original test data",
                        new(context.PluginId,"validation-audio"), new(context.PluginId,"validation-cover"),
                        new[] { new LocalSongChartRequest("one-note", "One-note CPU validation", new(context.PluginId,"validation-chart")) });
                    var added = catalog.RegisterSong(request);
                    if (!added.Success) throw new InvalidOperationException(added.Error?.Message);
                    songs.Add(added.Value!);
                }
                context.Log.Log(LogLevel.Info, "LocalCatalogFixture PUBLISHED songs=2; actualValidatedAssets=3; nativePlaybackAvailable=false.");
            }
            catch (Exception error)
            {
                foreach (var song in songs) song.Dispose(); group?.Dispose();
                context.Log.Log(LogLevel.Warning, "LocalCatalogFixture REFUSED " + error.Message);
            }
            void Ensure(string name, byte[] bytes)
            {
                var path = "catalog-validation/" + name;
                var list = storage.List(PluginStorageArea.Data);
                if (!list.Success) throw new InvalidOperationException(list.Error?.Message);
                if (list.Value!.Any(x => x.Name == path))
                {
                    var prior = storage.Read(PluginStorageArea.Data,path);
                    if (!prior.Success || !prior.Value!.SequenceEqual(bytes)) throw new InvalidOperationException("Preserving differing validation asset: " + path);
                }
                else
                {
                    var written = storage.Write(PluginStorageArea.Data,path,bytes);
                    if (!written.Success) throw new InvalidOperationException(written.Error?.Message);
                }
            }
        });
    }
    private static byte[] Wav()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 1600); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(1600);
        for (var i = 0; i < 800; i++) writer.Write((short)(300 * Math.Sin(2 * Math.PI * 440 * i / 8000)));
        return stream.ToArray();
    }
    private static byte[] Png()
    {
        using var output = new MemoryStream(); output.Write(new byte[] {137,80,78,71,13,10,26,10});
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header,1); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),1); header[8]=8; header[9]=6;
        Chunk("IHDR",header); using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed,CompressionLevel.Fastest,true)) z.Write(new byte[] {0,80,160,240,255});
        Chunk("IDAT",packed.ToArray()); Chunk("IEND",Array.Empty<byte>()); return output.ToArray();
        void Chunk(string kind,byte[] bytes)
        {
            var b = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(b,bytes.Length); output.Write(b);
            var body = Encoding.ASCII.GetBytes(kind).Concat(bytes).ToArray(); output.Write(body); uint crc=uint.MaxValue;
            foreach(var value in body) { crc^=value; for(var i=0;i<8;i++) crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0); }
            BinaryPrimitives.WriteUInt32BigEndian(b,~crc); output.Write(b);
        }
    }
    private const string Chart = "{\"fileVersion\":0,\"songsName\":\"LOCAL validation fixture\",\"bPM\":120,\"themes\":[{\"colorsList\":[{\"r\":255,\"g\":255,\"b\":255,\"a\":255},{\"r\":255,\"g\":255,\"b\":255,\"a\":255},{\"r\":255,\"g\":255,\"b\":255,\"a\":255}]}],\"lines\":[{\"linePoints\":[{\"time\":0,\"xPosition\":0,\"color\":{\"r\":255,\"g\":255,\"b\":255,\"a\":255},\"easeType\":0,\"canvasIndex\":0,\"floorPosition\":0},{\"time\":2,\"xPosition\":0,\"color\":{\"r\":255,\"g\":255,\"b\":255,\"a\":255},\"easeType\":0,\"canvasIndex\":0,\"floorPosition\":2}],\"notes\":[{\"type\":0,\"time\":1,\"floorPosition\":1,\"otherInformations\":[]}],\"judgeRingColor\":[],\"lineColor\":[]}],\"challengeTimes\":[],\"bpmShifts\":[{\"time\":0,\"value\":1,\"easeType\":0,\"floorPosition\":0}],\"canvasMoves\":[{\"index\":0,\"xPositionKeyPoints\":[{\"time\":0,\"value\":0,\"easeType\":0,\"floorPosition\":0}],\"speedKeyPoints\":[{\"time\":0,\"value\":1,\"easeType\":0,\"floorPosition\":0}]}],\"cameraMove\":{\"scaleKeyPoints\":[{\"time\":0,\"value\":1,\"easeType\":0,\"floorPosition\":0}],\"xPositionKeyPoints\":[{\"time\":0,\"value\":0,\"easeType\":0,\"floorPosition\":0}]}}";
}
