#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#if defined(RIZ_ENGINE_OWNED_TARGETS_BUILD)
#ifndef RIZ_FIXED_TARGETS_BUILD
#define RIZ_FIXED_TARGETS_BUILD
#endif
#define RIZ_ENGINE_FIXTURE_API extern "C" __declspec(dllexport)
#else
#define RIZ_ENGINE_FIXTURE_API extern "C" __declspec(dllimport)
#endif
#include "../RizToolbox.FixedPublisher/FixedFixtureApi.h"
#include "../RizToolbox.EngineBootstrap/EnginePublication.h"
#include <cstddef>

namespace RizEngineOwned {
constexpr std::uint32_t Version=1,Capacity=256;
enum Phase:std::uint32_t { Tls=4,C=5,Cpp=6,Dll=7,Outside=11 };
struct Row {
    std::uint32_t version,byteSize,phase,kind;
    std::uint32_t mode,threadId,returned,rawReturn;
    std::uint32_t caught,exceptionCode,exceptionFlags,exceptionParameters;
    std::uint32_t verifyBefore,verifyAfter,infoBeforeResult,infoAfterResult;
    std::uint64_t sequence,target,args[4];
    std::uint32_t calls[4];
    std::uint32_t releaseCount,releaseZeroFlag,detachZero,diagnosticsFailed;
    std::uint64_t exceptionPayload,exceptionKind;
    std::uint32_t infoBeforeErrorBefore,infoBeforeErrorAfter,infoAfterErrorBefore,infoAfterErrorAfter;
    std::uint64_t bodyBefore[4],bodyAfter[4];
    std::uint32_t outputByte,outputNodeMatches,nodeRefBefore,nodeRefAfter;
    RizEnginePublicationV4 before,after;
};
struct Snapshot {
    std::uint32_t version,byteSize,phaseMask,rowCount;
    std::uint64_t checks,failures,bodyCounters[4];
    Row rows[Capacity];
};
static_assert(sizeof(RizFixedCase)==280);
static_assert(offsetof(Row,sequence)==64&&offsetof(Row,calls)==112);
static_assert(offsetof(Row,before)==256&&offsetof(Row,after)==4352);
static_assert(sizeof(Row)==8448);
static_assert(offsetof(Snapshot,rows)==64&&sizeof(Snapshot)==2162752);
}

// Only the four fixed self-owned targets can be selected. Each output is a
// complete observation, including actual V4 snapshots before and after calls.
// Return 0 completed fixture invocation, 1 invalid/assertion failure, 2 busy/full.
RIZ_ENGINE_FIXTURE_API std::uint32_t __cdecl RizEngineOwned_Run(std::uint32_t kind,std::uint32_t mode,RizEngineOwned::Row* output);
RIZ_ENGINE_FIXTURE_API std::uint32_t __cdecl RizEngineOwned_GetSnapshot(RizEngineOwned::Snapshot* output);
RIZ_ENGINE_FIXTURE_API void* __cdecl RizEngineOwned_GetTarget(std::uint32_t kind);
RIZ_ENGINE_FIXTURE_API std::uint32_t __cdecl RizEngineOwned_GetBodyCounters(std::uint64_t* output);
#if defined(RIZ_ENGINE_OWNED_TARGETS_BUILD)
extern "C" void __cdecl RizEngineOwned_RunPhase(std::uint32_t phase);
#endif
