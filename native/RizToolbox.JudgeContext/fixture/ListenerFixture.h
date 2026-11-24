#pragma once
#include <cstddef>
#include <cstdint>
#include "../MidContext.h"

struct ListenerFixtureWork;
struct alignas(16) ListenerFixtureInvokable {
    void* klass;
    ListenerFixtureWork* owner;
    std::uint32_t index, path;
    const void* actualReturn;
    std::uint64_t actualFlags;
    std::uint64_t actualGpr[16];
    std::uint64_t explicitXmmAlignment; // Align the first XMM snapshot without implicit padding.
    RizEventMidXmmV1 actualXmm[16];
    std::uint32_t actualMxcsr;
    std::uint16_t actualFpcsr, reserved;
    std::uint64_t loopReserved;
    std::uint64_t loopGpr[16];
    RizEventMidXmmV1 loopXmm[16];
    std::uint32_t loopMxcsr;
    std::uint16_t loopFpcsr, loopReserved2;
    std::uint64_t explicitTailPadding; // Preserve the intended 16-byte total alignment.
};
static_assert(offsetof(ListenerFixtureInvokable, index) == 0x10);
static_assert(offsetof(ListenerFixtureInvokable, actualReturn) == 0x18);
static_assert(offsetof(ListenerFixtureInvokable, actualFlags) == 0x20);
static_assert(offsetof(ListenerFixtureInvokable, actualGpr) == 0x28);
static_assert(offsetof(ListenerFixtureInvokable, actualXmm) == 0xb0);
static_assert(offsetof(ListenerFixtureInvokable, actualMxcsr) == 0x1b0);
static_assert(offsetof(ListenerFixtureInvokable, loopGpr) == 0x1c0);
static_assert(offsetof(ListenerFixtureInvokable, loopXmm) == 0x240);
static_assert(offsetof(ListenerFixtureInvokable, loopMxcsr) == 0x340);
struct ListenerFixtureList {
    void* klass;
    void* monitor;
    ListenerFixtureInvokable** items;
    std::int32_t count, version;
};
static_assert(offsetof(ListenerFixtureList, items) == 0x10 && offsetof(ListenerFixtureList, count) == 0x18);
// These first fields alone are consumed by the fixture ASM. The rest of the
// native work structure and all synchronization remain in the C++ source.
struct ListenerFixturePrefix {
    std::uint64_t reserved;
    ListenerFixtureList* list;
    std::uint64_t loopFlags;
};
static_assert(sizeof(ListenerFixturePrefix) == 24);

extern "C" {
void RtLfTarget0(void*, const void*);
void RtLfTarget1(void*, const void*);
void RtLfCall(void*, void*, const void*);
void RtLfDirect();
void RtLfVirtual();
void RtLfClobber();
extern unsigned char RtLfCallerReturn;
extern unsigned char RtLfTarget0End, RtLfTarget1End;
extern unsigned char RtLfLoop0, RtLfLoop1;
extern unsigned char RtLfMid0, RtLfMid1, RtLfMidReturn0, RtLfMidReturn1;
extern unsigned char RtLfSite00, RtLfSite01, RtLfSite02;
extern unsigned char RtLfSite10, RtLfSite11, RtLfSite12;
extern unsigned char RtLfPost00, RtLfPost01, RtLfPost02;
extern unsigned char RtLfPost10, RtLfPost11, RtLfPost12;
}
