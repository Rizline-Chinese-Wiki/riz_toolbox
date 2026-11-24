#define RIZ_PLAYBACK_STOCK_EXPORTS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <new>
#include "ProgressResourceFixture.h"

extern "C" __declspec(dllexport) const char StockProgressResourceProfileMarker[] =
    "owned-progress-resource-continuity-v1";

namespace {
struct Type { void* Data; std::uint32_t Bits, Padding; };
struct Method { void* Entry; void* Virtual; void* Invoker; const char* Name; void* Class; void* Returns;
    void* Parameters; void* Runtime; void* Generic; std::uint32_t Token;
    std::uint16_t Flags, Implementation, Slot; unsigned char Count, Bits; };
std::uint64_t progressClass = 0x7072672d636c6970ull, resourceClass = 0x7265736f75726365ull;
std::uint64_t audioClipClass = 0x636c69702d6f776eull, foreignClasses[35]{};
Type voidType{nullptr, 0x00010000u, 0};
Type resourceType{&resourceClass, 0x00150000u, 0};
Type clipType{&audioClipClass, 0x00120000u, 0};
Type* resourceArguments[]{&resourceType};
Method clipMethod{reinterpret_cast<void*>(&StockProgressClip), reinterpret_cast<void*>(&StockProgressClip), nullptr,
    "set_Clip", &progressClass, &voidType, resourceArguments, nullptr, nullptr, 0x06001016u, 0x86, 0, 0xffff, 1, 0};
Method resourceMethod{reinterpret_cast<void*>(&StockResourceGet), reinterpret_cast<void*>(&StockResourceGet), nullptr,
    "get_Resource", &resourceClass, &clipType, nullptr, nullptr, nullptr, 0x06001017u, 0x86, 0, 0xffff, 0, 0};
struct AudioClip { void* Class; void* Monitor; std::uint64_t Tag; };
volatile LONG barrierCalls{}, storeConfirmed{}, tailReturns{}, barrierFlags{};
void* lastField{}; void* lastValue{}; void* lastMethod{}; void* lastStored{};
std::uint32_t lastThread{};
StockProgressResourceBoundary resourceWriterBoundary{};
StockResourceBoundary getterBoundary{};
}
static_assert(sizeof(Method) == 88, "Unity2022 MethodInfo");
static_assert(offsetof(StockProgress, ClipReference) == 0x30, "actual resource field");
static_assert(offsetof(StockResourceReference, GetResource) == 0x10, "owned reference dispatch");
static_assert(offsetof(StockResourceReference, Clip) == 0x18, "owned reference resource");

void __cdecl StockProgressClipTail(void* field, void* value, void* incoming) {
    InterlockedIncrement(&barrierCalls);
    lastField = field; lastValue = value; lastMethod = incoming;
    lastThread = GetCurrentThreadId();
    lastStored = *static_cast<void**>(field);
    if (lastStored != value) RaiseException(0xe2400050u, 0, 0, nullptr);
    InterlockedIncrement(&storeConfirmed);
    auto* self = static_cast<unsigned char*>(field) - 0x30;
    if (resourceWriterBoundary) resourceWriterBoundary(self, value, incoming, 1);
    if (InterlockedCompareExchange(&barrierFlags, 0, 0) & 1)
        RaiseException(0xe2400051u, 0, 0, nullptr);
    InterlockedIncrement(&tailReturns);
}
void* __cdecl StockMethodProgressClip() { return &clipMethod; }
void* __cdecl StockMethodResourceGet() { return &resourceMethod; }
void* __cdecl StockResourceGet(StockResourceReference* self, void* incoming) {
    if (!self) RaiseException(0xe2400052u, 0, 0, nullptr);
    InterlockedIncrement(&self->Gets);
    self->LastSelf = self; self->LastMethod = incoming;
    if (getterBoundary) getterBoundary(self, incoming, 1);
    if (self->Flags & 1) RaiseException(0xe2400053u, 0, 0, nullptr);
    self->LastClip = self->Clip;
    if (getterBoundary) getterBoundary(self, incoming, 2);
    if (self->Flags & 2) RaiseException(0xe2400054u, 0, 0, nullptr);
    return self->LastClip;
}
StockResourceReference* __cdecl StockFixtureCreateResourceReference(void* clip) {
    auto* value = new(std::nothrow) StockResourceReference{};
    if (value) { value->Class = resourceMethod.Class; value->GetResource = &StockResourceGet; value->Clip = clip; }
    return value;
}
void* __cdecl StockFixtureCreateAudioClip(std::uint64_t tag) {
    auto* value = new(std::nothrow) AudioClip{};
    if (value) { value->Class = &audioClipClass; value->Tag = tag; }
    return value;
}
StockProgress* __cdecl StockFixtureCreateProgressResourceReceiver(std::uint32_t classIndex, StockSource* source, void* reference) {
    if (classIndex >= 35) return nullptr;
    auto* value = new(std::nothrow) StockProgress{};
    if (value) {
        value->Class = classIndex == 0 ? static_cast<Method*>(StockMethodProgressPlay())->Class : &foreignClasses[classIndex];
        value->AudioSource = source; value->ClipReference = reference;
    }
    return value;
}
void __cdecl StockFixtureSetProgressResource(StockProgress* progress, void* reference) {
    if (progress) progress->ClipReference = reference;
}
void __cdecl StockFixtureConfigureResource(StockResourceReference* value, std::uint32_t flags, StockResourceBoundary boundary) {
    if (value) value->Flags = flags;
    getterBoundary = boundary;
}
std::uint64_t __cdecl StockFixtureResourceRead(StockResourceReference* value, std::uint32_t field) {
    if (!value) return 0;
    switch (field) {
    case 0: return static_cast<std::uint64_t>(InterlockedCompareExchange(&value->Gets, 0, 0));
    case 1: return reinterpret_cast<std::uint64_t>(value->Clip);
    case 2: return reinterpret_cast<std::uint64_t>(value->LastMethod);
    case 3: return reinterpret_cast<std::uint64_t>(value->LastSelf);
    case 4: return value->Flags;
    case 5: return reinterpret_cast<std::uint64_t>(value->LastClip);
    default: return 0;
    }
}
void __cdecl StockFixtureConfigureProgressResource(std::uint32_t flags, StockProgressResourceBoundary boundary) {
    InterlockedExchange(&barrierFlags, static_cast<LONG>(flags)); resourceWriterBoundary = boundary;
}
std::uint64_t __cdecl StockFixtureProgressResourceRead(std::uint32_t field) {
    switch (field) {
    case 0: return static_cast<std::uint64_t>(InterlockedCompareExchange(&barrierCalls, 0, 0));
    case 1: return reinterpret_cast<std::uint64_t>(lastField);
    case 2: return reinterpret_cast<std::uint64_t>(lastValue);
    case 3: return reinterpret_cast<std::uint64_t>(lastMethod);
    case 4: return reinterpret_cast<std::uint64_t>(lastStored);
    case 5: return lastThread;
    case 6: return static_cast<std::uint64_t>(InterlockedCompareExchange(&storeConfirmed, 0, 0));
    case 7: return static_cast<std::uint64_t>(InterlockedCompareExchange(&tailReturns, 0, 0));
    default: return 0;
    }
}
