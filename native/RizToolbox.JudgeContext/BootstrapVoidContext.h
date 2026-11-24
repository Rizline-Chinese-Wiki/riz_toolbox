#pragma once
#include "VoidContext.h"
#include "MidContext.h"

// Permanent native bootstrap transport. Configure prepares existing Void/Mid
// slots without publishing any machine code. Safe first publication remains the
// caller's separate responsibility. Attach publishes one immutable observer;
// an invocation keeps exactly the observer it read at its own Enter boundary.
struct RizVoidBootstrapConfigV1 {
    std::uint32_t abiVersion, byteSize;
    const void* target;
    RizVoidOriginalV1 original;
    const void* expectedMidReturnPc;
};
struct RizVoidBootstrapObserverV1 {
    std::uint32_t abiVersion, byteSize;
    RizVoidEnterV1 enter;
    RizVoidExitCallbackV1 exit;
    RizEventMidCallbackV1 mid;
    void* user;
};
struct RizVoidBootstrapInfoV1 {
    std::uint32_t abiVersion, byteSize, slot, attached;
    const void* target;
    RizVoidOriginalV1 original;
    void* entry;
    std::uint64_t epoch;
    std::uint32_t unknown, reserved;
};

// Config/Observer are copied. Callback functions and user roots must survive
// until process exit; there is no detach, replacement or slot reuse.
// Configure: 0 ready, 1 invalid, 2 permanently claimed, 3 dependency failure
// with sticky unknown. Claims the same index in both original Void/Mid tables.
RIZVC_EXPORT std::uint32_t __cdecl RizVoidBootstrap_Configure(std::uint32_t slot, const RizVoidBootstrapConfigV1* config);
// Attach: 0 attached, 1 invalid, 2 not ready, 3 wrong epoch, 4 already attached,
// 5 sticky unknown. GetInfo returns the unique process-lifetime epoch. The DLL
// must remain loaded for the entire process; unloading is never permitted.
RIZVC_EXPORT std::uint32_t __cdecl RizVoidBootstrap_Attach(std::uint32_t slot, std::uint64_t epoch, const RizVoidBootstrapObserverV1* observer);
// GetInfo: 0 ready, 1 invalid, 2 not ready. An initialized output always receives
// the real sticky unknown value, including dependency failures.
RIZVC_EXPORT std::uint32_t __cdecl RizVoidBootstrap_GetInfo(std::uint32_t slot, RizVoidBootstrapInfoV1* info);

static_assert(sizeof(RizVoidBootstrapConfigV1) == 0x20);
static_assert(offsetof(RizVoidBootstrapConfigV1, target) == 0x08);
static_assert(offsetof(RizVoidBootstrapConfigV1, original) == 0x10);
static_assert(offsetof(RizVoidBootstrapConfigV1, expectedMidReturnPc) == 0x18);
static_assert(sizeof(RizVoidBootstrapObserverV1) == 0x28);
static_assert(offsetof(RizVoidBootstrapObserverV1, enter) == 0x08);
static_assert(offsetof(RizVoidBootstrapObserverV1, exit) == 0x10);
static_assert(offsetof(RizVoidBootstrapObserverV1, mid) == 0x18);
static_assert(offsetof(RizVoidBootstrapObserverV1, user) == 0x20);
static_assert(sizeof(RizVoidBootstrapInfoV1) == 0x38);
static_assert(offsetof(RizVoidBootstrapInfoV1, target) == 0x10);
static_assert(offsetof(RizVoidBootstrapInfoV1, original) == 0x18);
static_assert(offsetof(RizVoidBootstrapInfoV1, entry) == 0x20);
static_assert(offsetof(RizVoidBootstrapInfoV1, epoch) == 0x28);
static_assert(offsetof(RizVoidBootstrapInfoV1, unknown) == 0x30);
static_assert(offsetof(RizVoidBootstrapInfoV1, reserved) == 0x34);
