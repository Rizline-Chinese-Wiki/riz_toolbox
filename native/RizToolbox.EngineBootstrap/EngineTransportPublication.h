#pragma once
#include <cstddef>
#include <cstdint>

// Declaration-only production transport publication ABI 5. This header does
// not implement an installer, authorize a startup window, or change ABI 4.
// Addresses and OS handles are uint64 machine values, never native pointers in
// this wire. No record contains a constructor, native container or PE pointer.
// Reserved fields and unused array entries are zero. Counts are exact occupied
// prefixes, bounded by the declared capacities; masks cannot contain high bits.
// Event overflow is sticky Unknown and cannot wrap/reuse the first events. All
// addresses, index mappings, fixed hashes, byte counts and RF chains are facts
// to verify independently; the presence of a schema field does not prove it.
namespace RizEngineTransportPublicationLayout {
inline constexpr std::uint32_t Version = 5;
inline constexpr std::uint64_t Magic = 0x3556425550545a52ull; // "RZTPUBV5"
inline constexpr std::uint32_t ModuleCount = 5, TypedCount = 13, SiteCount = 23;
inline constexpr std::uint32_t WindowCount = 36, FamilyCount = 12;
inline constexpr std::uint32_t SourceRuntimeFunctionCount = 27;
inline constexpr std::uint32_t RuntimeFunctionCapacity = 128, ResourceCapacity = 23;
inline constexpr std::uint32_t EventCapacity = 1024, WindowByteCapacity = 32;
inline constexpr std::uint32_t NoIndex = 0xffffffffu;
inline constexpr std::uint64_t AllTyped = (1ull << TypedCount) - 1;
inline constexpr std::uint64_t AllSites = (1ull << SiteCount) - 1;
inline constexpr std::uint64_t AllWindows = (1ull << WindowCount) - 1;
inline constexpr std::uint64_t AllFamilies = (1ull << FamilyCount) - 1;
}

