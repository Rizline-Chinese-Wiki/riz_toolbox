#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <cstring>
#include "EngineNodeManagedFixture.h"

namespace {
constexpr DWORD OriginalSeh = 0xe04e4d53;
constexpr std::uint64_t Payload = 0xfedcba9876543210ULL;
struct Work {
    RizEngineNodeManagedResultV1 result{};
    unsigned char owner[16]{}, enumerator[16]{}, name[16]{}, cache[16]{};
    unsigned char* nodePage{};
    unsigned char* outputPage{};
    std::uint8_t executionOutput{};
    std::uint32_t requestedReturn{};
};
Work records[256]{};
volatile LONG nextRecord{};
thread_local Work* current{};
struct BodyConfig { RizEngineNodeManagedBodyV1 observer{}; void* user{}; } bodyConfig;
void* volatile bodyPublished{};
volatile LONG bodyClaim{};
struct OriginalCpp { std::uint64_t payload; const Work* work; std::uint32_t kind; };
void Check(Work* work, bool okay)
{ ++work->result.nativeChecks; if (!okay) ++work->result.nativeFailures; }
Work* Reserve()
{
    LONG index = InterlockedCompareExchange(&nextRecord, 0, 0);
    for (;;) {
        if (index == 256) return nullptr;
        const auto seen = InterlockedCompareExchange(&nextRecord, index + 1, index);
        if (seen == index) return &records[index];
        index = seen;
    }
}
void WriteNode(Work* work)
{
    auto& result = work->result;
    std::memcpy(work->nodePage + 0x10, &result.enumHandle, 8);
    std::memcpy(work->nodePage + 0x18, &result.enumType, 4);
    std::memcpy(work->nodePage + 0x20, &result.enumCache, 8);
    std::memcpy(work->nodePage + 0x58, &result.owner, 8);
    std::memcpy(work->nodePage + 0x60, &result.referenceCount, 4);
}
void FreeNode(Work* work)
{
    if (work->result.nodeFreed) return;
    const auto okay = VirtualFree(work->nodePage, 0, MEM_RELEASE);
    Check(work, okay != FALSE);
    if (okay) work->result.nodeFreed = 1;
}
__declspec(noinline) std::uint8_t __cdecl TargetStart(void*, void*, void*, void**);
__declspec(noinline) std::uint32_t __cdecl TargetExecute(void*, std::uint8_t*);
__declspec(noinline) std::uint8_t __cdecl TargetRelease(void*);
__declspec(noinline) void __cdecl TargetDetach(void*);
void InvokeSeh(void* target, Work* work);
void InvokeAndCatch(void* target, Work* work);
void Nested(Work* parent, unsigned kind)
{
    auto* work = Reserve(); Check(parent, work != nullptr); if (!work) return;
    auto& result = work->result;
    result.abiVersion = 1; result.byteSize = sizeof(result); result.kind = kind;
    result.node = parent->result.node; result.owner = parent->result.owner;
    result.enumHandle = parent->result.enumHandle; result.enumCache = parent->result.enumCache;
    result.enumType = parent->result.enumType; result.referenceCount = parent->result.referenceCount;
    result.arg0 = result.node; result.arg1 = kind == 1 ? &work->executionOutput : nullptr;
    result.scenario = kind == 2 ? RizEngineNodeManagedFreeNode : 0u;
    work->nodePage = parent->nodePage; work->requestedReturn = kind == 2 ? 0u : 0xa5c3fe81u;
    InvokeAndCatch(kind == 1 ? reinterpret_cast<void*>(&TargetExecute) : reinterpret_cast<void*>(&TargetRelease), work);
    Check(parent, result.originalCalls == 1 && result.returned == 1 && result.caught == 0 && result.returnByte == (kind == 2 ? 0u : 0xa5c3fe81u)
        && result.nativeFailures == 0);
    parent->result.nativeChecks += result.nativeChecks;
    parent->result.nativeFailures += result.nativeFailures;
    parent->result.nestedCalls += result.originalCalls;
    if (result.nodeFreed) parent->result.nodeFreed = 1;
}
void Body(unsigned kind, void* arg0, void* arg1, void* arg2, void* arg3, const void* caller)
{
    auto* work = current;
    // An invalid external call to an owned target is outside this fixture's
    // Invoke contract; fail natively rather than guessing an owner from node.
    if (!work) RaiseException(OriginalSeh, EXCEPTION_NONCONTINUABLE, 0, nullptr);
    auto& result = work->result;
    ++result.originalCalls; result.originalCaller = caller;
    Check(work, result.kind == kind && result.arg0 == arg0 && result.arg1 == arg1 && result.arg2 == arg2 && result.arg3 == arg3);
    Check(work, result.originalCalls == 1 && caller != nullptr);
    const auto* body = static_cast<const BodyConfig*>(InterlockedCompareExchangePointer(&bodyPublished, nullptr, nullptr));
    if (body) body->observer(&result, body->user);
    if (result.mode == 1) {
        const ULONG_PTR values[]{ static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(work), kind };
        RaiseException(OriginalSeh, 0, 3, values);
    } else if (result.mode == 2) throw OriginalCpp{ Payload, work, kind };
    if (kind == 0) {
        if (result.scenario & RizEngineNodeManagedNestedExecute) Nested(work, 1);
        if (result.scenario & RizEngineNodeManagedNestedRelease) Nested(work, 2);
        result.outputNode = result.scenario & RizEngineNodeManagedNullOutputValue ? nullptr : result.node;
        if (arg3) *static_cast<void**>(arg3) = result.outputNode;
        if (result.scenario & RizEngineNodeManagedProtectOutput) {
            DWORD old{};
            Check(work, VirtualProtect(work->outputPage, 4096, PAGE_NOACCESS, &old) != FALSE);
            result.outputSlotProtected = 1;
        }
    } else {
        if (kind == 1) *static_cast<std::uint8_t*>(arg1) = 0x9d;
        if (result.scenario & RizEngineNodeManagedFreeNode) FreeNode(work);
    }
}
// Separate compiler-generated entry prologues are intentionally not folded.
// The actual Dobby test publishes these native targets, not managed delegates.
#pragma optimize("", off)
__declspec(noinline) std::uint8_t __cdecl TargetStart(void* owner, void* enumerator, void* name, void** output)
{ Body(0, owner, enumerator, name, output, _ReturnAddress()); return static_cast<std::uint8_t>(current->requestedReturn); }
__declspec(noinline) std::uint32_t __cdecl TargetExecute(void* node, std::uint8_t* output)
{ Body(1, node, output, nullptr, nullptr, _ReturnAddress()); return current->requestedReturn; }
__declspec(noinline) std::uint8_t __cdecl TargetRelease(void* node)
{ Body(2, node, nullptr, nullptr, nullptr, _ReturnAddress()); return static_cast<std::uint8_t>(current->requestedReturn); }
__declspec(noinline) void __cdecl TargetDetach(void* node)
{ Body(3, node, nullptr, nullptr, nullptr, _ReturnAddress()); }
#pragma optimize("", on)
LONG Filter(EXCEPTION_POINTERS* pointers, Work* work)
{
    auto& result = work->result; const auto* record = pointers->ExceptionRecord;
    result.exceptionCode = record->ExceptionCode; result.exceptionFlags = record->ExceptionFlags;
    result.exceptionParameterCount = record->NumberParameters; result.exceptionAddress = record->ExceptionAddress;
    for (unsigned i = 0; i != 4; ++i)
        result.exceptionInformation[i] = i < record->NumberParameters ? record->ExceptionInformation[i] : 0;
    return record->ExceptionCode == 0xe06d7363 ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER;
}
void InvokeSeh(void* target, Work* work)
{
    auto& result = work->result;
    __try {
        if (result.kind == 0) result.returnByte = reinterpret_cast<std::uint8_t(__cdecl*)(void*, void*, void*, void**)>(target)
            (result.arg0, result.arg1, result.arg2, static_cast<void**>(result.arg3));
        else if (result.kind == 1) result.returnByte = reinterpret_cast<std::uint32_t(__cdecl*)(void*, std::uint8_t*)>(target)
            (result.arg0, static_cast<std::uint8_t*>(result.arg1));
        else if (result.kind == 2) result.returnByte = reinterpret_cast<std::uint8_t(__cdecl*)(void*)>(target)(result.arg0);
        else reinterpret_cast<void(__cdecl*)(void*)>(target)(result.arg0);
        result.returned = 1;
    } __except (Filter(GetExceptionInformation(), work)) { result.caught = 1; }
}
void InvokeAndCatch(void* target, Work* work)
{
    auto* previous = current; current = work;
    try { InvokeSeh(target, work); }
    catch (const OriginalCpp& error) {
        Check(work, error.payload == Payload && error.work == work && error.kind == work->result.kind);
        Check(work, work->result.exceptionCode == 0xe06d7363 && work->result.exceptionParameterCount == 4
            && work->result.exceptionInformation[0] == 0x19930520 && work->result.exceptionInformation[1]
            && work->result.exceptionInformation[2] && work->result.exceptionInformation[3]);
        work->result.caught = 1;
    } catch (...) { Check(work, false); work->result.caught = 1; }
    current = previous;
}
}
RIZENM_EXPORT void* __cdecl RizEngineNodeManaged_GetTarget(std::uint32_t kind)
{
    return kind == 0 ? reinterpret_cast<void*>(&TargetStart) : kind == 1 ? reinterpret_cast<void*>(&TargetExecute)
        : kind == 2 ? reinterpret_cast<void*>(&TargetRelease) : kind == 3 ? reinterpret_cast<void*>(&TargetDetach) : nullptr;
}
RIZENM_EXPORT const RizEngineNodeManagedResultV1* __cdecl RizEngineNodeManaged_GetCurrent()
{ return current ? &current->result : nullptr; }
RIZENM_EXPORT std::uint32_t __cdecl RizEngineNodeManaged_SetBodyObserver(RizEngineNodeManagedBodyV1 observer, void* user)
{
    if (!observer || !user) return 1;
    if (InterlockedCompareExchange(&bodyClaim, 1, 0) != 0) return 2;
    bodyConfig = { observer, user }; InterlockedExchangePointer(&bodyPublished, &bodyConfig);
    return 0;
}
RIZENM_EXPORT std::uint32_t __cdecl RizEngineNodeManaged_Invoke(void* target, std::uint32_t kind,
    std::uint32_t mode, std::uint32_t scenario, std::uint32_t returnByte, RizEngineNodeManagedResultV1* output)
{
    constexpr unsigned StartFlags = RizEngineNodeManagedProtectOutput | RizEngineNodeManagedNestedExecute
        | RizEngineNodeManagedNestedRelease | RizEngineNodeManagedNullOutputSlot | RizEngineNodeManagedNullOutputValue;
    if (!target || kind > 3 || mode > 2 || scenario > 127 || (kind != 1 && returnByte > 255) || !output
        || output->abiVersion != 1 || output->byteSize != sizeof(*output) || output->reserved
        || (kind != 0 && (scenario & StartFlags))
        || (kind == 0 && (scenario & RizEngineNodeManagedFreeNode))
        || ((scenario & RizEngineNodeManagedProtectOutput) && (scenario & RizEngineNodeManagedNullOutputSlot))) return 1;
    auto* work = Reserve(); if (!work) return 2;
    auto& result = work->result;
    result.abiVersion = 1; result.byteSize = sizeof(result); result.kind = kind; result.mode = mode; result.scenario = scenario;
    work->requestedReturn = returnByte;
    work->nodePage = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    work->outputPage = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    if (!work->nodePage || !work->outputPage) return 2;
    result.node = work->nodePage; result.owner = work->owner; result.enumCache = kind == 0 ? work->enumerator : work->cache;
    result.enumHandle = 0xf123456700000000ULL | static_cast<std::uint64_t>(work - records + 1);
    result.enumType = kind == 0 ? 2u : (0xa5c30000u | static_cast<unsigned>(work - records + 1)); result.referenceCount = 3;
    WriteNode(work);
    result.arg0 = kind == 0 ? result.owner : result.node;
    result.arg1 = kind == 0 ? static_cast<void*>(work->enumerator) : kind == 1 ? &work->executionOutput : nullptr;
    result.arg2 = kind == 0 ? work->name : nullptr;
    result.arg3 = kind == 0 && !(scenario & RizEngineNodeManagedNullOutputSlot) ? work->outputPage : nullptr;
    if (scenario & RizEngineNodeManagedUnreadableNode) {
        DWORD old{}; Check(work, VirtualProtect(work->nodePage, 4096, PAGE_NOACCESS, &old) != FALSE);
    }
    InvokeAndCatch(target, work);
    Check(work, result.originalCalls == 1 && result.returned == (mode == 0 ? 1u : 0u)
        && result.caught == (mode == 0 ? 0u : 1u));
    if (!mode && kind != 3) Check(work, result.returnByte == returnByte);
    if (mode == 1) Check(work, result.exceptionCode == OriginalSeh && result.exceptionParameterCount == 3
        && result.exceptionInformation[0] == Payload && result.exceptionInformation[1] == reinterpret_cast<std::uint64_t>(work)
        && result.exceptionInformation[2] == kind);
    *output = result;
    return 0;
}
