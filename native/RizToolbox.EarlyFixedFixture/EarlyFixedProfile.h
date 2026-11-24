#pragma once
#include <cstdint>

// Compile-time fixture identities only. A record version is never a substitute
// for the parent's exact module path/hash/import graph and live tuple checks.
#if defined(RIZ_EARLY_ENGINE_KERNEL)
#if RIZ_EARLY_ENGINE_KERNEL != 1
#error RIZ_EARLY_ENGINE_KERNEL must be exactly 1 when defined
#endif
inline constexpr bool RizEarlyKernelProfile = true;
inline constexpr std::uint32_t RizEarlyMagic = 0x4b465245, RizEarlyVersion = 3;
inline constexpr std::uint32_t RizEarlyWorkloadVersion = 3;
inline constexpr wchar_t RizEarlyMappingEnvironment[] = L"RIZ_EARLY_KERNEL_MAPPING_NAME";
inline constexpr wchar_t RizEarlyPublisherModuleName[] = L"RizKernelFixedPublisher.dll";
inline constexpr wchar_t RizEarlyHelperModuleName[] = L"RizToolbox.EngineNodeKernel.dll";
inline constexpr wchar_t RizEarlyRecorderModuleNameLower[] = L"rizkernelownedrecorder.dll";
inline constexpr wchar_t RizEarlyProxyModuleNameLower[] = L"rizkernelownedproxy.dll";
inline constexpr wchar_t RizEarlyPublisherModuleNameLower[] = L"rizkernelfixedpublisher.dll";
inline constexpr wchar_t RizEarlyHelperModuleNameLower[] = L"riztoolbox.enginenodekernel.dll";
inline constexpr wchar_t RizEarlyHostModuleNameLower[] = L"rizkernelownedhost.exe";
inline constexpr wchar_t RizEarlyMissingPublisherModuleName[] = L"RizKernelOwnedMissingPublisher.dll";
inline constexpr const char* RizEarlyDebugModuleNames[]{"", "rizkernelfixedpublisher.dll",
    "riztoolbox.enginenodekernel.dll", "rizkernelownedrecorder.dll", "rizkernelownedproxy.dll",
    "rizfixedownedtargets.dll"};
inline constexpr char RizEarlyDebuggerUsage[] = "Usage: RizKernelOwnedDebugger.exe <absolute-owned-host> <debug-events.jsonl>";
inline constexpr char RizEarlyFixtureLabel[] = "early-kernel";
#else
inline constexpr bool RizEarlyKernelProfile = false;
inline constexpr std::uint32_t RizEarlyMagic = 0x58465245, RizEarlyVersion = 2;
inline constexpr std::uint32_t RizEarlyWorkloadVersion = 2;
inline constexpr wchar_t RizEarlyMappingEnvironment[] = L"RIZ_EARLY_FIXED_MAPPING_NAME";
inline constexpr wchar_t RizEarlyPublisherModuleName[] = L"RizFixedPublisher.dll";
inline constexpr wchar_t RizEarlyHelperModuleName[] = L"RizToolbox.JudgeContext.dll";
inline constexpr wchar_t RizEarlyRecorderModuleNameLower[] = L"rizownedrecorder.dll";
inline constexpr wchar_t RizEarlyProxyModuleNameLower[] = L"rizownedproxy.dll";
inline constexpr wchar_t RizEarlyPublisherModuleNameLower[] = L"rizfixedpublisher.dll";
inline constexpr wchar_t RizEarlyHelperModuleNameLower[] = L"riztoolbox.judgecontext.dll";
inline constexpr wchar_t RizEarlyHostModuleNameLower[] = L"rizownedhost.exe";
inline constexpr wchar_t RizEarlyMissingPublisherModuleName[] = L"RizOwnedMissingPublisher.dll";
inline constexpr const char* RizEarlyDebugModuleNames[]{"", "rizfixedpublisher.dll",
    "riztoolbox.judgecontext.dll", "rizownedrecorder.dll", "rizownedproxy.dll", "rizfixedownedtargets.dll"};
inline constexpr char RizEarlyDebuggerUsage[] = "Usage: RizOwnedDebugger.exe <absolute-owned-host> <debug-events.jsonl>";
inline constexpr char RizEarlyFixtureLabel[] = "early-fixed";
#endif
inline constexpr wchar_t RizEarlyTargetsModuleName[] = L"RizFixedOwnedTargets.dll";
inline constexpr wchar_t RizEarlyTargetsModuleNameLower[] = L"rizfixedownedtargets.dll";
