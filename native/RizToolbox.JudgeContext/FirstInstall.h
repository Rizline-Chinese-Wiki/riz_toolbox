#pragma once
#include <cstddef>
#include <cstdint>

#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZFI_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZFI_EXPORT extern "C"
#endif

// Fixed Windows x64 production pre-init producer. Its caller must be the
// supported Doorstop wrapper before the first original il2cpp_init invocation.
// No API here invokes game code, initializes IL2CPP, or starts CoreCLR.
enum RizFirstInstallPhaseV1 : std::uint32_t {
    RizFirstInstallNever = 0, RizFirstInstallDisabledNoWrite = 1,
    RizFirstInstallPreparing = 2, RizFirstInstallReady = 3,
    RizFirstInstallUnknown = 4
};
// Low 32 bits of the diagnostic on a V7 Unknown output. Zero means that
// the producer supplied no diagnostic. This value conveys no authority.
enum RizFirstInstallFailureReason : std::uint32_t {
    RizFirstInstallFailurePublishedCore = 1,
    RizFirstInstallFailurePublishedOperation = 2,
    RizFirstInstallFailurePublishedSingleEvent = 3,
    RizFirstInstallFailurePublishedTweenSource = 4,
    RizFirstInstallFailurePublishedVerificationException = 5,
    RizFirstInstallFailurePlaybackAttestation = 6,
    RizFirstInstallFailurePlaybackVerificationException = 7,
    RizFirstInstallFailurePreinitReentry = 8,
    RizFirstInstallFailureUnexpectedPrepareWrite = 9,
    RizFirstInstallFailureInstallCommit = 10,
    RizFirstInstallFailureInstallPublication = 11,
    RizFirstInstallFailureInstallException = 12,
    RizFirstInstallFailureInstallAbandoned = 13
};
enum RizFirstInstallCoreFailureDetail : std::uint32_t {
    RizFirstInstallCoreReceiptOrBlock = 1,
    RizFirstInstallCoreImageOrPrimaryUnwind = 2,
    RizFirstInstallCoreProtections = 3,
    RizFirstInstallCoreRuntimeInvoke = 4,
    RizFirstInstallCoreUnity = 5,
    RizFirstInstallCoreListener = 6,
    RizFirstInstallCoreMid = 7,
    RizFirstInstallCoreDetours = 8,
    RizFirstInstallCoreListenerUnwind = 9
};
struct RizFirstInstallInfoV1 {
    std::uint32_t abiVersion, byteSize, phase, unknown;
    std::uint32_t voidSlot, reserved0;
    std::uint64_t moduleBase, imageSize;
    const void* init;
    const void* runtimeTarget;
    const void* runtimeOriginal;
    const void* runtimeEntry;
    std::uint64_t runtimeEpoch;
    const void* unityTarget;
    const void* unityOriginal;
    const void* unityEntry;
    std::uint64_t unityEpoch;
    const void* midReturnPc;
    const void* relay;
    std::uint64_t writesStarted, reserved1;
};

// Process lifetime, exactly one attempt. 0 disabled with no game instruction
// write (call original init but skip this mod host); 1 all fixed publications
// ready; 2 unknown/repeated active entry (the caller must not run original init).
// Repeating a known disabled attempt returns 0. Repeating a ready, preparing,
// or unknown attempt returns 2 and permanently invalidates the receipt.
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_BeforeIl2CppInit(
    const void* originalInit, const wchar_t* absoluteDobbyPath);
// 0 valid output, 1 invalid header/reserved. A coherent ready snapshot is issued
// only after every instruction publication and dependency identity is known.
// The struct conveys internal identity evidence, not a public write capability.

