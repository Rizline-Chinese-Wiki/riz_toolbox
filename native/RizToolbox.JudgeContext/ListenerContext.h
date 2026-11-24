#pragma once
#include "BootstrapVoidContext.h"

// New, independent observer lane. It publishes no source bytes and does not
// upgrade the V1 first-install receipt. All objects remain process-owned.
inline constexpr std::uint32_t RizListenerSlotLimitV1 = 32;
enum RizListenerStageV1 : std::uint32_t {
    RizListenerBefore = 1, RizListenerReturned = 2, RizListenerAbnormal = 3
};
enum RizListenerFlagsV1 : std::uint32_t {
    RizListenerBeforeFaulted = 1, RizListenerParentAbnormal = 2,
    RizListenerMissingPost = 4, RizListenerTargetReadFaulted = 8
};
struct RizListenerConfigV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t parentEpoch;              // Actual Bootstrap slot epoch.
    const void* sourceReturnPc[3];          // source+5, source+5, source+6.
    const void* postTransportReturnPc[3];   // Actual post-stub observer CALL PC.
    const void* directTarget[2];            // Site 2 reads [saved RAX+178h] once.
};
struct RizListenerFrameV1 {
    std::uint32_t abiVersion, byteSize, slot, site;
    std::uint32_t stage, osThreadId, flags, reserved0;
    std::uint64_t epoch, parentSequence, childSequence, parentManagedToken;
    const void* event;
    const void* list;
    const void* invokable;
    const void* argRcx;
    const void* argRdx;
    const void* argR8;
    const void* argR9;
    const void* actualTarget;
    const void* sourceReturnPc;
    const void* transportReturnPc;
    std::uint64_t sourceRsp, index;
    // Pre/Post only, stack-scoped and immutable; null for abnormal parent close.
    // The Mid snapshot returnPc is the real transport CALL PC, not Source PC.
    const RizEventMidFrameV1* machineFrame;
    std::uint64_t reserved1;
};
using RizListenerBeforeV1 = std::uintptr_t(__cdecl*)(const RizListenerFrameV1*, void* user);
using RizListenerAfterV1 = void(__cdecl*)(const RizListenerFrameV1*, std::uintptr_t managedChildToken, void* user);
struct RizListenerObserverV1 {
    std::uint32_t abiVersion, byteSize;
    RizListenerBeforeV1 before;
    RizListenerAfterV1 after;
    void* user;
};
struct RizListenerInfoV1 {
    std::uint32_t abiVersion, byteSize, slot, attached;
    std::uint64_t epoch, parentEpoch;
    void* preEntry;
    void* postEntry;
    const void* sourceReturnPc[3];
    const void* postTransportReturnPc[3];
    const void* directTarget[2];
    std::uint64_t entered, exited, inFlight, faults, abnormal, overflow, missingPost;
    std::uint32_t unknown, reserved;
};

RIZVC_EXPORT void* __cdecl RizListenerContext_GetPreEntry();
RIZVC_EXPORT void* __cdecl RizListenerContext_GetPostEntry();
// Configure: 0 ready; 1 invalid ABI/pointers; 2 slot permanently claimed;
// 3 PC collision (including source versus post); 4 parent not ready;
// 5 wrong parent epoch; 6 sticky unknown. No instruction publication here.
RIZVC_EXPORT std::uint32_t __cdecl RizListenerContext_Configure(std::uint32_t slot, const RizListenerConfigV1* config);
// Attach: 0 attached; 1 invalid; 2 not ready; 3 wrong lane epoch;
// 4 attach already attempted; 5 sticky unknown. Both callbacks required.
RIZVC_EXPORT std::uint32_t __cdecl RizListenerContext_Attach(std::uint32_t slot, std::uint64_t epoch, const RizListenerObserverV1* observer);
// GetInfo: 0 ready, 1 invalid header/slot, 2 not ready. Unknown is real even
// after a failed permanent claim. InFlight is pre-to-post/abnormal-close only;
// enclosing native Void admission is the wider physical drain boundary.
RIZVC_EXPORT std::uint32_t __cdecl RizListenerContext_GetInfo(std::uint32_t slot, RizListenerInfoV1* info);

// Internal helper-to-helper contract, NOT an exported ABI. Bootstrap embeds one
// capture in each private TLS record and freezes it before any managed Enter.
struct RizListenerParentCapture {
    const void* state = nullptr;
    const void* observer = nullptr;
    std::uint64_t sequence = 0;
    std::size_t childBaseline = 0;
    std::uint64_t maskBaseline = 0;
    std::uint64_t masks[32]{};
};
struct RizListenerCurrentParent {
    std::uint32_t slot = 0;
    std::uint64_t epoch = 0;
    const void* self = nullptr;
    std::uintptr_t managedToken = 0;
    RizListenerParentCapture* capture = nullptr;
};
void RizListenerContext_CaptureParent(std::uint32_t slot, std::uint64_t parentEpoch, RizListenerParentCapture* capture);
void RizListenerContext_CloseParent(RizListenerParentCapture* capture, std::uint32_t parentFlags);
bool RizVoidBootstrap_CurrentListenerParent(RizListenerCurrentParent* parent);
bool RizVoidBootstrap_ListenerParentKnown(std::uint32_t slot, std::uint64_t epoch);
void RizVoidBootstrap_ListenerFault(std::uint32_t slot, std::uint64_t epoch);

static_assert(sizeof(RizListenerConfigV1) == 80);
static_assert(offsetof(RizListenerConfigV1, parentEpoch) == 8);
static_assert(offsetof(RizListenerConfigV1, sourceReturnPc) == 16);
static_assert(offsetof(RizListenerConfigV1, postTransportReturnPc) == 40);
static_assert(offsetof(RizListenerConfigV1, directTarget) == 64);
static_assert(sizeof(RizListenerFrameV1) == 176);
static_assert(offsetof(RizListenerFrameV1, epoch) == 32);
static_assert(offsetof(RizListenerFrameV1, event) == 64);
static_assert(offsetof(RizListenerFrameV1, actualTarget) == 120);
static_assert(offsetof(RizListenerFrameV1, sourceReturnPc) == 128);
static_assert(offsetof(RizListenerFrameV1, transportReturnPc) == 136);
static_assert(offsetof(RizListenerFrameV1, sourceRsp) == 144);
static_assert(offsetof(RizListenerFrameV1, machineFrame) == 160);
static_assert(sizeof(RizListenerObserverV1) == 32);
static_assert(offsetof(RizListenerObserverV1, before) == 8);
static_assert(offsetof(RizListenerObserverV1, after) == 16);
static_assert(offsetof(RizListenerObserverV1, user) == 24);
static_assert(sizeof(RizListenerInfoV1) == 176);
static_assert(offsetof(RizListenerInfoV1, epoch) == 16);
static_assert(offsetof(RizListenerInfoV1, preEntry) == 32);
static_assert(offsetof(RizListenerInfoV1, sourceReturnPc) == 48);
static_assert(offsetof(RizListenerInfoV1, postTransportReturnPc) == 72);
static_assert(offsetof(RizListenerInfoV1, directTarget) == 96);
static_assert(offsetof(RizListenerInfoV1, entered) == 112);
static_assert(offsetof(RizListenerInfoV1, unknown) == 168);
