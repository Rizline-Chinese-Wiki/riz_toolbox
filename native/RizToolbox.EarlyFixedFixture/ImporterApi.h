#pragma once
#include <cstddef>
#include <cstdint>
#include "EarlyFixedProfile.h"

#ifdef RIZ_EARLY_IMPORTER_BUILD
#define RIZ_EARLY_IMPORTER_API extern "C" __declspec(dllexport)
#define RIZ_EARLY_TARGET_API extern "C"
#else
#define RIZ_EARLY_IMPORTER_API extern "C" __declspec(dllimport)
#define RIZ_EARLY_TARGET_API extern "C" __declspec(dllimport)
#endif

inline constexpr std::uint32_t RizFixedOwnedExecuteReturn = 0xa5c3fe81u;
inline constexpr std::uint32_t RizFixedOwnedStartReturn = 0x81u;
inline constexpr std::uint32_t RizFixedOwnedReleaseReturn = 0xe7u;
inline constexpr std::uint8_t RizFixedOwnedExecuteOutput = 0x9du;
inline constexpr std::uint64_t OwnedEarlyExceptionPayload = 0xfedcba9876543210ull;
inline constexpr std::uint32_t OwnedEarlySehCode = 0xe0454c53u;

// Historical V2 type names retain their 112-byte layout. Workload records use
// RizEarlyWorkloadVersion (default 2, independent Kernel profile 3), so records
// from the other profile cannot be silently adopted. Helper frames retain
// their independent ABI-1 layouts and transport ABI 2.
// A native scalar expectation for this one actual invocation. Host observers
// may inspect it only outside the loader window. Pointers remain owned by the
// fixture until exit; none are game objects or game identity credentials.
struct OwnedEarlyCallV2 {
    std::uint32_t version, byteSize, phase, kind, nested, faultMode;
    std::uint64_t callId;
    void* args[4];
    void* node;
    void* owner;
    std::uint64_t enumHandle;
    void* enumCache;
    std::uint32_t enumType;
    std::int32_t referenceCount;
    std::uint32_t rawReturn, reserved;
};
struct OwnedEarlyPhaseReportV2 {
    std::uint32_t version, byteSize, phase, faultKind, faultMode, reserved;
    std::uint64_t phaseId, checks, failures;
    std::uint32_t originals[4], nested[4], returned[4], caught[4];
};
struct OwnedEarlySnapshotV2 {
    std::uint32_t version, byteSize, initializerMask, initializedPhases;
    std::uint64_t checks, failures, originals[4];
    std::uint64_t phases, workerRequests, workerStarts, workerFinishes;
    std::uint32_t reserved[4];
};
static_assert(sizeof(OwnedEarlyCallV2) == 112);
static_assert(offsetof(OwnedEarlyCallV2, args) == 32);
static_assert(offsetof(OwnedEarlyCallV2, node) == 64);
static_assert(sizeof(OwnedEarlyPhaseReportV2) == 112);
static_assert(sizeof(OwnedEarlySnapshotV2) == 112);

RIZ_EARLY_TARGET_API std::uint8_t __cdecl RizFixedOwnedStart(void* owner,
    void* enumerator, void* nameArgument, void** outputNode);
RIZ_EARLY_TARGET_API std::uint32_t __cdecl RizFixedOwnedExecute(void* node,
    std::uint8_t* executionOutput);
RIZ_EARLY_TARGET_API std::uint8_t __cdecl RizFixedOwnedRelease(void* node);
RIZ_EARLY_TARGET_API void __cdecl RizFixedOwnedDetach(void* node);

RIZ_EARLY_IMPORTER_API void* __cdecl OwnedEarlyGetTarget(std::uint32_t kind);
RIZ_EARLY_IMPORTER_API const OwnedEarlyCallV2* __cdecl OwnedEarlyGetCurrent();
// faultKind 4 / faultMode 0 means normal. For fault modes 1(SEH),2(C++), only
// that direct kind faults; Start's synchronous nested calls remain normal.
// Exception-bearing phases are refused until all four initializers completed.
RIZ_EARLY_IMPORTER_API std::uint32_t __cdecl OwnedEarlyRunPhase(std::uint32_t phase,
    std::uint32_t faultKind, std::uint32_t faultMode, OwnedEarlyPhaseReportV2* report);
RIZ_EARLY_IMPORTER_API std::uint32_t __cdecl OwnedEarlyGetSnapshot(OwnedEarlySnapshotV2* snapshot);
// In mode Worker the actual DllMain-created thread handle is retained. This
// accessor never waits: only the host, after loader return, joins that worker.
RIZ_EARLY_IMPORTER_API void* __cdecl OwnedEarlyGetInitializerWorker();
