#pragma once
#include <cstddef>
#include <cstdint>

// Independent V5 build contract. Generated facts never grant admission. This
// header has no mutable registration, runtime profile selection, or Ready API.
namespace RizEngineTransportCompileContract {
inline constexpr std::uint32_t Version=1, ModuleCount=5, EdgeCount=44;
inline constexpr std::uint32_t Bootstrap=0, Kernel=1, Target=2, Executable=3, Doorstop=4;
inline constexpr std::uint32_t SourceRfCount=27, OriginalRfFirst=27, RelayRfFirst=40, MinimumRuntimeFunctionCount=63;
// Synthetic chained primary records, if required by the exact relay contract,
// are explicit records following the 23 relay records, never hidden ancestors.
inline constexpr std::uint32_t SyntheticRfFirst=63, RuntimeFunctionCapacity=128;
enum HashPolicy:std::uint32_t { ObserveHeldSelf=1, PinWholeFile=2 };
struct ImageIdentity {
    const wchar_t* module;
    std::uint64_t fileBytes;
    std::uint32_t imageSize,timeDateStamp,entryRva,exceptionRva;
    std::uint8_t sha256[32];
    std::uint32_t hashPolicy,reserved;
};
struct ImportEdge {
    std::uint32_t source,destination;
    const char* symbol;
    std::uint32_t lookupRva,iatRva,exportRva;
};
inline constexpr const char* Symbols[EdgeCount]{
    "UnityMain2","WinHttpOpen","WinHttpGetProxyForUrl","WinHttpGetIEProxyConfigForCurrentUser","WinHttpCloseHandle",
    "RizEngineTransport_GetVersion","RizEngineTransport_GetInfoV5","RizEngineTransport_VerifyV5","RizEngineTransport_AbortAfterPublication",
    "RizEngineNodeContext_GetAbiVersion","RizEngineNodeContext_GetLimit","RizEngineNodeContext_GetFrameSize","RizEngineNodeContext_GetCompletionSize",
    "RizEngineNodeContext_GetEntry","RizEngineNodeContext_Configure","RizEngineNodeContext_Attach","RizEngineNodeContext_GetInfo",
    "RizEngineNodeContext_GetInlineAbiVersion","RizEngineNodeContext_GetInlineLimit","RizEngineNodeContext_GetInlineFrameSize","RizEngineNodeContext_GetInlineEntry",
    "RizEngineNodeContext_ConfigureInline","RizEngineNodeContext_AttachInline","RizEngineNodeContext_GetInlineInfo",
    "RizEngineSchedulerContext_GetAbiVersion","RizEngineSchedulerContext_GetLimit","RizEngineSchedulerContext_GetFrameSize","RizEngineSchedulerContext_GetCompletionSize",
    "RizEngineSchedulerContext_GetRowFrameSize","RizEngineSchedulerContext_GetEntry","RizEngineSchedulerContext_GetDrainRowEntry","RizEngineSchedulerContext_Configure",
    "RizEngineSchedulerContext_Attach","RizEngineSchedulerContext_GetInfo","RizEngineWaitContext_GetAbiVersion","RizEngineWaitContext_GetLimit",
    "RizEngineWaitContext_GetFrameSize","RizEngineWaitContext_GetCompletionSize","RizEngineWaitContext_GetPhaseFrameSize","RizEngineWaitContext_GetEntry",
    "RizEngineWaitContext_GetPhaseEntry","RizEngineWaitContext_Configure","RizEngineWaitContext_Attach","RizEngineWaitContext_GetInfo"
};
constexpr std::uint32_t Source(std::uint32_t edge){return edge==0?Executable:edge<5?Target:edge<9?Doorstop:Bootstrap;}
constexpr std::uint32_t Destination(std::uint32_t edge){return edge==0?Target:edge<5?Doorstop:edge<9?Bootstrap:Kernel;}
// Self-source ILT/IAT and self-destination EAT RVAs alone are zero in generated
// input: its own link has not happened yet. Runtime resolution must be unique,
// ordinary and directly inside the actual destination; no loader lookup fills
// missing edges. All other edge RVAs are exact nonzero disk facts.
static_assert(sizeof(ImageIdentity)==72&&sizeof(ImportEdge)==32);
}
