#pragma once
#include "EngineGameProfile.h"
#include "EngineBootstrapProductionContract.h"

#if defined(RIZ_ENGINE_BOOTSTRAP_OWNED) && RIZ_ENGINE_BOOTSTRAP_OWNED != 1
#error RIZ_ENGINE_BOOTSTRAP_OWNED must be omitted or exactly 1.
#endif
#if defined(RIZ_ENGINE_BOOTSTRAP_PRODUCTION) && RIZ_ENGINE_BOOTSTRAP_PRODUCTION != 1
#error RIZ_ENGINE_BOOTSTRAP_PRODUCTION must be omitted or exactly 1.
#endif
#if defined(RIZ_ENGINE_BOOTSTRAP_OWNED) && defined(RIZ_ENGINE_BOOTSTRAP_PRODUCTION)
#error Owned and production identities cannot be combined.
#endif
#ifndef RIZ_ENGINE_BOOTSTRAP_FAULT_STAGE
#define RIZ_ENGINE_BOOTSTRAP_FAULT_STAGE 0
#endif
#ifndef RIZ_ENGINE_BOOTSTRAP_FAULT_INDEX
#define RIZ_ENGINE_BOOTSTRAP_FAULT_INDEX 0
#endif
#if RIZ_ENGINE_BOOTSTRAP_FAULT_STAGE < 0 || RIZ_ENGINE_BOOTSTRAP_FAULT_STAGE > 8 || RIZ_ENGINE_BOOTSTRAP_FAULT_INDEX < 0 || RIZ_ENGINE_BOOTSTRAP_FAULT_INDEX > 3
#error Invalid fixed compile-time engine bootstrap fault.
#endif
#if !defined(RIZ_ENGINE_BOOTSTRAP_OWNED) && (RIZ_ENGINE_BOOTSTRAP_FAULT_STAGE != 0 || RIZ_ENGINE_BOOTSTRAP_FAULT_INDEX != 0)
#error A production candidate cannot contain an owned fault selector.
#endif
#if (RIZ_ENGINE_BOOTSTRAP_FAULT_STAGE == 0 || RIZ_ENGINE_BOOTSTRAP_FAULT_STAGE == 1 || RIZ_ENGINE_BOOTSTRAP_FAULT_STAGE == 8) && RIZ_ENGINE_BOOTSTRAP_FAULT_INDEX != 0
#error Global/no-fault stages require fixed index 0.
#endif
#if defined(RIZ_ENGINE_BOOTSTRAP_OWNED)
#include "EngineBootstrapOwnedProfile.h"
#elif defined(RIZ_ENGINE_BOOTSTRAP_PRODUCTION)
#include "EngineBootstrapProductionProfile.h"
#endif

namespace RizEngineBootstrapProfile {
using ImageIdentity = RizEngineGameProfile::ImageIdentity;
using FunctionIdentity = RizEngineGameProfile::FunctionIdentity;
using RuntimeFunction = RizEngineGameProfile::RuntimeFunction;
using UnwindIdentity = RizEngineGameProfile::UnwindIdentity;
inline constexpr wchar_t BootstrapModule[] = L"RizToolbox.EngineBootstrap.dll";
inline constexpr wchar_t KernelModule[] = L"RizToolbox.EngineNodeKernel.dll";
inline constexpr wchar_t DoorstopModule[] = L"winhttp.dll";
inline constexpr std::uint32_t Slot = 28;
inline constexpr unsigned char OwnedMarker[]{'R','I','Z','F','I','X','E','D','O','W','N','E','D','V','1',0};
inline constexpr unsigned PrefixLengths[4]{15,18,16,15};
inline constexpr unsigned FaultStage = RIZ_ENGINE_BOOTSTRAP_FAULT_STAGE;
inline constexpr unsigned FaultIndex = RIZ_ENGINE_BOOTSTRAP_FAULT_INDEX;
// Static file identities and a startup dependency graph do not establish the
// game's execution window. This permission is deliberately not generated.
inline constexpr bool ProductionFirstInstallAuthorized = false;
#if defined(RIZ_ENGINE_BOOTSTRAP_OWNED)
inline constexpr bool Owned = true;
inline constexpr bool Production = false;
inline constexpr std::uint32_t Profile = 1;
inline constexpr const auto& Executable = RizEngineBootstrapOwnedProfile::Executable;
inline constexpr const auto& Target = RizEngineBootstrapOwnedProfile::Target;
inline constexpr const auto& Functions = RizEngineBootstrapOwnedProfile::Functions;
inline constexpr const auto& Unwinds = RizEngineBootstrapOwnedProfile::Unwinds;
inline constexpr std::uint32_t MarkerRva = RizEngineBootstrapOwnedProfile::MarkerRva;
inline constexpr const ImageIdentity* KernelIdentity = &RizEngineBootstrapOwnedProfile::Kernel;
inline constexpr const ImageIdentity* DoorstopIdentity = nullptr;
inline constexpr const RizEngineBootstrapProductionContract::ImportEdge* ProductionEdges = nullptr;
inline constexpr unsigned ProductionEdgeCount = 0;
#else
inline constexpr bool Owned = false;
inline constexpr std::uint32_t Profile = 2;
inline constexpr const auto& Executable = RizEngineGameProfile::Executable;
inline constexpr const auto& Target = RizEngineGameProfile::UnityPlayer;
inline constexpr const auto& Functions = RizEngineGameProfile::Functions;
inline constexpr const auto& Unwinds = RizEngineGameProfile::Unwinds;
inline constexpr std::uint32_t MarkerRva = 0;
#if defined(RIZ_ENGINE_BOOTSTRAP_PRODUCTION)
inline constexpr bool Production = true;
inline constexpr const ImageIdentity* KernelIdentity = &RizEngineBootstrapProductionProfile::Kernel;
inline constexpr const ImageIdentity* DoorstopIdentity = &RizEngineBootstrapProductionProfile::Doorstop;
inline constexpr const auto* ProductionEdges = RizEngineBootstrapProductionProfile::Edges;
inline constexpr unsigned ProductionEdgeCount = sizeof(RizEngineBootstrapProductionProfile::Edges)/sizeof(RizEngineBootstrapProductionProfile::Edges[0]);
static_assert(ProductionEdgeCount == RizEngineBootstrapProductionContract::EdgeCount);
#else
inline constexpr bool Production = false;
// The current game profile is facts-only. A future independently pinned
// package must provide a Kernel identity and actual startup admission before
// production publication can be enabled. It is never inferred from fixtures.
inline constexpr const ImageIdentity* KernelIdentity = nullptr;
inline constexpr const ImageIdentity* DoorstopIdentity = nullptr;
inline constexpr const RizEngineBootstrapProductionContract::ImportEdge* ProductionEdges = nullptr;
inline constexpr unsigned ProductionEdgeCount = 0;
#endif
#endif
static_assert(!ProductionFirstInstallAuthorized);
static_assert(sizeof(Functions)/sizeof(Functions[0]) == 4);
static_assert(PrefixLengths[0] == 15 && PrefixLengths[1] == 18 && PrefixLengths[2] == 16 && PrefixLengths[3] == 15);
} // namespace RizEngineBootstrapProfile