enum RizEngineTransportStateV5 : std::uint32_t {
    RizEngineTransportNeverV5 = 0, RizEngineTransportValidatingV5 = 1,
    RizEngineTransportConfiguringV5 = 2, RizEngineTransportPublishingV5 = 3,
    RizEngineTransportReadyV5 = 4, RizEngineTransportDisabledNoSourceWriteV5 = 5,
    RizEngineTransportUnknownV5 = 6
};
enum RizEngineTransportProfileV5 : std::uint32_t {
    RizEngineTransportOwnedProfileV5 = 1, RizEngineTransportFixedGameCandidateV5 = 2
};
enum RizEngineTransportFailureV5 : std::uint32_t {
    RizEngineTransportNoFailureV5 = 0, RizEngineTransportDynamicLoadV5,
    RizEngineTransportRepeatedAttachV5, RizEngineTransportBadImageV5,
    RizEngineTransportBadHashV5, RizEngineTransportBadDependencyV5,
    RizEngineTransportBadSourceV5, RizEngineTransportBadUnwindV5,
    RizEngineTransportBadKernelV5, RizEngineTransportBadCoverageV5,
    RizEngineTransportConfigureFailedV5, RizEngineTransportNearAllocationFailedV5,
    RizEngineTransportRuntimeTableFailedV5, RizEngineTransportProtectFailedV5,
    RizEngineTransportWriteFailedV5, RizEngineTransportRestoreFailedV5,
    RizEngineTransportFlushFailedV5, RizEngineTransportBadReadbackV5,
    RizEngineTransportNativeExceptionV5, RizEngineTransportAdmissionUnprovenV5,
    RizEngineTransportCapacityExceededV5, RizEngineTransportEventOverflowV5,
    RizEngineTransportInvalidCallerV5, RizEngineTransportInvalidSnapshotV5
};
enum RizEngineTransportModuleKindV5 : std::uint32_t {
    RizEngineTransportBootstrapModuleV5 = 0, RizEngineTransportKernelModuleV5 = 1,
    RizEngineTransportUnityModuleV5 = 2, RizEngineTransportExecutableModuleV5 = 3,
    RizEngineTransportDoorstopModuleV5 = 4
};
// Typed indices 0..12 are also physical entry-window indices 0..12. The nine
// added originals occupy typed indices 4..12 in their fixed OriginalIndex order.
enum RizEngineTransportTypedKindV5 : std::uint32_t {
    RizEngineTransportNodeStartV5 = 0, RizEngineTransportNodeExecuteV5,
    RizEngineTransportNodeReleaseV5, RizEngineTransportNodeDetachV5,
    RizEngineTransportSchedulerRegisterV5, RizEngineTransportSchedulerRemoveV5,
    RizEngineTransportSchedulerConsumeV5, RizEngineTransportSchedulerPumpV5,
    RizEngineTransportSchedulerDrainV5, RizEngineTransportSchedulerCallbackV5,
    RizEngineTransportWaitYieldV5, RizEngineTransportWaitDispatchV5,
    RizEngineTransportWaitBatchV5
};
enum RizEngineTransportFamilyKindV5 : std::uint32_t {
    RizEngineTransportNodeFamilyV5 = 0, RizEngineTransportInlineFamilyV5 = 1,
    RizEngineTransportSchedulerFamilyV5 = 2, RizEngineTransportWaitFamilyV5 = 3
};
// Family indices: Node Start/Execute/Release/Detach 0..3, aggregate Inline 4,
// Scheduler Register/Remove/Consume/Pump/Drain/Callback 5..10, aggregate Wait 11.
// This is twelve permanent Configure claims, not thirteen Configure calls.
enum RizEngineTransportFamilyIndexV5 : std::uint32_t {
    RizEngineTransportStartFamilyIndexV5 = 0,
    RizEngineTransportExecuteFamilyIndexV5 = 1,
    RizEngineTransportReleaseFamilyIndexV5 = 2,
    RizEngineTransportDetachFamilyIndexV5 = 3,
    RizEngineTransportInlineFamilyIndexV5 = 4,
    RizEngineTransportRegisterFamilyIndexV5 = 5,
    RizEngineTransportRemoveFamilyIndexV5 = 6,
    RizEngineTransportConsumeFamilyIndexV5 = 7,
    RizEngineTransportPumpFamilyIndexV5 = 8,
    RizEngineTransportDrainFamilyIndexV5 = 9,
    RizEngineTransportCallbackFamilyIndexV5 = 10,
    RizEngineTransportWaitFamilyIndexV5 = 11
};
enum RizEngineTransportWindowRoleV5 : std::uint32_t {
    RizEngineTransportTypedWindowV5 = 0, RizEngineTransportSideWindowV5 = 1
};
enum RizEngineTransportWindowFlagsV5 : std::uint32_t {
    RizEngineTransportWindowPreparedV5 = 1, RizEngineTransportWindowWriteAttemptedV5 = 2,
    RizEngineTransportWindowWrittenV5 = 4, RizEngineTransportWindowReadbackV5 = 8,
    RizEngineTransportWindowProtectionRestoredV5 = 16, RizEngineTransportWindowFlushedV5 = 32,
    RizEngineTransportWindowExpandedV5 = 64, RizEngineTransportWindowConditionalBypassV5 = 128
};
enum RizEngineTransportFamilyFlagsV5 : std::uint32_t {
    RizEngineTransportFamilyConfiguredV5 = 1, RizEngineTransportFamilyIdentityVerifiedV5 = 2,
    RizEngineTransportFamilyRetainedV5 = 4
};
enum RizEngineTransportRuntimeFunctionKindV5 : std::uint32_t {
    RizEngineTransportSourceRuntimeFunctionV5 = 0,
    RizEngineTransportOriginalRuntimeFunctionV5 = 1,
    RizEngineTransportRelayRuntimeFunctionV5 = 2,
    // Metadata-only CHAININFO parent for early Dispatch: code range retains
    // the real source function, while unwind bytes live in the near arena.
    RizEngineTransportPartialPrimaryRuntimeFunctionV5 = 3
};
enum RizEngineTransportResourceFlagsV5 : std::uint32_t {
    RizEngineTransportAllocationOwnedV5 = 1, RizEngineTransportAllocationRxV5 = 2,
    RizEngineTransportFunctionTableRegisteredV5 = 4, RizEngineTransportResourceRetainedV5 = 8
};
enum RizEngineTransportMainThreadProofV5 : std::uint32_t {
    RizEngineTransportMainThreadUnknownV5 = 0,
    RizEngineTransportMainThreadOwnedContractV5 = 1,
    RizEngineTransportMainThreadFixedProductionContractV5 = 2
};

