using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeEngineTransportFirstInstall
{
    private void VerifyAbi()
    {
        uint Scalar(string name) => Export<ScalarNative>(_modules[1], name)();
        Require(Scalar("RizEngineNodeContext_GetAbiVersion") == 2 && Scalar("RizEngineNodeContext_GetLimit") == 32
            && Scalar("RizEngineNodeContext_GetFrameSize") == 112 && Scalar("RizEngineNodeContext_GetCompletionSize") == 32
            && Scalar("RizEngineNodeContext_GetInlineAbiVersion") == 1 && Scalar("RizEngineNodeContext_GetInlineLimit") == 32
            && Scalar("RizEngineNodeContext_GetInlineFrameSize") == 144
            && Scalar("RizEngineSchedulerContext_GetAbiVersion") == 1 && Scalar("RizEngineSchedulerContext_GetLimit") == 32
            && Scalar("RizEngineSchedulerContext_GetFrameSize") == 256 && Scalar("RizEngineSchedulerContext_GetCompletionSize") == 48
            && Scalar("RizEngineSchedulerContext_GetRowFrameSize") == 224
            && Scalar("RizEngineWaitContext_GetAbiVersion") == 1 && Scalar("RizEngineWaitContext_GetLimit") == 32
            && Scalar("RizEngineWaitContext_GetFrameSize") == 240 && Scalar("RizEngineWaitContext_GetCompletionSize") == 48
            && Scalar("RizEngineWaitContext_GetPhaseFrameSize") == 288
            && Marshal.SizeOf<NativeEngineNodeFrame>() == 112 && Marshal.SizeOf<NativeEngineNodeCompletion>() == 32 && Marshal.SizeOf<NativeEngineNodeInformation>() == 112
            && Marshal.SizeOf<NativeEngineInlineFrame>() == 144 && Marshal.SizeOf<NativeEngineInlineConfiguration>() == 88 && Marshal.SizeOf<NativeEngineInlineInformation>() == 160
            && Marshal.SizeOf<NativeEngineSchedulerFrame>() == 256 && Marshal.SizeOf<NativeEngineSchedulerCompletion>() == 48
            && Marshal.SizeOf<NativeEngineSchedulerRowFrame>() == 224 && Marshal.SizeOf<NativeEngineSchedulerConfiguration>() == 64 && Marshal.SizeOf<NativeEngineSchedulerInformation>() == 144
            && Marshal.SizeOf<NativeEngineWaitFrame>() == 240 && Marshal.SizeOf<NativeEngineWaitCompletion>() == 48
            && Marshal.SizeOf<NativeEngineWaitPhaseFrame>() == 288 && Marshal.SizeOf<NativeEngineWaitConfiguration>() == 432 && Marshal.SizeOf<NativeEngineWaitInformation>() == 176,
            "The actual V5 Kernel scalar ABI or managed wire layout differs.");
    }
    private void VerifyShape()
    {
        var p = _wire;
        Require(NativeEngineTransportPublicationLayout.ValidateHeader(p) && p.State == 4 && p.Failure == 0
            && p.Epoch is > 0 and <= long.MaxValue && p.SnapshotSequence != 0 && p.Claimed == 1
            && p.ProcessId == unchecked((uint)Environment.ProcessId) && p.OwnerThread != 0 && p.ProducerThread != 0
            && p.MainThread != 0 && p.MainThreadProof == _expectedProfile && p.Profile == _expectedProfile
            && p.AttachReason == 1 && p.StartupStatic == 1 && p.VerifyFailures == 0 && p.LastWin32Error == 0
            && p.SourceWriteAttempts == 36 && p.SourceWriteCompletions == 36 && p.ProtectionOutstanding == 0
            && p.ConfiguredFamilies == 4095 && p.BoundFamilies == 4095 && p.RetainedFamilies == 4095
            && p.PreparedWindows == NativeEngineTransportPublicationLayout.AllWindows
            && p.WriteAttemptedWindows == NativeEngineTransportPublicationLayout.AllWindows
            && p.CommittedWindows == NativeEngineTransportPublicationLayout.AllWindows
            && p.OutstandingProtectionWindows == 0 && p.ResourceCount == 1 && p.RetainedResources == 1
            && p.RuntimeFunctionCount == 67 && p.EventCount is > 0 and <= 1024 && p.EventOverflow == 0
            && p.FailureWindowIndex == uint.MaxValue && p.FailureFamilyIndex == uint.MaxValue && p.FailureResourceIndex == uint.MaxValue,
            "The actual V5 record is not a complete retained 12-family/36-window publication.");
        Require(p.FixedContractSha256.AsSpan().SequenceEqual(Convert.FromHexString(NativeEngineTransportFixedFacts.FixedContractSha256)),
            "V5 fixed-contract identity differs from independently compiled facts.");
        var roots = new[] { p.BootstrapModule, p.KernelModule, p.TargetModule, p.ExecutableModule, p.DoorstopModule };
        Require(roots.Distinct().Count() == 5 && roots.Select((value, i) => value == Address(_modules[i].Handle)).All(x => x),
            "V5 module roles differ from the five independent already-loaded images.");
        Require(_publication.IsZeroRange(NativeEngineTransportPublicationLayout.RuntimeFunctionOffset + checked((int)p.RuntimeFunctionCount * 112),
                (128 - checked((int)p.RuntimeFunctionCount)) * 112)
            && _publication.IsZeroRange(NativeEngineTransportPublicationLayout.ResourceOffset + 144, 22 * 144)
            && _publication.IsZeroRange(NativeEngineTransportPublicationLayout.EventOffset + checked((int)p.EventCount * 48),
                (1024 - checked((int)p.EventCount)) * 48), "Unused V5 record capacity is not zero.");
        for (var i = 0; i < p.EventCount; ++i)
        {
            var value = p.Events[i];
            Require(value.Sequence == checked((ulong)i + 1) && value.Stage is >= 1 and <= 23
                && value.ObjectKind <= 4 && value.Failure == 0, "V5 successful event history is incomplete or contains a failure.");
        }
        Require(p.Events[checked((int)p.EventCount - 1)].Stage == 19
            && p.Events[checked((int)p.EventCount - 1)].State == 4, "V5 has no actual terminal Published event.");
        if (_expectedProfile == 2)
            VerifyProductionAdmission(in p, NativeEngineTransportProfile.Read());
    }
    internal static void VerifyProductionAdmission(in NativeEngineTransportPublicationV5 p, NativeEngineTransportProfileData? profile)
    {
        Require(profile is not null && profile.ProductionAdmissionAuthorized,
            "Production V5 admission requires an authorized compiled NativeEngineTransportProfile.");
        Require(string.Equals(profile.FixedContractSha256, NativeEngineTransportFixedFacts.FixedContractSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.InteriorClosureSha256, NativeEngineTransportFixedFacts.StageAInteriorClosureSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.ColdTraceSha256, NativeEngineTransportFixedFacts.StageAColdTraceSha256, StringComparison.OrdinalIgnoreCase),
            "Production V5 CLR profile lacks verified Stage A closure and cold-trace bindings.");
        Require(p.Profile == 2 && p.MainThreadProof == 2 && p.StartupStatic == 1 && p.AttachReason == 1
            && p.MainThread != 0 && p.MainThread == p.ProducerThread && p.MainThread == p.OwnerThread,
            "Production V5 requires cold static attach and mainThread == producerThread == ownerThread.");
        Require(p.Admission.Version == 5 && p.Admission.ByteSize == NativeEngineTransportPublicationLayout.AdmissionByteSize
            && (p.Admission.WitnessFlags & 0x3Fu) == 0x3Fu && p.Admission.EarliestThreadId == p.MainThread
            && p.Admission.PreWriteThreadCount >= 1 && p.Admission.NonMainInteriorRipCount == 0
            && p.Admission.PostCommitThreadCount >= 1,
            "Production V5 admission receipt lacks complete exclusivity or earliest-thread witness.");
        Require(p.Admission.AdmissionAuthoritySha256 is { Length: 32 } && p.Admission.ExclusivitySha256 is { Length: 32 },
            "Production V5 admission digests are missing.");
        for (var i = 0; i < 5; ++i)
            Require(p.Modules[i].Sha256.AsSpan().SequenceEqual(Convert.FromHexString(profile.Hash(i))),
                "Production V5 live module digest differs from pinned CLR profile.");
        Span<byte> authorityInput = stackalloc byte[304];
        p.FixedContractSha256.CopyTo(authorityInput);
        Convert.FromHexString(profile.InteriorClosureSha256).CopyTo(authorityInput.Slice(32, 32));
        Convert.FromHexString(profile.ColdTraceSha256).CopyTo(authorityInput.Slice(64, 32));
        p.DependencyGraphSha256.CopyTo(authorityInput.Slice(96, 32));
        for (var i = 0; i < 5; ++i) p.Modules[i].Sha256.CopyTo(authorityInput.Slice(128 + i * 32, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(authorityInput.Slice(288, 4), p.MainThread);
        BinaryPrimitives.WriteUInt32LittleEndian(authorityInput.Slice(292, 4), p.MainThreadProof);
        BinaryPrimitives.WriteUInt32LittleEndian(authorityInput.Slice(296, 4), p.StartupStatic);
        BinaryPrimitives.WriteUInt32LittleEndian(authorityInput.Slice(300, 4), p.AttachReason);
        var expectedAuthority = SHA256.HashData(authorityInput);
        Require(p.Admission.AdmissionAuthoritySha256.AsSpan().SequenceEqual(expectedAuthority),
            "Production V5 admission authority digest mismatch.");
        Span<byte> exclInput = stackalloc byte[72];
        BinaryPrimitives.WriteUInt32LittleEndian(exclInput, p.Admission.WitnessFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(exclInput.Slice(4, 4), p.Admission.EarliestThreadId);
        BinaryPrimitives.WriteUInt32LittleEndian(exclInput.Slice(8, 4), p.Admission.PreWriteThreadCount);
        BinaryPrimitives.WriteUInt32LittleEndian(exclInput.Slice(12, 4), p.Admission.SuspendedThreadCount);
        BinaryPrimitives.WriteUInt32LittleEndian(exclInput.Slice(16, 4), p.Admission.NonMainInteriorRipCount);
        BinaryPrimitives.WriteUInt32LittleEndian(exclInput.Slice(20, 4), p.Admission.PostCommitThreadCount);
        BinaryPrimitives.WriteUInt64LittleEndian(exclInput.Slice(24, 8), p.CommittedWindows);
        BinaryPrimitives.WriteUInt64LittleEndian(exclInput.Slice(32, 8), p.ConfiguredFamilies);
        p.FixedContractSha256.CopyTo(exclInput.Slice(40, 32));
        var expectedExclusivity = SHA256.HashData(exclInput);
        Require(p.Admission.ExclusivitySha256.AsSpan().SequenceEqual(expectedExclusivity),
            "Production V5 exclusivity digest mismatch.");
    }
    private void VerifyModulesAndGraph()
    {
        for (var i = 0; i < 5; ++i)
        {
            var row = _wire.Modules[i]; var module = _modules[i];
            Require(row.Kind == i && row.Module == Address(module.Handle) && row.Machine == module.Machine
                && row.ImageSize == module.ImageSize && row.TimeDateStamp == module.Timestamp && row.FileBytes == (ulong)module.Data.Length
                && row.FileIndex == module.FileIndex && row.VolumeSerial == module.VolumeSerial && row.Flags == (i == 0 ? 7u : 15u)
                && row.EntryRva == module.EntryRva && row.ExceptionRva == module.ExceptionRva && row.HeldFile != 0
                && row.Sha256.AsSpan().SequenceEqual(Convert.FromHexString(module.Hash)), "A V5 module row differs from independent held-file/image facts.");
            Require(GetFileInformationByHandle(Pointer(row.HeldFile), out var held) && held.Volume == module.VolumeSerial
                && (((ulong)held.IndexHigh << 32) | held.IndexLow) == module.FileIndex
                && (((ulong)held.SizeHigh << 32) | held.SizeLow) == (ulong)module.Data.Length,
                "The producer's retained native file handle has a different actual file identity.");
        }
        if (_expectedProfile == 1)
        {
            var markerRva = _modules[2].ExportRva("RizEngineTransportOwnedMarker", 0x02);
            var expected = Encoding.ASCII.GetBytes("RIZTRNSPOWNEDV1\0");
            Require(expected.Length == 16 && _modules[2].Disk(markerRva, 16).AsSpan().SequenceEqual(expected)
                && ReadMemory(Pointer(TargetModule + markerRva), 16, _modules[2].Handle, 0x02).AsSpan().SequenceEqual(expected),
                "The owned V5 target lacks its exact immutable file-backed marker.");
        }
        if (TryVerifyActivityImportGraph()) return;
        using var payload = new MemoryStream();
        void Group(int source, int destination, IReadOnlyList<string> names)
        {
            var edges = _modules[source].VerifyImportsTo(_modules[destination], names);
            foreach (var name in names)
            {
                var edge = edges[name]; var row = new byte[20];
                BinaryPrimitives.WriteUInt32LittleEndian(row, checked((uint)source)); BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4), checked((uint)destination));
                BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(8), edge.Lookup); BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(12), edge.Iat);
                BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(16), edge.Export); payload.Write(row); payload.Write(Encoding.ASCII.GetBytes(name + '\0'));
            }
        }
        Group(3, 2, new[] { "UnityMain2" });
        Group(2, 4, new[] { "WinHttpOpen", "WinHttpGetProxyForUrl", "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle" });
        Group(4, 0, PublicationExports); Group(0, 1, KernelExports);
        Require(_wire.DependencyGraphSha256.AsSpan().SequenceEqual(SHA256.HashData(payload.ToArray())),
            "The V5 graph digest differs from independently resolved 44 ordinary ILT/IAT/EAT edges.");
    }
    private int SourceRfIndex(int functionIndex, uint rva)
    {
        var first = 0; for (var i = 0; i < functionIndex; ++i) first += _sourceFunctions[i].Count;
        var local = _sourceFunctions[functionIndex].FindIndex(x => rva >= x.Begin && rva < x.End);
        if (local < 0) throw new UnsupportedSignatureException("A fixed source window has no exact static runtime function.");
        return first + local;
    }
    private void VerifyTypedAndCoverage()
    {
        var sourceFirst = 0; var source = _modules[2]; var originals = new HashSet<ulong>(); var entries = new HashSet<ulong>();
        for (var i = 0; i < 13; ++i)
        {
            var fact = NativeEngineTransportFixedFacts.Function(i); var row = _wire.Typed[i]; var family = _wire.Families[NativeEngineTransportFixedFacts.TypedFamily(i)];
            Require(row.Version == 5 && row.ByteSize == 192 && row.Index == i && row.ChannelKind == NativeEngineTransportFixedFacts.TypedKind(i)
                && row.FamilyIndex == NativeEngineTransportFixedFacts.TypedFamily(i) && row.ModuleIndex == 2 && row.Module == TargetModule
                && row.WindowIndex == i && row.Flags == 7 && row.Rva == fact.Begin && row.EndRva == fact.End
                && row.PrefixBytes == fact.PrefixBytes && row.SourceBodyBytes == fact.End - fact.Begin
                && row.Target == TargetModule + fact.Begin && row.Continuation == row.Target + fact.PrefixBytes
                && row.SourceRfFirst == sourceFirst && row.SourceRfCount == fact.RfCount && _sourceFunctions[i].Count == fact.RfCount
                && row.OriginalRfFirst == 27 + i && row.OriginalRfCount == 1 && row.OriginalModuleIndex == 0
                && row.OriginalBytes == fact.PrefixBytes + 10 && row.ScratchRegister == (i == 9 ? 10u : 11u)
                && row.HelperSlot == 28 && row.HelperSlot == family.Slot && row.HelperEpoch == family.Epoch
                && originals.Add(row.Original) && entries.Add(row.Entry), "A V5 typed row differs from the complete fixed original/source contract.");
            sourceFirst += _sourceFunctions[i].Count;
            var body = source.Disk(fact.Begin, checked((int)(fact.End - fact.Begin)));
            Require(row.BodySha256.AsSpan().SequenceEqual(SHA256.HashData(body))
                && row.BodySha256.AsSpan().SequenceEqual(Convert.FromHexString(fact.BodySha256)), "The held fixed function body hash differs.");
            var originalRva = checked((uint)(row.Original - Address(_modules[0].Handle)));
            var code = _modules[0].Disk(originalRva, checked((int)row.OriginalBytes));
            Require(ReadMemory(Pointer(row.Original), code.Length, _modules[0].Handle, 0x20).AsSpan().SequenceEqual(code)
                && code.AsSpan(0, checked((int)fact.PrefixBytes)).SequenceEqual(body.AsSpan(0, checked((int)fact.PrefixBytes)))
                && row.OriginalCodeSha256.AsSpan().SequenceEqual(SHA256.HashData(code)), "The actual fixed original code differs from held prefix/tail bytes.");
            var tail = code.AsSpan(checked((int)fact.PrefixBytes), 10); var scratch = i == 9 ? (byte)0x15 : (byte)0x1d;
            Require(tail[0] == 0x4c && tail[1] == 0x8b && tail[2] == scratch && tail[7] == 0x41 && tail[8] == 0xff
                && tail[9] == (i == 9 ? 0xe2 : 0xe3), "A V5 original uses a different scratch register or epilog-shaped transfer.");
            var slot = checked((ulong)(checked((long)row.Original + fact.PrefixBytes + 7) + BinaryPrimitives.ReadInt32LittleEndian(tail.Slice(3))));
            Require(slot % 8 == 0 && BinaryPrimitives.ReadUInt64LittleEndian(ReadMemory(Pointer(slot), 8, _modules[0].Handle, 0)) == row.Continuation,
                "The original's actual private continuation slot changed.");
            var windows = Enumerable.Range(0, 36).Where(w => _wire.Windows[w].TypedIndex == i).OrderBy(w => _wire.Windows[w].PhysicalRva).ToArray();
            uint previousEnd = fact.Begin;
            foreach (var index in windows)
            {
                var window = _wire.Windows[index];
                Require(window.PhysicalRva >= previousEnd && window.PhysicalRva >= fact.Begin
                    && window.PhysicalRva <= fact.End && window.PhysicalBytes <= fact.End - window.PhysicalRva,
                    "V5 source windows overlap or leave their exact whole function.");
                VerifyWindow(index); window.PatchBytes.AsSpan(0, checked((int)window.PhysicalBytes)).CopyTo(body.AsSpan(checked((int)(window.PhysicalRva - fact.Begin))));
                previousEnd = checked(window.PhysicalRva + window.PhysicalBytes);
            }
            Require(windows.Length == 1 + Enumerable.Range(0, 23).Count(s => NativeEngineTransportFixedFacts.SiteFunction(s) == i)
                && ReadMemory(Pointer(row.Target), body.Length, source.Handle, 0x20).AsSpan().SequenceEqual(body),
                "V5 actual full function differs outside the complete nonoverlapping 36-window patch overlay.");
        }
        Require(sourceFirst == 27, "V5 must retain all 27 source runtime functions.");
    }
    private void VerifyWindow(int index)
    {
        var row = _wire.Windows[index]; var side = index >= 13; var siteIndex = index - 13;
        var typedIndex = side ? NativeEngineTransportFixedFacts.SiteFunction(siteIndex) : index;
        var fact = NativeEngineTransportFixedFacts.Function(typedIndex);
        var rva = side ? NativeEngineTransportFixedFacts.Site(siteIndex).Physical : fact.Begin;
        var bytes = side ? NativeEngineTransportFixedFacts.Site(siteIndex).PhysicalBytes : fact.PrefixBytes;
        var family = side ? NativeEngineTransportFixedFacts.SiteFamily(siteIndex) : NativeEngineTransportFixedFacts.TypedFamily(index);
        var expanded = side && NativeEngineTransportFixedFacts.Site(siteIndex).Expanded;
        Require(row.Version == 5 && row.ByteSize == 448 && row.Index == index && row.Role == (side ? 1u : 0u)
            && row.TypedIndex == typedIndex && row.SiteIndex == (side ? (uint)siteIndex : uint.MaxValue) && row.FamilyIndex == family
            && row.ModuleIndex == 2 && row.SourceRfIndex == SourceRfIndex(typedIndex, rva) && row.ResourceIndex == (side ? 0u : uint.MaxValue)
            && row.PhysicalRva == rva && row.PhysicalBytes == bytes && row.Source == TargetModule + rva
            && row.LogicalRva == (side ? NativeEngineTransportFixedFacts.Site(siteIndex).Logical : fact.Begin)
            && row.LogicalBytes == (side ? NativeEngineTransportFixedFacts.Site(siteIndex).LogicalBytes : fact.PrefixBytes)
            && row.Flags == (expanded ? 255u : 63u) && row.OriginalProtection == 0x20 && row.ObservedProtection == 0x20
            && row.ProtectAttempts == 1 && row.ProtectSucceeded == 1 && row.SourceWriteAttempts == 1 && row.SourceBytesWritten == bytes
            && row.ReadbackAttempts == 1 && row.ReadbackVerified == 1 && row.ReadbackBytes == bytes
            && row.RestoreAttempts == 1 && row.RestoreSucceeded == 1 && row.FlushAttempts == 1 && row.FlushSucceeded == 1
            && row.ProtectionOutstanding == 0 && row.LastWin32Error == 0 && row.FailureStage == 0
            && row.WriteSequence > 0 && row.ReadbackSequence > row.WriteSequence && row.CommitSequence > row.ReadbackSequence,
            "A V5 window lacks exact physical ownership or completed write/restore/flush responsibility.");
        var page = checked((ulong)Environment.SystemPageSize); var protectionBase = row.Source & ~(page - 1);
        Require(row.ProtectionBase == protectionBase && row.ProtectionBytes == ((row.Source + bytes - 1) & ~(page - 1)) + page - protectionBase,
            "V5 source protection obligation differs from actual page-rounded memory.");
        var expected = _modules[2].Disk(rva, checked((int)bytes));
        Require(row.ExpectedBytes.AsSpan(0, expected.Length).SequenceEqual(expected)
            && row.SourceWindowSha256.AsSpan().SequenceEqual(SHA256.HashData(expected))
            && IsZero(row.ExpectedBytes.AsSpan(expected.Length)) && IsZero(row.PatchBytes.AsSpan(expected.Length)) && IsZero(row.ObservedBytes.AsSpan(expected.Length))
            && row.ObservedBytes.AsSpan(0, expected.Length).SequenceEqual(row.PatchBytes.AsSpan(0, expected.Length))
            && ReadMemory(Pointer(row.Source), expected.Length, _modules[2].Handle, 0x20).AsSpan().SequenceEqual(row.PatchBytes.AsSpan(0, expected.Length)),
            "A V5 source window differs from independent original/readback bytes.");
        if (!side)
        {
            Require(row.PatchBytes[0] == 0xff && row.PatchBytes[1] == 0x25 && BinaryPrimitives.ReadUInt32LittleEndian(row.PatchBytes.AsSpan(2)) == 0
                && BinaryPrimitives.ReadUInt64LittleEndian(row.PatchBytes.AsSpan(6)) == _wire.Typed[index].Entry
                && row.PatchTarget == _wire.Typed[index].Entry && row.RelayRfFirst == 0 && row.RelayRfCount == 0
                && row.Relay == 0 && row.RelayReturnPc == 0 && row.ProbeEntry == 0 && row.LiteralAddress == 0 && row.RelayBytes == 0 && row.LiteralBytes == 0,
                "A V5 typed window is not its exact FF25 Kernel entry binding.");
            Require(row.PatchBytes.AsSpan(14, checked((int)bytes - 14)).ToArray().All(x => x == 0x90), "A typed patch has non-NOP padding.");
        }
        else
        {
            var site = _wire.Sites[siteIndex];
            Require(row.PatchBytes[0] == 0xe9 && RelativeTarget(row.Source + 5, row.PatchBytes.AsSpan(1)) == site.Relay
                && row.PatchTarget == site.Relay && row.Relay == site.Relay && row.RelayReturnPc == site.RelayReturnPc
                && row.Continuation == TargetModule + site.PhysicalContinuationRva && row.AlternateContinuation == site.AlternateContinuation
                && row.ProbeEntry == site.ProbeEntry && row.LiteralBytes == 8 && row.LiteralValue == site.ProbeEntry && row.ObservedLiteralValue == site.ProbeEntry
                && row.RelayRfFirst == 40 + siteIndex && row.RelayRfCount == 1
                && row.RelayOriginalProtection == 4 && row.RelayObservedProtection == 0x20,
                "A V5 side patch is not its exact relay/probe/continuation identity.");
            Require(row.PatchBytes.AsSpan(5, checked((int)bytes - 5)).ToArray().All(x => x == 0x90), "A side patch has non-NOP padding.");
        }
    }
    private static ulong RelativeTarget(ulong next, ReadOnlySpan<byte> encoded)
        => checked((ulong)(checked((long)next) + BinaryPrimitives.ReadInt32LittleEndian(encoded)));
    private static void PrivateMemory(ulong address, int bytes, ulong allocation, uint protection)
    {
        if (address == 0 || bytes <= 0 || address > ulong.MaxValue - checked((ulong)bytes))
            throw new UnsupportedSignatureException("Invalid bounded V5 near-memory range.");
        var end = address + checked((ulong)bytes);
        for (var at = address; at < end;)
        {
            Require(VirtualQuery(Pointer(at), out var actual, (nuint)Marshal.SizeOf<MemoryInformation>()) != 0
                && Address(actual.AllocationBase) == allocation && actual.State == 0x1000 && actual.Type == 0x20000
                && actual.Protect == protection && at >= Address(actual.BaseAddress)
                && at - Address(actual.BaseAddress) < (ulong)actual.RegionSize,
                "V5 near storage is not exact-owned committed MEM_PRIVATE memory with the required protection.");
            var next = checked(Address(actual.BaseAddress) + (ulong)actual.RegionSize);
            Require(next > at, "V5 near memory has invalid actual region bounds."); at = Math.Min(next, end);
        }
    }
    private static byte[] ReadPrivate(ulong address, int bytes, ulong allocation)
    {
        PrivateMemory(address, bytes, allocation, 0x20); var output = new byte[bytes];
        Require(ReadProcessMemory(GetCurrentProcess(), Pointer(address), output, (nuint)bytes, out var read) && read == (nuint)bytes,
            "Cannot copy actual registered V5 near memory."); return output;
    }
    private void VerifySitesAndResources()
    {
        var resource = _wire.Resources[0];
        Require(resource.Version == 5 && resource.ByteSize == 144 && resource.Index == 0 && resource.Flags == 15
            && resource.AllocationBase != 0 && resource.AllocationBytes is >= 8192 and <= 1048576
            && resource.AllocationBase >= TargetModule + _modules[2].ImageSize && resource.AllocationBase - TargetModule <= uint.MaxValue - 8192
            && resource.TableBase == TargetModule && resource.TableAddress == resource.AllocationBase && resource.TableCount == 23
            && resource.RfFirst == 40 && resource.RfCount == 27 && resource.AllocationProtection == 4 && resource.ObservedProtection == 0x20
            && resource.AllocationAttempts >= 1 && resource.AllocationSucceeded == 1 && resource.RxProtectAttempts == 1 && resource.RxProtectSucceeded == 1
            && resource.TableRegisterAttempts == 1 && resource.TableRegisterSucceeded == 1 && resource.RootRetained == 1
            && resource.ProtectionOutstanding == 0 && resource.TableRegistered == 1 && resource.AllocationOwned == 1,
            "V5 has no complete exact-owned registered near resource.");
        PrivateMemory(resource.AllocationBase, checked((int)resource.AllocationBytes), resource.AllocationBase, 0x20);
        Require(resource.TableSha256.AsSpan().SequenceEqual(SHA256.HashData(ReadPrivate(resource.TableAddress, 23 * 12, resource.AllocationBase))),
            "V5 actual registered near runtime-function table hash changed.");
        for (var i = 0; i < 23; ++i)
        {
            var fact = NativeEngineTransportFixedFacts.Site(i); var site = _wire.Sites[i]; var window = _wire.Windows[13 + i];
            var function = NativeEngineTransportFixedFacts.SiteFunction(i); var familyIndex = NativeEngineTransportFixedFacts.SiteFamily(i); var family = _wire.Families[familyIndex];
            var channel = fact.Family == 0 ? 0u : fact.Family == 1 ? 4u : function < 4 ? checked((uint)function + 3) : function < 10 ? checked((uint)function + 3) : checked((uint)function - 10);
            var phase = fact.Family == 0 ? fact.Phase + 1 : fact.Phase;
            Require(site.Version == 5 && site.ByteSize == 128 && site.Index == i && site.FamilyIndex == familyIndex
                && site.ChannelKind == channel && site.PhaseKind == phase && site.WindowIndex == 13 + i && site.FunctionIndex == function
                && site.ObservationRva == fact.Logical && site.LogicalBytes == fact.LogicalBytes
                && site.LogicalContinuationRva == fact.LogicalContinuation && site.PhysicalContinuationRva == fact.PhysicalContinuation
                && site.BranchTargetRva == fact.Branch && site.SitePc == TargetModule + fact.Logical
                && site.BypassReplayRva == NativeEngineTransportFixedFacts.BypassRva(i) && site.BypassReplayBytes == NativeEngineTransportFixedFacts.BypassBytes(i)
                && site.BypassContinuationRva == NativeEngineTransportFixedFacts.BypassContinuation(i)
                && site.AlternateContinuation == (fact.Expanded ? TargetModule + NativeEngineTransportFixedFacts.BypassContinuation(i) : 0)
                && site.HelperEpoch == family.Epoch && site.HelperSlot == family.Slot && site.Flags == 7
                && site.SourceRfIndex == SourceRfIndex(function, fact.Physical) && site.ResourceIndex == 0
                && site.Relay == resource.AllocationBase + 512 + checked((ulong)i * 256),
                "V5 logical, physical and actual relay site identities differ from independent fixed facts.");
            Require(window.RelayBytes is > 6 and <= 160 && site.RelayReturnPc >= site.Relay + 6 && site.RelayReturnPc < site.Relay + window.RelayBytes
                && window.LiteralAddress == site.Relay + 160 && site.Relay + 256 <= resource.AllocationBase + resource.AllocationBytes,
                "V5 actual CALL return PC/literal escapes its exact relay runtime function.");
            var code = ReadPrivate(site.Relay, checked((int)window.RelayBytes), resource.AllocationBase);
            Require(window.RelayCodeSha256.AsSpan().SequenceEqual(SHA256.HashData(code)), "V5 actual relay code hash differs from the verified immutable publication.");
            var call = ReadPrivate(site.RelayReturnPc - 6, 6, resource.AllocationBase);
            Require(call[0] == 0xff && call[1] == 0x15 && RelativeTarget(site.RelayReturnPc, call.AsSpan(2)) == window.LiteralAddress
                && BinaryPrimitives.ReadUInt64LittleEndian(ReadPrivate(window.LiteralAddress, 8, resource.AllocationBase)) == site.ProbeEntry,
                "V5 probe CALL/literal differs from its exact loaded Kernel entry.");
            var expectedEntry = fact.Family == 0 ? _inlineEntry() : fact.Family == 1 ? _rowEntry() : _phaseEntry();
            Require(site.ProbeEntry == Address(expectedEntry), "V5 relay probe is not the actual loaded Kernel family entry.");
            Memory(expectedEntry, 1, _modules[1].Handle, 0x20);
        }
    }
    private void VerifyRuntimeFunctions()
    {
        var functions = _wire.RuntimeFunctions; var sourceIndex = 0;
        for (var i = 0; i < 13; ++i)
            foreach (var function in _sourceFunctions[i]) { VerifyStaticRf(functions[sourceIndex], function, sourceIndex, 0, 2); ++sourceIndex; }
        for (var i = 0; i < 13; ++i) VerifyStaticRf(functions[27 + i], _originalFunctions[i], 27 + i, 1, 0);
        var resource = _wire.Resources[0];
        for (var i = 0; i < 23; ++i)
        {
            var row = functions[40 + i]; var site = _wire.Sites[i]; var window = _wire.Windows[13 + i]; var synthetic = i is >= 17 and <= 20;
            Require(row.Version == 5 && row.ByteSize == 112 && row.Index == 40 + i && row.Kind == 2
                && row.ModuleIndex == 2 && row.ResourceIndex == 0 && row.TableEntryIndex == i && row.ImageBase == TargetModule
                && row.RuntimeFunctionAddress == resource.TableAddress + checked((ulong)i * 12)
                && row.BeginRva == site.Relay - TargetModule && row.EndRva == site.Relay - TargetModule + window.RelayBytes
                && row.UnwindRva == site.Relay - TargetModule + 176 && row.UnwindBytes == 16 && row.UnwindFlags == 4
                && row.PrologBytes == 0 && row.CodeSlots == 0 && row.FrameRegisterAndOffset == 0
                && row.ParentIndex == (synthetic ? checked((uint)(63 + i - 17)) : site.SourceRfIndex), "V5 relay RF/chain identity changed.");
            var actual = ReadPrivate(row.RuntimeFunctionAddress, 12, resource.AllocationBase);
            Require(TripleMatches(actual, row.BeginRva, row.EndRva, row.UnwindRva), "Actual registered relay table tuple changed.");
            var unwind = ReadPrivate(TargetModule + row.UnwindRva, 16, resource.AllocationBase); var parent = functions[checked((int)row.ParentIndex)];
            Require(unwind.AsSpan(0, 4).SequenceEqual(new byte[] { 0x21, 0, 0, 0 })
                && TripleMatches(unwind.AsSpan(4), parent.BeginRva, parent.EndRva, parent.UnwindRva)
                && row.UnwindSha256.AsSpan().SequenceEqual(SHA256.HashData(unwind)), "V5 complete actual CHAININFO parent/hash changed.");
            foreach (var pc in new[] { site.Relay, site.RelayReturnPc, site.Relay + window.RelayBytes - 1 })
                Require(RtlLookupFunctionEntry(pc, out var imageBase, IntPtr.Zero) == Pointer(row.RuntimeFunctionAddress) && imageBase == TargetModule,
                    "The real relay PC/return PC lacks its exact Windows dynamic runtime-function registration.");
            if (synthetic)
            {
                var syntheticIndex = 63 + i - 17; var primary = functions[syntheticIndex]; var source = functions[checked((int)site.SourceRfIndex)];
                Require(primary.Version == 5 && primary.ByteSize == 112 && primary.Index == syntheticIndex && primary.Kind == 3
                    && primary.ModuleIndex == 2 && primary.ResourceIndex == 0 && primary.TableEntryIndex == uint.MaxValue && primary.ParentIndex == uint.MaxValue
                    && primary.ImageBase == TargetModule && primary.RuntimeFunctionAddress == TargetModule + row.UnwindRva + 4
                    && primary.BeginRva == source.BeginRva && primary.EndRva == source.EndRva && primary.UnwindRva == site.Relay - TargetModule + 192
                    && primary.UnwindBytes == 12 && primary.UnwindFlags == 0 && primary.PrologBytes == 0 && primary.CodeSlots == 3 && primary.FrameRegisterAndOffset == 0,
                    "V5 early-Dispatch synthetic primary is not the exact metadata-only partial frame.");
                var partial = ReadPrivate(TargetModule + primary.UnwindRva, 12, resource.AllocationBase);
                Require(partial.AsSpan().SequenceEqual(new byte[] { 1, 0, 3, 0, 0, 1, 0x2a, 0, 0, 0x70, 0, 0 })
                    && primary.UnwindSha256.AsSpan().SequenceEqual(SHA256.HashData(partial)), "V5 early-Dispatch unwind borrows an unexecuted late RBX save.");
            }
        }
    }
    private static bool TripleMatches(ReadOnlySpan<byte> bytes, uint begin, uint end, uint unwind)
        => bytes.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == begin
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4)) == end && BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8)) == unwind;
    private void VerifyStaticRf(NativeEngineTransportRuntimeFunctionV5 row, Function expected, int index, uint kind, int moduleIndex)
    {
        var module = _modules[moduleIndex]; var unwind = module.Unwind(expected); var flags = checked((uint)(unwind[0] >> 3));
        var frame = checked((uint)((unwind[3] & 15) | ((unwind[3] >> 4) * 16 << 8)));
        Require(row.Version == 5 && row.ByteSize == 112 && row.Index == index && row.Kind == kind && row.ModuleIndex == moduleIndex
            && row.ResourceIndex == uint.MaxValue && row.ImageBase == Address(module.Handle)
            && row.TableEntryIndex == module.FunctionOrdinal(expected)
            && row.RuntimeFunctionAddress == Address(module.Handle) + module.ExceptionRva + checked((ulong)row.TableEntryIndex * 12)
            && row.BeginRva == expected.Begin && row.EndRva == expected.End && row.UnwindRva == expected.Unwind
            && row.UnwindBytes == unwind.Length && row.UnwindFlags == flags && row.PrologBytes == unwind[1] && row.CodeSlots == unwind[2]
            && row.FrameRegisterAndOffset == frame && row.UnwindSha256.AsSpan().SequenceEqual(SHA256.HashData(unwind)),
            "V5 source/original RF differs from its independently held complete unwind metadata.");
        module.VerifyUnwind(expected);
        var actual = RtlLookupFunctionEntry(Address(module.Handle) + expected.Begin, out var imageBase, IntPtr.Zero);
        Require(Address(actual) == row.RuntimeFunctionAddress && imageBase == Address(module.Handle), "The retained actual static RF pointer changed.");
        if ((flags & 4) != 0)
        {
            var parent = unwind.AsSpan(unwind.Length - 12).ToArray(); var matches = Enumerable.Range(0, 40).Where(i => _wire.RuntimeFunctions[i].ImageBase == row.ImageBase
                && TripleMatches(parent, _wire.RuntimeFunctions[i].BeginRva, _wire.RuntimeFunctions[i].EndRva, _wire.RuntimeFunctions[i].UnwindRva)).ToArray();
            Require(matches.Length == 1 && row.ParentIndex == matches[0], "The exact source CHAININFO parent is missing or ambiguous.");
        }
        else Require(row.ParentIndex == uint.MaxValue, "A primary static RF has an unexpected parent.");
    }
}
