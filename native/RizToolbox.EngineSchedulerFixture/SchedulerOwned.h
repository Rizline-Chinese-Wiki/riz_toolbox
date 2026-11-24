#pragma once
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstddef>

// Only owned test objects. Layout copies the independently documented scalar
// fields; padding is not interpreted as scheduler semantics.
struct SchedulerOwnedSource {
    std::uint64_t dueTimeBits;
    std::int64_t frame;
    std::uint32_t intervalBits;
    std::uint8_t repeat, padding15[3];
    void* payload;
    void* callback;
    void* cleanup;
    std::uint32_t ownerId, category, creationTag, padding3c;
};
static_assert(sizeof(SchedulerOwnedSource) == 0x40);
static_assert(offsetof(SchedulerOwnedSource, payload) == 0x18);
static_assert(offsetof(SchedulerOwnedSource, callback) == 0x20);
static_assert(offsetof(SchedulerOwnedSource, cleanup) == 0x28);
static_assert(offsetof(SchedulerOwnedSource, creationTag) == 0x38);
struct SchedulerOwnedEntry {
    unsigned char treeLinks[0x20];
    SchedulerOwnedSource record;
};
static_assert(offsetof(SchedulerOwnedEntry, record) == 0x20);
struct SchedulerOwnedManager {
    unsigned char opaque[0x30];
    void* tree;
    std::uint64_t count;
    SchedulerOwnedEntry* iterator;
    std::uint32_t tag, padding4c;
    SchedulerOwnedEntry* rows[16];
};
static_assert(offsetof(SchedulerOwnedManager, tree) == 0x30);
static_assert(offsetof(SchedulerOwnedManager, count) == 0x38);
static_assert(offsetof(SchedulerOwnedManager, iterator) == 0x40);
static_assert(offsetof(SchedulerOwnedManager, tag) == 0x48);

extern "C" {
std::uint64_t RtSchedulerFixture_Register(void* tree, void* outputSlot, void* source, void* arg3);
std::uint64_t RtSchedulerFixture_Remove(void* manager, void* record, void* entry, void* arg3);
std::uint64_t RtSchedulerFixture_Consume(void* manager, void* unused, void* entry, void* arg3);
std::uint64_t RtSchedulerFixture_Pump(void* manager, void* mask, void* arg2, void* arg3);
std::uint64_t RtSchedulerFixture_Drain(void* manager, void* arg1, void* arg2, void* arg3);
std::uint64_t RtSchedulerFixture_Callback(void* owner, void* payload, void* arg2, void* arg3);
}