#pragma pack(push, 8)
struct RizEngineTransportModuleV5 {
    std::uint32_t kind, machine, imageSize, timeDateStamp;
    std::uint64_t module, fileBytes, fileIndex;
    std::uint32_t volumeSerial, flags;
    std::uint8_t sha256[32];
    std::uint64_t heldFile;
    std::uint32_t entryRva, exceptionRva;
};

struct RizEngineTransportTypedRowV5 {
    std::uint32_t version, byteSize, index, channelKind;
    std::uint32_t familyIndex, moduleIndex, windowIndex, flags;
    std::uint64_t module, target, original, continuation, entry, helperEpoch;
    std::uint32_t rva, endRva, prefixBytes, sourceRfFirst;
    std::uint32_t sourceRfCount, originalRfFirst, originalRfCount, helperSlot;
    std::uint32_t originalBytes, originalModuleIndex, scratchRegister, sourceBodyBytes;
    std::uint8_t bodySha256[32], originalCodeSha256[32];
};

// Site indices 0..2 = Inline Begin/EndFree/EndRetained; 3..4 = Scheduler
// DrainRow Begin/End; 5..22 = the eighteen Wait phases in Configure site order.
// A site's physical window is 13+site.index. Its logical PC may lie inside an
// enlarged physical block. Never configure a Kernel site with physicalRva in
// place of observationRva, or relay with relayReturnPc in place of source PC.
struct RizEngineTransportSiteV5 {
    std::uint32_t version, byteSize, index, familyIndex;
    std::uint32_t channelKind, phaseKind, windowIndex, functionIndex;
    std::uint32_t observationRva, logicalBytes, logicalContinuationRva, physicalContinuationRva;
    std::uint32_t branchTargetRva, bypassReplayRva, bypassReplayBytes, bypassContinuationRva;
    std::uint64_t sitePc, relayReturnPc, probeEntry, helperEpoch;
    std::uint32_t helperSlot, flags, sourceRfIndex, resourceIndex;
    std::uint64_t relay, alternateContinuation;
};

struct RizEngineTransportFamilyV5 {
    std::uint32_t version, byteSize, index, familyKind;
    std::uint32_t channelKind, slot, abiVersion, frameBytes;
    std::uint32_t completionBytes, infoBytes, configBytes, observerBytes;
    // For aggregate Inline/Wait, scalar target/original/entry are zero; the
    // complete typedMask plus typed rows/configSha256 identifies all members.
    std::uint64_t epoch, module, entry, target, original;
    // The aggregate Inline and Wait families cover one/three typed functions
    // respectively, but their source writes still belong to distinct windows.
    std::uint64_t typedMask, siteMask, windowMask;
    std::uint32_t configureAttempts, configureSucceeded, flags, lastWin32Error;
    std::uint32_t mainOsThread, helperUnknown, retainedClaim, reserved0;
    // Exact zero-initialized bytes of this family's actual Configure ABI, with
    // no observer or caller-supplied proof injected. Recheck identity via Info.
    std::uint8_t configSha256[32];
};

// One exact source write obligation. All 36 expected ranges must be sorted and
// nonoverlapping within their full fixed bodies before the first source write.
// Full-body verification overlays committed patch bytes only on these ranges;
// every byte outside them must still equal the pinned body. A partial write is
// never a committed window, even if another window in that function succeeded.
struct RizEngineTransportWindowV5 {
    std::uint32_t version, byteSize, index, role;
    std::uint32_t typedIndex, siteIndex, familyIndex, flags;
    std::uint32_t moduleIndex, sourceRfIndex, physicalRva, physicalBytes;
    std::uint32_t logicalRva, logicalBytes, resourceIndex, relayRfFirst;
    std::uint32_t relayRfCount, literalBytes, relayBytes, reserved0;
    std::uint64_t source, patchTarget, relay, relayReturnPc, continuation;
    std::uint64_t alternateContinuation, probeEntry, literalAddress, literalValue;
    std::uint32_t originalProtection, observedProtection, protectAttempts, protectSucceeded;
    std::uint32_t sourceWriteAttempts, sourceBytesWritten, readbackAttempts, readbackVerified;
    std::uint32_t restoreAttempts, restoreSucceeded, flushAttempts, flushSucceeded;
    std::uint32_t protectionOutstanding, lastWin32Error, readbackBytes, reserved1;
    std::uint64_t protectionBase, protectionBytes, writeSequence, commitSequence;
    std::uint8_t expectedBytes[32], patchBytes[32], observedBytes[32];
    std::uint8_t relayCodeSha256[32], sourceWindowSha256[32];
    std::uint64_t observedLiteralValue;
    std::uint32_t relayOriginalProtection, relayObservedProtection, failureStage, reserved2;
    std::uint64_t readbackSequence, reserved3;
};

