#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <cstring>
#include <limits>
#include <utility>
#include "OperationContext.h"
#include "TweenAdmission.h"
#include "TweenRetirementContext.h"
#if !defined(RIZ_TWEEN_ADMISSION_FIXTURE) || defined(RIZ_TWEEN_SINGLE_EVENT_FIXTURE)
#define RIZ_TWEEN_SINGLE_EVENT_CONTEXT_ENABLED
#endif
#if defined(RIZ_TWEEN_SINGLE_EVENT_CONTEXT_ENABLED)
#include "TweenSingleEventContext.h"
#endif
#if !defined(RIZ_TWEEN_ADMISSION_FIXTURE)
#include "TweenSourceContext.h"
#include "TweenSourceContext.References.h"
#endif

namespace {
constexpr std::size_t limit = 32;
struct State {
    RizOperationConfigV1 config{};
    volatile LONG configured = 0;
    bool bootstrap = false;
    std::uint32_t kind{}, slot{};
    void* target{};
    std::uint64_t epoch{};
    RizOperationBootstrapObserverV1 observer{};
    void* volatile publishedObserver = nullptr;
    volatile LONG attachAttempted = 0, unknown = 0;
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, enterFaults = 0, exitFaults = 0;
};
State states[4][limit];
volatile LONG64 sequenceSource = 0;
volatile LONG64 epochSource = 0;
std::uint64_t NextSequence()
{
    auto current = InterlockedCompareExchange64(&sequenceSource, 0, 0);
    for (;;) {
        if (current < 0 || current == (std::numeric_limits<LONG64>::max)()) return 0;
        const auto next = current + 1;
        const auto seen = InterlockedCompareExchange64(&sequenceSource, next, current);
        if (seen == current) return static_cast<std::uint64_t>(next);
        current = seen;
    }
}
const RizOperationBootstrapObserverV1* Snapshot(State* state)
{
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1) return nullptr;
    return static_cast<const RizOperationBootstrapObserverV1*>(InterlockedCompareExchangePointer(&state->publishedObserver, nullptr, nullptr));
}
std::uintptr_t Enter(State* state, const RizOperationFrameV1* frame,
    const RizOperationBootstrapObserverV1* observer, bool* faulted)
{
    __try {
        if (state->bootstrap) return observer ? observer->enter(frame, observer->user) : 0;
        return state->config.enter(frame, state->config.user);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        *faulted = true; InterlockedIncrement64(&state->enterFaults);
        if (state->bootstrap) InterlockedExchange(&state->unknown, 1);
        if(RizTweenRetirementOperationDomain(state->kind,state->slot)!=RizTweenRetirementDomainCount)RizTweenRetirementFault();
        return 0;
    }
}
void Exit(State* state, const RizOperationFrameV1* frame, const RizOperationBootstrapObserverV1* observer,
    std::uintptr_t token, std::uint32_t flags)
{
    __try {
        if (state->bootstrap) { if (observer) observer->exit(frame, token, flags, observer->user); }
        else state->config.exit(frame, token, flags, state->config.user);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        InterlockedIncrement64(&state->exitFaults);
        if (state->bootstrap) InterlockedExchange(&state->unknown, 1);
        if(RizTweenRetirementOperationDomain(state->kind,state->slot)!=RizTweenRetirementDomainCount)RizTweenRetirementFault();
    }
}
void Dispatch(State* state, const RizOperationFrameV1* frame, const RizOperationBootstrapObserverV1* observer
#if defined(RIZ_TWEEN_SINGLE_EVENT_CONTEXT_ENABLED)
    ,const RizTweenSingleEventEntryCapture* singleCapture=nullptr
#endif
    )
{
    // No original is available before Configure. Retain a permanent failure
    // without calling an invented original or terminating the host process.
    // The invalid invocation is not counted as a completed physical dispatch.
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1) {
        InterlockedExchange(&state->unknown, 1); InterlockedIncrement64(&state->enterFaults);
        InterlockedCompareExchange(&state->configured, -2, 0);
        RizTweenRetirementFault(); return;
    }
    InterlockedIncrement64(&state->inFlight); InterlockedIncrement64(&state->entered);
    const auto admission = RizTweenAdmissionEnter(state->kind, state->slot, frame->self, frame->arg0);
