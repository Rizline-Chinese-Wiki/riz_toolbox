#define RIZ_PLAYBACK_STOCK_EXPORTS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <cstring>
#include <new>
#include "PlaybackStockFixture.h"

#ifndef RIZ_STOCK_UNITY_BACKING_PROFILE
#error This storage belongs only to the separately built Unity-backed fixture profile.
#endif

// Deliberately read-only PE data, independently sealed by the profile issuer.
extern "C" __declspec(dllexport) const char StockUnityBackingProfileMarker[] = "owned-unity-backing-lifetime-v1";

namespace {
struct NativeBacking {
    std::uint64_t OwnedCookie;
    volatile LONG InstanceId;
    std::uint32_t Reserved;
    NativeBacking* Previous;
    unsigned char PitchPadding[0x1f4-24];
    float Pitch;
};
struct WrapperHeader { void* Class; void* Monitor; void* CachedPtr; };
// Ownership records only govern fixture mutation/free. Observers read the
// actual wrapper field and actual backing + 8, never this allocation ledger.
struct Storage {
    void* Value; std::uint32_t Kind; Storage* Next;
    NativeBacking* Backings; unsigned char* Statics;
};
SRWLOCK storageLock = SRWLOCK_INIT;
Storage* storageHead;
volatile LONG nextInstanceId = 1000;
std::uint64_t storageAllocated, storageFreed, backingsAllocated, backingsFreed;
Storage* Find(void* value, std::uint32_t kind) {
    for (auto* row = storageHead; row; row = row->Next)
        if (row->Value == value && row->Kind == kind) return row;
    return nullptr;
}
Storage* Add(void* value, std::uint32_t kind) {
    if (!value || kind < StockUnitySourceStorage || kind > StockUnityResourceStorage) return nullptr;
    for (auto* row = storageHead; row; row = row->Next) if (row->Value == value) return nullptr;
    auto* row = new(std::nothrow) Storage{value, kind, storageHead, nullptr, nullptr};
    if (row) { storageHead = row; ++storageAllocated; }
    return row;
}
NativeBacking* NewBacking(Storage* row, std::int32_t instanceId) {
    auto* backing = new(std::nothrow) NativeBacking{0x554e4954594f574eull, instanceId, 0, row->Backings, {}, 1.0f};
    if (backing) { row->Backings = backing; ++backingsAllocated; }
    return backing;
}
}
static_assert(sizeof(StockUnityClass) == 0xc0 && offsetof(StockUnityClass, StaticFields) == 0xb8);
static_assert(offsetof(StockSource, CachedPtr) == 0x10 && offsetof(StockSource, Pitch) == 0x18);
static_assert(offsetof(StockSource, Time) == 0x20 && offsetof(StockSource, Clip) == 0x30);
static_assert(offsetof(StockUnityClip, CachedPtr) == 0x10 && offsetof(StockUnityClip, Length) == 0x18);
static_assert(offsetof(NativeBacking, InstanceId) == 8 && sizeof(LONG) == 4);
static_assert(offsetof(NativeBacking, Pitch) == 0x1f4);

float __cdecl StockFixtureNativeGetPitch(StockSource* source) {
    if (!source || !source->CachedPtr) RaiseException(0xe2460010u, 0, 0, nullptr);
    return static_cast<volatile NativeBacking*>(source->CachedPtr)->Pitch;
}

