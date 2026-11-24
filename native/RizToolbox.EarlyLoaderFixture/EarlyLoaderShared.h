#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#include <cstddef>
#include <cstdint>

// Self-owned fixture protocol only. No field is a game installation credential.
inline constexpr std::uint32_t RizEarlyMagic = 0x594c5245, RizEarlyVersion = 1;
inline constexpr std::uint32_t RizEarlyEventCapacity = 4096, RizEarlyPrefixBytes = 32;
inline constexpr std::uint32_t RizEarlySlot = 29;
inline constexpr wchar_t RizEarlyMappingEnvironment[] = L"RIZ_EARLY_MAPPING_NAME";
enum RizEarlyState : LONG { RizEarlyCreated, RizEarlyRecording, RizEarlyInstalling,
    RizEarlyFixtureReady, RizEarlyDisabledNoWrite, RizEarlyUnknown, RizEarlyCompleted };
enum RizEarlyMode : std::uint32_t { RizEarlyNormal, RizEarlyPriorCall, RizEarlyLate,
    RizEarlyWorker, RizEarlyReentry, RizEarlyDuplicate, RizEarlyWrongModule,
    RizEarlyWrongHash, RizEarlyWrongArchitecture, RizEarlyWrongPrefix,
    RizEarlyWrongAbi, RizEarlyMissingDependency, RizEarlyIncompleteLog,
    RizEarlyDependencyNotReady };
enum RizEarlyFaultStage : std::uint32_t { RizEarlyFaultNone, RizEarlyBeforePrepare,
    RizEarlyAfterPrepare, RizEarlyBeforeConfigure, RizEarlyAfterConfigure,
    RizEarlyBeforeCommit, RizEarlyAfterCommit };
enum RizEarlyPhase : std::uint32_t { RizEarlyNoPhase, RizEarlyTlsPhase,
    RizEarlyCPhase, RizEarlyCppPhase, RizEarlyDllPhase, RizEarlyHostPhase,
    RizEarlyWorkerPhase, RizEarlySehPhase, RizEarlyCppExceptionPhase,
    RizEarlyHostAfterAttachPhase };
enum RizEarlyEventType : std::uint32_t {
    RizEarlyRecorderAttach = 1, RizEarlyProxyAttachBegin, RizEarlyDependencyVerified,
    RizEarlyTargetValidated, RizEarlyPrepareBegin, RizEarlyPrepareEnd,
    RizEarlyConfigureBegin, RizEarlyConfigureEnd, RizEarlyCommitBegin,
    RizEarlyCommitEnd, RizEarlyReady, RizEarlyReject, RizEarlyTerminating,
    RizEarlyInstallDeferred, RizEarlyProxyAnchor, RizEarlyDependencyAttach, RizEarlyOverflowProbe,
    RizEarlyTargetEnter = 20, RizEarlyTargetExit, RizEarlyPhaseBegin,
    RizEarlyPhaseEnd, RizEarlyInitializerEnter, RizEarlyInitializerExit,
    RizEarlyOriginalException, RizEarlyOriginalCaught, RizEarlyTargetArguments,
    RizEarlyWorkerRequested = 30, RizEarlyWorkerStarted, RizEarlyWorkerFinished,
    RizEarlyObserverEnter = 40, RizEarlyObserverExit, RizEarlyObserverAttached,
    RizEarlyObserverArguments, RizEarlyObserverSnapshot, RizEarlyObserverCompletion, RizEarlyObserverIdentity,
    RizEarlyHostCompleted = 50
};
enum RizEarlyFailure : std::uint32_t { RizEarlyFailureNone, RizEarlyBadMapping,
    RizEarlyBadConfig, RizEarlyEventOverflow, RizEarlyAlreadyClaimed,
    RizEarlyPriorExecution, RizEarlyBadDependency, RizEarlyBadAbi,
    RizEarlyBadModule, RizEarlyBadHash, RizEarlyBadImage, RizEarlyBadPrefix,
    RizEarlyBadMemory, RizEarlyBadTarget, RizEarlyPrepareFailed,
    RizEarlyConfigureFailed, RizEarlyCommitFailed, RizEarlyPostWriteMismatch,
    RizEarlyInjectedFailure, RizEarlyInitializerRace, RizEarlyUnexpectedException,
    RizEarlyHostAssertion };

struct RizEarlyImageConfigV1 {
    std::uint64_t fileBytes;
    std::uint32_t timeDateStamp, reserved;
    unsigned char sha256[32];
};
struct RizEarlyConfigV1 {
    std::uint32_t version, byteSize, mode, faultStage, faultIndex, flags;
    std::uint32_t eventLimit, expectedHelperAbi;
    // Ordinary, non-forwarded modules: Importer, helper, pinned Dobby.
    RizEarlyImageConfigV1 images[3];
};
struct RizEarlyHeaderV1 {
    std::uint32_t magic, version, byteSize, eventCapacity;
    volatile LONG64 nextSequence;
    volatile LONG eventCount, overflow, state, failure, writeAttempts, writeCompletions;
    volatile LONG childPid, producerClaim, importerInitializers, producerThread;
    volatile LONG targetCalls[4];
    std::uint64_t recorderModule, proxyModule, importerModule, helperModule, dobbyModule;
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
struct RizEarlySharedV1 {
    RizEarlyHeaderV1 header;
    RizEarlyTupleV1 tuples[4];
    RizEarlyEventV1 events[RizEarlyEventCapacity];
};

static_assert(sizeof(RizEarlyImageConfigV1) == 48 && sizeof(RizEarlyConfigV1) == 176);
static_assert(sizeof(RizEarlyHeaderV1) == 360 && offsetof(RizEarlyHeaderV1, config) == 152);
static_assert(offsetof(RizEarlyHeaderV1, targetCalls) == 64);
static_assert(sizeof(RizEarlyTupleV1) == 216 && offsetof(RizEarlyTupleV1, originalBytes) == 104);
static_assert(sizeof(RizEarlyEventV1) == 104 && offsetof(RizEarlyEventV1, module) == 40);
static_assert(offsetof(RizEarlySharedV1, tuples) == 360 && offsetof(RizEarlySharedV1, events) == 1224);
static_assert(sizeof(RizEarlySharedV1) == 427208);

#ifdef RIZ_EARLY_RECORDER_BUILD
#define RIZ_EARLY_RECORDER_API extern "C" __declspec(dllexport)
#else
#define RIZ_EARLY_RECORDER_API extern "C" __declspec(dllimport)
#endif
RIZ_EARLY_RECORDER_API RizEarlySharedV1* __cdecl RizEarlyRecorder_GetShared();
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