// Internal listener prefix embedded in the sole current V7 packet.
struct RizFirstInstallInfoV2 {
    RizFirstInstallInfoV1 common;
    std::uint32_t capabilityVersion, listenerSlot;
    std::uint64_t listenerEpoch;
    const void* preEntry;
    const void* postEntry;
    const void* preRelay;
    const void* postRelay;
    const void* sourcePc[3];
    const void* sourceReturnPc[3];
    const void* postStub[3];
    const void* postTransportReturnPc[3];
    const void* directTarget[2];
    const void* functionTable;
    std::uint64_t functionTableBase;
    const void* unwindInfo[3];
    std::uint32_t sourceWidth[3], postWidth[3];
    std::uint32_t sourceProtection[3], postProtection[3];
    std::uint32_t registeredFunctionCount, prewriteLookupChecks, prewriteBoundaryChecks, reserved2;
    std::uint64_t allocationSize;
};
// Fixed-profile operation rows. id0..13 follow UnityEventOperation declaration
// order; id14..19 follow CoroutineStopOperation order; id20..24 are Play,
// AddTweenToWaitingList, ExecuteTween, Update and Awake; id25 is the exact
// UnityEvent<Single>.Invoke target. Original/entry/epoch are
// real immutable native claims, never metadata supplied by a managed caller.
struct RizFirstInstallOperationV7 {
    std::uint32_t id, kind, slot, reserved;
    const void* target;
    const void* original;
    const void* entry;
    std::uint64_t epoch;
    std::uint32_t rva, bodySize, unwindRva, protection;
};
struct RizTweenSingleEventPublicationV1 {
    std::uint32_t capabilityVersion, byteSize, phase, unknown;
    std::uint32_t operationId, kind, slot, expectedWrites;
    std::uint64_t epoch, parentEpoch;
    const void* midEntry;
    const void* preEntry;
    const void* postEntry;
    const void* allocation;
    std::uint64_t allocationSize;
    const void* midRelay;
    const void* preRelay;
    const void* postRelay;
    const void* directTarget;
    const void* spanPc[7];
    const void* sourceReturnPc[3];
    const void* midReturnPc;
    const void* postStub[3];
    const void* postTransportReturnPc[3];
    const void* functionTable;
    std::uint64_t functionTableBase;
    const void* unwindInfo[3];
    std::uint32_t spanWidth[7], protection[7];
    std::uint32_t stubSize[3], unwindSize[3];
    std::uint32_t registeredFunctionCount, lookupChecks, boundaryChecks, reserved;
    std::uint32_t configuredMask, publishedMask;
    std::uint64_t attemptedWrites, completedWrites;
};
struct RizFirstInstallInfoV7 {
    RizFirstInstallInfoV2 publication;
    std::uint32_t operationCapabilityVersion, operationCount, expectedWrites, reserved;
    std::uint64_t configuredMask, publishedMask;
    RizFirstInstallOperationV7 operations[26];
    RizTweenSingleEventPublicationV1 singleEvent;
};
// The sole publication export requires abiVersion=7 and byteSize=2528.
// Twenty-six rows and all 64 writes are verified; partial/Unknown retains exact
// native claims and masks. This read never authorizes a first installation.
// Common.reserved1 must be zero on input. It is zero on output except when
// phase=Unknown and unknown=1, where the low 32 bits give the first latched
// reason and the high 32 bits carry optional reason-specific detail.
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_GetInfoV7(RizFirstInstallInfoV7* info);
struct RizTweenSourcePublicationV1;
struct RizTweenSourceMetadataV1;
struct RizTweenSourceReferencesObserverV1;
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_GetTweenSourceInfoV1(RizTweenSourcePublicationV1* info);
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_BindTweenSourceMetadata(const RizTweenSourceMetadataV1* metadata);
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_AttachTweenSourceReferencesV1(const RizTweenSourceReferencesObserverV1* observer);

