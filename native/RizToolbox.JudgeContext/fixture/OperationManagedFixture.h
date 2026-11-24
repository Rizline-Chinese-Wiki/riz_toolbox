#pragma once
#include <cstddef>
#include <cstdint>
#define RIZOM_EXPORT extern "C" __declspec(dllexport)

struct RizOperationManagedResultV1 {
    std::uint32_t abiVersion, byteSize, kind, mode;
    std::uint32_t returned, caught, exceptionCode, exceptionFlags;
    std::uint32_t exceptionParameterCount, nativeChecks, nativeFailures, reserved;
    const void* self;
    const void* arg0;
    const void* arg1;
    const void* method;
    const void* originalCaller;
    std::uint64_t originalCalls;
    std::uint64_t exceptionInformation[4];
    const void* exceptionAddress;
    std::uint64_t reservedTail;
};
// Self-owned offline original functions, not game identities/Ready. kind0/1/2
// matches OperationContext. GetMethod returns an opaque byte address only.
RIZOM_EXPORT void* __cdecl RizOperationManaged_GetOriginal(std::uint32_t kind);
RIZOM_EXPORT const void* __cdecl RizOperationManaged_GetMethod();
// mode0 normal, 1 original SEH, 2 original C++. Return0 is native completion;
// inspect caught/result to distinguish original unwind. Return1 invalid header,
// pointer/kind/mode, return2 permanent capacity exhausted. No exception crosses
// the P/Invoke boundary. 64 permanent records, no destroy/reuse or game calls.
RIZOM_EXPORT std::uint32_t __cdecl RizOperationManaged_Invoke(void* entry, std::uint32_t kind,
    std::uint32_t mode, const void* hiddenMethod, RizOperationManagedResultV1* result);

static_assert(sizeof(RizOperationManagedResultV1) == 144);
static_assert(offsetof(RizOperationManagedResultV1, self) == 48);
static_assert(offsetof(RizOperationManagedResultV1, originalCalls) == 88);
static_assert(offsetof(RizOperationManagedResultV1, exceptionInformation) == 96);
static_assert(offsetof(RizOperationManagedResultV1, exceptionAddress) == 128);
static_assert(offsetof(RizOperationManagedResultV1, reservedTail) == 136);
