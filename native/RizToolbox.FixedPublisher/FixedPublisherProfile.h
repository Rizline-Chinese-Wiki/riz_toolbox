#pragma once
#include <cstdint>

// An omitted selector is the existing JudgeContext fixture profile. Requiring
// the exact value 1 prevents an explicit 0/other value from silently producing
// a different component than its import library and consumer expect.
#if defined(RIZ_EARLY_ENGINE_KERNEL) && RIZ_EARLY_ENGINE_KERNEL != 1
#error RIZ_EARLY_ENGINE_KERNEL must be omitted for legacy or set to 1 for Kernel.
#endif

namespace RizFixedPublisherProfile {
#if defined(RIZ_EARLY_ENGINE_KERNEL)
inline constexpr std::uint32_t RecordVersion = 2;
inline constexpr wchar_t PublisherModule[] = L"RizKernelFixedPublisher.dll";
inline constexpr wchar_t HelperModule[] = L"RizToolbox.EngineNodeKernel.dll";
inline constexpr bool UsesEngineNodeKernel = true;
#else
inline constexpr std::uint32_t RecordVersion = 1;
inline constexpr wchar_t PublisherModule[] = L"RizFixedPublisher.dll";
inline constexpr wchar_t HelperModule[] = L"RizToolbox.JudgeContext.dll";
inline constexpr bool UsesEngineNodeKernel = false;
#endif
// Both profiles remain restricted to the same self-owned fixture image. The
// version selects a record/profile; it is not a game publication credential.
inline constexpr wchar_t TargetsModule[] = L"RizFixedOwnedTargets.dll";
}