// A complete RF/unwind identity, not merely the RF containing a prefix. Source
// groups cover all 27 fixed RFs (including CHAININFO); original groups cover all
// thirteen static original RFs. Relay groups may contain multiple RFs and must
// fit the remaining capacity. first/count ranges refer to this global table.
// parentIndex is the exact CHAININFO parent or NoIndex; no ancestor is inferred
// from PC proximity. unwindSha256 covers unwindBytes beginning at imageBase+
// unwindRva, including aligned code slots and its chained/handler trailer. This
// does not claim interpretation of handler-specific LSDA or startup permission.
struct RizEngineTransportRuntimeFunctionV5 {
    std::uint32_t version, byteSize, index, kind;
    std::uint32_t moduleIndex, resourceIndex, tableEntryIndex, parentIndex;
    std::uint64_t imageBase, runtimeFunctionAddress;
    std::uint32_t beginRva, endRva, unwindRva, unwindBytes;
    // frameRegisterAndOffset: low byte register number, next byte frame offset
    // in bytes (not scaled UNWIND_INFO units); upper sixteen bits are zero.
    std::uint32_t unwindFlags, prologBytes, codeSlots, frameRegisterAndOffset;
    std::uint8_t unwindSha256[32];
};

// At most one resource index per side window, with explicit sharing allowed:
// several windows may reference one retained near arena and its exact RF range.
// Counts/ranges never imply one table per relay. Nonallocated static originals
// use a module RF identity and NoIndex here. A successful allocation/table or
// permanent Configure claim is retention responsibility before source writes.
// No abort/Disabled path may erase it or unload code/unwind storage it roots.
struct RizEngineTransportResourceV5 {
    std::uint32_t version, byteSize, index, flags;
    std::uint64_t allocationBase, allocationBytes, tableBase, tableAddress;
    std::uint32_t tableCount, rfFirst, rfCount, allocationProtection;
    std::uint32_t observedProtection, allocationAttempts, allocationSucceeded, rxProtectAttempts;
    std::uint32_t rxProtectSucceeded, tableRegisterAttempts, tableRegisterSucceeded, rootRetained;
    std::uint32_t lastWin32Error, protectionOutstanding, tableRegistered, allocationOwned;
    std::uint8_t tableSha256[32];
};

enum RizEngineTransportEventObjectV5 : std::uint32_t {
    RizEngineTransportPublicationObjectV5 = 0, RizEngineTransportModuleObjectV5,
    RizEngineTransportFamilyObjectV5, RizEngineTransportWindowObjectV5,
    RizEngineTransportResourceObjectV5
};
enum RizEngineTransportEventStageV5 : std::uint32_t {
    RizEngineTransportEnteredV5 = 1, RizEngineTransportModuleValidatedV5,
    RizEngineTransportBodyValidatedV5, RizEngineTransportCoverageValidatedV5,
    RizEngineTransportOriginalPreparedV5, RizEngineTransportRelayPreparedV5,
    RizEngineTransportResourceAllocatedV5, RizEngineTransportResourceProtectedV5,
    RizEngineTransportTableRegisteredV5, RizEngineTransportFamilyConfiguredEventV5,
    RizEngineTransportProtectAttemptedV5, RizEngineTransportProtectSucceededEventV5,
    RizEngineTransportWriteAttemptedEventV5, RizEngineTransportWriteProgressV5,
    RizEngineTransportReadbackVerifiedV5, RizEngineTransportProtectionRestoredEventV5,
    RizEngineTransportFlushedV5, RizEngineTransportCommittedV5,
    RizEngineTransportPublishedV5, RizEngineTransportAbortedV5,
    RizEngineTransportVerifyFailedV5, RizEngineTransportFailedV5,
    RizEngineTransportEventsOverflowedV5
};
struct RizEngineTransportEventV5 {
    std::uint64_t sequence;
    std::uint32_t stage, objectKind, objectIndex, state;
    std::uint32_t failure, win32Error, bytesWritten, flags;
    std::uint64_t detail;
};

