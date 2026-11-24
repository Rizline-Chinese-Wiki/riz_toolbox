#pragma once
#include <cstddef>
#include <cstdint>

// Orthogonal source identity only. Neither this diagnostic projection nor its
// epoch is a Clock publication capability. The native receipt is registry-owned.
struct RizFreshSourceSnapshotV1 {
    std::uint64_t owner, ownerEpoch, playerWatch, playerOrdinal, progressWatch, progressOrdinal;
    std::uint64_t backingWatch, backingOrdinal, player, audioSource, progress, clip;
    std::uint64_t sourceNative, clipNative, sourceFence, audioAttempt, writer, scheduling, entered, exited;
    std::int32_t instanceIdOffset, sourceId, clipId;
    std::uint32_t reserved;
};
struct RizFreshSourceInfoV1 {
    std::uint32_t version, size, slot, phase; // 0 pending, 1 issued, 2 claimed, 3 released, 4 failed
    void* receipt;
    std::uint64_t sourceEpoch;
    RizFreshSourceSnapshotV1 source;
};
// Read-only rejection evidence from the same native operation. The source mask
// is valid only when the existing coordinator reached its owner-thread branch;
// the current TLS scope is not a claim about which historical write set a bit.
enum RizFreshSourceDiagnosticOrigin : std::uint32_t {
    RizFreshSourceDiagnosticNone = 0,
    RizFreshSourceDiagnosticSingleReturnPrecheck = 1,
    RizFreshSourceDiagnosticSourceCoordinator = 2,
    RizFreshSourceDiagnosticJointCommit = 3
};
enum RizFreshSourceSingleReturnMask : std::uint32_t {
    RizFreshSourceAuthorityClosed = 1,
    RizFreshSourceLogicalBlocked = 2
};
enum RizFreshSourceBlockMask : std::uint32_t {
    RizFreshSourceFreshBlocked = 1,
    RizFreshSourceLowerPitchBlocked = 2,
    RizFreshSourceAudioBlocked = 4,
    RizFreshSourceBackingBlocked = 8,
    RizFreshSourceLogicalClosed = 16
};
#pragma pack(push, 8)
struct RizFreshSourceFailureV2 {
    std::uint32_t version, size, valid, status;
    std::uint32_t origin, action, singleReturnMask, sourceBlockMask;
    std::uint32_t sourceMaskValid, scopeValid, role, kind;
    std::uint32_t depth, childFlags;
    std::uint64_t sequence, parentSequence, argBits;
    std::uint32_t parentRole, parentKind, parentStage, parentFlags, parentDepth;
    std::uint32_t engineChildTestedMask, engineChildMatchedMask;
    std::uint32_t parentEngineBeginTestedMask, parentEngineBeginMatchedMask;
    std::uint32_t callerInLowerBody, callerLowerOffset, reserved;
    std::uint64_t parentArgBits;
};
#pragma pack(pop)
// Called only native-to-native while GetTime.authorityGate -> Source.Owner.Lock
// are held. No managed callback or caller-supplied snapshot enters this path.
using RizFreshSourceCommit = std::uint32_t(__cdecl*)(void*, std::uint32_t, const RizFreshSourceSnapshotV1*);
using RizFreshSourceCoordinate = std::uint32_t(__cdecl*)(void*, void*, std::uint32_t, RizFreshSourceCommit, void*);
using RizFreshSourceCoordinateWithDiagnostic = std::uint32_t(__cdecl*)(void*, void*, std::uint32_t,
    RizFreshSourceCommit, void*, RizFreshSourceFailureV2*);
static_assert(sizeof(RizFreshSourceSnapshotV1) == 176);
static_assert(sizeof(RizFreshSourceInfoV1) == 208);
static_assert(sizeof(RizFreshSourceFailureV2) == 136);
static_assert(alignof(RizFreshSourceFailureV2) == 8);
static_assert(offsetof(RizFreshSourceFailureV2, sequence) == 56);
static_assert(offsetof(RizFreshSourceFailureV2, argBits) == 72);
static_assert(offsetof(RizFreshSourceFailureV2, parentRole) == 80);
static_assert(offsetof(RizFreshSourceFailureV2, parentArgBits) == 128);
