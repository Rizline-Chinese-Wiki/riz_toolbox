#pragma once
#include <cstddef>
#include <cstdint>
#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZEWC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZEWC_EXPORT extern "C"
#endif

// Independent observe-only ABI 1. These machine carriers preserve four actual
// integer-register inputs and full RAX; they do not assert a C++ return type.
enum RizEngineWaitKindV1 : std::uint32_t {
    RizEngineWaitYield = 0, RizEngineWaitDispatch = 1, RizEngineWaitBatch = 2,
    RizEngineWaitNodeStart = 3, RizEngineWaitNodeExecute = 4,
    RizEngineWaitNodeRelease = 5, RizEngineWaitNodeDetach = 6,
    RizEngineWaitSchedulerRegister = 7, RizEngineWaitSchedulerRemove = 8,
    RizEngineWaitSchedulerConsume = 9, RizEngineWaitSchedulerPump = 10,
    RizEngineWaitSchedulerDrain = 11, RizEngineWaitSchedulerCallback = 12
};
enum RizEngineWaitFlagsV1 : std::uint32_t {
    RizEngineWaitSnapshotRequested = 1, RizEngineWaitSnapshotValid = 2,
    RizEngineWaitSnapshotFaulted = 4, RizEngineWaitUnknown = 8,
    RizEngineWaitMainThread = 16, RizEngineWaitParentMatched = 32,
    RizEngineWaitSourceDispatch = 64, RizEngineWaitSourceBatch = 128,
    RizEngineWaitSnapshotCached = 256, RizEngineWaitKnownCallback = 512,
    RizEngineWaitCallbackExpected = 1024, RizEngineWaitCleanupExpected = 2048
};
enum RizEngineWaitExitFlagsV1 : std::uint32_t {
    RizEngineWaitReturned = 1, RizEngineWaitAbnormal = 2,
    RizEngineWaitEnterFaulted = 4, RizEngineWaitRawRaxValid = 8,
    RizEngineWaitMissingPhase = 16, RizEngineWaitExitUnknown = 32
};
enum RizEngineWaitStageFlagsV1 : std::uint32_t {
    RizEngineWaitBeforeWrite = 1, RizEngineWaitFieldsWritten = 2,
    RizEngineWaitSharedReferenceReturned = 4, RizEngineWaitOwnerNull = 8,
    RizEngineWaitCallbackClaimed = 16, RizEngineWaitCallbackCompleted = 32,
    RizEngineWaitCleanupClaimed = 64, RizEngineWaitCleanupCompleted = 128,
    RizEngineWaitCallbackSlotClearReached = 256,
    RizEngineWaitZeroOldReference = 512, RizEngineWaitCommitted = 1024,
    RizEngineWaitMissingRequiredPhase = 2048
};
// Exactly these 18 source-PC/relay-return-PC pairs, ordered as this enum.
// A pre-replay event is a Begin/Reached fact, never a completed write.
enum RizEngineWaitPhaseV1 : std::uint32_t {
    RizEngineWaitPublishBegin = 0,             // 790069 RDI wait/R14 node/EBX owner
    RizEngineWaitOldReferenceDecision = 1,     // 79008D RCX old wait, flags from TEST
    RizEngineWaitOldReferenceReturned = 2,     // 790098 cached old wait, no dereference
    RizEngineWaitPublishCommitted = 3,         // 7900A0 after node.wait store/ref inc
    RizEngineWaitDetachOwnerBegin = 4,         // 7938B3 RAX wait/RDI node
    RizEngineWaitDetachNestedOwnerBegin = 5,   // 793983 RAX wait/RDI node
    RizEngineWaitReleaseClearBegin = 6,        // 78F6F5 RAX wait/RBX node
    RizEngineWaitReleaseFieldsCleared = 7,     // 78F704 before shared-ref CALL
    RizEngineWaitReleaseReferenceReturned = 8, // 78F711 before node.wait clear
    RizEngineWaitStartClearBegin = 9,          // 792527 RAX wait/RBX node
    RizEngineWaitStartFieldsCleared = 10,      // 792536 before shared-ref CALL
    RizEngineWaitStartReferenceReturned = 11,  // 792543 before node.wait clear
    RizEngineWaitDispatchOwnerResult = 12,    // 5A527C RAX actual owner or null
    RizEngineWaitDispatchCallbackArmed = 13,  // 5A5281 before actual callback CALL
    RizEngineWaitDispatchCleanupDecision = 14,// 5A528F RAX re-read cleanup; copy payload now
    RizEngineWaitDispatchClearReached = 15,   // 5A529A before callback-slot clear
    RizEngineWaitBatchObjectBegin = 16,       // 5DA63B RBP wait/RSI manager/RDI index
    RizEngineWaitBatchObjectEnd = 17,         // 5DA660 after shared-ref CALL, all cached
    RizEngineWaitPhaseAbnormal = 18           // missing required stage at scope closing
};
struct RizEngineWaitTupleV1 {
    void* callback; void* cleanup; void* payload;
    std::uint32_t ownerId, reserved;
};
struct RizEngineWaitFrameV1 {
    std::uint32_t abiVersion, byteSize, kind, flags;
    std::uint32_t slot, threadId, depth, reserved;
    std::uint64_t epoch, sequence, parentSequence, rootSequence;
    // Original Node/Scheduler sequence, not supplied by an exported setter.
    std::uint64_t externalSequence, sourceSequence, operationSequence;
    void* arg0; void* arg1; void* arg2; void* arg3;
    const void* caller; const void* target;
    void* wait; void* node; void* nodeOwner;
    std::uint64_t enumHandle;
    void* enumCache;
    std::uint32_t enumType; std::int32_t referenceCount;
    std::uint32_t nodeSnapshotFlags, reserved1;
    RizEngineWaitTupleV1 tuple;
    void* manager; std::uint64_t index;
};
struct RizEngineWaitCompletionV1 {
    std::uint32_t abiVersion, byteSize, flags, outputFlags;
    std::uint64_t rawRax;
    // Exact original completion flags/raw return, not normalized booleans.
    std::uint32_t externalFlags, externalRawReturn;
    void* outputNode;
    std::uint64_t phases;
};
struct RizEngineWaitPhaseFrameV1 {
    std::uint32_t abiVersion, byteSize, kind, flags;
    std::uint32_t slot, threadId, depth, scopeKind;
    std::uint64_t epoch, sequence, externalSequence, parentSequence, rootSequence;
    std::uint64_t operationSequence, eventSequence, sourceSequence;
    const void* sitePc; const void* relayReturnPc;
    void* wait; void* node; void* manager; std::uint64_t index;
    void* oldWait; void* actualOwner; void* actualCleanup; void* actualPayload;
    RizEngineWaitTupleV1 tuple;
    void* nodeOwner; std::uint64_t enumHandle; void* enumCache;
    std::uint32_t enumType; std::int32_t referenceCount;
    std::uint32_t nodeSnapshotFlags, stageFlags;
    void* actualCallback;
    // Initially the enclosing actual scope's caller. After a private actual
    // callback/cleanup claim, this is that child's exact original CALL return
    // PC, sourceExternalSequence is its original Node/Scheduler sequence, and
    // rawRax/completionFlags become its completed return/flags at the next phase.
    const void* originalCaller;
    std::uint64_t rawRax;
    // PublishBegin alone uses rawRax to carry zero-extended actual EBX ownerId.
    std::uint32_t completionFlags, reserved;
    std::uint64_t sourceExternalSequence;
};
struct RizEngineWaitSiteV1 { const void* sitePc; const void* relayReturnPc; };
struct RizEngineWaitConfigV1 {
    std::uint32_t abiVersion, byteSize;
    void* targets[3]; void* originals[3];
    void* nodeTargets[4]; void* schedulerTargets[6];
    std::uint32_t mainThreadId, reserved;
    RizEngineWaitSiteV1 sites[18];
};
using RizEngineWaitEnterV1 = std::uintptr_t(__cdecl*)(const RizEngineWaitFrameV1*, void*);
using RizEngineWaitExitV1 = void(__cdecl*)(const RizEngineWaitFrameV1*, std::uintptr_t,
    const RizEngineWaitCompletionV1*, void*);
