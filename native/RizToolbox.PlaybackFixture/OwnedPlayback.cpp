#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>
extern "C" float OwnedPlayback_GetTime(void*, void*);
extern "C" { unsigned char OwnedPlayback_Initialized = 0; }
namespace {
struct MethodInfo2022 {
    void* entry; void* virtualEntry; void* invoker; const char* name; void* klass; void* returns;
    void* parameters; void* runtimeData; void* genericData;
    std::uint32_t token; std::uint16_t flags, implementationFlags, slot; unsigned char parameterCount, bitfield;
};
struct PrimitiveType { void* data; std::uint32_t bits; std::uint32_t padding; } singleType{nullptr, 0x000c0000u, 0};
std::uint64_t ownedClass = 0x72697a706c617962ull;
MethodInfo2022 method{reinterpret_cast<void*>(&OwnedPlayback_GetTime), reinterpret_cast<void*>(&OwnedPlayback_GetTime), nullptr,
    "GetTime", &ownedClass, &singleType, nullptr, nullptr, nullptr, 0x06000001u, 0x86, 0, 0xffff, 0, 0};
volatile LONG calls = 0, branch = 0, block = 0;
void* lastSelf = nullptr; void* lastMethod = nullptr;
HANDLE entered = nullptr, release = nullptr;
std::uint32_t returnedBits = 0x3f800000u;
}
extern "C" float OwnedPlayback_Body(void* self, void* incomingMethod, unsigned initialized) {
    InterlockedIncrement(&calls); InterlockedExchange(&branch, static_cast<LONG>(initialized));
    lastSelf = self; lastMethod = incomingMethod;
    const auto mode = InterlockedCompareExchange(&block, 0, 0);
    if (mode != 0) {
        SetEvent(entered); if (WaitForSingleObject(release, 10000) != WAIT_OBJECT_0) RaiseException(0xe1000451u, 0, 0, nullptr);
    }
    if (mode == 2) RaiseException(0xe1000452u, 0, 0, nullptr);
    float value{}; std::memcpy(&value, &returnedBits, sizeof(value)); return value;
}
extern "C" __declspec(dllexport) void* RizOwnedPlayback_GetTarget() { return reinterpret_cast<void*>(&OwnedPlayback_GetTime); }
extern "C" __declspec(dllexport) void* RizOwnedPlayback_GetMethod() { return &method; }
extern "C" __declspec(dllexport) void* RizOwnedPlayback_GetClass() { return &ownedClass; }
extern "C" __declspec(dllexport) void RizOwnedPlayback_Set(unsigned initialized, unsigned bits, unsigned shouldBlock) {
    if (!entered) entered = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!release) release = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    ResetEvent(entered); ResetEvent(release);
    OwnedPlayback_Initialized = static_cast<unsigned char>(initialized); returnedBits = bits;
    InterlockedExchange(&block, static_cast<LONG>(shouldBlock));
}
extern "C" __declspec(dllexport) unsigned RizOwnedPlayback_WaitEntered() { return WaitForSingleObject(entered, 10000); }
extern "C" __declspec(dllexport) void RizOwnedPlayback_Release() { SetEvent(release); }
extern "C" __declspec(dllexport) unsigned RizOwnedPlayback_Read(unsigned kind) {
    return kind == 0 ? static_cast<unsigned>(InterlockedCompareExchange(&calls, 0, 0)) : static_cast<unsigned>(InterlockedCompareExchange(&branch, 0, 0));
}
extern "C" __declspec(dllexport) void* RizOwnedPlayback_Last(unsigned kind) { return kind == 0 ? lastSelf : lastMethod; }
static_assert(sizeof(MethodInfo2022) == 88, "Pinned Unity2022 metadata carrier size");
extern "C" __declspec(dllexport) unsigned RizOwnedPlayback_Invoke(void* entry, void* self, void* incomingMethod,
    unsigned* bits, unsigned* code) {
    __try {
        const auto value = reinterpret_cast<float(*)(void*, void*)>(entry)(self, incomingMethod);
        std::memcpy(bits, &value, sizeof(value)); *code = 0; return 0;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) { *code = GetExceptionCode(); return 1; }
}
