#pragma once
#include <stdint.h>

// Windows x64 only. Every pointer is an opaque identity, never dereferenced.
enum RtInitKind : uint32_t {
    RtStart, RtGetter, RtPlay, RtTooltip, RtProgress, RtMode, RtInstantaneous, RtEndRange, RtKindCount
};
struct RtInitRequest {
    uint32_t size;
    uint32_t kind;
    void* entry;
    void* self;
    void* argument;
    void* method;
    uint32_t scalar;
    uint32_t reserved;
};
struct RtInitResult {
    uint32_t size;
    uint32_t status;
    uint32_t exceptionCode;
    uint32_t threadId;
    void* reference;
};
struct RtInitObservation {
    uint32_t size;
    uint32_t kind;
    uint64_t entered;
    uint64_t exited;
    void* self;
    void* argument;
    void* method;
    uint64_t returned;
    uint32_t scalar;
    uint32_t threadId;
};
// Status: 1 returned, 2 caught the configured original exception, 3 unexpected.
// ExceptionKind: 0 none, 1 SEH, 2 the fixture's typed C++ exception.
struct RtInitCatchResult {
    uint32_t size;
    uint32_t status;
    uint32_t exceptionKind;
    uint32_t exceptionCode;
    uint64_t payload;
    void* reference;
    uint32_t threadId;
    uint32_t originalKind;
};
using RtInitObserver = void(__cdecl*)(uint32_t kind, void* context);

#ifdef RT_INIT_BUILD
#define RT_INIT_API extern "C" __declspec(dllexport)
#else
#define RT_INIT_API extern "C" __declspec(dllimport)
#endif
RT_INIT_API void* __cdecl RtInitTarget(uint32_t kind);
RT_INIT_API uint32_t __cdecl RtInitReset();
RT_INIT_API uint32_t __cdecl RtInitRead(uint32_t kind, RtInitObservation* observation);
RT_INIT_API void __cdecl RtInitSetObserver(RtInitObserver observer, void* context);
RT_INIT_API void __cdecl RtInitSetReference(void* reference);
RT_INIT_API uint32_t __cdecl RtInitPlayInputBits();
RT_INIT_API uint32_t __cdecl RtInitSetFault(uint32_t kind, uint32_t mode);
RT_INIT_API uint32_t __cdecl RtInitInvoke(const RtInitRequest* request, RtInitResult* result);
RT_INIT_API uint32_t __cdecl RtInitInvokeWorker(const RtInitRequest* request, RtInitResult* result);
RT_INIT_API uint32_t __cdecl RtInitInvokeCatch(const RtInitRequest* request, RtInitCatchResult* result);

// Joint native fixture only. Kind 4 has a parent patchable entry and a separate
// five-byte mid-site. Configure once before any invocation or parent detour;
// failure stays claimed and returns 0. It is not a live game patch publisher.
RT_INIT_API uint32_t __cdecl RtEventMidConfigure(uint32_t kind, void* helperEntry, void* actualList);
RT_INIT_API void* __cdecl RtEventMidExpectedReturn(uint32_t kind);

// Independent fixture-only pre-mid gate, kind4 exclusively. Control succeeds
// only while no kind4 original is active, resets State, and blocks/unblocks the
// next calls without changing the existing post-mid RtVoidControl gate.
// State: 0 not reached since Control, 1 reached, 2 finite wait timed out.
// Resume leaves the reached state visible. Timeout raises native 0xE0564254.
RT_INIT_API uint32_t __cdecl RtEventMidControlBefore(uint32_t kind, uint32_t block);
RT_INIT_API uint32_t __cdecl RtEventMidBeforeState(uint32_t kind);
RT_INIT_API void __cdecl RtEventMidResumeBefore(uint32_t kind);
