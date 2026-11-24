#pragma once
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstddef>
#include <cstdint>
#include "../RizToolbox.JudgeContext/EngineWaitContext.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include "../RizToolbox.JudgeContext/EngineSchedulerContext.h"
#include "../RizToolbox.JudgeContext/MidContext.h"

// Owned test allocations only. The shared reference and native-node reference
// are separate counters, exactly as the static observation contract requires.
struct WaitOwnedObject {
    void* vtable;
    std::uint32_t sharedOpaque, sharedReferences;
    void* callback;
    void* cleanup;
    void* payload;
    std::uint32_t ownerId, reserved;
};
static_assert(sizeof(WaitOwnedObject) == 0x30);
static_assert(offsetof(WaitOwnedObject, callback) == 0x10);
static_assert(offsetof(WaitOwnedObject, ownerId) == 0x28);
struct WaitOwnedNode {
    unsigned char opaque0[0x10];
    std::uint64_t enumHandle;
    std::uint32_t enumType, opaque1c;
    void* enumCache;
    unsigned char opaque28[0x30];
    void* owner;
    std::int32_t references;
    unsigned char opaque64[0x14];
    WaitOwnedObject* wait;
    unsigned char opaque80[8];
};
static_assert(sizeof(WaitOwnedNode) == 0x88);
static_assert(offsetof(WaitOwnedNode, owner) == 0x58);
static_assert(offsetof(WaitOwnedNode, wait) == 0x78);
struct WaitOwnedManager {
    unsigned char opaque[0x190];
    WaitOwnedObject** objects;
    std::uint64_t opaque198, count;
};
static_assert(offsetof(WaitOwnedManager, objects) == 0x190);
static_assert(offsetof(WaitOwnedManager, count) == 0x1a0);

extern "C" {
std::uint64_t RtWaitFixture_Yield(void*,void*,void*,void*);
std::uint64_t RtWaitFixture_Dispatch(void*,void*,void*,void*);
std::uint64_t RtWaitFixture_Batch(void*,void*,void*,void*);
std::uint8_t RtWaitFixture_Start(void*,void*,void*,void*);
std::uint8_t RtWaitFixture_Release(void*);
void RtWaitFixture_Detach(void*);
std::uint64_t RtWaitFixture_CallbackTarget(void*,void*,void*,void*);
std::uint8_t RtWaitFixture_CleanupTarget(void*);
}
