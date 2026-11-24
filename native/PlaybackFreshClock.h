#pragma once
#include "PlaybackFreshSource.h"
#include "PlaybackLogicalHandoff.h"

// Selectors are generated only behind the Runtime's registered ExecutionReceipt.
// Source independently locates actual retained physical journal rows. These
// numbers, and the returned information POD, are never publication capabilities.
struct RizFreshClockBaselineV1 {
    std::uint32_t version, size;
    std::uint64_t ownerEpoch, rootSequence, rootToken, leafSequence, leafToken;
    std::uint64_t writer, source, scheduling, leafCaller;
    std::uint32_t rootRole, rootSlot, leafSlot, reserved;
};
static_assert(sizeof(RizFreshClockBaselineV1) == 96);

// 1 prepare, 2 lifetime read/actual GetTime, 3 arm, 4 publish. A successful
// return holds Source.Owner.Lock. The bound LeaveFresh releases it exactly once.
// GetTime.authorityGate is always acquired first. No managed callback runs here.
// V2 Arm creates the logical gate inside the same Source lock. Its commit
// receives that actual gate; it never fabricates an old bootstrap or epoch.
using RizFreshClockCommit = std::uint32_t(__cdecl*)(void*, std::uint32_t,
    const RizFreshSourceSnapshotV1*, void*);
using RizFreshClockEnter = std::uint32_t(__cdecl*)(void*, void*, const RizFreshClockBaselineV1*,
    std::uint32_t, RizFreshClockCommit, void*, RizLogicalCommit, void*);
