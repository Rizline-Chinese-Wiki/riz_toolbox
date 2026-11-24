using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime.Hooks;

// These six ordinary cases test managed wire layout/structural rejection only.
// The separate RequireNativeLayoutEvidence entry requires a real successful
// native producer receipt; an absent environment variable is never a pass.
internal static class NativeEngineTransportPublicationLayoutTests
{
    internal const string EvidenceEnvironment = "RIZ_ENGINE_TRANSPORT_LAYOUT_JSON";
    private const string NativeHeaderSha256 = "a4d6e011535e8f96fff3eb611ab75cf010fec47fa95fb8ce0b86238b27289c97";
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Managed V5 publication Marshal layout retains all eight records and fixed inline array offsets", ManagedLayout),
        ("Managed V5 publication round-trips full-width addresses and all inline bytes without truncation", FullWireRoundTrip),
        ("Managed V5 publication refuses old schema identities without granting Ready from DTO shape", SchemaIdentity),
        ("Managed V5 publication bounds all family window resource and member mask bits", MaskBounds),
        ("Managed V5 publication refuses wrong counts capacities and malformed inline arrays", CountsAndCapacities),
        ("Managed V5 publication refuses reserved values at the last inline record boundaries", ReservedFields)
    };

    private static void Check(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }

    private static ulong Size<T>() where T : struct => checked((ulong)Marshal.SizeOf<T>());
    private static int At<T>(string member) where T : struct => checked((int)Marshal.OffsetOf<T>(member));
    private static ulong RootAt(string member) => checked((ulong)At<NativeEngineTransportPublicationV5>(member));

    private static Dictionary<string, ulong> ManagedNativeValues() => new() {
        ["version"] = NativeEngineTransportPublicationLayout.Version,
        ["magic"] = NativeEngineTransportPublicationLayout.Magic,
        ["publicationBytes"] = Size<NativeEngineTransportPublicationV5>(), ["alignment"] = 8,
        ["moduleBytes"] = Size<NativeEngineTransportModuleV5>(),
        ["typedBytes"] = Size<NativeEngineTransportTypedRowV5>(),
        ["siteBytes"] = Size<NativeEngineTransportSiteV5>(),
        ["familyBytes"] = Size<NativeEngineTransportFamilyV5>(),
        ["windowBytes"] = Size<NativeEngineTransportWindowV5>(),
        ["runtimeFunctionBytes"] = Size<NativeEngineTransportRuntimeFunctionV5>(),
        ["resourceBytes"] = Size<NativeEngineTransportResourceV5>(),
        ["eventBytes"] = Size<NativeEngineTransportEventV5>(),
        ["modulesOffset"] = RootAt(nameof(NativeEngineTransportPublicationV5.Modules)),
        ["typedOffset"] = RootAt(nameof(NativeEngineTransportPublicationV5.Typed)),
        ["sitesOffset"] = RootAt(nameof(NativeEngineTransportPublicationV5.Sites)),
        ["familiesOffset"] = RootAt(nameof(NativeEngineTransportPublicationV5.Families)),
        ["windowsOffset"] = RootAt(nameof(NativeEngineTransportPublicationV5.Windows)),
        ["runtimeFunctionsOffset"] = RootAt(nameof(NativeEngineTransportPublicationV5.RuntimeFunctions)),
        ["resourcesOffset"] = RootAt(nameof(NativeEngineTransportPublicationV5.Resources)),
        ["eventsOffset"] = RootAt(nameof(NativeEngineTransportPublicationV5.Events)),
        ["moduleCount"] = NativeEngineTransportPublicationLayout.ModuleCount,
        ["typedCount"] = NativeEngineTransportPublicationLayout.TypedCount,
        ["siteCount"] = NativeEngineTransportPublicationLayout.SiteCount,
        ["windowCount"] = NativeEngineTransportPublicationLayout.WindowCount,
        ["familyCount"] = NativeEngineTransportPublicationLayout.FamilyCount
    };

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct AlignmentProbe { internal byte Before; internal NativeEngineTransportPublicationV5 Value; }
    private static void ManagedLayout()
    {
        var actual = ManagedNativeValues();
        var expected = new Dictionary<string, ulong> {
            ["version"] = 5, ["magic"] = 0x3556425550545a52UL, ["publicationBytes"] = 91376, ["alignment"] = 8,
            ["moduleBytes"] = 96, ["typedBytes"] = 192, ["siteBytes"] = 128, ["familyBytes"] = 176,
            ["windowBytes"] = 448, ["runtimeFunctionBytes"] = 112, ["resourceBytes"] = 144, ["eventBytes"] = 48,
            ["modulesOffset"] = 320, ["typedOffset"] = 800, ["sitesOffset"] = 3296, ["familiesOffset"] = 6240,
            ["windowsOffset"] = 8352, ["runtimeFunctionsOffset"] = 24480, ["resourcesOffset"] = 38816, ["eventsOffset"] = 42128,
            ["moduleCount"] = 5, ["typedCount"] = 13, ["siteCount"] = 23, ["windowCount"] = 36, ["familyCount"] = 12
        };
        foreach (var pair in expected) Check(actual[pair.Key] == pair.Value, "Managed wire layout differs: " + pair.Key);
        Check(At<AlignmentProbe>(nameof(AlignmentProbe.Value)) == 8, "The publication must align to eight bytes.");
        Check(At<NativeEngineTransportWindowV5>(nameof(NativeEngineTransportWindowV5.ExpectedBytes)) == 248
            && At<NativeEngineTransportWindowV5>(nameof(NativeEngineTransportWindowV5.ObservedLiteralValue)) == 408
            && At<NativeEngineTransportRuntimeFunctionV5>(nameof(NativeEngineTransportRuntimeFunctionV5.UnwindSha256)) == 80,
            "Nested byte arrays and RF identity fields moved.");
        foreach (var type in WireTypes())
        {
            var layout = type.StructLayoutAttribute;
            Check(layout is not null && layout.Value == LayoutKind.Sequential && layout.Pack == 8,
                "Every wire record requires explicit sequential Pack=8.");
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                var element = field.FieldType.IsArray ? field.FieldType.GetElementType()! : field.FieldType;
                Check(element == typeof(uint) || element == typeof(ulong) || element == typeof(byte) || WireTypes().Contains(element),
                    "A wire field contains a native pointer, bool or unmanaged-width-dependent type: " + field.Name);
                if (field.FieldType.IsArray)
                    Check(field.GetCustomAttribute<MarshalAsAttribute>()?.Value == UnmanagedType.ByValArray,
                        "A wire array cannot escape as a borrowed native pointer.");
            }
        }
    }
    private static Type[] WireTypes() => new[] {
        typeof(NativeEngineTransportModuleV5), typeof(NativeEngineTransportTypedRowV5), typeof(NativeEngineTransportSiteV5),
        typeof(NativeEngineTransportFamilyV5), typeof(NativeEngineTransportWindowV5), typeof(NativeEngineTransportRuntimeFunctionV5),
        typeof(NativeEngineTransportResourceV5), typeof(NativeEngineTransportEventV5), typeof(NativeEngineTransportAdmissionReceiptV5),
        typeof(NativeEngineTransportPublicationV5)
    };

    private static NativeEngineTransportPublicationV5 ReadWire(byte[] bytes)
    {
        Check(bytes.Length == NativeEngineTransportPublicationLayout.ByteSize, "An owned wire copy requires the whole publication.");
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try { Marshal.Copy(bytes, 0, memory, bytes.Length); return Marshal.PtrToStructure<NativeEngineTransportPublicationV5>(memory); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static byte[] WriteWire(in NativeEngineTransportPublicationV5 value)
    {
        var memory = Marshal.AllocHGlobal(NativeEngineTransportPublicationLayout.ByteSize);
        try
        {
            Marshal.StructureToPtr(value, memory, false);
            var bytes = new byte[NativeEngineTransportPublicationLayout.ByteSize]; Marshal.Copy(memory, bytes, 0, bytes.Length);
            Marshal.DestroyStructure<NativeEngineTransportPublicationV5>(memory); return bytes;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static void FullWireRoundTrip()
    {
        var raw = new byte[NativeEngineTransportPublicationLayout.ByteSize];
        for (var index = 0; index < raw.Length; ++index) raw[index] = unchecked((byte)(index * 73 + 19));
        const ulong high = 0xf123456789abcdefUL;
        var slots = new[] { 320 + 4 * 96 + 16, 800 + 12 * 192 + 32, 3296 + 22 * 128 + 64,
            6240 + 11 * 176 + 48, 8352 + 35 * 448 + 80, 24480 + 127 * 112 + 32,
            38816 + 22 * 144 + 16, 42128 + 1023 * 48 };
        foreach (var slot in slots) BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(slot, 8), high);
        var value = ReadWire(raw);
        Check(value.Modules[^1].Module == high && value.Typed[^1].Module == high && value.Sites[^1].SitePc == high
            && value.Families[^1].Epoch == high && value.Windows[^1].Source == high
            && value.RuntimeFunctions[^1].ImageBase == high && value.Resources[^1].AllocationBase == high
            && value.Events[^1].Sequence == high, "A high-bit numeric address/sequence was narrowed or sign-converted.");
        Check(value.Modules[^1].Sha256.AsSpan().SequenceEqual(raw.AsSpan(320 + 4 * 96 + 48, 32))
            && value.Typed[^1].BodySha256.AsSpan().SequenceEqual(raw.AsSpan(800 + 12 * 192 + 128, 32))
            && value.Families[^1].ConfigSha256.AsSpan().SequenceEqual(raw.AsSpan(6240 + 11 * 176 + 144, 32))
            && value.Windows[^1].ExpectedBytes.AsSpan().SequenceEqual(raw.AsSpan(8352 + 35 * 448 + 248, 32))
            && value.RuntimeFunctions[^1].UnwindSha256.AsSpan().SequenceEqual(raw.AsSpan(24480 + 127 * 112 + 80, 32))
            && value.Resources[^1].TableSha256.AsSpan().SequenceEqual(raw.AsSpan(38816 + 22 * 144 + 112, 32)),
            "A fixed 32-byte array at the end of a wire table was truncated or misaligned.");
        var copied = WriteWire(value); Check(raw.AsSpan().SequenceEqual(copied), "The full 91376-byte Marshal round-trip changed wire bytes.");
        raw[0] ^= 0xff;
        Check(value.Version != BinaryPrimitives.ReadUInt32LittleEndian(raw), "The decoded wire must own its copied numeric values.");
    }

    private static NativeEngineTransportPublicationV5 Header()
    {
        var value = ReadWire(new byte[NativeEngineTransportPublicationLayout.ByteSize]);
        value.Version = 5; value.ByteSize = 91376; value.SchemaMagic = NativeEngineTransportPublicationLayout.Magic;
        value.ModuleCount = 5; value.TypedCount = 13; value.SiteCount = 23; value.WindowCount = 36; value.FamilyCount = 12;
        value.EventCapacity = 1024; return value;
    }
    private delegate void Mutation(ref NativeEngineTransportPublicationV5 value);
    private static void RefuseMutation(string reason, Mutation mutate)
    {
        var value = Header(); mutate(ref value);
        Check(!NativeEngineTransportPublicationLayout.ValidateHeader(value), "Malformed publication was accepted: " + reason);
    }
    private static void SchemaIdentity()
    {
        Check(NativeEngineTransportPublicationLayout.ValidateHeader(Header()), "The zero owned wire DTO should be structurally representable.");
        foreach (var version in new uint[] { 0, 1, 2, 4, 6 })
            RefuseMutation("version " + version, (ref NativeEngineTransportPublicationV5 value) => value.Version = version);
        foreach (var size in new uint[] { 0, 4096, 91375, 91377, uint.MaxValue })
            RefuseMutation("size " + size, (ref NativeEngineTransportPublicationV5 value) => value.ByteSize = size);
        RefuseMutation("magic", (ref NativeEngineTransportPublicationV5 value) => value.SchemaMagic ^= 1UL << 63);
        var typedOnly = Header(); typedOnly.State = 4;
        Check(NativeEngineTransportPublicationLayout.ValidateHeader(typedOnly) && typedOnly.Epoch == 0 && typedOnly.TargetModule == 0,
            "The layout validator must remain structural; actual Ready authority requires the future independent owner.");
    }
    private static void MaskBounds()
    {
        var value = Header(); value.ConfiguredFamilies = value.BoundFamilies = value.RetainedFamilies = (1UL << 12) - 1;
        value.PreparedWindows = value.WriteAttemptedWindows = value.CommittedWindows = value.OutstandingProtectionWindows = (1UL << 36) - 1;
        value.RetainedResources = (1UL << 23) - 1;
        value.Families[^1].TypedMask = (1UL << 13) - 1; value.Families[^1].SiteMask = (1UL << 23) - 1;
        value.Families[^1].WindowMask = (1UL << 36) - 1;
        Check(NativeEngineTransportPublicationLayout.MaskBounds(value), "The last valid bit in each table should fit the wire.");
        var mutations = new Mutation[] {
            (ref NativeEngineTransportPublicationV5 x) => x.ConfiguredFamilies = 1UL << 12,
            (ref NativeEngineTransportPublicationV5 x) => x.BoundFamilies = 1UL << 12,
            (ref NativeEngineTransportPublicationV5 x) => x.RetainedFamilies = 1UL << 63,
            (ref NativeEngineTransportPublicationV5 x) => x.PreparedWindows = 1UL << 36,
            (ref NativeEngineTransportPublicationV5 x) => x.WriteAttemptedWindows = 1UL << 63,
            (ref NativeEngineTransportPublicationV5 x) => x.CommittedWindows = 1UL << 36,
            (ref NativeEngineTransportPublicationV5 x) => x.OutstandingProtectionWindows = 1UL << 36,
            (ref NativeEngineTransportPublicationV5 x) => x.RetainedResources = 1UL << 23,
            (ref NativeEngineTransportPublicationV5 x) => x.Families[^1].TypedMask = 1UL << 13,
            (ref NativeEngineTransportPublicationV5 x) => x.Families[^1].SiteMask = 1UL << 23,
            (ref NativeEngineTransportPublicationV5 x) => x.Families[^1].WindowMask = 1UL << 36
        };
        foreach (var mutate in mutations)
        {
            var wrong = Header(); mutate(ref wrong);
            Check(!NativeEngineTransportPublicationLayout.MaskBounds(wrong) && !NativeEngineTransportPublicationLayout.ValidateHeader(wrong),
                "A high mask bit cannot be accepted by either structural entry point.");
        }
    }
    private static void CountsAndCapacities()
    {
        var mutations = new Mutation[] {
            (ref NativeEngineTransportPublicationV5 x) => x.ModuleCount = 4,
            (ref NativeEngineTransportPublicationV5 x) => x.TypedCount = 12,
            (ref NativeEngineTransportPublicationV5 x) => x.SiteCount = 24,
            (ref NativeEngineTransportPublicationV5 x) => x.WindowCount = 13,
            (ref NativeEngineTransportPublicationV5 x) => x.FamilyCount = 13,
            (ref NativeEngineTransportPublicationV5 x) => x.RuntimeFunctionCount = 129,
            (ref NativeEngineTransportPublicationV5 x) => x.ResourceCount = 24,
            (ref NativeEngineTransportPublicationV5 x) => x.EventCapacity = 64,
            (ref NativeEngineTransportPublicationV5 x) => x.EventCount = 1025,
            (ref NativeEngineTransportPublicationV5 x) => x.Modules = new NativeEngineTransportModuleV5[4],
            (ref NativeEngineTransportPublicationV5 x) => x.Typed = null!,
            (ref NativeEngineTransportPublicationV5 x) => x.Sites = new NativeEngineTransportSiteV5[22],
            (ref NativeEngineTransportPublicationV5 x) => x.Families = new NativeEngineTransportFamilyV5[13],
            (ref NativeEngineTransportPublicationV5 x) => x.Windows = new NativeEngineTransportWindowV5[35],
            (ref NativeEngineTransportPublicationV5 x) => x.RuntimeFunctions = new NativeEngineTransportRuntimeFunctionV5[127],
            (ref NativeEngineTransportPublicationV5 x) => x.Resources = new NativeEngineTransportResourceV5[24],
            (ref NativeEngineTransportPublicationV5 x) => x.Events = new NativeEngineTransportEventV5[64],
            (ref NativeEngineTransportPublicationV5 x) => x.FixedContractSha256 = new byte[31],
            (ref NativeEngineTransportPublicationV5 x) => x.Modules[^1].Sha256 = null!,
            (ref NativeEngineTransportPublicationV5 x) => x.Windows[^1].ObservedBytes = new byte[33]
        };
        foreach (var mutate in mutations) RefuseMutation("count or inline capacity", mutate);
        var maximum = Header(); maximum.RuntimeFunctionCount = 128; maximum.ResourceCount = 23; maximum.EventCount = 1024;
        Check(NativeEngineTransportPublicationLayout.ValidateHeader(maximum), "Bounded arrays must include their exact maximum count.");
    }
    private static void ReservedFields()
    {
        var mutations = new Mutation[] {
            (ref NativeEngineTransportPublicationV5 x) => x.Reserved0 = 1,
            (ref NativeEngineTransportPublicationV5 x) => x.Families[^1].Reserved0 = 1,
            (ref NativeEngineTransportPublicationV5 x) => x.Windows[^1].Reserved0 = 1,
            (ref NativeEngineTransportPublicationV5 x) => x.Windows[^1].Reserved1 = 1,
            (ref NativeEngineTransportPublicationV5 x) => x.Windows[^1].Reserved2 = 1,
            (ref NativeEngineTransportPublicationV5 x) => x.Windows[^1].Reserved3 = 1UL << 63,
            (ref NativeEngineTransportPublicationV5 x) => x.RuntimeFunctions[^1].FrameRegisterAndOffset = 0x10000
        };
        foreach (var mutate in mutations) RefuseMutation("reserved boundary field", mutate);
        var validLowBytes = Header(); validLowBytes.RuntimeFunctions[^1].FrameRegisterAndOffset = 0xf005;
        Check(NativeEngineTransportPublicationLayout.ValidateHeader(validLowBytes), "The RF frame register/byte-offset low two bytes are not reserved.");
    }

    // Root registers this as a distinct mandatory evidence CLI mode. A normal
    // six-case pass must not be reported as an executed native/CLR comparison.
    internal static void RequireNativeLayoutEvidence()
    {
        var path = Environment.GetEnvironmentVariable(EvidenceEnvironment);
        Check(!string.IsNullOrWhiteSpace(path), "Native layout evidence is mandatory: set " + EvidenceEnvironment + ".");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path!)); var receipt = document.RootElement;
        Check(receipt.GetProperty("schemaVersion").GetUInt32() == 1
            && receipt.GetProperty("kind").GetString() == "engine-transport-publication-layout-checks"
            && receipt.GetProperty("passed").GetBoolean() && receipt.GetProperty("buildExitCode").GetInt32() == 0
            && receipt.GetProperty("childExitCode").GetInt32() == 0
            && receipt.GetProperty("failure").ValueKind == JsonValueKind.Null,
            "The actual native layout producer did not complete a successful build and run.");
        var child = receipt.GetProperty("child");
        Check(child.GetProperty("schemaVersion").GetUInt32() == 1
            && child.GetProperty("kind").GetString() == "engine-transport-publication-layout"
            && child.GetProperty("passed").GetBoolean(), "The saved child is not a successful actual V5 native layout result.");
        foreach (var pair in ManagedNativeValues())
            Check(child.GetProperty(pair.Key).GetUInt64() == pair.Value, "Actual native/CLR layout differs: " + pair.Key);
        Check(child.GetProperty("selfOwnedOnly").GetBoolean() && !child.GetProperty("installerExecuted").GetBoolean()
            && !child.GetProperty("gameExecuted").GetBoolean() && !child.GetProperty("productionAdmissionVerified").GetBoolean(),
            "A layout-only proof cannot silently acquire installer or production authority.");
        var headerFound = false;
        foreach (var input in receipt.GetProperty("inputs").EnumerateArray())
        {
            var source = input.GetProperty("path").GetString()!;
            if (!string.Equals(Path.GetFileName(source), "EngineTransportPublication.h", StringComparison.OrdinalIgnoreCase)) continue;
            Check(!headerFound && string.Equals(input.GetProperty("sha256").GetString(), NativeHeaderSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))), NativeHeaderSha256, StringComparison.OrdinalIgnoreCase),
                "The native layout producer's header must be the current exact frozen V5 source.");
            headerFound = true;
        }
        Check(headerFound, "The actual producer must identify its compiled V5 header SHA.");
        foreach (var artifact in receipt.GetProperty("files").EnumerateArray())
        {
            var artifactPath = artifact.GetProperty("path").GetString()!; var bytes = File.ReadAllBytes(artifactPath);
            Check((ulong)bytes.LongLength == artifact.GetProperty("bytes").GetUInt64()
                && string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), artifact.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase),
                "A native layout artifact changed after its actual producer receipt.");
        }
        ManagedLayout(); FullWireRoundTrip();
    }
}
