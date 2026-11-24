#define RIZ_PLAYBACK_STOCK_EXPORTS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <new>
#include "StartInitializationFixture.h"
namespace {
struct MethodInfo2022 {
    void* Entry; void* VirtualEntry; void* Invoker; const char* Name; void* Class; void* Returns;
    void* Parameters; void* RuntimeData; void* GenericData;
    std::uint32_t Token; std::uint16_t Flags, ImplementationFlags, Slot;
    unsigned char ParameterCount, Bitfield;
};
std::uint64_t startClass = 0x73746172743235ull;
struct PrimitiveType { void* Data; std::uint32_t Bits, Padding; } boolType{nullptr, 0x00020000u, 0};
MethodInfo2022 startMethod{reinterpret_cast<void*>(&StockStartMoveNext), reinterpret_cast<void*>(&StockStartMoveNext),
    nullptr, "MoveNext", &startClass, &boolType, nullptr, nullptr, nullptr, 0x0600100bu, 0x81, 0, 0xffff, 0, 0};
StockStartBoundary boundary;
volatile LONG firstAllocations = 0, allAllocations = 0;
}
static_assert(offsetof(StockStartEnumerator, State) == 0x10 && offsetof(StockStartEnumerator, Progress) == 0x20);
static_assert(offsetof(StockStartEnumerator, Clip) == 0x38 && offsetof(StockStartEnumerator, Candidate) == 0x40);
static_assert(offsetof(StockProgress, AudioSource) == 0x28 && offsetof(StockProgress, Player) == 0x38);
extern "C" __declspec(dllexport) void* __cdecl StockMethodStartMoveNext() { return &startMethod; }
extern "C" __declspec(dllexport) StockStartEnumerator* __cdecl StockFixtureCreateStart(StockProgress* progress, void* clip) {
    auto* value = new(std::nothrow) StockStartEnumerator{};
    if (value) { value->Class = &startClass; value->Progress = progress; value->Clip = clip; }
    return value;
}
extern "C" __declspec(dllexport) void __cdecl StockFixtureSetStartBoundary(StockStartBoundary callback) { boundary = callback; }
extern "C" __declspec(dllexport) void __cdecl StockFixtureConfigureStartWaitUntil(StockStartEnumerator* value,
    void* receiver, void* reference, void* incomingMethod) {
    if (value) { value->WaitUntilReceiver = receiver; value->WaitUntilReference = reference; value->WaitUntilMethod = incomingMethod; }
}
extern "C" void __cdecl StockStartBoundaryBody(StockStartEnumerator* value, std::uint32_t at) {
    if (boundary) boundary(value, at);
    if (value->ThrowBoundary == at) RaiseException(0xe2400030u + at, 0, 0, nullptr);
}
extern "C" StockPlayer* __cdecl StockStartAllocatePlayer(StockStartEnumerator* value) {
    auto* player = new(std::nothrow) StockPlayer{};
    if (!player) RaiseException(0xe2400038u, 0, 0, nullptr);
    // Owned fixture allocation uses the actual method metadata's class. It
    // never borrows an existing Progress.Player, even for the very first Start.
    player->Class = static_cast<MethodInfo2022*>(StockMethodGetTime())->Class;
    player->Active = 1; player->Dsp = 100;
    InterlockedIncrement(&allAllocations);
    if (!value->Progress->Player) InterlockedIncrement(&firstAllocations);
    return player;
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl StockFixtureStartAllocationCount() { return static_cast<std::uint32_t>(InterlockedCompareExchange(&allAllocations, 0, 0)); }
extern "C" __declspec(dllexport) std::uint32_t __cdecl StockFixtureStartNoPlayerAllocationCount() { return static_cast<std::uint32_t>(InterlockedCompareExchange(&firstAllocations, 0, 0)); }
extern "C" __declspec(dllexport) int __cdecl StockFixtureInvokeStart(void* target, StockStartEnumerator* value,
    void* method, std::uint32_t* raw, std::uint32_t* exceptionCode) {
    if (!target || !value || !raw || !exceptionCode) return 0;
    __try { *raw = reinterpret_cast<std::uint8_t(__cdecl*)(StockStartEnumerator*,void*)>(target)(value,method); *exceptionCode = 0; return 1; }
    __except(EXCEPTION_EXECUTE_HANDLER) { *raw = 0; *exceptionCode = GetExceptionCode(); return 2; }
}