int __cdecl StockFixtureUnityRegisterStorage(void* value, std::uint32_t kind) {
    AcquireSRWLockExclusive(&storageLock);
    auto* row = Add(value, kind);
    ReleaseSRWLockExclusive(&storageLock);
    return row ? 1 : 0;
}
int __cdecl StockFixtureUnityInitializeWrapper(void* value, std::uint32_t kind) {
    if (kind != StockUnitySourceStorage && kind != StockUnityClipStorage) return 0;
    AcquireSRWLockExclusive(&storageLock);
    auto* row = Add(value, kind);
    auto* backing = row ? NewBacking(row, InterlockedIncrement(&nextInstanceId)) : nullptr;
    if (backing) static_cast<WrapperHeader*>(value)->CachedPtr = backing;
    else if (row) { storageHead = row->Next; --storageAllocated; delete row; }
    ReleaseSRWLockExclusive(&storageLock);
    return backing ? 1 : 0;
}
StockUnityClip* __cdecl StockFixtureUnityCreateClip(float length, std::int32_t instanceId, std::uint64_t tag) {
    auto* value = new(std::nothrow) StockUnityClip{};
    if (!value) return nullptr;
    if (!StockFixtureUnityInitializeWrapper(value, StockUnityClipStorage)) { delete value; return nullptr; }
    value->Length = length; value->Tag = tag;
    InterlockedExchange(&static_cast<NativeBacking*>(value->CachedPtr)->InstanceId, instanceId);
    return value;
}
StockUnityClass* __cdecl StockFixtureUnityCreateClass(std::uint32_t staticBytes, std::int32_t firstStaticInt32) {
    if (staticBytes != 0 && (staticBytes < 4 || staticBytes > 64)) return nullptr;
    auto* value = new(std::nothrow) StockUnityClass{};
    if (!value) return nullptr;
    auto* statics = staticBytes ? new(std::nothrow) unsigned char[staticBytes]{} : nullptr;
    if (staticBytes && !statics) { delete value; return nullptr; }
    if (statics) std::memcpy(statics, &firstStaticInt32, sizeof(firstStaticInt32));
    value->StaticFields = statics;
    AcquireSRWLockExclusive(&storageLock);
    auto* row = Add(value, StockUnityClassStorage);
    if (row) row->Statics = statics;
    ReleaseSRWLockExclusive(&storageLock);
    if (!row) { delete[] statics; delete value; return nullptr; }
    return value;
}
int __cdecl StockFixtureUnityMutate(void* value, std::uint32_t kind, std::uint32_t operation, std::int32_t instanceId) {
    if (kind != StockUnitySourceStorage && kind != StockUnityClipStorage) return 0;
    AcquireSRWLockExclusive(&storageLock);
    auto* row = Find(value, kind);
    auto* wrapper = static_cast<WrapperHeader*>(value);
    int result = 0;
    if (row) {
        if (operation == StockUnityDetach) { wrapper->CachedPtr = nullptr; result = 1; }
        else if (operation == StockUnityReplace) {
            if (auto* backing = NewBacking(row, instanceId)) { wrapper->CachedPtr = backing; result = 1; }
        }
        else if (operation == StockUnityReattach && !wrapper->CachedPtr && row->Backings) {
            wrapper->CachedPtr = row->Backings; result = 1;
        }
        else if (operation == StockUnityChangeInstanceId) {
            for (auto* backing = row->Backings; backing; backing = backing->Previous)
                if (backing == wrapper->CachedPtr) { InterlockedExchange(&backing->InstanceId, instanceId); result = 1; break; }
        }
    }
    ReleaseSRWLockExclusive(&storageLock);
    return result;
}
int __cdecl StockFixtureUnityDestroy(void* value, std::uint32_t kind) {
    AcquireSRWLockExclusive(&storageLock);
    Storage** link = &storageHead;
    while (*link && ((*link)->Value != value || (*link)->Kind != kind)) link = &(*link)->Next;
    auto* row = *link;
    if (row) {
        *link = row->Next;
        while (row->Backings) {
            auto* backing = row->Backings; row->Backings = backing->Previous;
            delete backing; ++backingsFreed;
        }
        delete[] row->Statics;
        // All registered types are trivial owned C++ allocations. This also
        // handles actual Start allocations explicitly adopted by the harness.
        ::operator delete(row->Value);
        delete row; ++storageFreed;
    }
    ReleaseSRWLockExclusive(&storageLock);
    return row ? 1 : 0;
}
std::uint64_t __cdecl StockFixtureUnityRead(std::uint32_t field) {
    AcquireSRWLockShared(&storageLock);
    const auto result = field == 0 ? storageAllocated : field == 1 ? storageFreed
        : field == 2 ? backingsAllocated : field == 3 ? backingsFreed
        : field == 4 ? sizeof(StockSource) : field == 5 ? sizeof(StockUnityClip)
        : field == 6 ? offsetof(StockUnityClass, StaticFields) : field == 7 ? offsetof(NativeBacking, InstanceId)
        : field == 8 ? offsetof(StockSource, Pitch) : field == 9 ? offsetof(StockSource, Time)
        : field == 10 ? offsetof(StockSource, Clip) : field == 11 ? offsetof(StockUnityClip, Length)
        : field == 14 ? offsetof(NativeBacking, Pitch) : field == 15 ? sizeof(NativeBacking)
        : field == 12 ? offsetof(StockSource, GetTimeOriginalCalls) : field == 13 ? offsetof(StockSource, LastGetTimeMethod) : 0;
    ReleaseSRWLockShared(&storageLock);
    return result;
}
