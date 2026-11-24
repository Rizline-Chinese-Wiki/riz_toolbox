#pragma once
#include "FirstInstall.h"

// Read-only evidence of the one private pre-Prepare FirstInstall transaction.
// A copied packet/token never configures, prepares, publishes or adopts a hook.
// Both readers revalidate the retained producer and every original; there is no
// public capture/register/accept-mask operation. Resources stay until process exit.
struct RizFirstInstallPlaybackAttestationV1 {
    std::uint32_t abiVersion, byteSize, state, unknown;
    const void* token;
    std::uint64_t epoch;
    const void* module;
    const void* helper;
    const void* dobby;
    std::uint32_t imageSize, originalCount, rangeCount, completedWrites;
    std::uint32_t supportedOriginals, lookupChecks, boundaryChecks, reserved;
    unsigned char fileSha256[32];
};
struct RizFirstInstallPlaybackRangeV1 {
    std::uint32_t abiVersion, byteSize, kind, id;
    const void* address;
    const void* original;
    const void* entry;
    std::uint64_t epoch;
    const void* functionTable;
    std::uint64_t functionTableBase;
    std::uint32_t length, protection, movedByteCount, originalByteCount;
    std::uint32_t functionCount, unwindByteCount, lookupChecks, boundaryChecks;
    std::uint64_t reserved0, reserved1;
    unsigned char beforeBytes[32], afterBytes[32];
    const void* continuation;
};
// 0 verified complete evidence, 1 malformed request, 2 unavailable/unknown.
// Get also copies bounded diagnostics on 2; neither reader issues CLR authority.
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_GetPlaybackAttestationV1(RizFirstInstallPlaybackAttestationV1* info);
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_ReadPlaybackAttestedRangeV1(
    const void* token, std::uint32_t index, RizFirstInstallPlaybackRangeV1* range);
// One bounded current read of all 64 immutable rows. The complete producer is
// verified before and after copying, under the same read lock. No cached
// acceptance or per-row verification window is exported.
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_ReadPlaybackAttestedRangesV1(
    const void* token, RizFirstInstallPlaybackRangeV1* ranges, std::uint32_t count);
static_assert(sizeof(RizFirstInstallPlaybackAttestationV1) == 120);
static_assert(offsetof(RizFirstInstallPlaybackAttestationV1, token) == 16);
static_assert(offsetof(RizFirstInstallPlaybackAttestationV1, fileSha256) == 88);
static_assert(sizeof(RizFirstInstallPlaybackRangeV1) == 184);
static_assert(offsetof(RizFirstInstallPlaybackRangeV1, beforeBytes) == 112);
static_assert(offsetof(RizFirstInstallPlaybackRangeV1, continuation) == 176);