using RizEngineWaitPhaseCallbackV1 = void(__cdecl*)(const RizEngineWaitPhaseFrameV1*, void*);
struct RizEngineWaitObserverV1 {
    std::uint32_t abiVersion, byteSize;
    RizEngineWaitEnterV1 enter; RizEngineWaitExitV1 exit;
    RizEngineWaitPhaseCallbackV1 phase; void* user;
};
struct RizEngineWaitInfoV1 {
    std::uint32_t abiVersion, byteSize, slot, configured;
    std::uint32_t attached, unknown, mainThreadId, reserved;
    std::uint64_t epoch, entered, exited, inFlight, faults, snapshotFaults, phases, unmatched;
    void* targets[3]; void* originals[3]; void* entries[3]; void* phaseEntry;
};
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetAbiVersion();
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetLimit();
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetFrameSize();
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetCompletionSize();
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetPhaseFrameSize();
RIZEWC_EXPORT void* __cdecl RizEngineWaitContext_GetEntry(std::uint32_t kind, std::uint32_t slot);
RIZEWC_EXPORT void* __cdecl RizEngineWaitContext_GetPhaseEntry();
// Configure once: 0 ready/1 invalid/2 claimed/3 duplicate identity/4 exhaustion.
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_Configure(std::uint32_t slot, const RizEngineWaitConfigV1*);
// Attach once: 0 ready/1 invalid/2 unconfigured/3 epoch mismatch/4 attempted/5 Unknown.
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_Attach(std::uint32_t slot, std::uint64_t epoch, const RizEngineWaitObserverV1*);
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetInfo(std::uint32_t slot, RizEngineWaitInfoV1*);

