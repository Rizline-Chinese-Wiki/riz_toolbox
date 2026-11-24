namespace RizToolbox.Runtime.Hooks;

// Generated only by a final candidate build; this never grants startup permission.
internal static partial class NativeEngineTransportProfile
{
    internal static NativeEngineTransportProfileData? Read()
    { NativeEngineTransportProfileData? value = null; Populate(ref value); return value; }
    static partial void Populate(ref NativeEngineTransportProfileData? value);
}
internal sealed class NativeEngineTransportProfileData
{
    private readonly string[] _hashes;
    internal NativeEngineTransportProfileData(string bootstrapSha256, string kernelSha256,
        string targetSha256, string executableSha256, string doorstopSha256, uint[] functionRvas)
        : this(bootstrapSha256, kernelSha256, targetSha256, executableSha256, doorstopSha256, functionRvas, false, "", "", "")
    {
    }
    internal NativeEngineTransportProfileData(string bootstrapSha256, string kernelSha256,
        string targetSha256, string executableSha256, string doorstopSha256, uint[] functionRvas,
        bool productionAdmissionAuthorized, string fixedContractSha256, string interiorClosureSha256, string coldTraceSha256)
    {
        var hashes = new[] { bootstrapSha256, kernelSha256, targetSha256, executableSha256, doorstopSha256 };
        if (hashes.Any(x => x is null || x.Length != 64 || x.Any(c => !Uri.IsHexDigit(c))))
            throw new ArgumentException("Five independently compiled V5 image fingerprints are required.");
        if (!string.Equals(targetSha256, NativeEngineTransportFixedFacts.FixedTargetSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(executableSha256, "f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The V5 production profile must retain the exact independently fixed game images.");
        ArgumentNullException.ThrowIfNull(functionRvas);
        if (functionRvas.Length != 13 || functionRvas.Where((rva, i) => rva != NativeEngineTransportFixedFacts.Function(i).Begin).Any())
            throw new ArgumentException("V5 requires all thirteen independently fixed function RVAs.");
        if (productionAdmissionAuthorized)
        {
            if (!string.Equals(fixedContractSha256, NativeEngineTransportFixedFacts.FixedContractSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(interiorClosureSha256, NativeEngineTransportFixedFacts.StageAInteriorClosureSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(coldTraceSha256, NativeEngineTransportFixedFacts.StageAColdTraceSha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Production admission requires exact Stage A closure and cold-trace bindings.");
        }
        _hashes = hashes.Select(x => x.ToLowerInvariant()).ToArray();
        ProductionAdmissionAuthorized = productionAdmissionAuthorized
            && string.Equals(fixedContractSha256, NativeEngineTransportFixedFacts.FixedContractSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(interiorClosureSha256, NativeEngineTransportFixedFacts.StageAInteriorClosureSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(coldTraceSha256, NativeEngineTransportFixedFacts.StageAColdTraceSha256, StringComparison.OrdinalIgnoreCase);
        FixedContractSha256 = fixedContractSha256.ToLowerInvariant();
        InteriorClosureSha256 = interiorClosureSha256.ToLowerInvariant();
        ColdTraceSha256 = coldTraceSha256.ToLowerInvariant();
    }
    internal bool ProductionAdmissionAuthorized { get; }
    internal string FixedContractSha256 { get; }
    internal string InteriorClosureSha256 { get; }
    internal string ColdTraceSha256 { get; }
    internal string Hash(int index) => _hashes[index is >= 0 and < 5 ? index : throw new ArgumentOutOfRangeException(nameof(index))];
}
// Metadata from the independently inspected fixed PE. Actual body/prefix/RF/
// unwind bytes are independently re-read from the pinned held image files.
internal static class NativeEngineTransportFixedFacts
{
    internal const string FixedContractSha256 = "b85a2e3e5b7a1db5c807ddabfa7409f0f9bd8f549e7603a06dd70c980b6c4bdb";
    internal const string FixedTargetSha256 = "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1";
    internal const string StageAInteriorClosureSha256 = "49dbe1ab74887767684a211c39c72ba636177fcae34caf5074b9e04fc2664cbe";
    internal const string StageAColdTraceSha256 = "5e4c1ac878ef5912d8c6fda0db7242b6cb1322c3f97cdd1524f4ce1203fa5ae5";
    internal readonly record struct FunctionFact(string Name, uint Begin, uint End, uint PrefixBytes, uint RfCount, string BodySha256);
    internal readonly record struct SiteFact(string Name, uint Family, uint Phase, uint Logical, uint LogicalBytes,
        uint Physical, uint PhysicalBytes, uint LogicalContinuation, uint PhysicalContinuation, uint Branch, bool Expanded);
    private static readonly FunctionFact[] Functions = {
        new("NodeStart", 0x792240, 0x7929bf, 15, 1, "6a24dc40b10797a33529428b7d82821f1cb646a72d9108cccb884bfe2f8d7721"),
        new("NodeExecute", 0x78f840, 0x78fa2e, 18, 1, "1ffd5505fa6c15a9315524ad28f9846b8ba4ef04cbf9cf5c0a807fbd21a997f6"),
        new("NodeRelease", 0x78f680, 0x78f768, 16, 3, "771128974dd1a81b49b7f6a9743a492a752cf30aa42bd95073df111cf159d1a3"),
        new("NodeDetach", 0x793840, 0x793a5b, 15, 1, "942344fdd9c188c5e9b8371d6e03ce8fb8f944bbfcd520cadd3ef5715dc5edf7"),
        new("MRegister", 0x44d630, 0x44d756, 15, 1, "ce57348a38b6eb9041cece746e038dddd5c676721f39c4c2f857669c03b74f20"),
        new("MRemove", 0x447ad0, 0x447c26, 14, 1, "3586c1418372fd581f21df83addb7ec59256794fc52374d0fc55593a732758ff"),
        new("MConsume", 0x447c30, 0x447d60, 14, 1, "400ac94b4e996135f4a5602be15a0bcebf3f123e8f650b4dbb003544b6225abb"),
        new("MPump", 0x447d60, 0x447faf, 15, 3, "cafab343388f190607027203284728c3ce672cfe89115a50257e0faae66e3d7c"),
        new("MDrain", 0x447750, 0x4478ac, 14, 3, "6cdea79f34ee8ddcec81015abe94c299df9732c54327e10cc0ba94040b9f435d"),
        new("MCallback", 0x78f5e0, 0x78f673, 17, 1, "0cb5566bcbbe2510032fe522b586bd1940f621ba1e41ba2c93dd2a67098beafc"),
        new("NYield", 0x78fc50, 0x79014b, 15, 3, "938b7a48664df2e0b0195fa2075f130f285a9498e0ce87693269dc2c6db3696f"),
        new("NDispatch", 0x5a5260, 0x5a5343, 14, 1, "7143eab8a6af6b584a341b1b41857b7ce9f74a4108987508ca803bdab1d11ccf"),
        new("NBatch", 0x5da510, 0x5da6db, 14, 7, "01f8cbd43685808116a8c8e34fe4cf7053de54595988ae8ade76676b351d6657"),
    };
    private static readonly SiteFact[] Sites = {
        new("InlineBegin", 0, 0, 0x7924cd, 7, 0x7924cd, 7, 0x7924d4, 0x7924d4, 0x0, false),
        new("InlineEndFree", 0, 1, 0x79257d, 5, 0x79257d, 5, 0x79299e, 0x79299e, 0x79299e, false),
        new("InlineEndRetained", 0, 2, 0x79259d, 5, 0x79259d, 5, 0x79299e, 0x79299e, 0x79299e, false),
        new("DrainRowBegin", 1, 0, 0x447787, 6, 0x447787, 6, 0x44778d, 0x44778d, 0x4477f1, false),
        new("DrainRowEnd", 1, 1, 0x447866, 5, 0x447866, 5, 0x44786b, 0x44786b, 0x0, false),
        new("PublishBegin", 2, 0, 0x790069, 10, 0x790069, 10, 0x790073, 0x790073, 0x0, false),
        new("OldReferenceDecision", 2, 1, 0x79008d, 6, 0x79008d, 6, 0x790093, 0x790093, 0x790098, false),
        new("OldReferenceReturned", 2, 2, 0x790098, 8, 0x790098, 8, 0x7900a0, 0x7900a0, 0x0, false),
        new("PublishCommitted", 2, 3, 0x7900a0, 5, 0x7900a0, 5, 0x79012e, 0x79012e, 0x79012e, false),
        new("DetachOwnerBegin", 2, 4, 0x7938b3, 10, 0x7938aa, 19, 0x7938bd, 0x7938bd, 0x0, true),
        new("DetachNestedOwnerBegin", 2, 5, 0x793983, 10, 0x793983, 10, 0x79398d, 0x79398d, 0x0, false),
        new("ReleaseClearBegin", 2, 6, 0x78f6f5, 7, 0x78f6f0, 12, 0x78f6fc, 0x78f6fc, 0x0, true),
        new("ReleaseFieldsCleared", 2, 7, 0x78f704, 8, 0x78f704, 8, 0x78f70c, 0x78f70c, 0x0, false),
        new("ReleaseReferenceReturned", 2, 8, 0x78f711, 8, 0x78f711, 8, 0x78f719, 0x78f719, 0x0, false),
        new("StartClearBegin", 2, 9, 0x792527, 7, 0x792522, 12, 0x79252e, 0x79252e, 0x0, true),
        new("StartFieldsCleared", 2, 10, 0x792536, 8, 0x792536, 8, 0x79253e, 0x79253e, 0x0, false),
        new("StartReferenceReturned", 2, 11, 0x792543, 9, 0x792543, 9, 0x79254c, 0x79254c, 0x0, false),
        new("DispatchOwnerResult", 2, 12, 0x5a527c, 5, 0x5a527c, 5, 0x5a5281, 0x5a5281, 0x5a528b, false),
        new("DispatchCallbackArmed", 2, 13, 0x5a5281, 7, 0x5a5281, 7, 0x5a5288, 0x5a5288, 0x0, false),
        new("DispatchCleanupDecision", 2, 14, 0x5a528f, 5, 0x5a528f, 5, 0x5a5294, 0x5a5294, 0x5a529a, false),
        new("DispatchClearReached", 2, 15, 0x5a529a, 8, 0x5a529a, 8, 0x5a52a2, 0x5a52a2, 0x0, false),
        new("BatchObjectBegin", 2, 16, 0x5da63b, 6, 0x5da63b, 6, 0x5da641, 0x5da641, 0x5da64c, false),
        new("BatchObjectEnd", 2, 17, 0x5da660, 10, 0x5da660, 10, 0x5da66a, 0x5da66a, 0x0, false),
    };
    internal static FunctionFact Function(int index) => Functions[index is >= 0 and < 13 ? index : throw new ArgumentOutOfRangeException(nameof(index))];
    internal static SiteFact Site(int index) => Sites[index is >= 0 and < 23 ? index : throw new ArgumentOutOfRangeException(nameof(index))];
    internal static int TypedFamily(int typed) => typed < 4 ? typed : typed < 10 ? typed + 1 : 11;
    internal static uint TypedKind(int typed) => checked((uint)(typed < 4 ? typed : typed < 10 ? typed - 4 : typed - 10));
    internal static int SiteFunction(int site) => site < 3 ? 0 : site < 5 ? 8 : site < 9 ? 10 : site < 11 ? 3 : site < 14 ? 2 : site < 17 ? 0 : site < 21 ? 11 : 12;
    internal static int SiteFamily(int site) => site < 3 ? 4 : site < 5 ? 9 : 11;
    internal static uint BypassRva(int site) => site == 9 ? 0x7938b6u : site == 11 ? 0x78f715u : site == 14 ? 0x792547u : 0;
    internal static uint BypassBytes(int site) => site == 9 ? 7u : site == 11 ? 4u : site == 14 ? 5u : 0;
    internal static uint BypassContinuation(int site) => site == 9 ? 0x7938bdu : site == 11 ? 0x78f719u : site == 14 ? 0x79254cu : 0;
}
