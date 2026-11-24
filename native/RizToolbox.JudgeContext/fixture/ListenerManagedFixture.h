#pragma once
#include "../ListenerContext.h"

// Self-owned offline DLL only. No production receipt, fixed-game image, SDK
// entry point, observer Attach, or game/IL2CPP invocation is provided here.
#define RIZLM_EXPORT extern "C" __declspec(dllexport)
enum RizListenerManagedModeV1 : std::uint32_t {
    RizLmNormal = 0, RizLmOriginalSeh = 1, RizLmOriginalCpp = 2,
    RizLmTargetReadAv = 3, RizLmForeignOriginal = 4
};
enum RizListenerManagedWorkFlagsV1 : std::uint32_t {
    RizLmHoldBeforeMid = 1, RizLmHoldInCallee = 2, RizLmNativeWorker = 4
};
struct RizListenerManagedConfigV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint32_t targetIndex, mode, site, flags, count, reserved;
    void* nestedWork; // Optional permanent work; invoked once at this work's site.
};
struct RizListenerManagedInfoV1 {
    std::uint32_t abiVersion, byteSize, targetIndex, setupStatus;
    RizVoidBootstrapInfoV1 parent;
    const void* midReturnPc;
    const void* method; // Opaque, never dereferenced by any helper or CLR test.
    RizListenerInfoV1 lane;
    std::uint32_t sourceWidth[3], postWidth[3];
    std::uint32_t codeWrites, prewriteLookupChecks, prewriteBoundaryChecks, registeredFunctionCount;
    std::uint32_t unknown, reserved;
    const void* functionTable;
    const void* allocation;
    std::uint64_t allocationSize;
};
struct RizListenerManagedResultV1 {
    std::uint32_t abiVersion, byteSize, state, mode, targetIndex, osThreadId;
    std::uint32_t returned, caught, exceptionCode, exceptionFlags, exceptionParameterCount, holdReached;
    std::uint64_t exceptionInformation[3];
    const void* exceptionAddress;
    std::uint64_t originalCalls, replayChecks, returnPcChecks, nativeChecks, nativeFailures, nestedInvocations;
    std::uint64_t invoked[3];
    const void* actualReturn[3];
    const void* event;
    const void* list;
    const void* invokable[3];
    const void* args;
    std::uint64_t afterCount, exceptionInformation3; // Fourth MSVC x64 C++ parameter.
};

// All statuses are fixture statuses, never first-install Ready. Setup configures
// target0=primarySlot and target1=primarySlot-1 (1 <= primarySlot <= 31), once.
// Setup: 0 success; 1 invalid; 2 already attempted; 3 permanently failed.
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_Setup(const wchar_t* helperPath, const wchar_t* dobbyPath, std::uint32_t primarySlot);
// GetInfo: 0 actual identity; 1 invalid; 2 setup not complete. Both channels are
// configured but initially unattached; C# must Attach actual helper exports.
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_GetInfo(std::uint32_t targetIndex, RizListenerManagedInfoV1* info);
RIZLM_EXPORT const void* __cdecl RizListenerManaged_GetMethod(std::uint32_t targetIndex);
// Config40: target0/1, site0..2, count0..3, known flags, reserved0. A nestedWork
// must be another configured work in this DLL. 128 permanent records/events;
// no destroy/reuse prevents a lifetime race with native observer snapshots.
// Create: 0 created; 1 invalid; 2 setup not complete; 3 capacity; 4 event failure.
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_CreateWork(const RizListenerManagedConfigV1* config, void** work);
// Invoke: 0 invocation completed (inspect Result.caught, not this status, for
// original exceptions); 1 invalid work; 2 already started; 3 worker API failure.
// Native catches preserve original SEH code/flags/parameters and verify the
// typed C++ payload. Its fourth Windows parameter is exceptionInformation3.
// no original/native exception is allowed to escape through a CLR P/Invoke.
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_InvokeWork(void* work);
// Result240 state: 0 created, 1 invoking, 2 completed. Other changing counters
// are only copied at state2; held work exposes immutable identity and hold flag.
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_ReadWork(void* work, RizListenerManagedResultV1* result);
// WaitHeld: 0 signaled; 1 invalid; 2 timeout; 3 API failure. Bounded <=30 seconds.
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_WaitHeld(void* work, std::uint32_t timeoutMs);
// Resume: 0 signaled; 1 invalid; 2 API failure. Native hold itself has 15s limit.
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_Resume(void* work);

static_assert(sizeof(RizListenerManagedConfigV1) == 40);
static_assert(offsetof(RizListenerManagedConfigV1, nestedWork) == 32);
static_assert(sizeof(RizListenerManagedInfoV1) == 336);
static_assert(offsetof(RizListenerManagedInfoV1, parent) == 16);
static_assert(offsetof(RizListenerManagedInfoV1, midReturnPc) == 72);
static_assert(offsetof(RizListenerManagedInfoV1, method) == 80);
static_assert(offsetof(RizListenerManagedInfoV1, lane) == 88);
static_assert(offsetof(RizListenerManagedInfoV1, sourceWidth) == 264);
static_assert(offsetof(RizListenerManagedInfoV1, codeWrites) == 288);
static_assert(offsetof(RizListenerManagedInfoV1, functionTable) == 312);
static_assert(sizeof(RizListenerManagedResultV1) == 240);
static_assert(offsetof(RizListenerManagedResultV1, exceptionInformation) == 48);
static_assert(offsetof(RizListenerManagedResultV1, originalCalls) == 80);
static_assert(offsetof(RizListenerManagedResultV1, invoked) == 128);
static_assert(offsetof(RizListenerManagedResultV1, event) == 176);
static_assert(offsetof(RizListenerManagedResultV1, afterCount) == 224);
