#pragma once
#include <cstdint>
// Isolated, additive joint-helper protocol. Existing wire layouts are unchanged.
struct RizLogicalSourceSnapshot {
    std::uint64_t owner, physicalEpoch, entered, exited, inFlight, writer, source, scheduling;
    // Set only by the private native initializer record, never by a caller's
    // graph DTO. The ordinary cross-run path leaves these four members zero.
    std::uint64_t initializer, initializedPlayer, initializedSource, initializedClip;
};
using RizLogicalCommit = std::uint32_t(__cdecl*)(void*, void*, std::uint32_t,
    const RizLogicalSourceSnapshot*, void*);
using RizLogicalBind = void*(__cdecl*)(void*, RizLogicalCommit, void*);
using RizLogicalAction = std::uint32_t(__cdecl*)(void*, std::uint32_t, void*);
// 1 waiting for joint drain, 2 admitted/source closed, 3 rooted source ready,
// 4 dormant final publication, 5 armed. Source opens only after managed owner
// registration; no managed callback executes under either native lock.
struct RizLogicalHandoffInfoV1 {
    std::uint32_t version, size, phase, reserved;
    void* receipt;
    std::uint64_t oldEpoch, newEpoch, oldRevision, initialRevision, currentRevision;
    std::uint64_t getTimeInFlight, sourceInFlight, sourceWriter, sourceGeneration, sourceScheduling;
};
static_assert(sizeof(RizLogicalSourceSnapshot) == 96);
static_assert(sizeof(RizLogicalHandoffInfoV1) == 104);
