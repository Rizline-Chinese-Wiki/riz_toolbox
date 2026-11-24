#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#include <cstddef>
#include <cstdint>
#include "EarlyFixedProfile.h"
#include "../RizToolbox.FixedPublisher/FixedPublisher.h"

// New no-Dobby early fixture protocol. Historical V1 C++ type names preserve
// the common prefix source API only. Default wire is V2; the independently
// named EngineNodeKernel profile uses V3 with the same bounded record layout.
// No field is a game installation credential.
inline constexpr std::uint32_t RizEarlyEventCapacity = 4096, RizEarlyPrefixBytes = 32;
inline constexpr std::uint32_t RizEarlySlot = 28;
static_assert(RizFixedVersion == (RizEarlyKernelProfile ? 2u : 1u));
enum RizEarlyState : LONG { RizEarlyCreated, RizEarlyRecording, RizEarlyInstalling,
    RizEarlyFixtureReady, RizEarlyDisabledNoWrite, RizEarlyUnknown, RizEarlyCompleted };
enum RizEarlyMode : std::uint32_t { RizEarlyNormal, RizEarlyPriorCall, RizEarlyLate,
    RizEarlyWorker, RizEarlyReentry, RizEarlyDuplicate, RizEarlyWrongModule,
    RizEarlyWrongHash, RizEarlyWrongArchitecture, RizEarlyWrongPrefix,
    RizEarlyWrongAbi, RizEarlyMissingDependency, RizEarlyIncompleteLog,
    RizEarlyDependencyNotReady, RizEarlyBaselineOnly, RizEarlyCaptureBusyProbe };
// faultStage0..14 maps exactly to RizFixedFaultStageV1, faultIndex=kind.
// flags is reserved and must remain zero. PartialWrite uses one actual byte.
using RizEarlyFaultStage = RizFixedFaultStageV1;
enum RizEarlyPhase : std::uint32_t { RizEarlyNoPhase, RizEarlyTlsPhase,
    RizEarlyCPhase, RizEarlyCppPhase, RizEarlyDllPhase, RizEarlyHostPhase,
    RizEarlyWorkerPhase, RizEarlySehPhase, RizEarlyCppExceptionPhase,
    RizEarlyHostAfterAttachPhase, RizEarlyBaselineSehPhase, RizEarlyBaselineCppPhase };
enum RizEarlyEventType : std::uint32_t {
    RizEarlyRecorderAttach = 1, RizEarlyProxyAttachBegin, RizEarlyDependencyVerified,
    RizEarlyTargetValidated, RizEarlyPrepareBegin, RizEarlyPrepareEnd,
    RizEarlyConfigureBegin, RizEarlyConfigureEnd, RizEarlyCommitBegin,
    RizEarlyCommitEnd, RizEarlyReady, RizEarlyReject, RizEarlyTerminating,
    RizEarlyInstallDeferred, RizEarlyProxyAnchor, RizEarlyDependencyAttach, RizEarlyOverflowProbe,
    RizEarlyTargetEnter = 20, RizEarlyTargetExit, RizEarlyPhaseBegin,
    RizEarlyPhaseEnd, RizEarlyInitializerEnter, RizEarlyInitializerExit,
    RizEarlyOriginalException, RizEarlyOriginalCaught, RizEarlyTargetArguments, RizEarlyOriginalExceptionDetails,
    RizEarlyWorkerRequested = 30, RizEarlyWorkerStarted, RizEarlyWorkerFinished,
    RizEarlyObserverEnter = 40, RizEarlyObserverExit, RizEarlyObserverAttached,
    RizEarlyObserverArguments, RizEarlyObserverSnapshot, RizEarlyObserverCompletion, RizEarlyObserverIdentity,
    RizEarlyTargetDiagnostics, RizEarlyOriginalTypedCaught,
    RizEarlyHostCompleted = 50,
    RizEarlyPrepareGroupBegin = 60, RizEarlyPrepareGroupEnd,
    RizEarlyBindGroupBegin, RizEarlyBindGroupEnd, RizEarlySnapshotCaptured, RizEarlyCaptureBusyObserved
};
enum RizEarlyFailure : std::uint32_t { RizEarlyFailureNone, RizEarlyBadMapping,
    RizEarlyBadConfig, RizEarlyEventOverflow, RizEarlyAlreadyClaimed,
    RizEarlyPriorExecution, RizEarlyBadDependency, RizEarlyBadAbi,
    RizEarlyBadModule, RizEarlyBadHash, RizEarlyBadImage, RizEarlyBadPrefix,
    RizEarlyBadMemory, RizEarlyBadTarget, RizEarlyPrepareFailed,
    RizEarlyConfigureFailed, RizEarlyCommitFailed, RizEarlyPostWriteMismatch,
    RizEarlyInjectedFailure, RizEarlyInitializerRace, RizEarlyUnexpectedException,
    RizEarlyHostAssertion, RizEarlyCaptureFault };

