#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <cstring>
#include <utility>
#include "CoroutineContext.h"

namespace {
constexpr std::size_t limit = 32;
struct State {
    RizCoroutineConfigV1 config{};
    volatile LONG configured = 0;
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, enterFaults = 0, exitFaults = 0;
};
State states[3][limit];
std::uintptr_t Enter(State* state, const RizCoroutineFrameV1* frame, bool* faulted)
{
    __try { return state->config.enter(frame, state->config.user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { *faulted = true; InterlockedIncrement64(&state->enterFaults); return 0; }
}
void Exit(State* state, const RizCoroutineFrameV1* frame, std::uintptr_t token, std::uintptr_t result, std::uint32_t flags)
{
    __try { state->config.exit(frame, token, result, flags, state->config.user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { InterlockedIncrement64(&state->exitFaults); }
}
std::uintptr_t Dispatch(State* state, const RizCoroutineFrameV1* frame)
{
    if (InterlockedCompareExchange(&state->configured, 1, 1) != 1) RaiseFailFastException(nullptr, nullptr, 0);
    InterlockedIncrement64(&state->inFlight);
    InterlockedIncrement64(&state->entered);
    std::uintptr_t token = 0, result = 0;
    bool faulted = false, returned = false;
    __try {
        token = Enter(state, frame, &faulted);
        if (frame->kind == RizCoroutineStart)
            result = reinterpret_cast<std::uintptr_t>(reinterpret_cast<void*(__cdecl*)(void*, void*, const void*)>(state->config.original)
                (frame->self, frame->enumerator, frame->method));
        else if (frame->kind == RizCoroutineFactory)
            result = reinterpret_cast<std::uintptr_t>(reinterpret_cast<void*(__cdecl*)(void*, const void*)>(state->config.original)(frame->self, frame->method));
        else
            result = reinterpret_cast<std::uint8_t(__cdecl*)(void*, const void*)>(state->config.original)(frame->self, frame->method);
        returned = true;
    }
    __finally {
        const auto flags = (returned ? RizCoroutineReturned : RizCoroutineAbnormal) | (faulted ? RizCoroutineEnterFaulted : 0u);
        Exit(state, frame, token, result, flags);
        InterlockedIncrement64(&state->exited);
        InterlockedDecrement64(&state->inFlight);
    }
    return result;
}
template<std::size_t Slot> __declspec(noinline) void* __cdecl StartEntry(void* self, void* enumerator, const void* method)
{
    const RizCoroutineFrameV1 frame{ 1, sizeof(RizCoroutineFrameV1), RizCoroutineStart, 0, self, enumerator, method, _ReturnAddress() };
    return reinterpret_cast<void*>(Dispatch(&states[RizCoroutineStart][Slot], &frame));
}
template<std::size_t Slot> __declspec(noinline) std::uint8_t __cdecl MoveEntry(void* self, const void* method)
{
    const RizCoroutineFrameV1 frame{ 1, sizeof(RizCoroutineFrameV1), RizCoroutineMoveNext, 0, self, nullptr, method, _ReturnAddress() };
    return static_cast<std::uint8_t>(Dispatch(&states[RizCoroutineMoveNext][Slot], &frame));
}
template<std::size_t Slot> __declspec(noinline) void* __cdecl FactoryEntry(void* self, const void* method)
{
    const RizCoroutineFrameV1 frame{ 1, sizeof(RizCoroutineFrameV1), RizCoroutineFactory, 0, self, nullptr, method, _ReturnAddress() };
    return reinterpret_cast<void*>(Dispatch(&states[RizCoroutineFactory][Slot], &frame));
}
template<std::size_t... Slots> auto StartEntries(std::index_sequence<Slots...>)
{ return std::array<void*, sizeof...(Slots)>{ reinterpret_cast<void*>(&StartEntry<Slots>)... }; }
template<std::size_t... Slots> auto MoveEntries(std::index_sequence<Slots...>)
{ return std::array<void*, sizeof...(Slots)>{ reinterpret_cast<void*>(&MoveEntry<Slots>)... }; }
template<std::size_t... Slots> auto FactoryEntries(std::index_sequence<Slots...>)
{ return std::array<void*, sizeof...(Slots)>{ reinterpret_cast<void*>(&FactoryEntry<Slots>)... }; }
const auto startEntries = StartEntries(std::make_index_sequence<limit>{});
const auto moveEntries = MoveEntries(std::make_index_sequence<limit>{});
const auto factoryEntries = FactoryEntries(std::make_index_sequence<limit>{});
}
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_GetLimit() { return static_cast<std::uint32_t>(limit); }
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_GetFrameSize() { return sizeof(RizCoroutineFrameV1); }
RIZCC_EXPORT void* __cdecl RizCoroutineContext_GetEntry(std::uint32_t kind, std::uint32_t slot)
{
    if (kind > RizCoroutineFactory || slot >= limit) return nullptr;
    return kind == RizCoroutineStart ? startEntries[slot] : kind == RizCoroutineFactory ? factoryEntries[slot] : moveEntries[slot];
}
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_Configure(std::uint32_t kind, std::uint32_t slot, const RizCoroutineConfigV1* config)
{
    if (kind > RizCoroutineFactory || slot >= limit || !config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || !config->original || !config->enter || !config->exit) return 1;
    auto* state = &states[kind][slot];
    if (InterlockedCompareExchange(&state->configured, -1, 0) != 0) return 2;
    state->config = *config;
    InterlockedExchange(&state->configured, 1);
    return 0;
}
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_GetStats(std::uint32_t kind, std::uint32_t slot, RizCoroutineStatsV1* statistics)
{
    if (kind > RizCoroutineFactory || slot >= limit || !statistics || statistics->abiVersion != 1 || statistics->byteSize != sizeof(*statistics)) return 1;
    auto* state = &states[kind][slot];
    statistics->entered = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->entered, 0, 0));
    statistics->exited = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exited, 0, 0));
    statistics->inFlight = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->inFlight, 0, 0));
    statistics->enterFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->enterFaults, 0, 0));
    statistics->exitFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exitFaults, 0, 0));
    return 0;
}
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_ReadConfigurationV1(std::uint32_t kind, std::uint32_t slot, RizCoroutineConfigurationV1* value)
{
    if (kind > RizCoroutineFactory || slot >= limit || !value
        || value->abiVersion != 1 || value->byteSize != sizeof(*value)) return 1;
    // Never leave a prior successful receipt in an unsuccessful output buffer.
    *value = RizCoroutineConfigurationV1{1, sizeof(RizCoroutineConfigurationV1)};
    auto* state = &states[kind][slot];
    if (InterlockedCompareExchange(&state->configured, 1, 1) != 1) return 2;
    RizCoroutineConfigurationV1 current{1, sizeof(RizCoroutineConfigurationV1), 1, kind, slot, 0,
        RizCoroutineContext_GetEntry(kind, slot), state->config};
    MemoryBarrier();
    if (InterlockedCompareExchange(&state->configured, 1, 1) != 1
        || current.entry != RizCoroutineContext_GetEntry(kind, slot)
        || std::memcmp(&current.configuration, &state->config, sizeof(state->config)) != 0) return 2;
    *value = current;
    return 0;
}
