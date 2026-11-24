#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <werapi.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "../riz_init_call.h"

// This EXE links the exact production riz_init_call.c object. The ready input
// is controlled; no actual Doorstop producer, CoreCLR or game runs here.
namespace {
constexpr DWORD OriginalSeh = 0xE052494F, BootstrapSeh = 0xE0524942;
constexpr std::uint64_t Payload = 0xFEDCBA9876543210ULL;
constexpr char DomainText[] = "Riz native high-pointer domain";
struct NativeCppException { std::uint64_t payload; const char* domain; bool bootstrap; };
int checks{}, failures{}, ready{}, resultValue{}, originalCalls{}, bootstrapCalls{};
int originalMode{}, bootstrapMode{};
bool recursiveOriginal{}, recursiveBootstrap{}, repeat{}, returned{}, caught{};
DWORD caughtCode{};
DWORD firstChanceCode{}, firstChanceFlags{}, caughtFlags{}, firstChanceParameters{}, caughtParameters{};
std::uint64_t firstChancePayload[3]{}, caughtPayload[3]{};
unsigned firstChanceCount{};
char* domain{};
std::uint32_t order[8]{};
unsigned orderCount{};
void Check(bool condition, const char* expression, int line)
{
    ++checks;
    if (!condition) { ++failures; std::fprintf(stderr, "FAILED Doorstop init line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
void Record(std::uint32_t event) { CHECK(orderCount < 8); if (orderCount < 8) order[orderCount++] = event; }
void PrintCheckpoint(const char* stage)
{
    std::printf("{\"kind\":\"doorstop-init-rejection-checkpoint\",\"stage\":\"%s\",\"originalCalls\":%d,\"bootstrapCalls\":%d,\"checks\":%d,\"failures\":%d}\n",
        stage, originalCalls, bootstrapCalls, checks, failures);
    std::fflush(stdout);
}
int __cdecl Original(const char* actualDomain);
void __cdecl Bootstrap();
LONG CALLBACK ObserveFirstChance(EXCEPTION_POINTERS* pointers)
{
    const auto* exception = pointers->ExceptionRecord;
    if (exception->ExceptionCode != OriginalSeh && exception->ExceptionCode != BootstrapSeh)
        return EXCEPTION_CONTINUE_SEARCH;
    ++firstChanceCount;
    firstChanceCode = exception->ExceptionCode;
    firstChanceFlags = exception->ExceptionFlags;
    firstChanceParameters = exception->NumberParameters;
    for (unsigned index = 0; index != 3; ++index)
        firstChancePayload[index] = index < exception->NumberParameters ? exception->ExceptionInformation[index] : 0;
    // Passive first-chance evidence, before any frame's exception filter. The
    // original record and control flow are not changed by this observer.
    return EXCEPTION_CONTINUE_SEARCH;
}
int __cdecl Original(const char* actualDomain)
{
    CHECK(actualDomain == domain && reinterpret_cast<std::uintptr_t>(actualDomain) > 0xffffffffULL);
    CHECK(std::memcmp(actualDomain, DomainText, sizeof(DomainText)) == 0);
    CHECK(++originalCalls == 1 && bootstrapCalls == 0); Record(1);
    if (recursiveOriginal) {
        PrintCheckpoint("recursive-original");
        RizInvokeIl2CppInit(ready, domain, Original, Bootstrap);
        CHECK(false);
    }
    if (originalMode == 1) {
        Record(2);
        const ULONG_PTR values[]{static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(actualDomain), 0};
        RaiseException(OriginalSeh, 0, 3, values);
    } else if (originalMode == 2) { Record(2); throw NativeCppException{Payload, actualDomain, false}; }
    Record(3); return resultValue;
}
void __cdecl Bootstrap()
{
    CHECK(ready == 1 && resultValue != 0 && originalCalls == 1 && ++bootstrapCalls == 1);
    CHECK(orderCount == 3 && order[0] == 0 && order[1] == 1 && order[2] == 3); Record(4);
    if (recursiveBootstrap) {
        PrintCheckpoint("recursive-bootstrap");
        RizInvokeIl2CppInit(ready, domain, Original, Bootstrap);
        CHECK(false);
    }
    if (bootstrapMode == 1) {
        Record(5);
        const ULONG_PTR values[]{static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(domain), 1};
        RaiseException(BootstrapSeh, 0, 3, values);
    } else if (bootstrapMode == 2) { Record(5); throw NativeCppException{Payload, domain, true}; }
    Record(6);
}
LONG CatchSeh(EXCEPTION_POINTERS* pointers)
{
    const auto* exception = pointers->ExceptionRecord;
    if (exception->ExceptionCode == 0xE06D7363) return EXCEPTION_CONTINUE_SEARCH;
    caughtFlags = exception->ExceptionFlags; caughtParameters = exception->NumberParameters;
    for (unsigned index = 0; index != 3; ++index)
        caughtPayload[index] = index < exception->NumberParameters ? exception->ExceptionInformation[index] : 0;
    CHECK(exception->ExceptionCode == (bootstrapMode ? BootstrapSeh : OriginalSeh));
    // RaiseException requested flags=0, but the observed first-chance record
    // may already contain Windows provenance flags. Preserve that actual
    // record through the production C frames instead of inventing flags=0.
    CHECK(firstChanceCount == 1 && exception->ExceptionCode == firstChanceCode
        && exception->ExceptionFlags == firstChanceFlags
        && exception->NumberParameters == firstChanceParameters
        && std::memcmp(caughtPayload, firstChancePayload, sizeof(caughtPayload)) == 0);
    CHECK(exception->NumberParameters == 3
        && exception->ExceptionInformation[0] == Payload
        && exception->ExceptionInformation[1] == reinterpret_cast<ULONG_PTR>(domain)
        && exception->ExceptionInformation[2] == static_cast<ULONG_PTR>(bootstrapMode ? 1 : 0));
    caught = true; caughtCode = exception->ExceptionCode; return EXCEPTION_EXECUTE_HANDLER;
}
int observedResult{};
void ExecuteSeh()
{
    __try { observedResult = RizInvokeIl2CppInit(ready, domain, Original, ready ? Bootstrap : nullptr); returned = true; }
    __except (CatchSeh(GetExceptionInformation())) { }
}
void Execute()
{
    try { ExecuteSeh(); }
    catch (const NativeCppException& exception) {
        CHECK(exception.payload == Payload && exception.domain == domain && exception.bootstrap == (bootstrapMode != 0));
        caught = true; caughtCode = 0xE06D7363;
    }
}
bool Equal(const wchar_t* mode, const wchar_t* text) { return _wcsicmp(mode, text) == 0; }
}

int wmain(int argc, wchar_t** argv)
{
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    CHECK(SUCCEEDED(WerSetFlags(WER_FAULT_REPORTING_NO_UI)));
    if (argc != 2) { std::fprintf(stderr, "One independent init-contract case is required.\n"); return 2; }
    const auto* mode = argv[1];
    ready = std::wcsncmp(mode, L"disabled-", 9) == 0 ? 0 : 1;
    resultValue = 17;
    if (Equal(mode, L"ready-zero") || Equal(mode, L"disabled-zero")) resultValue = 0;
    else if (Equal(mode, L"ready-negative") || Equal(mode, L"disabled-negative")) resultValue = static_cast<int>(0x81234567u);
    else if (Equal(mode, L"ready-original-seh") || Equal(mode, L"disabled-original-seh")) originalMode = 1;
    else if (Equal(mode, L"ready-original-cpp") || Equal(mode, L"disabled-original-cpp")) originalMode = 2;
    else if (Equal(mode, L"bootstrap-seh")) bootstrapMode = 1;
    else if (Equal(mode, L"bootstrap-cpp")) bootstrapMode = 2;
    else if (Equal(mode, L"recursive-original")) recursiveOriginal = true;
    else if (Equal(mode, L"recursive-bootstrap")) recursiveBootstrap = true;
    else if (Equal(mode, L"repeat")) repeat = true;
    else if (Equal(mode, L"invalid-ready")) ready = 2;
    else if (!Equal(mode, L"ready-positive") && !Equal(mode, L"disabled-positive")) {
        std::fprintf(stderr, "Unknown init-contract case\n"); return 2;
    }
    domain = static_cast<char*>(VirtualAlloc(reinterpret_cast<void*>(0x123400000000ULL), 4096,
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    if (!domain) domain = static_cast<char*>(VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    CHECK(domain != nullptr && reinterpret_cast<std::uintptr_t>(domain) > 0xffffffffULL); if (!domain) return 2;
    std::memcpy(domain, DomainText, sizeof(DomainText));
    const auto firstChanceObserver = AddVectoredExceptionHandler(1, ObserveFirstChance);
    CHECK(firstChanceObserver != nullptr);
    Record(0);
    // Ready is controlled input here, not a real producer receipt. Fatal cases
    // print a checkpoint which the supervising PowerShell verifies together
    // with the process's actual STATUS_FAIL_FAST_EXCEPTION exit code.
    if (ready == 2) PrintCheckpoint("invalid-ready");
    Execute();
    if (recursiveOriginal || recursiveBootstrap || ready == 2) { CHECK(false); return 3; }
    CHECK(originalCalls == 1);
    if (repeat) {
        CHECK(returned && !caught && bootstrapCalls == 1 && observedResult == resultValue);
        PrintCheckpoint("repeat"); RizInvokeIl2CppInit(ready, domain, Original, Bootstrap); CHECK(false); return 3;
    }
    CHECK(returned == (originalMode == 0 && bootstrapMode == 0));
    CHECK(caught == (originalMode != 0 || bootstrapMode != 0));
    CHECK(bootstrapCalls == (ready && !originalMode && resultValue != 0 ? 1 : 0));
    if (returned) CHECK(observedResult == resultValue);
    if (originalMode) {
        CHECK(orderCount == 3 && order[0] == 0 && order[1] == 1 && order[2] == 2);
        CHECK(caughtCode == (originalMode == 1 ? OriginalSeh : 0xE06D7363));
    } else if (bootstrapMode) {
        CHECK(orderCount == 5 && order[0] == 0 && order[1] == 1 && order[2] == 3 && order[3] == 4 && order[4] == 5);
        CHECK(caughtCode == (bootstrapMode == 1 ? BootstrapSeh : 0xE06D7363));
    } else if (bootstrapCalls) CHECK(orderCount == 5 && order[0] == 0 && order[1] == 1 && order[2] == 3 && order[3] == 4 && order[4] == 6);
    else CHECK(orderCount == 3 && order[0] == 0 && order[1] == 1 && order[2] == 3);
    CHECK(firstChanceObserver && RemoveVectoredExceptionHandler(firstChanceObserver) != 0);
    std::printf("{\"schemaVersion\":1,\"kind\":\"doorstop-production-init-call\",\"mode\":\"%ls\",\"passed\":%s,\"checks\":%d,\"failures\":%d,\"originalCalls\":%d,\"bootstrapCalls\":%d,\"readyControlled\":true,\"originalIntBits\":%u,\"returned\":%s,\"caught\":%s,\"exceptionCode\":%lu,\"raiseExceptionSourceFlags\":0,\"sehFirstChanceCount\":%u,\"sehFirstChanceCode\":%lu,\"sehFirstChanceFlags\":%lu,\"sehCaughtFlags\":%lu,\"sehFirstChanceParameters\":%lu,\"sehCaughtParameters\":%lu,\"sehFirstChancePayload\":[%llu,%llu,%llu],\"sehCaughtPayload\":[%llu,%llu,%llu],\"domainPointer\":%llu,\"domainHighPointer\":true,\"productionSourceExecuted\":true,\"actualProducerExecuted\":false,\"coreClrExecuted\":false,\"gameCodeExecuted\":false,\"coldStartupVerified\":false}\n",
        mode, failures ? "false" : "true", checks, failures, originalCalls, bootstrapCalls, static_cast<unsigned>(resultValue), returned ? "true" : "false", caught ? "true" : "false", caughtCode,
        firstChanceCount, firstChanceCode, firstChanceFlags, caughtFlags, firstChanceParameters, caughtParameters,
        firstChancePayload[0], firstChancePayload[1], firstChancePayload[2], caughtPayload[0], caughtPayload[1], caughtPayload[2], reinterpret_cast<std::uint64_t>(domain));
    VirtualFree(domain, 0, MEM_RELEASE); return failures ? 1 : 0;
}