// Link-private integration only. Macro excludes every definition/dependency in
// the old Judge DLL. No exported scope setter/claim API exists. The exact actual
// dispatcher owns storage until Pop, including its Enter/Exit observer phases.
struct RizEngineNodeFrameV1; struct RizEngineNodeCompletionV1;
struct RizEngineSchedulerFrameV1; struct RizEngineSchedulerCompletionV1;
#ifdef RIZ_ENGINE_WAIT_CONTEXT
namespace RizEngineWaitPrivate {
struct alignas(16) BoundaryStorage { unsigned char bytes[2048]; };
void BeginNode(BoundaryStorage*, const RizEngineNodeFrameV1*, const void* actualTarget);
void BeginScheduler(BoundaryStorage*, const RizEngineSchedulerFrameV1*, const void* actualTarget);
// AFTER CopyBefore/PrepareSnapshot, BEFORE actual typed Enter. Wait physical
// registration precedes old consumers: their actual call is bound subsequently.
void Entered(BoundaryStorage*);
void Original(BoundaryStorage*);
void Closing(BoundaryStorage*); // BEFORE side/typed completion or observers
void CompleteNode(BoundaryStorage*, const RizEngineNodeCompletionV1*);
void CompleteScheduler(BoundaryStorage*, const RizEngineSchedulerCompletionV1*);
// Complete* is AFTER the old typed Exit's actual Complete/Dispose has finished;
// it never claims a managed call existed when Wait Enter ran.
void Pop(BoundaryStorage*); // AFTER actual typed Exit, always from finally
// Old scheduler stack may not see intervening Wait/Node calls. Permit its
// source lookup only through an exact uninterrupted private scheduler chain.
bool CanUseSchedulerSource(BoundaryStorage*, const RizEngineSchedulerFrameV1* actualSource, bool requireDirect);
}
#endif
// All 13 actual scopes are physical masks (including foreign/unattached calls).
// Observer freezes once at Begin; later Attach cannot capture that scope or its
// phases. Only direct original-phase private sources can claim callback/cleanup.
// Snapshot arbitrary wait tuples; node scalars only for actual configured
// callback/cleanup targets or actual Node input. Never read node after original,
// old wait after reference return, or batch object at End. No all-writers,
// cancellation, future-closure, patch installation or production admission.
static_assert(sizeof(RizEngineWaitTupleV1) == 32);
static_assert(sizeof(RizEngineWaitFrameV1) == 240);
static_assert(offsetof(RizEngineWaitFrameV1, epoch) == 32);
static_assert(offsetof(RizEngineWaitFrameV1, externalSequence) == 64);
static_assert(offsetof(RizEngineWaitFrameV1, arg0) == 88);
static_assert(offsetof(RizEngineWaitFrameV1, tuple) == 192);
static_assert(sizeof(RizEngineWaitCompletionV1) == 48);
static_assert(offsetof(RizEngineWaitCompletionV1, rawRax) == 16);
static_assert(sizeof(RizEngineWaitPhaseFrameV1) == 288);
static_assert(offsetof(RizEngineWaitPhaseFrameV1, operationSequence) == 72);
static_assert(offsetof(RizEngineWaitPhaseFrameV1, sitePc) == 96);
static_assert(offsetof(RizEngineWaitPhaseFrameV1, tuple) == 176);
static_assert(offsetof(RizEngineWaitPhaseFrameV1, sourceExternalSequence) == 280);
static_assert(sizeof(RizEngineWaitConfigV1) == 432);
static_assert(offsetof(RizEngineWaitConfigV1, sites) == 144);
static_assert(sizeof(RizEngineWaitObserverV1) == 40);
static_assert(sizeof(RizEngineWaitInfoV1) == 176);
