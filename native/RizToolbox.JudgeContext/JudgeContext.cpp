#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstring>
#include "JudgeContext.h"

extern "C" void RizJudgeContext_Entry();
namespace {
RizJudgeConfigV1 config{};
volatile LONG configured = 0; // 0 empty, -1 publication in progress, 1 immutable/ready
volatile LONG enabled = 0;
volatile LONG64 entered = 0, exited = 0, inFlight = 0, enterFaults = 0, exitFaults = 0;

std::uintptr_t EnterObserver(const RizJudgeFrameV1* frame, bool* faulted) {
    __try {
        return config.enter(frame, config.userContext);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *faulted = true;
        InterlockedIncrement64(&enterFaults);
        return 0;
    }
}
void ExitObserver(const RizJudgeFrameV1* frame, std::uintptr_t token, std::uint32_t flags) {
    __try {
        config.exit(frame, token, flags, config.userContext);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        // Observer failure cannot mask or replace an original exception.
        InterlockedIncrement64(&exitFaults);
    }
}
}

RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_Configure(const RizJudgeConfigV1* value) {
    if (!value || value->abiVersion != 1 || value->byteSize != sizeof(RizJudgeConfigV1) ||
        !value->original || !value->enter || !value->exit || value->reserved || value->initiallyEnabled > 1)
        return 1;
    if (InterlockedCompareExchange(&configured, -1, 0) != 0) return 2;
    config = *value;
    InterlockedExchange(&enabled, static_cast<LONG>(value->initiallyEnabled));
    InterlockedExchange(&configured, 1); // publish after the entire immutable configuration
    return 0;
}
RIZJC_EXPORT void* __cdecl RizJudgeContext_GetEntry() {
    return reinterpret_cast<void*>(&RizJudgeContext_Entry);
}
RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_GetFrameSize() { return sizeof(RizJudgeFrameV1); }
RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_SetEnabled(std::uint32_t value) {
    if (value > 1 || InterlockedCompareExchange(&configured, 1, 1) != 1) return 1;
    InterlockedExchange(&enabled, static_cast<LONG>(value));
    return 0;
}
RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_GetStats(RizJudgeStatsV1* value) {
    if (!value || value->abiVersion != 1 || value->byteSize != sizeof(RizJudgeStatsV1)) return 1;
    value->entered = static_cast<std::uint64_t>(InterlockedCompareExchange64(&entered, 0, 0));
    value->exited = static_cast<std::uint64_t>(InterlockedCompareExchange64(&exited, 0, 0));
    value->inFlight = static_cast<std::uint64_t>(InterlockedCompareExchange64(&inFlight, 0, 0));
    value->enterFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&enterFaults, 0, 0));
    value->exitFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&exitFaults, 0, 0));
    return 0;
}
RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_ReadConfigurationV1(RizJudgeConfigurationV1* value) {
    if (!value || value->abiVersion != 1 || value->byteSize != sizeof(RizJudgeConfigurationV1)) return 1;
    if (InterlockedCompareExchange(&configured, 1, 1) != 1) return 2;
    RizJudgeConfigurationV1 current{1, sizeof(RizJudgeConfigurationV1), 1, 0,
        reinterpret_cast<void*>(&RizJudgeContext_Entry), {}};
    current.enabled = static_cast<std::uint32_t>(InterlockedCompareExchange(&enabled, 0, 0));
    current.configuration = config;
    MemoryBarrier();
    if (InterlockedCompareExchange(&configured, 1, 1) != 1
        || std::memcmp(&current.configuration, &config, sizeof(config)) != 0
        || current.enabled != static_cast<std::uint32_t>(InterlockedCompareExchange(&enabled, 0, 0))) return 2;
    *value = current;
    return 0;
}

// No C++ objects requiring destructors: MSVC emits native SEH __finally unwind
// metadata. Original is inside no managed/native catch; it is never swallowed/retried.
extern "C" void __cdecl RizJudgeContext_Dispatch(const RizJudgeFrameV1* frame) {
    if (InterlockedCompareExchange(&configured, 1, 1) != 1) {
        // Publishing a detour before Configure is a host integration error, not an
        // ordinary fall-through path: an original trampoline does not yet exist.
        RaiseFailFastException(nullptr, nullptr, 0);
    }
    const auto original = config.original;
    void* receiver = reinterpret_cast<void*>(frame->gpr[RizRcx]);
    const void* method = reinterpret_cast<const void*>(frame->gpr[RizR9]);
    std::int32_t judge;
    float delta;
    std::memcpy(&judge, &frame->gpr[RizRdx], sizeof(judge));
    std::memcpy(&delta, &frame->xmm[2].low, sizeof(delta));
    // Arguments are copied before observers; the public callback contract is const.
    const bool observe = InterlockedCompareExchange(&enabled, 0, 0) != 0;
    std::uintptr_t token = 0;
    bool enterFaulted = false;
    bool returned = false;
    InterlockedIncrement64(&inFlight);
    __try {
        if (observe) {
            InterlockedIncrement64(&entered);
            token = EnterObserver(frame, &enterFaulted);
        }
        original(receiver, judge, delta, method); // exactly once, outside any __except
        returned = true;
    } __finally {
        if (observe) {
            const auto flags = (returned ? RizOriginalReturned : RizOriginalAbnormal) |
                (enterFaulted ? RizEnterFaulted : 0u);
            ExitObserver(frame, token, flags); // captured observation survives logical disable
            InterlockedIncrement64(&exited);
        }
        InterlockedDecrement64(&inFlight);
    }
}
