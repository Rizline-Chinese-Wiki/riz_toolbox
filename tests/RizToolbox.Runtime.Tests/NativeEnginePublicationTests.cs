using System.Buffers.Binary;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

// Semantic transport tests are separate from the actual native-owned startup
// fixture. These records never create an owner or authorize any native write.
internal static class NativeEnginePublicationTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Engine publication V4 has an exact immutable 4096-byte layout separate from Kernel ABI2 scalar1", Layout),
        ("Engine publication accepts only complete owned startup shape and refuses unadmitted game profiles", StartupShape),
        ("Engine publication rejects wrong module identities masks flags and retained-file responsibility", ModuleShape),
        ("Engine publication rejects partial four-kind writes protections epochs and typed patch bytes", RowShape),
        ("Engine publication detects every changed immutable publication byte including unused storage", ImmutableIdentity),
        ("Engine publication channel adoption accepts prior calls but rejects attached duplicates and every native fault", KernelIdentity),
        ("Engine production first installation cannot be authorized by an owned or caller-created receipt", ProductionAdmission)
    };
    private static void Check(bool condition, string message = "Engine V4 publication semantic regression failed.")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    { try { action(); } catch (UnsupportedSignatureException) { return; } throw new InvalidOperationException("Expected exact publication refusal."); }
    private static void U(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
    private static void Q(byte[] data, int at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at, 8), value);
    private static byte[] Valid()
    {
        var data = new byte[4096]; U(data, 0, 4); U(data, 4, 4096); U(data, 8, 4); Q(data, 16, 1);
        U(data, 24, 123); U(data, 28, 456); U(data, 32, 1); U(data, 36, 1); U(data, 40, 1); U(data, 44, 1);
        U(data, 48, 15); U(data, 52, 15); U(data, 56, 15); U(data, 60, 4); U(data, 64, 4); U(data, 72, 1); U(data, 120, 31);
        Q(data, 80, 0x50001230); // Opaque native static-startup lpReserved identity, never a managed pointer to dereference.
        var modules = new ulong[] { 0x10000000, 0x20000000, 0x30000000, 0x40000000 };
        for (var i = 0; i < 4; ++i)
        {
            Q(data, 88 + i * 8, modules[i]); var at = 128 + i * 96;
            U(data, at, (uint)i); U(data, at + 4, 0x8664); U(data, at + 8, 0x10000); U(data, at + 12, 0x12345678);
            Q(data, at + 16, modules[i]); Q(data, at + 24, 0x2000); Q(data, at + 32, (ulong)i + 10);
            U(data, at + 40, 7); U(data, at + 44, i == 0 ? 7u : 15u); data.AsSpan(at + 48, 32).Fill((byte)(i + 1));
            Q(data, at + 80, (ulong)i + 0x100); U(data, at + 88, i == 1 ? 0u : 0x1000u); U(data, at + 92, 0x8000);
        }
        for (var i = 0; i < 4; ++i)
        {
            var kind = (NativeEngineNodeKind)i; var prefix = NativeEnginePublication.Prefix(kind); var at = 512 + i * 320;
            U(data, at, 4); U(data, at + 4, 320); U(data, at + 8, (uint)i); U(data, at + 12, (uint)prefix.Length);
            Q(data, at + 16, modules[2]); Q(data, at + 24, modules[2] + (ulong)(0x1000 + i * 0x100));
            Q(data, at + 32, modules[0] + (ulong)(0x1000 + i * 0x100));
            Q(data, at + 40, modules[2] + (ulong)(0x1000 + i * 0x100 + prefix.Length));
            var entry = modules[1] + (ulong)(0x1000 + i * 0x100); Q(data, at + 48, entry); Q(data, at + 56, 1);
            U(data, at + 64, (uint)(0x1000 + i * 0x100)); U(data, at + 68, 0x20); U(data, at + 72, 0x20); U(data, at + 76, 7);
            foreach (var offset in new[] { 80, 84, 88, 96, 100, 104, 108, 112, 116 }) U(data, at + offset, 1);
            U(data, at + 92, (uint)prefix.Length); Q(data, at + 128, (ulong)i + 1); U(data, at + 136, 28);
            data.AsSpan(at + 144, 32).Fill(0x90); prefix.CopyTo(data.AsSpan(at + 144));
            data.AsSpan(at + 144, 32).CopyTo(data.AsSpan(at + 176)); data[at + 176] = 0xff; data[at + 177] = 0x25;
            data.AsSpan(at + 178, 4).Clear(); Q(data, at + 182, entry);
            for (var pad = 14; pad < prefix.Length; ++pad) data[at + 176 + pad] = 0x90;
            data.AsSpan(at + 176, 32).CopyTo(data.AsSpan(at + 208));
            data.AsSpan(at + 240, 32).Fill(3); data.AsSpan(at + 272, 32).Fill(0x31); U(data, at + 304, 1);
        }
        Q(data, 1792, 1); U(data, 1800, 1); U(data, 1804, 4); U(data, 1808, 4);
        return data;
    }
    private static bool Shape(byte[] bytes) => NativeEnginePublication.HasReadyShape(NativeEnginePublication.Decode(bytes), 1);
    private static void Layout()
    {
        var bytes = Valid(); var value = NativeEnginePublication.Decode(bytes);
        Check(NativeEnginePublication.ByteSize == 4096 && NativeEnginePublication.ModuleOffset == 128 && NativeEnginePublication.ModuleBytes == 96
            && NativeEnginePublication.RowOffset == 512 && NativeEnginePublication.RowBytes == 320
            && NativeEnginePublication.EventOffset == 1792 && NativeEnginePublication.EventBytes == 32);
        Check(value.Bytes.Length == 4096 && value.ModuleAt(2).Address == value.TargetModule && value.RowAt(NativeEngineNodeKind.Execute).Kind == 1
            && value.RowAt(NativeEngineNodeKind.Release).BodySha256.SequenceEqual(bytes.AsSpan(512 + 2 * 320 + 272, 32)));
        bytes[0] = 1; Check(value.RecordVersion == 4 && Shape(value.Bytes.ToArray()), "Decode must retain an immutable owned byte copy.");
        Reject(() => NativeEnginePublication.Decode(new byte[4095])); Reject(() => NativeEnginePublication.Decode(new byte[4097]));
        Check(Marshal.SizeOf<NativeEngineNodeInformation>() == 112 && Marshal.SizeOf<NativeEngineNodeFrame>() == 112
            && Marshal.SizeOf<NativeEngineNodeCompletion>() == 32 && value.RecordVersion != 2 && value.RecordVersion != 1);
    }
    private static void StartupShape()
    {
        Check(Shape(Valid()));
        foreach (var pair in new (int Offset, uint Value)[] { (0,1),(0,2),(0,3),(0,5),(4,1600),(4,4095),(8,0),(8,5),(8,6),(12,16),
            (24,0),(28,0),(32,0),(32,2),(36,0),(40,0),(44,0),(44,2),(48,14),(52,7),(56,0),(60,3),(60,5),(64,3),
            (68,1),(72,0),(72,65),(76,1),(120,15),(120,63),(124,1) })
        { var bytes = Valid(); U(bytes, pair.Offset, pair.Value); Check(!Shape(bytes), "Invalid startup field accepted: " + pair.Offset); }
        foreach (var at in new[] { 16, 80, 88, 96, 104, 112 })
        { var bytes = Valid(); Q(bytes, at, 0); Check(!Shape(bytes), "Invalid startup pointer/epoch accepted."); }
        var dynamicLoad = Valid(); Q(dynamicLoad, 80, 0);
        Check(!Shape(dynamicLoad), "A null real lpReserved cannot prove native static startup even when the other flags claim it.");
        var opaqueStartup = Valid(); Q(opaqueStartup, 80, 0xf123456789abcdef);
        Check(Shape(opaqueStartup), "The nonzero startup witness is opaque identity evidence and is never dereferenced by the shape validator.");
        var game = Valid(); U(game, 44, 2);
        Check(!NativeEnginePublication.HasReadyShape(NativeEnginePublication.Decode(game), 2), "A plausible caller receipt must never grant missing game admission.");
    }
    private static void ModuleShape()
    {
        for (var index = 0; index < 4; ++index)
        {
            var at = 128 + index * 96;
            foreach (var (offset, value) in new (int,uint)[] { (0,99),(4,0x14c),(8,0),(12,0),(40,0),(44,0),(44,31),(92,0),(92,0x10000) })
            { var bytes = Valid(); U(bytes, at + offset, value); Check(!Shape(bytes), "Invalid module scalar accepted."); }
            foreach (var offset in new[] { 16,24,32,80 })
            { var bytes = Valid(); Q(bytes, at + offset, 0); Check(!Shape(bytes), "Missing retained module/file responsibility accepted."); }
            var hash = Valid(); hash.AsSpan(at + 48, 32).Clear(); Check(!Shape(hash), "A zero module identity hash was accepted.");
            var entry = Valid(); U(entry, at + 88, index == 1 ? 1u : 0u); Check(!Shape(entry), "Wrong real/no-entry module profile accepted.");
        }
    }
    private static void RowShape()
    {
        for (var index = 0; index < 4; ++index)
        {
            var at = 512 + index * 320;
            foreach (var (offset, value) in new (int,uint)[] { (0,1),(0,2),(4,112),(8,99),(12,14),(64,0),(68,0x40),(72,0x40),(76,3),
                (80,0),(84,0),(88,0),(92,0),(96,0),(100,0),(104,0),(108,0),(112,0),(116,0),(120,1),(124,5),(136,27),(136,32),
                (140,1),(304,0),(304,65),(308,1),(312,1),(316,1) })
            { var bytes = Valid(); U(bytes, at + offset, value); Check(!Shape(bytes), "Incomplete row field accepted: " + offset); }
            foreach (var offset in new[] { 16,24,32,40,48,56,128 })
            { var bytes = Valid(); Q(bytes, at + offset, 0); Check(!Shape(bytes), "Incomplete exact row tuple/epoch accepted."); }
            foreach (var offset in new[] { 144,176,177,178,182,208,240 })
            { var bytes = Valid(); bytes[at + offset] ^= 0x80; Check(!Shape(bytes), "Wrong original/patch/readback/module identity accepted."); }
            var body = Valid(); body.AsSpan(at + 272, 32).Clear(); Check(!Shape(body), "Missing complete-body fingerprint accepted.");
        }
    }
    private static void ImmutableIdentity()
    {
        var bytes = Valid(); var expected = NativeEnginePublication.Decode(bytes);
        Check(expected.SameImmutableRecord(NativeEnginePublication.Decode(bytes)));
        for (var offset = 0; offset < bytes.Length; ++offset)
        {
            var changed = bytes.ToArray(); changed[offset] ^= 1;
            Check(!expected.SameImmutableRecord(NativeEnginePublication.Decode(changed)), "Changed immutable field escaped stable double-read check at " + offset);
        }
        var laterEpoch = bytes.ToArray(); Q(laterEpoch, 512 + 128, 99);
        Check(Shape(laterEpoch) && !expected.SameImmutableRecord(NativeEnginePublication.Decode(laterEpoch)),
            "A valid-looking replacement Kernel epoch still must not revive a previously adopted publication.");
        foreach (var offset in new[] { 1792 + 32, 3839, 3840, 4095 })
        { var changed = bytes.ToArray(); changed[offset] = 1; Check(!Shape(changed), "Unused event/reserved storage was accepted."); }
    }
    private static void KernelIdentity()
    {
        var row = NativeEnginePublication.Decode(Valid()).RowAt(NativeEngineNodeKind.Execute); var id = row.Identity;
        var info = new NativeEngineNodeInformation { Version = 1, Size = 112, Kind = 1, Slot = 28, Target = id.Target,
            Original = id.Original, Entry = id.Entry, Epoch = id.Epoch, Entered = 91, Exited = 90, InFlight = 1 };
        Check(NativeEngineNodeContextBridge.MatchesInformation(info, id, false), "Real early/prior calls must not be mistaken for an unconfigured channel.");
        info.Attached = 1;
        Check(!NativeEngineNodeContextBridge.MatchesInformation(info, id, false) && NativeEngineNodeContextBridge.MatchesInformation(info, id, true),
            "An already-attached actual channel must reject a second managed adoption.");
        foreach (var field in new[] { "Version","Size","Kind","Slot","Unknown","Target","Original","Entry","Epoch","EnterFaults","ExitFaults","SnapshotFaults","OutputFaults" })
        {
            var bad = info;
            switch (field) {
                case "Version": bad.Version=2; break; case "Size": bad.Size=4096; break; case "Kind": bad.Kind=3; break;
                case "Slot": bad.Slot=27; break; case "Unknown": bad.Unknown=1; break; case "Target": bad.Target=new(1); break;
                case "Original": bad.Original=new(1); break; case "Entry": bad.Entry=new(1); break; case "Epoch": bad.Epoch++; break;
                case "EnterFaults": bad.EnterFaults=1; break; case "ExitFaults": bad.ExitFaults=1; break;
                case "SnapshotFaults": bad.SnapshotFaults=1; break; case "OutputFaults": bad.OutputFaults=1; break;
            }
            Check(!NativeEngineNodeContextBridge.MatchesInformation(bad, id, true), "Changed Kernel field accepted: " + field);
        }
    }
    private static void ProductionAdmission()
    {
        Check(!NativeEngineFirstInstall.ProductionStartupAdmissionKnown);
        Reject(() => NativeEngineFirstInstall.RequireReady());
        Check(NativeEngineFirstInstall.ProductionKernelSha256.Length == 64 && NativeEngineFirstInstall.ProductionTargetSha256.Length == 64
            && NativeEngineFirstInstall.ProductionExecutableSha256.Length == 64);
    }
}
