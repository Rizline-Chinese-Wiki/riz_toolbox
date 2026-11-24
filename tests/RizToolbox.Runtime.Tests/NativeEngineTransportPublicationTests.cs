using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

// Managed immutable-copy/admission-boundary tests only. Actual V5 producer to
// CLR adoption requires its independent owned process and is not signed here.
internal static class NativeEngineTransportPublicationTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("V5 immutable snapshot owns caller bytes and every decoded inline array", SnapshotOwnsCopies),
        ("V5 immutable comparison includes the entire record including final unused event capacity", SnapshotComparesAllBytes),
        ("V5 production remains unavailable even with complete candidate identity data", ProfileCannotGrantAdmission),
        ("V5 bridge factories require the sealed owner rather than a lifetime or mutable DTO", FactoriesRequireOwner),
        ("V5 fixed source windows preserve all thirteen functions and the three expanded blocks", FixedCoverage),
        ("Playback image composition requires the concrete retained V5 owner", PlaybackImageRequiresOwner),
        ("V5 production admission enforces hashes, cold static attach, exclusivity digests, thread identity, and keeps V4 closed", ProductionAdmissionRefusalCounterTests)
    };
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static byte[] Header()
    {
        var bytes = new byte[NativeEngineTransportPublicationLayout.ByteSize];
        void U32(string field, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(checked((int)Marshal.OffsetOf<NativeEngineTransportPublicationV5>(field))), value);
        U32(nameof(NativeEngineTransportPublicationV5.Version), 5); U32(nameof(NativeEngineTransportPublicationV5.ByteSize), checked((uint)bytes.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), NativeEngineTransportPublicationLayout.Magic);
        U32(nameof(NativeEngineTransportPublicationV5.ModuleCount), 5); U32(nameof(NativeEngineTransportPublicationV5.TypedCount), 13);
        U32(nameof(NativeEngineTransportPublicationV5.SiteCount), 23); U32(nameof(NativeEngineTransportPublicationV5.WindowCount), 36);
        U32(nameof(NativeEngineTransportPublicationV5.FamilyCount), 12); U32(nameof(NativeEngineTransportPublicationV5.EventCapacity), 1024);
        return bytes;
    }
    private static void SnapshotOwnsCopies()
    {
        var raw = Header(); var original = raw.ToArray(); var snapshot = NativeEngineTransportPublication.Decode(raw);
        raw[800 + 12 * 192 + 128] = 0xa5;
        Check(snapshot.CopyBytes().AsSpan().SequenceEqual(original), "The input buffer leaked into the immutable snapshot.");
        var value = snapshot.CopyWire(); value.Typed[^1].BodySha256[^1] = 0xff; value.Modules[^1].Sha256[0] = 0xdd;
        value.Resources[^1].TableSha256[0] = 1; value.Windows[^1].PatchBytes[^1] = 2; value.Events[^1].Detail = ulong.MaxValue;
        var next = snapshot.CopyWire();
        Check(next.Typed[^1].BodySha256[^1] == 0 && next.Modules[^1].Sha256[0] == 0 && next.Resources[^1].TableSha256[0] == 0
            && next.Windows[^1].PatchBytes[^1] == 0 && next.Events[^1].Detail == 0, "Mutable DTO arrays escaped as a borrowed owner record.");
        var exported = snapshot.CopyBytes(); exported[^1] = 9;
        Check(snapshot.CopyBytes().AsSpan().SequenceEqual(original), "A returned byte array mutated publication ownership.");
    }
    private static void SnapshotComparesAllBytes()
    {
        var raw = Header(); var first = NativeEngineTransportPublication.Decode(raw);
        raw[^1] = 1; var last = NativeEngineTransportPublication.Decode(raw);
        Check(!first.SameBytes(last), "The final publication byte was omitted from immutable comparison.");
        Check(first.SameBytes(NativeEngineTransportPublication.Decode(first.CopyBytes())), "An exact owned copy differs.");
        Check(first.IsZeroRange(raw.Length - 1, 1) && !last.IsZeroRange(raw.Length - 1, 1), "Unused tail validation cannot observe nonzero capacity.");
        Throws<UnsupportedSignatureException>(() => NativeEngineTransportPublication.Decode(raw.AsSpan(0, raw.Length - 1)));
    }
    private static void ProfileCannotGrantAdmission()
    {
        var rvas = Enumerable.Range(0, 13).Select(i => NativeEngineTransportFixedFacts.Function(i).Begin).ToArray();
        var profile = new NativeEngineTransportProfileData(new string('1', 64), new string('2', 64),
            NativeEngineTransportFixedFacts.FixedTargetSha256, NativeEngineFirstInstall.ProductionExecutableSha256, new string('3', 64), rvas);
        Check(profile.Hash(0) == new string('1', 64) && !NativeEngineTransportFirstInstall.ProductionStartupAdmissionKnown,
            "Static identity data cannot grant a V5 execution window.");
        Throws<UnsupportedSignatureException>(() => NativeEngineTransportFirstInstall.RequireReady());
        rvas[^1]++;
        Throws<ArgumentException>(() => new NativeEngineTransportProfileData(new string('1', 64), new string('2', 64),
            NativeEngineTransportFixedFacts.FixedTargetSha256, NativeEngineFirstInstall.ProductionExecutableSha256, new string('3', 64), rvas));
        Throws<ArgumentException>(() => new NativeEngineTransportProfileData(new string('1', 64), new string('2', 64),
            new string('4', 64), NativeEngineFirstInstall.ProductionExecutableSha256, new string('3', 64), Enumerable.Range(0, 13).Select(i => NativeEngineTransportFixedFacts.Function(i).Begin).ToArray()));
    }
    private static void FactoriesRequireOwner()
    {
        var owner = typeof(NativeEngineTransportFirstInstall);
        Check(owner.IsSealed && owner.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length == 0,
            "The V5 owner must not expose a caller-created constructor.");
        foreach (var bridge in new[] { typeof(NativeEngineNodeContextBridge), typeof(NativeEngineInlineContextBridge),
            typeof(NativeEngineSchedulerContextBridge), typeof(NativeEngineWaitContextBridge) })
        {
            var methods = bridge.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(x => x.Name == "AdoptPublished").ToArray();
            Check(methods.Any(x => x.GetParameters()[0].ParameterType == owner), "A V5 bridge lacks its concrete sealed-owner adoption path.");
            Check(methods.All(x => x.GetParameters().All(p => p.ParameterType != typeof(INativeEnginePublicationLifetime)
                && p.ParameterType != typeof(NativeEngineTransportPublicationV5))), "A lifetime/DTO can substitute for production ownership.");
        }
    }
    private static void FixedCoverage()
    {
        Check(Enumerable.Range(0, 13).Sum(i => (long)NativeEngineTransportFixedFacts.Function(i).RfCount) == 27,
            "The complete source RF families were truncated.");
        var ranges = Enumerable.Range(0, 13).Select(i => (Rva: NativeEngineTransportFixedFacts.Function(i).Begin, Bytes: NativeEngineTransportFixedFacts.Function(i).PrefixBytes))
            .Concat(Enumerable.Range(0, 23).Select(i => (Rva: NativeEngineTransportFixedFacts.Site(i).Physical, Bytes: NativeEngineTransportFixedFacts.Site(i).PhysicalBytes)))
            .OrderBy(x => x.Rva).ToArray();
        Check(ranges.Length == 36 && Enumerable.Range(1, 35).All(i => ranges[i - 1].Rva + ranges[i - 1].Bytes <= ranges[i].Rva),
            "The 36 physical windows overlap.");
        Check(NativeEngineTransportFixedFacts.Site(9).Physical == 0x7938aa && NativeEngineTransportFixedFacts.Site(9).PhysicalBytes == 19
            && NativeEngineTransportFixedFacts.Site(11).Physical == 0x78f6f0 && NativeEngineTransportFixedFacts.Site(11).PhysicalBytes == 12
            && NativeEngineTransportFixedFacts.Site(14).Physical == 0x792522 && NativeEngineTransportFixedFacts.Site(14).PhysicalBytes == 12,
            "A covered-tail block regressed to an unsafe logical-only patch window.");
    }
    private static void PlaybackImageRequiresOwner()
    {
        Check(!NativeEngineTransportFirstInstall.ProductionStartupAdmissionKnown,
            "The managed test process cannot grant game playback transport authority.");
        Check(NativeEngineTransportFirstInstall.RetainPlaybackImage(IntPtr.Zero, new string('1', 64)) is null
            && NativeEngineTransportFirstInstall.RetainPlaybackImage(new IntPtr(1234), new string('1', 64)) is null,
            "An arbitrary module/hash pair cannot manufacture a retained playback producer.");
        var compose = typeof(NativeEngineTransportFirstInstall).GetMethod("ComposeVerifiedPlaybackWindows",
            BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing exact-owner composer.");
        Check(compose.DeclaringType == typeof(NativeEngineTransportFirstInstall) && !compose.IsPublic && !compose.IsStatic
            && compose.GetParameters().All(value => value.ParameterType != typeof(INativeEnginePublicationLifetime)
                && value.ParameterType != typeof(NativeEngineTransportPublicationV5)
                && !typeof(Delegate).IsAssignableFrom(value.ParameterType)),
            "A DTO, lifetime, callback or static helper cannot substitute for the actual independently verified producer.");
    }
    private static void ProductionAdmissionRefusalCounterTests()
    {
        var rvas = Enumerable.Range(0, 13).Select(i => NativeEngineTransportFixedFacts.Function(i).Begin).ToArray();
        var mod0 = new string('a', 64); var mod1 = new string('b', 64); var mod4 = new string('c', 64);
        var admittedProfile = new NativeEngineTransportProfileData(mod0, mod1,
            NativeEngineTransportFixedFacts.FixedTargetSha256, NativeEngineFirstInstall.ProductionExecutableSha256, mod4,
            rvas, true, NativeEngineTransportFixedFacts.FixedContractSha256,
            NativeEngineTransportFixedFacts.StageAInteriorClosureSha256, NativeEngineTransportFixedFacts.StageAColdTraceSha256);
        Check(admittedProfile.ProductionAdmissionAuthorized, "Stage A pinned constructor should record ProductionAdmissionAuthorized.");
        // Non-Rizline process and V4 must still stay closed.
        Check(!NativeEngineTransportFirstInstall.ProductionStartupAdmissionKnown, "Non-Rizline test host must keep V5 RequireReady gate closed.");
        Check(!NativeEngineFirstInstall.ProductionStartupAdmissionKnown, "V4 ProductionStartupAdmissionKnown must stay permanently false.");
        Throws<UnsupportedSignatureException>(() => NativeEngineFirstInstall.RequireReady());
        // Stage A profile hash mismatch refusal.
        Throws<ArgumentException>(() => new NativeEngineTransportProfileData(mod0, mod1,
            NativeEngineTransportFixedFacts.FixedTargetSha256, NativeEngineFirstInstall.ProductionExecutableSha256, mod4,
            rvas, true, new string('0', 64), NativeEngineTransportFixedFacts.StageAInteriorClosureSha256, NativeEngineTransportFixedFacts.StageAColdTraceSha256));
        Throws<ArgumentException>(() => new NativeEngineTransportProfileData(mod0, mod1,
            NativeEngineTransportFixedFacts.FixedTargetSha256, NativeEngineFirstInstall.ProductionExecutableSha256, mod4,
            rvas, true, NativeEngineTransportFixedFacts.FixedContractSha256, new string('0', 64), NativeEngineTransportFixedFacts.StageAColdTraceSha256));
        Throws<ArgumentException>(() => new NativeEngineTransportProfileData(mod0, mod1,
            NativeEngineTransportFixedFacts.FixedTargetSha256, NativeEngineFirstInstall.ProductionExecutableSha256, mod4,
            rvas, true, NativeEngineTransportFixedFacts.FixedContractSha256, NativeEngineTransportFixedFacts.StageAInteriorClosureSha256, new string('0', 64)));

        NativeEngineTransportPublicationV5 BuildSignedWire()
        {
            var w = NativeEngineTransportPublication.Decode(Header()).CopyWire();
            w.Profile = 2; w.MainThreadProof = 2; w.StartupStatic = 1; w.AttachReason = 1;
            w.MainThread = 4242; w.ProducerThread = 4242; w.OwnerThread = 4242;
            w.ConfiguredFamilies = 4095; w.CommittedWindows = NativeEngineTransportPublicationLayout.AllWindows;
            w.FixedContractSha256 = Convert.FromHexString(NativeEngineTransportFixedFacts.FixedContractSha256);
            w.DependencyGraphSha256 = Enumerable.Repeat((byte)0x55, 32).ToArray();
            for (var i = 0; i < 5; ++i) w.Modules[i].Sha256 = Convert.FromHexString(admittedProfile.Hash(i));
            w.Admission.Version = 5; w.Admission.ByteSize = 96; w.Admission.WitnessFlags = 0x3fu;
            w.Admission.EarliestThreadId = 4242; w.Admission.PreWriteThreadCount = 4; w.Admission.SuspendedThreadCount = 3;
            w.Admission.NonMainInteriorRipCount = 0; w.Admission.PostCommitThreadCount = 4;
            Span<byte> authIn = stackalloc byte[304];
            w.FixedContractSha256.CopyTo(authIn);
            Convert.FromHexString(admittedProfile.InteriorClosureSha256).CopyTo(authIn.Slice(32, 32));
            Convert.FromHexString(admittedProfile.ColdTraceSha256).CopyTo(authIn.Slice(64, 32));
            w.DependencyGraphSha256.CopyTo(authIn.Slice(96, 32));
            for (var i = 0; i < 5; ++i) w.Modules[i].Sha256.CopyTo(authIn.Slice(128 + i * 32, 32));
            BinaryPrimitives.WriteUInt32LittleEndian(authIn.Slice(288, 4), w.MainThread);
            BinaryPrimitives.WriteUInt32LittleEndian(authIn.Slice(292, 4), w.MainThreadProof);
            BinaryPrimitives.WriteUInt32LittleEndian(authIn.Slice(296, 4), w.StartupStatic);
            BinaryPrimitives.WriteUInt32LittleEndian(authIn.Slice(300, 4), w.AttachReason);
            w.Admission.AdmissionAuthoritySha256 = SHA256.HashData(authIn);
            Span<byte> exclIn = stackalloc byte[72];
            BinaryPrimitives.WriteUInt32LittleEndian(exclIn, w.Admission.WitnessFlags);
            BinaryPrimitives.WriteUInt32LittleEndian(exclIn.Slice(4, 4), w.Admission.EarliestThreadId);
            BinaryPrimitives.WriteUInt32LittleEndian(exclIn.Slice(8, 4), w.Admission.PreWriteThreadCount);
            BinaryPrimitives.WriteUInt32LittleEndian(exclIn.Slice(12, 4), w.Admission.SuspendedThreadCount);
            BinaryPrimitives.WriteUInt32LittleEndian(exclIn.Slice(16, 4), w.Admission.NonMainInteriorRipCount);
            BinaryPrimitives.WriteUInt32LittleEndian(exclIn.Slice(20, 4), w.Admission.PostCommitThreadCount);
            BinaryPrimitives.WriteUInt64LittleEndian(exclIn.Slice(24, 8), w.CommittedWindows);
            BinaryPrimitives.WriteUInt64LittleEndian(exclIn.Slice(32, 8), w.ConfiguredFamilies);
            w.FixedContractSha256.CopyTo(exclIn.Slice(40, 32));
            w.Admission.ExclusivitySha256 = SHA256.HashData(exclIn);
            return w;
        }
        var valid = BuildSignedWire();
        NativeEngineTransportFirstInstall.VerifyProductionAdmission(in valid, admittedProfile);
        // 1. Missing/unauthorized CLR profile or bad module hash.
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, null); });
        var unauthProfile = new NativeEngineTransportProfileData(mod0, mod1, NativeEngineTransportFixedFacts.FixedTargetSha256, NativeEngineFirstInstall.ProductionExecutableSha256, mod4, rvas);
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, unauthProfile); });
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.Modules[0].Sha256[0] ^= 0xff; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        // 2. Late history (StartupStatic == 0, AttachReason != 1, missing PreWriteClean witness).
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.StartupStatic = 0; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.AttachReason = 2; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.Admission.WitnessFlags &= ~0x08u; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        // 3. Lost exclusivity (NonMainInteriorRipCount > 0, missing ExclusivityHeld/Resumed, tampered exclusivity digest).
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.Admission.NonMainInteriorRipCount = 1; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.Admission.WitnessFlags &= ~0x10u; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.Admission.ExclusivitySha256[0] ^= 0xff; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        // 6. Wrong MainThreadProof / MainThread != ProducerThread / EarliestThreadId != MainThread / tampered AdmissionAuthoritySha256.
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.MainThreadProof = 1; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.ProducerThread = 9999; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.Admission.EarliestThreadId = 9999; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
        Throws<UnsupportedSignatureException>(() => { var w = BuildSignedWire(); w.Admission.AdmissionAuthoritySha256[0] ^= 0xff; NativeEngineTransportFirstInstall.VerifyProductionAdmission(in w, admittedProfile); });
    }
}