enum RizEngineTransportAdmissionWitnessFlagsV5 : std::uint32_t {
    RizEngineTransportWitnessStageACompiledV5 = 1u << 0,
    RizEngineTransportWitnessEarliestMainV5 = 1u << 1,
    RizEngineTransportWitnessSuspendedCheckedV5 = 1u << 2,
    RizEngineTransportWitnessPreWriteCleanV5 = 1u << 3,
    RizEngineTransportWitnessExclusivityHeldV5 = 1u << 4,
    RizEngineTransportWitnessPostCommitResumedV5 = 1u << 5,
    RizEngineTransportWitnessAllV5 = 0x3fu
};

struct RizEngineTransportAdmissionReceiptV5 {
    std::uint32_t version, byteSize, witnessFlags, earliestThreadId;
    std::uint32_t preWriteThreadCount, suspendedThreadCount, nonMainInteriorRipCount, postCommitThreadCount;
    std::uint8_t admissionAuthoritySha256[32];
    std::uint8_t exclusivitySha256[32];
};

struct RizEngineTransportPublicationV5 {
    std::uint32_t version, byteSize, state, failure;
    std::uint64_t schemaMagic, epoch, snapshotSequence;
    std::uint32_t ownerThread, processId, attachReason, startupStatic;
    std::uint32_t profile, claimed, producerThread, mainThread;
    // ownerThread/producerThread are not evidence of the game's main thread.
    // Fixed production adoption requires a separately established mainThread
    // proof and equality with every configured main-OSTID-bearing family.
    std::uint32_t mainThreadProof, verifyFailures, lastWin32Error, eventCount;
    std::uint32_t eventOverflow, sourceWriteAttempts, sourceWriteCompletions, protectionOutstanding;
    std::uint32_t moduleCount, typedCount, siteCount, windowCount;
    std::uint32_t familyCount, runtimeFunctionCount, resourceCount, eventCapacity;
    std::uint64_t configuredFamilies, boundFamilies, retainedFamilies, preparedWindows;
    // Committed means the entire physical range was written, read back,
    // protection restored and flushed; partial byte progress never sets it.
    std::uint64_t writeAttemptedWindows, committedWindows, outstandingProtectionWindows, retainedResources;
    std::uint64_t bootstrapModule, kernelModule, targetModule, executableModule, doorstopModule;
    std::uint32_t failureWindowIndex, failureFamilyIndex, failureResourceIndex, reserved0;
    std::uint8_t fixedContractSha256[32], dependencyGraphSha256[32];
    RizEngineTransportModuleV5 modules[5];
    RizEngineTransportTypedRowV5 typed[13];
    RizEngineTransportSiteV5 sites[23];
    RizEngineTransportFamilyV5 families[12];
    RizEngineTransportWindowV5 windows[36];
    RizEngineTransportRuntimeFunctionV5 runtimeFunctions[128];
    RizEngineTransportResourceV5 resources[23];
    RizEngineTransportEventV5 events[1024];
    RizEngineTransportAdmissionReceiptV5 admission;
};
#pragma pack(pop)

#ifdef RIZ_ENGINE_TRANSPORT_PUBLICATION_BUILD
#define RIZ_ENGINE_TRANSPORT_PUBLICATION_API extern "C" __declspec(dllexport)
#else
#define RIZ_ENGINE_TRANSPORT_PUBLICATION_API extern "C" __declspec(dllimport)
#endif
RIZ_ENGINE_TRANSPORT_PUBLICATION_API std::uint32_t __cdecl RizEngineTransport_GetVersion();
// Input: output->version=5 and output->byteSize=sizeof(V5). The implementation
// must additionally return/validate Magic. Nonwaiting copy: 0 success, 1 bad
// input, 2 busy. All record arrays are inline; no borrowed pointer-to-record or
// pointer-to-array escapes. Recorded numeric addresses still require the native
// and CLR owners' independent actual-memory/module validation.
RIZ_ENGINE_TRANSPORT_PUBLICATION_API std::uint32_t __cdecl RizEngineTransport_GetInfoV5(
    RizEngineTransportPublicationV5* output);
