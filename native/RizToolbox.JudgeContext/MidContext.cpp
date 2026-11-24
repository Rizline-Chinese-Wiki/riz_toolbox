#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include "MidContext.h"

extern "C" void RizEventMidContext_Entry();

namespace {
constexpr std::size_t limit = 32;
struct State {
    RizEventMidConfigV1 config{};
    volatile LONG configured = 0; // 0 empty, -1 publishing, 1 ready; never reset.
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, faults = 0;
};
State states[limit];
SRWLOCK configurationLock = SRWLOCK_INIT;

// This helper catches only faults from the observation callback. It never calls
// or replays game code. The frame and all pointer-valued fields remain opaque.
void Observe(State* state, const RizEventMidFrameV1* frame)
{
    __try { state->config.callback(frame, state->config.user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { InterlockedIncrement64(&state->faults); }
}
}

// Only MidEntry.asm calls this symbol. Dispatch never acquires the configuration
// lock: an observer on a native worker cannot wait for the installing thread.
extern "C" void __cdecl RizEventMidContext_Dispatch(const RizEventMidFrameV1* frame)
{
    if (!frame || frame->abiVersion != 1 || frame->byteSize != sizeof(*frame)
        || frame->reserved0 != 0 || frame->reserved1 != 0) return;
    State* selected = nullptr;
    for (auto& state : states) {
        if (InterlockedCompareExchange(&state.configured, 1, 1) == 1
            && state.config.expectedReturnPc == reinterpret_cast<const void*>(frame->returnPc)) {
            selected = &state;
            break;
        }
    }
    if (!selected) return;
    InterlockedIncrement64(&selected->inFlight);
    InterlockedIncrement64(&selected->entered);
    __try { Observe(selected, frame); }
    __finally {
        InterlockedIncrement64(&selected->exited);
        InterlockedDecrement64(&selected->inFlight);
    }
    // ASM still restores the machine snapshot after this function returns. Its
    // enclosing native void0 parent supplies the wider physical drain boundary.
}

RIZEMC_EXPORT void* __cdecl RizEventMidContext_GetEntry()
{
    return reinterpret_cast<void*>(&RizEventMidContext_Entry);
}
RIZEMC_EXPORT std::uint32_t __cdecl RizEventMidContext_GetLimit()
{
    return static_cast<std::uint32_t>(limit);
}
RIZEMC_EXPORT std::uint32_t __cdecl RizEventMidContext_GetFrameSize()
{
    return sizeof(RizEventMidFrameV1);
}
RIZEMC_EXPORT std::uint32_t __cdecl RizEventMidContext_Configure(std::uint32_t slot, const RizEventMidConfigV1* config)
{
    if (slot >= limit || !config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || !config->expectedReturnPc || !config->callback || config->reserved != 0) return 1;
    AcquireSRWLockExclusive(&configurationLock);
    auto* state = &states[slot];
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 0) {
        ReleaseSRWLockExclusive(&configurationLock);
        return 2;
    }
    for (const auto& candidate : states) {
        if (candidate.configured != 0 && candidate.config.expectedReturnPc == config->expectedReturnPc) {
            ReleaseSRWLockExclusive(&configurationLock);
            return 3;
        }
    }
    InterlockedExchange(&state->configured, -1);
    state->config = *config;
    InterlockedExchange(&state->configured, 1);
    ReleaseSRWLockExclusive(&configurationLock);
    return 0;
}
RIZEMC_EXPORT std::uint32_t __cdecl RizEventMidContext_GetStats(std::uint32_t slot, RizEventMidStatsV1* statistics)
{
    if (slot >= limit || !statistics || statistics->abiVersion != 1 || statistics->byteSize != sizeof(*statistics)) return 1;
    auto* state = &states[slot];
    statistics->entered = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->entered, 0, 0));
    statistics->exited = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exited, 0, 0));
    statistics->inFlight = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->inFlight, 0, 0));
    statistics->faults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->faults, 0, 0));
    return 0;
}
