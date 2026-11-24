using System.Buffers.Binary;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Sdk;
using static Assert;

internal static class ContentFileByteLimitRegression
{
    private const int OneMiB = 1024 * 1024;
    private const int SixteenMiB = 16 * OneMiB;

    internal static (string Name, Action Test)[] Cases => new (string, Action)[]
    {
        ("Caller WAV byte limit rejects a replaced larger valid file before backend Create", ReplacedLargerFile),
        ("Caller WAV byte limit accepts its exact byte boundary and rejects one byte below", ExactBoundary),
        ("Invalid content byte limits reject directory approval and audio upload", InvalidLimits),
        ("Content byte limits accept one byte and the default ceiling but reject empty files", InclusiveLimits)
    };

    private static void ReplacedLargerFile()
    {
        using var f = new Fixture();
        using var context = f.Context();
        var playback = f.Play(context);
        var request = f.File with { MaximumBytes = OneMiB };
        using (var small = Need(playback.LoadWav(request))) Check(small.IsValid);
        Equal(1, f.Backend.Created.Count);

        // A previous successful load cannot approve a later, larger file at the same path.
        File.WriteAllBytes(Path.Combine(f.Directory, "tone.wav"), WavOfLength(OneMiB + 2));
        var refused = playback.LoadWav(request);
        Check(!refused.Success);
        Equal(ErrorCode.ContentInvalid, refused.Error!.Code);
        Equal(1, f.Backend.Created.Count);
        Equal(0, f.Backend.Sources.Count);

        // The same larger WAV is valid under the default allowance; format errors
        // cannot account for the restricted request's rejection.
        using var larger = Need(playback.LoadWav(f.File));
        Check(larger.IsValid);
        Equal(2, f.Backend.Created.Count);
    }

    private static void ExactBoundary()
    {
        using var f = new Fixture();
        using var context = f.Context();
        File.WriteAllBytes(Path.Combine(f.Directory, "tone.wav"), WavOfLength(OneMiB));
        var playback = f.Play(context);
        var request = f.File with { MaximumBytes = OneMiB };
        using var loaded = Need(playback.LoadWav(request));
        Check(loaded.IsValid);
        Equal(1, f.Backend.Created.Count);

        var refused = playback.LoadWav(request with { MaximumBytes = OneMiB - 1 });
        Check(!refused.Success);
        Equal(ErrorCode.ContentInvalid, refused.Error!.Code);
        Equal(1, f.Backend.Created.Count);
    }

    private static void InvalidLimits()
    {
        using var f = new Fixture();
        using var context = f.Context();
        var playback = f.Play(context);
        var approvals = 0;
        var files = new ContentFileAccess(_ => { approvals++; return f.Directory; },
            _ => { approvals++; return f.Directory; });
        foreach (var limit in new[] { -1, 0, SixteenMiB + 1, int.MaxValue })
        {
            var request = f.File with { MaximumBytes = limit };
            Throws<InvalidDataException>(() => files.Read(context.PluginId, request, default));
            var refused = playback.LoadWav(request);
            Check(!refused.Success);
            Equal(ErrorCode.ContentInvalid, refused.Error!.Code);
        }
        Equal(0, approvals);
        Equal(0, f.Backend.Created.Count);
    }

    private static void InclusiveLimits()
    {
        using var f = new Fixture();
        var files = new ContentFileAccess(_ => f.Directory, _ => f.Directory);
        Equal(SixteenMiB, f.File.MaximumBytes);
        var ceiling = files.Read("test.a", f.File with { MaximumBytes = SixteenMiB }, default);
        Check(Wav().SequenceEqual(ceiling));

        File.WriteAllBytes(Path.Combine(f.Directory, "one.bin"), new byte[] { 0x5a });
        var minimum = new ContentFile(ContentFileArea.Package, "one.bin") { MaximumBytes = 1 };
        var single = files.Read("test.a", minimum, default);
        Equal(1, single.Length);
        Equal((byte)0x5a, single[0]);

        File.WriteAllBytes(Path.Combine(f.Directory, "empty.bin"), Array.Empty<byte>());
        Throws<InvalidDataException>(() => files.Read("test.a",
            minimum with { RelativePath = "empty.bin" }, default));
    }

    private static byte[] WavOfLength(int length)
    {
        if (length < 46 || (length & 1) != 0) throw new ArgumentOutOfRangeException(nameof(length));
        var bytes = new byte[length];
        Wav().AsSpan(0, 44).CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), length - 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), length - 44);
        return bytes;
    }
}