#if !defined(RIZ_TWEEN_ADMISSION_FIXTURE)
    RizTweenSourceOperationTicketV1 source{1,sizeof(source)};
    bool sourceEntered = false;
    if (!admission.skipped) {
        const bool play = state->kind == RizOperationVoid0 && state->slot == 25;
        const bool update = state->kind == RizOperationVoid0 && state->slot == 24;
        const bool execute = state->kind == RizOperationRef1Void && state->slot == 19;
        if (play || update || execute) {
            const auto operation = update ? RizTweenSourceUpdate : play ? RizTweenSourcePlay : RizTweenSourceExecute;
            void* receiver = execute ? frame->arg0 : frame->self;
            void* manager = play ? nullptr : frame->self;
            sourceEntered = RizTweenSourceOperationEnter(operation, receiver, manager, frame->sequence, &source) == 0;
        }
    }
#endif
    std::uintptr_t token = 0; bool faulted = false, returned = false, originalCalled = false;
#if defined(RIZ_TWEEN_SINGLE_EVENT_CONTEXT_ENABLED)
    // Staged native-only integration: the owned machine fixture compiles the
    // actual Single entry/dispatch. Production FirstInstall remains unchanged
    // until its new publication/wire contract is adopted as one transaction.
    const auto singleParent=singleCapture?RizTweenSingleEventBegin(*singleCapture,*frame,state->epoch):RizTweenSingleEventParent{};
