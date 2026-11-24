#pragma once
#include <cstddef>
#include <cstdint>

#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZRI_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZRI_EXPORT extern "C"
#endif

// Actual il2cpp_runtime_invoke Windows x64 ABI: four pointer arguments and one
// pointer result. Native original exceptions never cross a CLR wrapper.
using RizRuntimeInvokeOriginalV1 = void*(__cdecl*)(const void* method, void* self,
    void** arguments, void** exception);
struct RizRuntimeInvokeFrameV1 {
    std::uint32_t abiVersion, byteSize;
    const void* method;
    void* self;
    void** arguments;
    void** exception;
    void* result;
    const void* caller;
    std::uint32_t osThreadId, reserved;
};
using RizRuntimeInvokeAfterV1 = void(__cdecl*)(const RizRuntimeInvokeFrameV1*, void* user);
struct RizRuntimeInvokeConfigV1 {
    std::uint32_t abiVersion, byteSize;
    const void* target;
    RizRuntimeInvokeOriginalV1 original;
};
struct RizRuntimeInvokeObserverV1 {
    std::uint32_t abiVersion, byteSize;
    RizRuntimeInvokeAfterV1 after;
    void* user;
};
struct RizRuntimeInvokeInfoV1 {
    std::uint32_t abiVersion, byteSize, attached, unknown;
    const void* target;
    RizRuntimeInvokeOriginalV1 original;
    void* entry;
    std::uint64_t epoch, entered, exited, inFlight, observerFaults;
};

// Permanent process-lifetime channel, separate from all slot tables. Configure
// only copies configuration; the producer owns safe first detour publication.
// 0 ready, 1 invalid, 2 already permanently claimed, 5 epoch exhausted/unknown.
RIZRI_EXPORT std::uint32_t __cdecl RizRuntimeInvokeContext_Configure(const RizRuntimeInvokeConfigV1* config);
// Attach copies one immutable observer. 0 attached, 1 invalid, 2 not ready,
// 3 wrong epoch, 4 already attached/attempted, 5 sticky unknown. A native call
// snapshots the descriptor at entry; old calls never borrow a late observer.
RIZRI_EXPORT std::uint32_t __cdecl RizRuntimeInvokeContext_Attach(std::uint64_t epoch, const RizRuntimeInvokeObserverV1* observer);
// 0 ready, 1 invalid ABI, 2 not ready. Legal output always receives counters and
// actual sticky unknown. DLL, original, callback and user roots cannot unload.
RIZRI_EXPORT std::uint32_t __cdecl RizRuntimeInvokeContext_GetInfo(RizRuntimeInvokeInfoV1* info);

static_assert(sizeof(void*) == 8, "Windows x64 required");
static_assert(sizeof(RizRuntimeInvokeConfigV1) == 24);
static_assert(offsetof(RizRuntimeInvokeConfigV1, target) == 8);
static_assert(offsetof(RizRuntimeInvokeConfigV1, original) == 16);
static_assert(sizeof(RizRuntimeInvokeObserverV1) == 24);
static_assert(offsetof(RizRuntimeInvokeObserverV1, after) == 8);
static_assert(offsetof(RizRuntimeInvokeObserverV1, user) == 16);
static_assert(sizeof(RizRuntimeInvokeFrameV1) == 64);
static_assert(offsetof(RizRuntimeInvokeFrameV1, method) == 8);
static_assert(offsetof(RizRuntimeInvokeFrameV1, self) == 16);
static_assert(offsetof(RizRuntimeInvokeFrameV1, arguments) == 24);
static_assert(offsetof(RizRuntimeInvokeFrameV1, exception) == 32);
static_assert(offsetof(RizRuntimeInvokeFrameV1, result) == 40);
static_assert(offsetof(RizRuntimeInvokeFrameV1, caller) == 48);
static_assert(offsetof(RizRuntimeInvokeFrameV1, osThreadId) == 56);
static_assert(offsetof(RizRuntimeInvokeFrameV1, reserved) == 60);
static_assert(sizeof(RizRuntimeInvokeInfoV1) == 80);
static_assert(offsetof(RizRuntimeInvokeInfoV1, attached) == 8);
static_assert(offsetof(RizRuntimeInvokeInfoV1, unknown) == 12);
static_assert(offsetof(RizRuntimeInvokeInfoV1, target) == 16);
static_assert(offsetof(RizRuntimeInvokeInfoV1, original) == 24);
static_assert(offsetof(RizRuntimeInvokeInfoV1, entry) == 32);
static_assert(offsetof(RizRuntimeInvokeInfoV1, epoch) == 40);
static_assert(offsetof(RizRuntimeInvokeInfoV1, entered) == 48);
static_assert(offsetof(RizRuntimeInvokeInfoV1, exited) == 56);
static_assert(offsetof(RizRuntimeInvokeInfoV1, inFlight) == 64);
static_assert(offsetof(RizRuntimeInvokeInfoV1, observerFaults) == 72);
