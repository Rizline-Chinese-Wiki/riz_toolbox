#pragma once
#include <cstddef>
#include <cstdint>
#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZOC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZOC_EXPORT extern "C"
#endif

enum RizOperationKindV1 : std::uint32_t {
    RizOperationVoid0 = 0, RizOperationRef1Void = 1, RizOperationRef2Void = 2,
    // The fourth ABI carries one raw IEEE-754 Single in XMM1. Its bits are
    // copied into frame.arg0 solely to keep the existing 64-byte frame wire.
    RizOperationSingle1Void = 3
};
enum RizOperationExitV1 : std::uint32_t {
    RizOperationReturned = 1, RizOperationAbnormal = 2, RizOperationEnterFaulted = 4,
    // The dedicated fixed Tween admission policy skipped the original. Before
    // and After still pair, but this never carries Returned or Abnormal.
    RizOperationSkipped = 8
};
struct RizOperationFrameV1 {
    std::uint32_t abiVersion, byteSize, kind, reserved;
    void* self;
    void* arg0;
    void* arg1;
    const void* method;
    const void* caller;
    std::uint64_t sequence;
};
using RizOperationEnterV1 = std::uintptr_t(__cdecl*)(const RizOperationFrameV1*, void* user);
using RizOperationExitCallbackV1 = void(__cdecl*)(const RizOperationFrameV1*, std::uintptr_t token,
    std::uint32_t flags, void* user);
struct RizOperationConfigV1 {
    std::uint32_t abiVersion, byteSize;
    void* original;
    RizOperationEnterV1 enter;
    RizOperationExitCallbackV1 exit;
    void* user;
};
struct RizOperationStatsV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t entered, exited, inFlight, enterFaults, exitFaults;
};

// Four independent tables, 32 permanent slots per kind. Configure copies one
// immutable descriptor and does not publish source instructions. The helper and
// callback/user roots must remain loaded/alive until process exit. No detach,
// replacement, disabled forwarding, code installation or first-write receipt.
// Native originals: kind0 void(self,method), kind1 void(self,arg0,method),
// kind2 void(self,arg0,arg1,method), and kind3 void(self,float,method) with
// arg0 containing the exact 32-bit XMM1 payload. kind0..2 use pointer arguments.
// Sequence is globally positive and never reused. After signed64 exhaustion it
// is permanently 0 (invalid observation); original/Exit still execute normally.
// An invalid invocation before Configure has no original: it returns without
// dispatch, permanently claims the slot Unknown and cannot later be configured.
// GetInfo reports nonzero status/Unknown and no successful physical invocation.
RIZOC_EXPORT std::uint32_t __cdecl RizOperationContext_GetLimit();
RIZOC_EXPORT std::uint32_t __cdecl RizOperationContext_GetFrameSize();
RIZOC_EXPORT void* __cdecl RizOperationContext_GetEntry(std::uint32_t kind, std::uint32_t slot);
// Configure: 0 ready; 1 invalid kind/slot/ABI/required pointer or helper entry as
// original; 2 permanently claimed (including a concurrent incomplete claim).
RIZOC_EXPORT std::uint32_t __cdecl RizOperationContext_Configure(std::uint32_t kind, std::uint32_t slot,
    const RizOperationConfigV1* config);
// Stats: 0 copied, including all-zero counters of an unconfigured slot;
// 1 invalid header/kind/slot. Statistics do not establish first-write safety.
RIZOC_EXPORT std::uint32_t __cdecl RizOperationContext_GetStats(std::uint32_t kind, std::uint32_t slot,
    RizOperationStatsV1* statistics);

// Independent pre-init configuration and one late immutable observer attachment.
// Original/target never change after Configure, which does not patch code.
struct RizOperationBootstrapConfigV1 {
    std::uint32_t abiVersion, byteSize;
    void* target;
    void* original;
};
struct RizOperationBootstrapObserverV1 {
    std::uint32_t abiVersion, byteSize;
    RizOperationEnterV1 enter;
    RizOperationExitCallbackV1 exit;
    void* user;
};
struct RizOperationBootstrapInfoV1 {
    std::uint32_t abiVersion, byteSize, kind, slot, attached, unknown;
    void* target;
    void* original;
    void* entry;
    std::uint64_t epoch, entered, exited, inFlight, enterFaults, exitFaults;
};
// Configure: 0 ready, 1 invalid, 2 permanently claimed, 3 dependency/epoch
// failure (sticky Unknown; no slot reuse). All pointers/resources live forever.
RIZOC_EXPORT std::uint32_t __cdecl RizOperationBootstrap_Configure(std::uint32_t kind, std::uint32_t slot,
    const RizOperationBootstrapConfigV1* config);
// Attach: 0 ready, 1 invalid, 2 not bootstrap-ready, 3 epoch mismatch,
// 4 already attempted/attached, 5 sticky Unknown. Exactly one attempt publishes
// the descriptor. Entry snapshots it once before frame/sequence creation;
// old unattached calls never borrow a later observer, including at Exit.
RIZOC_EXPORT std::uint32_t __cdecl RizOperationBootstrap_Attach(std::uint32_t kind, std::uint32_t slot,
    std::uint64_t epoch, const RizOperationBootstrapObserverV1* observer);
// GetInfo: 0 configured bootstrap, 1 invalid header/kind/slot,
// 2 genuinely unconfigured, 3 legacy V1 or incomplete permanently claimed.
// Incomplete claims report unknown=1. Even 2/3 reports real entry,
// kind/slot/counters/Unknown; caller input identity is never trusted.
RIZOC_EXPORT std::uint32_t __cdecl RizOperationBootstrap_GetInfo(std::uint32_t kind, std::uint32_t slot,
    RizOperationBootstrapInfoV1* information);

static_assert(sizeof(void*) == 8);
static_assert(sizeof(RizOperationFrameV1) == 64);
static_assert(offsetof(RizOperationFrameV1, self) == 16);
static_assert(offsetof(RizOperationFrameV1, arg0) == 24);
static_assert(offsetof(RizOperationFrameV1, arg1) == 32);
static_assert(offsetof(RizOperationFrameV1, method) == 40);
static_assert(offsetof(RizOperationFrameV1, caller) == 48);
static_assert(offsetof(RizOperationFrameV1, sequence) == 56);
static_assert(sizeof(RizOperationConfigV1) == 40);
static_assert(offsetof(RizOperationConfigV1, original) == 8);
static_assert(offsetof(RizOperationConfigV1, enter) == 16);
static_assert(offsetof(RizOperationConfigV1, exit) == 24);
static_assert(offsetof(RizOperationConfigV1, user) == 32);
static_assert(sizeof(RizOperationStatsV1) == 48);
static_assert(sizeof(RizOperationBootstrapConfigV1) == 24);
static_assert(sizeof(RizOperationBootstrapObserverV1) == 32);
static_assert(sizeof(RizOperationBootstrapInfoV1) == 96);
static_assert(offsetof(RizOperationBootstrapInfoV1, target) == 24);
static_assert(offsetof(RizOperationBootstrapInfoV1, original) == 32);
static_assert(offsetof(RizOperationBootstrapInfoV1, entry) == 40);
static_assert(offsetof(RizOperationBootstrapInfoV1, epoch) == 48);
static_assert(offsetof(RizOperationBootstrapInfoV1, enterFaults) == 80);