static_assert(sizeof(void*) == 8, "Windows x64 required");
static_assert(sizeof(RizFirstInstallInfoV1) == 144);
static_assert(offsetof(RizFirstInstallInfoV1, voidSlot) == 16);
static_assert(offsetof(RizFirstInstallInfoV1, moduleBase) == 24);
static_assert(offsetof(RizFirstInstallInfoV1, init) == 40);
static_assert(offsetof(RizFirstInstallInfoV1, runtimeTarget) == 48);
static_assert(offsetof(RizFirstInstallInfoV1, runtimeOriginal) == 56);
static_assert(offsetof(RizFirstInstallInfoV1, runtimeEntry) == 64);
static_assert(offsetof(RizFirstInstallInfoV1, runtimeEpoch) == 72);
static_assert(offsetof(RizFirstInstallInfoV1, unityTarget) == 80);
static_assert(offsetof(RizFirstInstallInfoV1, unityOriginal) == 88);
static_assert(offsetof(RizFirstInstallInfoV1, unityEntry) == 96);
static_assert(offsetof(RizFirstInstallInfoV1, unityEpoch) == 104);
static_assert(offsetof(RizFirstInstallInfoV1, midReturnPc) == 112);
static_assert(offsetof(RizFirstInstallInfoV1, relay) == 120);
static_assert(offsetof(RizFirstInstallInfoV1, writesStarted) == 128);
static_assert(offsetof(RizFirstInstallInfoV1, reserved1) == 136);
static_assert(sizeof(RizFirstInstallInfoV2) == 416);
static_assert(offsetof(RizFirstInstallInfoV2, common) == 0);
static_assert(offsetof(RizFirstInstallInfoV2, capabilityVersion) == 144);
static_assert(offsetof(RizFirstInstallInfoV2, listenerSlot) == 148);
static_assert(offsetof(RizFirstInstallInfoV2, listenerEpoch) == 152);
static_assert(offsetof(RizFirstInstallInfoV2, preEntry) == 160);
static_assert(offsetof(RizFirstInstallInfoV2, postEntry) == 168);
static_assert(offsetof(RizFirstInstallInfoV2, preRelay) == 176);
static_assert(offsetof(RizFirstInstallInfoV2, postRelay) == 184);
static_assert(offsetof(RizFirstInstallInfoV2, sourcePc) == 192);
static_assert(offsetof(RizFirstInstallInfoV2, sourceReturnPc) == 216);
static_assert(offsetof(RizFirstInstallInfoV2, postStub) == 240);
static_assert(offsetof(RizFirstInstallInfoV2, postTransportReturnPc) == 264);
static_assert(offsetof(RizFirstInstallInfoV2, directTarget) == 288);
static_assert(offsetof(RizFirstInstallInfoV2, functionTable) == 304);
static_assert(offsetof(RizFirstInstallInfoV2, functionTableBase) == 312);
static_assert(offsetof(RizFirstInstallInfoV2, unwindInfo) == 320);
static_assert(offsetof(RizFirstInstallInfoV2, sourceWidth) == 344);
static_assert(offsetof(RizFirstInstallInfoV2, postWidth) == 356);
static_assert(offsetof(RizFirstInstallInfoV2, sourceProtection) == 368);
static_assert(offsetof(RizFirstInstallInfoV2, postProtection) == 380);
static_assert(offsetof(RizFirstInstallInfoV2, registeredFunctionCount) == 392);
static_assert(offsetof(RizFirstInstallInfoV2, prewriteLookupChecks) == 396);
static_assert(offsetof(RizFirstInstallInfoV2, prewriteBoundaryChecks) == 400);
static_assert(offsetof(RizFirstInstallInfoV2, reserved2) == 404);
static_assert(offsetof(RizFirstInstallInfoV2, allocationSize) == 408);
static_assert(sizeof(RizFirstInstallOperationV7) == 64);
static_assert(offsetof(RizFirstInstallOperationV7, target) == 16);
static_assert(offsetof(RizFirstInstallOperationV7, original) == 24);
static_assert(offsetof(RizFirstInstallOperationV7, entry) == 32);
static_assert(offsetof(RizFirstInstallOperationV7, epoch) == 40);
static_assert(offsetof(RizFirstInstallOperationV7, protection) == 60);
static_assert(sizeof(RizTweenSingleEventPublicationV1) == 416);
static_assert(offsetof(RizTweenSingleEventPublicationV1, epoch) == 32);
static_assert(offsetof(RizTweenSingleEventPublicationV1, midEntry) == 48);
static_assert(offsetof(RizTweenSingleEventPublicationV1, allocation) == 72);
static_assert(offsetof(RizTweenSingleEventPublicationV1, midRelay) == 88);
static_assert(offsetof(RizTweenSingleEventPublicationV1, spanPc) == 120);
static_assert(offsetof(RizTweenSingleEventPublicationV1, sourceReturnPc) == 176);
static_assert(offsetof(RizTweenSingleEventPublicationV1, midReturnPc) == 200);
static_assert(offsetof(RizTweenSingleEventPublicationV1, postStub) == 208);
static_assert(offsetof(RizTweenSingleEventPublicationV1, postTransportReturnPc) == 232);
static_assert(offsetof(RizTweenSingleEventPublicationV1, functionTable) == 256);
static_assert(offsetof(RizTweenSingleEventPublicationV1, unwindInfo) == 272);
static_assert(offsetof(RizTweenSingleEventPublicationV1, spanWidth) == 296);
static_assert(offsetof(RizTweenSingleEventPublicationV1, protection) == 324);
static_assert(offsetof(RizTweenSingleEventPublicationV1, stubSize) == 352);
static_assert(offsetof(RizTweenSingleEventPublicationV1, unwindSize) == 364);
static_assert(offsetof(RizTweenSingleEventPublicationV1, registeredFunctionCount) == 376);
static_assert(offsetof(RizTweenSingleEventPublicationV1, configuredMask) == 392);
static_assert(offsetof(RizTweenSingleEventPublicationV1, attemptedWrites) == 400);
static_assert(sizeof(RizFirstInstallInfoV7) == 2528);
static_assert(offsetof(RizFirstInstallInfoV7, operationCapabilityVersion) == 416);
static_assert(offsetof(RizFirstInstallInfoV7, configuredMask) == 432);
static_assert(offsetof(RizFirstInstallInfoV7, publishedMask) == 440);
static_assert(offsetof(RizFirstInstallInfoV7, operations) == 448);
static_assert(offsetof(RizFirstInstallInfoV7, operations) + 25 * sizeof(RizFirstInstallOperationV7) == 2048);
static_assert(offsetof(RizFirstInstallInfoV7, singleEvent) == 2112);
