#pragma once
#include <cstdint>
// Independent private initialization V2. This is not the Source/Writer wire.
// A diagnostic projection never authorizes new roots, a clock, or a transfer.
struct RizPlaybackInitializationInfoV2 {
    std::uint32_t version, size, phase, blocked;
    std::uint64_t owner, ownerEpoch, ordinal, enumerator, progress, source, clip, player;
    std::uint64_t yieldSequence, yieldToken, finalSequence, finalToken, childSequence, childToken;
    std::uint64_t writer, sourceFence, scheduling, receipt;
};
static_assert(sizeof(RizPlaybackInitializationInfoV2) == 160);
// Enter success retains Owner.Lock exclusive until Leave. Only a native owner
// holding GetTime.authorityGate may compose it with policy mutation/consumption.
// 1 holds the source leaf gate until FreshLeave; 0 declines without a hold.
// 2 is a recognized Unity backing boundary; 3 is sticky physical Unknown.
// Neither rejection holds a gate. The GetTime owner closes publication while
// retaining its exact old policy/session for an actual initializer transfer.
using RizPlaybackFreshEnter = int(__cdecl*)(void*);
using RizPlaybackFreshLeave = void(__cdecl*)(void*);

using RizPlaybackInitializationBegin = std::uint32_t(__cdecl*)(void*, void*, void*);

// Read-only projection of the exact native initializer's backing interruption.
// Neither this copied POD nor its pointer fields can issue retry authority.
struct RtPlaybackBackingInitializationInfoV1 {
    std::uint32_t Version, Size, Invalidated, Reason;
    std::uint64_t Owner, OwnerEpoch, Initializer, InitializerOrdinal,
        Enumerator, Progress, YieldSequence, BackingWatchReceipt,
        BackingWatchOrdinal, InvalidationSourceFence;
};
static_assert(sizeof(RtPlaybackBackingInitializationInfoV1) == 96);

// An actual direct lower SetPitch interrupted this exact phase-six initializer.
// Copied diagnostic fields cannot issue the private retry or reopen old roots.
struct RtPlaybackLowerPitchInitializationInfoV1 {
    std::uint32_t Version, Size, Invalidated, Reason;
    std::uint64_t Owner, OwnerEpoch, Initializer, InitializerOrdinal,
        Enumerator, Progress, YieldSequence, AttemptSequence, AttemptToken,
        InvalidationWriter, InvalidationSourceFence, PlayerWatchReceipt;
};
static_assert(sizeof(RtPlaybackLowerPitchInitializationInfoV1) == 112);

// Independent first-capture V1. No predecessor policy, revision or logical epoch
// exists in this protocol. Epoch is allocated only after both physical drains.
struct RizPlaybackInitialCaptureInfoV1 {
    std::uint32_t version, size, phase, reserved;
    void* receipt;
    std::uint64_t epoch, getTimeInFlight, sourceInFlight, sourceOwner, physicalEpoch;
    std::uint64_t writer, sourceFence, scheduling, initializer, player, source, clip;
};
static_assert(sizeof(RizPlaybackInitialCaptureInfoV1) == 120);