struct RizEarlyImageConfigV1 {
    std::uint64_t fileBytes;
    std::uint32_t timeDateStamp, reserved;
    unsigned char sha256[32];
};
struct RizEarlyConfigV1 {
    std::uint32_t version, byteSize, mode, faultStage, faultIndex, flags;
    std::uint32_t eventLimit, expectedHelperAbi;
    // Ordinary non-forwarded images in order: fixed targets, helper, publisher.
    RizEarlyImageConfigV1 images[3];
};
struct RizEarlyHeaderV1 {
    std::uint32_t magic, version, byteSize, eventCapacity;
    volatile LONG64 nextSequence;
    volatile LONG eventCount, overflow, state, failure, writeAttempts, writeCompletions;
    volatile LONG childPid, producerClaim, importerInitializers, producerThread;
    volatile LONG targetCalls[4];
    std::uint64_t recorderModule, proxyModule, importerModule, helperModule, publisherModule;
    volatile LONG64 recorderAttachSequence, proxyEnterSequence, readySequence, terminationSequence;
    RizEarlyConfigV1 config;
    std::uint32_t reserved[8];
};
struct RizEarlyTupleV1 {
    std::uint32_t version, byteSize, kind, slot;
    std::uint64_t module, target, original, entry, epoch;
    std::uint32_t rva, originalProtect, publishedProtect, reserved;
    std::uint64_t prepareSequence, configureSequence, writeAttemptSequence, writeCompleteSequence;
    unsigned char originalBytes[32], publishedBytes[32], moduleSha256[32];
    volatile LONG prepared, configured, committed, failure;
};
struct RizEarlyEventV1 {
    // Zero until the entire row has been copied. Published last by Interlocked.
    volatile LONG64 sequence;
    std::uint32_t pid, tid, type, phase, kind, index, result, flags;
    std::uint64_t module, target, original, entry, epoch, value0, value1, caller;
};
struct RizEarlyActualMemoryRowV2 {
    std::uint32_t kind, protection;
    std::uint64_t target, allocationBase;
    unsigned char bytes[32];
};
struct RizEarlyActualMemoryV2 {
    // Independent scalar-memory record stays version 1 in both wire profiles.
    std::uint32_t version, byteSize, systemPageSize, reserved;
    RizEarlyActualMemoryRowV2 rows[4];
};
struct RizEarlyPublisherSnapshotV2 {
    std::uint32_t version, byteSize;
    std::uint64_t captureCalls;
    std::uint32_t infoResult, memoryResult, publisherState, flags;
};
// Busy and capture errors are permanent for this owned process. Bit 8 covers
// either an Info query failure or an unreadable independent memory capture;
// infoResult/memoryResult retain the actual individual outcomes.
enum RizEarlySnapshotFlagsV2 : std::uint32_t { RizEarlyInfoAvailable = 1,
    RizEarlyMemoryAvailable = 2, RizEarlyCaptureBusy = 4, RizEarlyMemoryFault = 8 };
