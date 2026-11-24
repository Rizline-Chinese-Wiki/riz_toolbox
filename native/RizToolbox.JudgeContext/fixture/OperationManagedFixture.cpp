#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include "OperationManagedFixture.h"

namespace {
constexpr DWORD OriginalSeh = 0xe04f4d53;
constexpr std::uint64_t Payload = 0xfedcba9876543210ULL;
unsigned char opaqueMethod[16]{};
struct Work {
    RizOperationManagedResultV1 result{};
    std::uint64_t storage0 = 0x1111222233334444ULL, storage1 = 0x5555666677778888ULL;
};
Work works[64]{};
volatile LONG nextWork = 0;
struct OriginalCpp { std::uint64_t payload; const Work* self; std::uint32_t kind; };
void Check(Work* work, bool condition)
{ ++work->result.nativeChecks; if (!condition) ++work->result.nativeFailures; }
void Original(Work* work, std::uint32_t kind, void* arg0, void* arg1, const void* method, const void* caller)
{
    auto& result = work->result;
    ++result.originalCalls; result.originalCaller = caller;
    Check(work, result.kind == kind && result.self == work && result.arg0 == arg0 && result.arg1 == arg1 && result.method == method);
    Check(work, caller != nullptr && result.originalCalls == 1);
    if (result.mode == 1) {
        const ULONG_PTR values[]{ static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(work), kind };
        RaiseException(OriginalSeh, 0, 3, values);
    } else if (result.mode == 2) throw OriginalCpp{ Payload, work, kind };
}
__declspec(noinline) void __cdecl Original0(void* self, const void* method)
{ const auto* caller = _ReturnAddress(); Original(static_cast<Work*>(self), 0, nullptr, nullptr, method, caller); }
__declspec(noinline) void __cdecl Original1(void* self, void* arg0, const void* method)
{ const auto* caller = _ReturnAddress(); Original(static_cast<Work*>(self), 1, arg0, nullptr, method, caller); }
__declspec(noinline) void __cdecl Original2(void* self, void* arg0, void* arg1, const void* method)
{ const auto* caller = _ReturnAddress(); Original(static_cast<Work*>(self), 2, arg0, arg1, method, caller); }
LONG Filter(EXCEPTION_POINTERS* pointers, Work* work)
{
    const auto* record = pointers->ExceptionRecord; auto& result = work->result;
    result.exceptionCode = record->ExceptionCode; result.exceptionFlags = record->ExceptionFlags;
    result.exceptionParameterCount = record->NumberParameters; result.exceptionAddress = record->ExceptionAddress;
    for (unsigned i = 0; i != 4; ++i)
        result.exceptionInformation[i] = i < record->NumberParameters ? record->ExceptionInformation[i] : 0;
    return record->ExceptionCode == 0xe06d7363 ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER;
}
// Separate SEH leaf so the original C++ exception can continue into a real
// typed native catch. Observers live in the actual helper, not in this fixture.
void InvokeSeh(void* entry, Work* work)
{
    __try {
        const auto& result = work->result;
        if (result.kind == 0)
            reinterpret_cast<void(__cdecl*)(void*, const void*)>(entry)(work, result.method);
        else if (result.kind == 1)
            reinterpret_cast<void(__cdecl*)(void*, void*, const void*)>(entry)(work, const_cast<void*>(result.arg0), result.method);
        else
            reinterpret_cast<void(__cdecl*)(void*, void*, void*, const void*)>(entry)(work, const_cast<void*>(result.arg0), const_cast<void*>(result.arg1), result.method);
        work->result.returned = 1;
    } __except (Filter(GetExceptionInformation(), work)) { work->result.caught = 1; }
}
}
RIZOM_EXPORT void* __cdecl RizOperationManaged_GetOriginal(std::uint32_t kind)
{
    return kind == 0 ? reinterpret_cast<void*>(&Original0) : kind == 1 ? reinterpret_cast<void*>(&Original1)
        : kind == 2 ? reinterpret_cast<void*>(&Original2) : nullptr;
}
RIZOM_EXPORT const void* __cdecl RizOperationManaged_GetMethod() { return opaqueMethod; }
RIZOM_EXPORT std::uint32_t __cdecl RizOperationManaged_Invoke(void* entry, std::uint32_t kind,
    std::uint32_t mode, const void* method, RizOperationManagedResultV1* output)
{
    if (!entry || !method || kind > 2 || mode > 2 || !output || output->abiVersion != 1 || output->byteSize != sizeof(*output)
        || output->reserved != 0 || output->reservedTail != 0) return 1;
    LONG index = InterlockedCompareExchange(&nextWork, 0, 0);
    for (;;) {
        if (index == 64) return 2;
        const auto seen = InterlockedCompareExchange(&nextWork, index + 1, index);
        if (seen == index) break;
        index = seen;
    }
    auto* work = &works[index]; auto& result = work->result;
    result.abiVersion = 1; result.byteSize = sizeof(result); result.kind = kind; result.mode = mode;
    result.self = work; result.arg0 = kind > 0 ? &work->storage0 : nullptr;
    result.arg1 = kind > 1 ? &work->storage1 : nullptr; result.method = method;
    try { InvokeSeh(entry, work); }
    catch (const OriginalCpp& error) {
        Check(work, error.payload == Payload && error.self == work && error.kind == kind);
        HMODULE own{};
        const auto found = GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&Original0), &own);
        Check(work, result.exceptionCode == 0xe06d7363 && result.exceptionParameterCount == 4
            && result.exceptionInformation[0] == 0x19930520 && result.exceptionInformation[1] != 0
            && result.exceptionInformation[2] != 0 && found && result.exceptionInformation[3] == reinterpret_cast<std::uint64_t>(own));
        result.caught = 1;
    } catch (...) { Check(work, false); result.caught = 1; }
    Check(work, result.originalCalls == 1 && result.returned == (mode == 0 ? 1u : 0u) && result.caught == (mode == 0 ? 0u : 1u));
    if (mode == 1) Check(work, result.exceptionCode == OriginalSeh && result.exceptionParameterCount == 3
        && result.exceptionInformation[0] == Payload && result.exceptionInformation[1] == reinterpret_cast<std::uint64_t>(work)
        && result.exceptionInformation[2] == kind);
    *output = result; return 0;
}
