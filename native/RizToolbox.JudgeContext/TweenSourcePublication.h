#pragma once
#include <cstddef>
#include <cstdint>

// Permanent source relays are prepared and sealed by the actual pre-init
// producer. This copied identity never grants installation or object authority.
inline constexpr std::uint32_t RizTweenPublishedSourceCount = 22;
struct RizTweenPublishedSourceSiteV1 {
    std::uint32_t site, beginRva, endRva, parentRva;
    const void* sourcePc;
    const void* preRelay;
    const void* postRelay;
    const void* preReturnPc;
    const void* postReturnPc;
    const void* continuation;
    const void* deadContinuation;
    const void* unwindInfo;
    std::uint32_t width, protection;
};
struct RizTweenSourcePublicationV1 {
    std::uint32_t abiVersion, byteSize, prepared, published, unknown, siteCount, bindingCount, functionCount;
    std::uint64_t epoch, publishedMask;
    const void* moduleBase;
    const void* allocation;
    const void* functionTable;
    const void* entry;
    std::uint64_t allocationSize, reserved;
    RizTweenPublishedSourceSiteV1 sites[RizTweenPublishedSourceCount];
};
using RizTweenSourceWriteV1 = bool(*)(unsigned char*, const unsigned char*, const unsigned char*, std::size_t, unsigned long);

bool RizTweenSourcePreparePublication(void* actualModule);
bool RizTweenSourceCommitPublication(RizTweenSourceWriteV1 write);
bool RizTweenSourceVerifyPublication(bool requirePublished, std::uint32_t* failureDetail = nullptr);
bool RizTweenSourceCopyPublication(RizTweenSourcePublicationV1* output);
// Used only to extend the enclosing producer's complete function-body seal.
bool RizTweenSourceGetPublishedPatch(std::uint32_t site, const void** source,
    const unsigned char** bytes, std::size_t* size);
static_assert(sizeof(RizTweenPublishedSourceSiteV1) == 88);
static_assert(sizeof(RizTweenSourcePublicationV1) == 2032);
