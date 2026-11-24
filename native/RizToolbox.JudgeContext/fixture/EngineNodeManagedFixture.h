#pragma once
#include <cstddef>
#include <cstdint>
#define RIZENM_EXPORT extern "C" __declspec(dllexport)

enum RizEngineNodeManagedScenarioV1 : std::uint32_t {
    RizEngineNodeManagedFreeNode = 1,
    RizEngineNodeManagedProtectOutput = 2,
    RizEngineNodeManagedNestedExecute = 4,
    RizEngineNodeManagedNestedRelease = 8,
    RizEngineNodeManagedUnreadableNode = 16,
    RizEngineNodeManagedNullOutputSlot = 32,
    RizEngineNodeManagedNullOutputValue = 64
};
struct RizEngineNodeManagedResultV1 {
    std::uint32_t abiVersion, byteSize, kind, mode;
    std::uint32_t scenario, returned, caught, returnByte;
    std::uint32_t exceptionCode, exceptionFlags, exceptionParameterCount, nativeChecks;
    std::uint32_t nativeFailures, nodeFreed, outputSlotProtected, reserved;
    void* arg0;
    void* arg1;
    void* arg2;
    void* arg3;
    void* node;
    void* owner;
    std::uint64_t enumHandle;
    void* enumCache;
    std::uint32_t enumType;
    std::int32_t referenceCount;
    std::uint64_t originalCalls;
    const void* originalCaller;
    void* outputNode;
    const void* exceptionAddress;
    std::uint64_t exceptionInformation[4];
    std::uint64_t nestedCalls;
};
// Self-owned native functions only. Each target has the exact four-kind helper
// ABI, no MethodInfo. Callers may use real Dobby Prepare/Configure/Commit on
// these targets. This DLL does not install anything or execute game code.
RIZENM_EXPORT void* __cdecl RizEngineNodeManaged_GetTarget(std::uint32_t kind);
// mode 0 normal, 1 original SEH, 2 original typed C++. scenario flags above;
// returnByte is the complete raw uint32 EAX for Execute and raw AL 0..255
// for Start/Release. Its historical field name and Result208 layout remain.
// FreeNode is for Execute/Release/Detach; nested flags and
// output flags are for Start. NestedExecute/Release call the real target so an
// installed detour is included. NestedRelease frees the same node before Start
// returns its pointer, to catch accidental After dereference of a dead node.
// Return 0 means invocation completed at this native boundary; inspect caught.
// 1 invalid input, 2 permanent record/VirtualAlloc capacity exhausted. Native
// originals never let SEH/C++ cross P/Invoke. All records live until exit.
RIZENM_EXPORT std::uint32_t __cdecl RizEngineNodeManaged_Invoke(void* target, std::uint32_t kind,
    std::uint32_t mode, std::uint32_t scenario, std::uint32_t returnByte,
    RizEngineNodeManagedResultV1* result);
// Optional native-only test instrumentation. GetCurrent exposes this thread's
// permanent fixture record while its native original is executing. The body
// observer is attach-once and runs inside each owned original before side
// effects; it lets the native EXE coordinate a deterministic old-call/Attach
// crossing. Neither function reads a node or participates in helper behavior.
using RizEngineNodeManagedBodyV1 = void(__cdecl*)(const RizEngineNodeManagedResultV1*, void* user);
RIZENM_EXPORT const RizEngineNodeManagedResultV1* __cdecl RizEngineNodeManaged_GetCurrent();
RIZENM_EXPORT std::uint32_t __cdecl RizEngineNodeManaged_SetBodyObserver(RizEngineNodeManagedBodyV1 observer, void* user);

static_assert(sizeof(RizEngineNodeManagedResultV1) == 208);
static_assert(offsetof(RizEngineNodeManagedResultV1, arg0) == 64);
static_assert(offsetof(RizEngineNodeManagedResultV1, node) == 96);
static_assert(offsetof(RizEngineNodeManagedResultV1, enumHandle) == 112);
static_assert(offsetof(RizEngineNodeManagedResultV1, originalCalls) == 136);
static_assert(offsetof(RizEngineNodeManagedResultV1, exceptionInformation) == 168);
static_assert(offsetof(RizEngineNodeManagedResultV1, nestedCalls) == 200);
