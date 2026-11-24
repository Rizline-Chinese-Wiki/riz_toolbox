#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstring>
#include "InitializationContext.h"

namespace {
struct State {
    RizInitializationConfigV1 config{};
    volatile LONG configured = 0; // 0 empty, -1 publishing, 1 ready; never reset.
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, enterFaults = 0, exitFaults = 0, skipped = 0;
};
State states[2]; // Independent of the Judge register-snapshot configuration.

std::uint32_t Enter(State* state, void* self, void* information, const void* method,
    std::uintptr_t* token, bool* faulted)
{
    __try { return state->config.enter(self, information, method, token, state->config.userContext); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        *faulted = true; InterlockedIncrement64(&state->enterFaults); return 0;
    }
}
void* Exit(State* state, void* self, void* information, const void* method,
    void* returnedReference, std::uintptr_t token, std::uint32_t flags)
{
    __try { return state->config.exit(self, information, method, returnedReference, token, flags, state->config.userContext); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        // Contain callback faults even while unwinding the original exception.
        // Never replace that original exception, retry Exit or retry the original.
        InterlockedIncrement64(&state->exitFaults); return nullptr;
    }
}

// POD-only locals allow native __finally unwind metadata. The original call has
// no __except/C++ catch here or in either typed wrapper. Its SEH/C++ exception
// crosses only native frames and reaches the native caller unchanged.
__declspec(noinline) void* Dispatch(std::uint32_t kind, void* self, void* information, const void* method)
{
    auto* state = &states[kind];
    if (InterlockedCompareExchange(&state->configured, 1, 1) != 1)
        RaiseFailFastException(nullptr, nullptr, 0);
    std::uintptr_t token = 0;
    bool enterFaulted = false, originalStarted = false, returned = false, skipped = false;
    void* reference = nullptr;
    void* validated = nullptr;
    InterlockedIncrement64(&state->inFlight);
    InterlockedIncrement64(&state->entered);
    __try {
        const auto allow = Enter(state, self, information, method, &token, &enterFaulted);
        if (allow == 1 && !enterFaulted) {
            originalStarted = true;
            if (kind == RizInitializationStart)
                reinterpret_cast<RizInitializationStartV1>(state->config.original)(self, method);
            else
                reference = reinterpret_cast<RizInitializationGetterV1>(state->config.original)(self, information, method);
            returned = true;
        } else {
            skipped = true; InterlockedIncrement64(&state->skipped);
        }
    }
    __finally {
        const auto flags = (returned ? RizInitializationReturned : 0u)
            | (originalStarted && !returned ? RizInitializationAbnormal : 0u)
            | (enterFaulted ? RizInitializationEnterFaulted : 0u)
            | (skipped ? RizInitializationSkipped : 0u);
        validated = Exit(state, self, information, method, reference, token, flags);
        InterlockedIncrement64(&state->exited);
        InterlockedDecrement64(&state->inFlight);
    }
    // Skip never authorizes a synthesized successful getter result, even if a
    // faulty Exit returns a non-null pointer after Enter refused the original.
    return returned && !skipped ? validated : nullptr;
}
void __cdecl StartEntry(void* self, const void* method) { Dispatch(RizInitializationStart, self, nullptr, method); }
void* __cdecl GetterEntry(void* self, void* information, const void* method)
{ return Dispatch(RizInitializationGetter, self, information, method); }
}

RIZIC_EXPORT void* __cdecl RizInitializationContext_GetEntry(std::uint32_t kind)
{
    if (kind == RizInitializationStart) return reinterpret_cast<void*>(&StartEntry);
    if (kind == RizInitializationGetter) return reinterpret_cast<void*>(&GetterEntry);
    return nullptr;
}
RIZIC_EXPORT std::uint32_t __cdecl RizInitializationContext_GetConfigSize() { return sizeof(RizInitializationConfigV1); }
RIZIC_EXPORT std::uint32_t __cdecl RizInitializationContext_Configure(std::uint32_t kind, const RizInitializationConfigV1* value)
{
    if (kind > RizInitializationGetter || !value || value->abiVersion != 1 || value->byteSize != sizeof(*value)
        || !value->original || !value->enter || !value->exit) return 1;
    auto* state = &states[kind];
    if (InterlockedCompareExchange(&state->configured, -1, 0) != 0) return 2;
    state->config = *value;
    InterlockedExchange(&state->configured, 1); // full barrier after immutable copy.
    return 0;
}
RIZIC_EXPORT std::uint32_t __cdecl RizInitializationContext_GetStats(std::uint32_t kind, void* statisticsOrPublication)
{
    if (kind > RizInitializationGetter || !statisticsOrPublication) return 1;
    std::uint32_t header[2]{};
    std::memcpy(header, statisticsOrPublication, sizeof(header));
    auto* state = &states[kind];
    if (header[0] == 2 && header[1] == sizeof(RizInitializationPublicationV2)) {
        if (InterlockedCompareExchange(&state->configured, 1, 1) != 1) return 2;
        const RizInitializationPublicationV2 ready{2, sizeof(RizInitializationPublicationV2), 1, kind,
            RizInitializationContext_GetEntry(kind), state, state->config};
        std::memcpy(statisticsOrPublication, &ready, sizeof(ready));
        return 0;
    }
    if (header[0] != 1 || header[1] != sizeof(RizInitializationStatsV1)) return 1;
    auto* value = static_cast<RizInitializationStatsV1*>(statisticsOrPublication);
    value->entered = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->entered, 0, 0));
    value->exited = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exited, 0, 0));
    value->inFlight = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->inFlight, 0, 0));
    value->enterFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->enterFaults, 0, 0));
    value->exitFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exitFaults, 0, 0));
    value->skipped = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->skipped, 0, 0));
    return 0;
}