// Read-only with respect to source code/configuration. Like V4 Verify, this has
// no caller-supplied target or proof setter. Success must not mutate the exact
// publication snapshot. On failure it permanently records Unknown and retains
// every claim/resource/write obligation. CLR must GetInfo before/after Verify,
// require version+size+magic, same Ready epoch and exact successful snapshot,
// then independently validate all identities/ranges/masks and fixed contracts.
// This declaration and a successful owned Verify do not authorize production.
RIZ_ENGINE_TRANSPORT_PUBLICATION_API std::uint32_t __cdecl RizEngineTransport_VerifyV5();
// A later proxy/entry failure cannot abandon prepared or partial publication.
// This records failure/retention; it is not permission to free registered tables,
// unload permanent Configure owners, rollback live code, or open future gates.
RIZ_ENGINE_TRANSPORT_PUBLICATION_API void __cdecl RizEngineTransport_AbortAfterPublication();

static_assert(sizeof(std::uint32_t) == 4 && sizeof(std::uint64_t) == 8);
static_assert(sizeof(RizEngineTransportModuleV5) == 96);
static_assert(offsetof(RizEngineTransportModuleV5, sha256) == 48);
static_assert(sizeof(RizEngineTransportTypedRowV5) == 192);
static_assert(offsetof(RizEngineTransportTypedRowV5, bodySha256) == 128);
static_assert(sizeof(RizEngineTransportSiteV5) == 128);
static_assert(offsetof(RizEngineTransportSiteV5, sitePc) == 64);
static_assert(sizeof(RizEngineTransportFamilyV5) == 176);
static_assert(offsetof(RizEngineTransportFamilyV5, typedMask) == 88);
static_assert(offsetof(RizEngineTransportFamilyV5, configSha256) == 144);
static_assert(sizeof(RizEngineTransportWindowV5) == 448);
static_assert(offsetof(RizEngineTransportWindowV5, source) == 80);
static_assert(offsetof(RizEngineTransportWindowV5, expectedBytes) == 248);
static_assert(offsetof(RizEngineTransportWindowV5, observedLiteralValue) == 408);
static_assert(sizeof(RizEngineTransportRuntimeFunctionV5) == 112);
static_assert(offsetof(RizEngineTransportRuntimeFunctionV5, unwindSha256) == 80);
static_assert(sizeof(RizEngineTransportResourceV5) == 144);
static_assert(offsetof(RizEngineTransportResourceV5, tableSha256) == 112);
static_assert(sizeof(RizEngineTransportEventV5) == 48);
static_assert(offsetof(RizEngineTransportPublicationV5, modules) == 320);
static_assert(offsetof(RizEngineTransportPublicationV5, typed) == 800);
static_assert(offsetof(RizEngineTransportPublicationV5, sites) == 3296);
static_assert(offsetof(RizEngineTransportPublicationV5, families) == 6240);
static_assert(offsetof(RizEngineTransportPublicationV5, windows) == 8352);
static_assert(offsetof(RizEngineTransportPublicationV5, runtimeFunctions) == 24480);
static_assert(offsetof(RizEngineTransportPublicationV5, resources) == 38816);
static_assert(sizeof(RizEngineTransportAdmissionReceiptV5) == 96);
static_assert(offsetof(RizEngineTransportPublicationV5, events) == 42128);
static_assert(offsetof(RizEngineTransportPublicationV5, admission) == 91280);
static_assert(sizeof(RizEngineTransportPublicationV5) == 91376);
static_assert(alignof(RizEngineTransportPublicationV5) == 8);
static_assert(RizEngineTransportPublicationLayout::WindowCount < 64);
static_assert(RizEngineTransportPublicationLayout::ResourceCapacity < 64);
