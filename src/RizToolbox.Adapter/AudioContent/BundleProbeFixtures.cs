using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace RizToolbox.Adapter.AudioContent;

// Original fixture data shared in source form with the example validation provider.
internal static class BundleProbeFixtures
{
    internal static byte[] Wav()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 1600); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(1600);
        for (var i = 0; i < 800; i++) writer.Write((short)(300 * Math.Sin(2 * Math.PI * 440 * i / 8000)));
        return stream.ToArray();
    }
    internal static byte[] Png()
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
    internal const string Chart = "{\"fileVersion\":0,\"songsName\":\"LOCAL validation fixture\",\"bPM\":120,\"themes\":[{\"colorsList\":[{\"r\":255,\"g\":255,\"b\":255,\"a\":255},{\"r\":255,\"g\":255,\"b\":255,\"a\":255},{\"r\":255,\"g\":255,\"b\":255,\"a\":255}]}],\"lines\":[{\"linePoints\":[{\"time\":0,\"xPosition\":0,\"color\":{\"r\":255,\"g\":255,\"b\":255,\"a\":255},\"easeType\":0,\"canvasIndex\":0,\"floorPosition\":0},{\"time\":2,\"xPosition\":0,\"color\":{\"r\":255,\"g\":255,\"b\":255,\"a\":255},\"easeType\":0,\"canvasIndex\":0,\"floorPosition\":2}],\"notes\":[{\"type\":0,\"time\":1,\"floorPosition\":1,\"otherInformations\":[]}],\"judgeRingColor\":[],\"lineColor\":[]}],\"challengeTimes\":[],\"bpmShifts\":[{\"time\":0,\"value\":1,\"easeType\":0,\"floorPosition\":0}],\"canvasMoves\":[{\"index\":0,\"xPositionKeyPoints\":[{\"time\":0,\"value\":0,\"easeType\":0,\"floorPosition\":0}],\"speedKeyPoints\":[{\"time\":0,\"value\":1,\"easeType\":0,\"floorPosition\":0}]}],\"cameraMove\":{\"scaleKeyPoints\":[{\"time\":0,\"value\":1,\"easeType\":0,\"floorPosition\":0}],\"xPositionKeyPoints\":[{\"time\":0,\"value\":0,\"easeType\":0,\"floorPosition\":0}]}}";
}
