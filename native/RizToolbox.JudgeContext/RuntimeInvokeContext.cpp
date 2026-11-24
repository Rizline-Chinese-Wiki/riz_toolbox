#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include "RuntimeInvokeContext.h"

namespace {
struct State {
    RizRuntimeInvokeConfigV1 config{};
    RizRuntimeInvokeObserverV1 observer{};
    volatile LONG configured = 0; // 0 empty, -1 configuring, 1 ready, -2 unknown.
    volatile LONG attaching = 0;
    volatile LONG unknown = 0;
    PVOID volatile published = nullptr;
    std::uint64_t epoch = 0;
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, observerFaults = 0;
};
State state;
volatile LONG64 epochSource = 0;
volatile LONG epochExhausted = 0;

bool Known()
{
    return InterlockedCompareExchange(&state.unknown, 0, 0) == 0;
}
const RizRuntimeInvokeObserverV1* ReadObserver()
{
    return static_cast<const RizRuntimeInvokeObserverV1*>(
        InterlockedCompareExchangePointer(&state.published, nullptr, nullptr));
}
void Observe(const RizRuntimeInvokeObserverV1* observer, const RizRuntimeInvokeFrameV1* frame)
{
    __try { observer->after(frame, observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        InterlockedIncrement64(&state.observerFaults);
        InterlockedExchange(&state.unknown, 1);
    }
}

// No managed code wraps original. The native finally runs during both SEH and
// C++ unwind; After is reached only after the original returns normally.
__declspec(noinline) void* Dispatch(const RizRuntimeInvokeObserverV1* observer, RizRuntimeInvokeFrameV1* frame)
{
    if (InterlockedCompareExchange(&state.configured, 1, 1) != 1)
        RaiseFailFastException(nullptr, nullptr, 0);
    InterlockedIncrement64(&state.inFlight);
    InterlockedIncrement64(&state.entered);
    __try {
        const auto result = state.config.original(frame->method, frame->self, frame->arguments, frame->exception);
        frame->result = result;
        if (observer) Observe(observer, frame);
        return result;
    }
    __finally {
        InterlockedIncrement64(&state.exited);
        InterlockedDecrement64(&state.inFlight);
    }
}

__declspec(noinline) void* __cdecl Entry(const void* method, void* self, void** arguments, void** exception)
{
    // Snapshot precedes original invocation and callback availability checks.
    // No stack/TLS lookup can lend an old call a newly attached observer.
    const auto* observer = ReadObserver();
    RizRuntimeInvokeFrameV1 frame{ 1, sizeof(RizRuntimeInvokeFrameV1), method, self,
        arguments, exception, nullptr, _ReturnAddress(), GetCurrentThreadId(), 0 };
    return Dispatch(observer, &frame);
}
}

RIZRI_EXPORT std::uint32_t __cdecl RizRuntimeInvokeContext_Configure(const RizRuntimeInvokeConfigV1* config)
{
    if (!config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || !config->target || !config->original) return 1;
    if (InterlockedCompareExchange(&state.configured, -1, 0) != 0) return 2;
    state.config = *config;
    const auto epoch = InterlockedIncrement64(&epochSource);
    if (epoch <= 0 || InterlockedCompareExchange(&epochExhausted, 0, 0) != 0) {
        InterlockedExchange(&epochExhausted, 1);
        InterlockedExchange(&state.unknown, 1);
        InterlockedExchange(&state.configured, -2);
        return 5;
    }
    state.epoch = static_cast<std::uint64_t>(epoch);
    InterlockedExchange(&state.configured, 1);
    return 0;
}

RIZRI_EXPORT std::uint32_t __cdecl RizRuntimeInvokeContext_Attach(std::uint64_t epoch,
    const RizRuntimeInvokeObserverV1* observer)
{
    if (!observer || observer->abiVersion != 1 || observer->byteSize != sizeof(*observer) || !observer->after) return 1;
    if (InterlockedCompareExchange(&state.configured, 1, 1) != 1) return Known() ? 2u : 5u;
    if (epoch == 0 || epoch != state.epoch) return 3;
    if (!Known()) return 5;
    if (InterlockedCompareExchange(&state.attaching, -1, 0) != 0) return 4;
    if (!Known()) {
        InterlockedExchange(&state.attaching, -2);
        return 5;
    }
    state.observer = *observer;
    InterlockedExchangePointer(&state.published, &state.observer);
    InterlockedExchange(&state.attaching, 1);
    return 0;
}

RIZRI_EXPORT std::uint32_t __cdecl RizRuntimeInvokeContext_GetInfo(RizRuntimeInvokeInfoV1* info)
{
    if (!info || info->abiVersion != 1 || info->byteSize != sizeof(*info)) return 1;
    const auto phase = InterlockedCompareExchange(&state.configured, 0, 0);
    info->target = nullptr;
    info->original = nullptr;
    info->entry = reinterpret_cast<void*>(&Entry);
    info->epoch = 0;
    if (phase == 1 || phase == -2) {
        info->target = state.config.target;
        info->original = state.config.original;
        info->epoch = state.epoch;
    }
    info->attached = ReadObserver() ? 1u : 0u;
    info->unknown = Known() ? 0u : 1u;
    info->entered = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state.entered, 0, 0));
    info->exited = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state.exited, 0, 0));
    info->inFlight = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state.inFlight, 0, 0));
    info->observerFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state.observerFaults, 0, 0));
    return phase == 1 ? 0u : 2u;
}