#endif
    __try {
        token = Enter(state, frame, observer, &faulted);
#if defined(RIZ_TWEEN_SINGLE_EVENT_CONTEXT_ENABLED)
        RizTweenSingleEventOriginal(singleParent,token);
#endif
        if (admission.skipped) { /* Keep actual Before/After pairing without calling the original. */ }
        else if (frame->kind == RizOperationVoid0) {
            originalCalled = true;
            reinterpret_cast<void(__cdecl*)(void*, const void*)>(state->config.original)(frame->self, frame->method);
        } else if (frame->kind == RizOperationSingle1Void) {
            originalCalled = true;
            float value{};
            const auto bits = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(frame->arg0));
            std::memcpy(&value, &bits, sizeof(value));
            reinterpret_cast<void(__cdecl*)(void*, float, const void*)>(state->config.original)(frame->self, value, frame->method);
        } else if (frame->kind == RizOperationRef1Void) {
            originalCalled = true;
            reinterpret_cast<void(__cdecl*)(void*, void*, const void*)>(state->config.original)(frame->self, frame->arg0, frame->method);
        } else {
            originalCalled = true;
            reinterpret_cast<void(__cdecl*)(void*, void*, void*, const void*)>(state->config.original)(frame->self, frame->arg0, frame->arg1, frame->method);
        }
        returned = !admission.skipped;
    }
    __finally {
        const auto flags = (admission.skipped ? RizOperationSkipped : returned ? RizOperationReturned : RizOperationAbnormal)
            | (faulted ? RizOperationEnterFaulted : 0u);
#if defined(RIZ_TWEEN_SINGLE_EVENT_CONTEXT_ENABLED)
        RizTweenSingleEventClose(singleParent,returned&&!faulted);
#endif
#if !defined(RIZ_TWEEN_ADMISSION_FIXTURE)
        // Publish unfinished source references while the real managed parent is
        // still open. Its After callback may then drain the abnormal journal.
        if (sourceEntered) RizTweenSourceOperationPrepareExitReferences(&source);
#endif
        Exit(state, frame, observer, token, flags);
#if !defined(RIZ_TWEEN_ADMISSION_FIXTURE)
        // Keep the real native source parent alive throughout managed After;
        // its journal is drained while the physical operation still owns it.
        if (sourceEntered) RizTweenSourceOperationExit(&source, returned && !faulted);
#endif
        RizTweenAdmissionExit(admission, originalCalled, returned, faulted
            || InterlockedCompareExchange64(&state->exitFaults, 0, 0) != 0);
        InterlockedIncrement64(&state->exited); InterlockedDecrement64(&state->inFlight);
    }
}
template<std::size_t Slot> __declspec(noinline) void __cdecl VoidEntry(void* self, const void* method)
{
    RizTweenRetirementScope retirement(RizTweenRetirementOperationDomain(RizOperationVoid0,Slot));
    const auto* observer = Snapshot(&states[RizOperationVoid0][Slot]);
    const RizOperationFrameV1 frame{ 1, sizeof(frame), RizOperationVoid0, 0, self, nullptr, nullptr, method, _ReturnAddress(), NextSequence() };
    Dispatch(&states[RizOperationVoid0][Slot], &frame, observer);
}
template<std::size_t Slot> __declspec(noinline) void __cdecl Ref1Entry(void* self, void* arg0, const void* method)
{
    RizTweenRetirementScope retirement(RizTweenRetirementOperationDomain(RizOperationRef1Void,Slot));
    const auto* observer = Snapshot(&states[RizOperationRef1Void][Slot]);
    const RizOperationFrameV1 frame{ 1, sizeof(frame), RizOperationRef1Void, 0, self, arg0, nullptr, method, _ReturnAddress(), NextSequence() };
    Dispatch(&states[RizOperationRef1Void][Slot], &frame, observer);
}
template<std::size_t Slot> __declspec(noinline) void __cdecl Ref2Entry(void* self, void* arg0, void* arg1, const void* method)
{
    RizTweenRetirementScope retirement(RizTweenRetirementOperationDomain(RizOperationRef2Void,Slot));
    const auto* observer = Snapshot(&states[RizOperationRef2Void][Slot]);
    const RizOperationFrameV1 frame{ 1, sizeof(frame), RizOperationRef2Void, 0, self, arg0, arg1, method, _ReturnAddress(), NextSequence() };
    Dispatch(&states[RizOperationRef2Void][Slot], &frame, observer);
}
template<std::size_t Slot> __declspec(noinline) void __cdecl SingleEntry(void* self, float value, const void* method)
{
    RizTweenRetirementScope retirement(RizTweenRetirementOperationDomain(RizOperationSingle1Void,Slot));
#if defined(RIZ_TWEEN_SINGLE_EVENT_CONTEXT_ENABLED)
    const auto singleCapture=Slot==31?RizTweenSingleEventCaptureEntry():RizTweenSingleEventEntryCapture{};
#endif
    const auto* observer = Snapshot(&states[RizOperationSingle1Void][Slot]);
    std::uint32_t bits{};
    std::memcpy(&bits, &value, sizeof(bits));
    const RizOperationFrameV1 frame{ 1, sizeof(frame), RizOperationSingle1Void, 0, self,
        reinterpret_cast<void*>(static_cast<std::uintptr_t>(bits)), nullptr, method, _ReturnAddress(), NextSequence() };
    Dispatch(&states[RizOperationSingle1Void][Slot], &frame, observer
#if defined(RIZ_TWEEN_SINGLE_EVENT_CONTEXT_ENABLED)
        ,&singleCapture
#endif
        );
}
template<std::size_t... Slots> auto VoidEntries(std::index_sequence<Slots...>)
{ return std::array<void*, sizeof...(Slots)>{ reinterpret_cast<void*>(&VoidEntry<Slots>)... }; }
template<std::size_t... Slots> auto Ref1Entries(std::index_sequence<Slots...>)
{ return std::array<void*, sizeof...(Slots)>{ reinterpret_cast<void*>(&Ref1Entry<Slots>)... }; }
template<std::size_t... Slots> auto Ref2Entries(std::index_sequence<Slots...>)
{ return std::array<void*, sizeof...(Slots)>{ reinterpret_cast<void*>(&Ref2Entry<Slots>)... }; }
template<std::size_t... Slots> auto SingleEntries(std::index_sequence<Slots...>)
{ return std::array<void*, sizeof...(Slots)>{ reinterpret_cast<void*>(&SingleEntry<Slots>)... }; }
const auto voidEntries = VoidEntries(std::make_index_sequence<limit>{});
const auto ref1Entries = Ref1Entries(std::make_index_sequence<limit>{});
const auto ref2Entries = Ref2Entries(std::make_index_sequence<limit>{});
const auto singleEntries = SingleEntries(std::make_index_sequence<limit>{});
bool IsEntry(void* pointer)
{
    for (std::size_t slot = 0; slot != limit; ++slot)
        if (pointer == voidEntries[slot] || pointer == ref1Entries[slot] || pointer == ref2Entries[slot]
            || pointer == singleEntries[slot]) return true;
    return false;
}
}
RIZOC_EXPORT std::uint32_t __cdecl RizOperationContext_GetLimit() { return static_cast<std::uint32_t>(limit); }
RIZOC_EXPORT std::uint32_t __cdecl RizOperationContext_GetFrameSize() { return sizeof(RizOperationFrameV1); }
RIZOC_EXPORT void* __cdecl RizOperationContext_GetEntry(std::uint32_t kind, std::uint32_t slot)
{
    if (kind > RizOperationSingle1Void || slot >= limit) return nullptr;
    return kind == RizOperationVoid0 ? voidEntries[slot] : kind == RizOperationRef1Void ? ref1Entries[slot]
        : kind == RizOperationRef2Void ? ref2Entries[slot] : singleEntries[slot];
}
RIZOC_EXPORT std::uint32_t __cdecl RizOperationContext_Configure(std::uint32_t kind, std::uint32_t slot, const RizOperationConfigV1* config)
{
    if (kind > RizOperationSingle1Void || slot >= limit || !config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || !config->original || !config->enter || !config->exit || IsEntry(config->original)) return 1;
    auto* state = &states[kind][slot];
    if (InterlockedCompareExchange(&state->configured, -1, 0) != 0) return 2;
    state->kind = kind; state->slot = slot;
    state->config = *config; InterlockedExchange(&state->configured, 1); return 0;
}
RIZOC_EXPORT std::uint32_t __cdecl RizOperationContext_GetStats(std::uint32_t kind, std::uint32_t slot, RizOperationStatsV1* stats)
{
    if (kind > RizOperationSingle1Void || slot >= limit || !stats || stats->abiVersion != 1 || stats->byteSize != sizeof(*stats)) return 1;
    auto* state = &states[kind][slot];
    stats->entered = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->entered, 0, 0));
    stats->exited = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exited, 0, 0));
    stats->inFlight = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->inFlight, 0, 0));
    stats->enterFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->enterFaults, 0, 0));
    stats->exitFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exitFaults, 0, 0)); return 0;
}
RIZOC_EXPORT std::uint32_t __cdecl RizOperationBootstrap_Configure(std::uint32_t kind, std::uint32_t slot,
    const RizOperationBootstrapConfigV1* config)
{
    if (kind > RizOperationSingle1Void || slot >= limit || !config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || !config->target || !config->original || config->target == config->original || IsEntry(config->target) || IsEntry(config->original)) return 1;
    auto* state = &states[kind][slot];
    if (InterlockedCompareExchange(&state->configured, -1, 0) != 0) return 2;
    auto epoch = InterlockedCompareExchange64(&epochSource, 0, 0);
    for (;;) {
        if (epoch < 0 || epoch == (std::numeric_limits<LONG64>::max)()) {
            InterlockedExchange(&state->unknown, 1); InterlockedExchange(&state->configured, -2);
            if(RizTweenRetirementOperationDomain(kind,slot)!=RizTweenRetirementDomainCount)RizTweenRetirementFault();
            return 3;
        }
        const auto next = epoch + 1;
        const auto seen = InterlockedCompareExchange64(&epochSource, next, epoch);
        if (seen == epoch) { epoch = next; break; }
        epoch = seen;
    }
    state->kind = kind; state->slot = slot;
    state->config.original = config->original; state->target = config->target;
    state->epoch = static_cast<std::uint64_t>(epoch); state->bootstrap = true;
    InterlockedExchange(&state->configured, 1); return 0;
}
RIZOC_EXPORT std::uint32_t __cdecl RizOperationBootstrap_Attach(std::uint32_t kind, std::uint32_t slot,
    std::uint64_t epoch, const RizOperationBootstrapObserverV1* observer)
{
    if (kind > RizOperationSingle1Void || slot >= limit || !observer || observer->abiVersion != 1
        || observer->byteSize != sizeof(*observer) || !observer->enter || !observer->exit || epoch == 0) return 1;
    auto* state = &states[kind][slot];
    if (InterlockedCompareExchange(&state->unknown, 0, 0) != 0) return 5;
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1 || !state->bootstrap) return 2;
    if (state->epoch != epoch) return 3;
    if (InterlockedCompareExchange(&state->attachAttempted, 1, 0) != 0) return 4;
    state->observer = *observer;
    InterlockedExchangePointer(&state->publishedObserver, &state->observer);
    return 0;
}
RIZOC_EXPORT std::uint32_t __cdecl RizOperationBootstrap_GetInfo(std::uint32_t kind, std::uint32_t slot,
    RizOperationBootstrapInfoV1* info)
{
    if (kind > RizOperationSingle1Void || slot >= limit || !info || info->abiVersion != 1 || info->byteSize != sizeof(*info)) return 1;
    auto* state = &states[kind][slot]; const auto configured = InterlockedCompareExchange(&state->configured, 0, 0);
    RizOperationBootstrapInfoV1 result{}; result.abiVersion = 1; result.byteSize = sizeof(result);
    result.kind = kind; result.slot = slot; result.entry = RizOperationContext_GetEntry(kind, slot);
    result.entered = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->entered, 0, 0));
    result.exited = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exited, 0, 0));
    result.inFlight = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->inFlight, 0, 0));
    result.enterFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->enterFaults, 0, 0));
    result.exitFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exitFaults, 0, 0));
    if (configured == 1 && state->bootstrap) {
        result.target = state->target; result.original = state->config.original; result.epoch = state->epoch;
        result.attached = Snapshot(state) ? 1u : 0u;
        if (result.enterFaults != 0 || result.exitFaults != 0) InterlockedExchange(&state->unknown, 1);
    }
    result.unknown = InterlockedCompareExchange(&state->unknown, 0, 0) != 0 || configured < 0 ? 1u : 0u;
    *info = result;
    return configured == 0 ? 2u : configured != 1 ? 3u : state->bootstrap ? 0u : 3u;
}