struct RizEarlySharedV1 {
    RizEarlyHeaderV1 header;
    RizEarlyTupleV1 tuples[4];
    RizEarlyEventV1 events[RizEarlyEventCapacity];
    RizFixedInfoV1 publisherInfo;
    RizEarlyActualMemoryV2 actualMemory;
    RizEarlyPublisherSnapshotV2 publisherSnapshot;
};

static_assert(sizeof(RizEarlyImageConfigV1) == 48 && sizeof(RizEarlyConfigV1) == 176);
static_assert(sizeof(RizEarlyHeaderV1) == 360 && offsetof(RizEarlyHeaderV1, config) == 152);
static_assert(offsetof(RizEarlyHeaderV1, targetCalls) == 64);
static_assert(sizeof(RizEarlyTupleV1) == 216 && offsetof(RizEarlyTupleV1, originalBytes) == 104);
static_assert(sizeof(RizEarlyEventV1) == 104 && offsetof(RizEarlyEventV1, module) == 40);
static_assert(offsetof(RizEarlySharedV1, tuples) == 360 && offsetof(RizEarlySharedV1, events) == 1224);
static_assert(sizeof(RizEarlyActualMemoryRowV2) == 56 && sizeof(RizEarlyActualMemoryV2) == 240);
static_assert(sizeof(RizEarlyPublisherSnapshotV2) == 32);
static_assert(offsetof(RizEarlySharedV1, publisherInfo) == 427208);
static_assert(offsetof(RizEarlySharedV1, actualMemory) == 448848);
static_assert(offsetof(RizEarlySharedV1, publisherSnapshot) == 449088);
static_assert(sizeof(RizEarlySharedV1) == 449120);

#ifdef RIZ_EARLY_RECORDER_BUILD
#define RIZ_EARLY_RECORDER_API extern "C" __declspec(dllexport)
#else
#define RIZ_EARLY_RECORDER_API extern "C" __declspec(dllimport)
#endif
RIZ_EARLY_RECORDER_API RizEarlySharedV1* __cdecl RizEarlyRecorder_GetShared();
// Capture actual publisher GetInfo and independent live source bytes/protection.
// No event is emitted here, so overflow/failure capture cannot recurse.
RIZ_EARLY_RECORDER_API std::uint32_t __cdecl RizEarlyRecorder_Capture();
RIZ_EARLY_RECORDER_API std::uint64_t __cdecl RizEarlyRecorder_Event(std::uint32_t type,
    std::uint32_t phase, std::uint32_t kind, std::uint32_t index, std::uint32_t result,
    std::uint32_t flags, std::uint64_t module, std::uint64_t target,
    std::uint64_t original, std::uint64_t entry, std::uint64_t epoch,
    std::uint64_t value0, std::uint64_t value1, std::uint64_t caller);
// Terminates only this owned process. Evidence uncertainty or any attempted
// machine-code write is sticky Unknown; otherwise records DisabledNoWrite.
RIZ_EARLY_RECORDER_API __declspec(noreturn) void __cdecl RizEarlyRecorder_Fail(std::uint32_t failure, std::uint32_t uncertain);

#ifdef RIZ_EARLY_PROXY_BUILD
#define RIZ_EARLY_PROXY_API extern "C" __declspec(dllexport)
#else
#define RIZ_EARLY_PROXY_API extern "C" __declspec(dllimport)
#endif
RIZ_EARLY_PROXY_API std::uint32_t __cdecl RizEarlyProxy_Anchor();
RIZ_EARLY_PROXY_API std::uint32_t __cdecl RizEarlyProxy_RunInstall();
RIZ_EARLY_PROXY_API const RizEarlyTupleV1* __cdecl RizEarlyProxy_Inspect(std::uint32_t kind);
