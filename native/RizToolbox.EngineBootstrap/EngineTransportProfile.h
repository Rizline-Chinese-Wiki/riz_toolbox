#pragma once
#include "EngineTransportCompileContract.h"
#include "EngineTransportGameProfile.h"
#include "EngineTransportAdmissionFacts.h"

#if defined(RIZ_ENGINE_TRANSPORT_OWNED) && RIZ_ENGINE_TRANSPORT_OWNED != 1
#error RIZ_ENGINE_TRANSPORT_OWNED must be omitted or exactly one.
#endif
#if defined(RIZ_ENGINE_TRANSPORT_PRODUCTION) && RIZ_ENGINE_TRANSPORT_PRODUCTION != 1
#error RIZ_ENGINE_TRANSPORT_PRODUCTION must be omitted or exactly one.
#endif
#if defined(RIZ_ENGINE_TRANSPORT_OWNED) == defined(RIZ_ENGINE_TRANSPORT_PRODUCTION)
#error Select exactly one compile-time V5 profile.
#endif
#if defined(RIZ_ENGINE_TRANSPORT_OWNED) && (!defined(RIZ_ENGINE_TRANSPORT_OWNED_IMAGE) || RIZ_ENGINE_TRANSPORT_OWNED_IMAGE != 1)
#error The cold owned V5 publisher requires its separately compiled MEM_IMAGE target path.
#endif
#if defined(RIZ_ENGINE_TRANSPORT_PRODUCTION) && defined(RIZ_ENGINE_TRANSPORT_OWNED_IMAGE)
#error A fixed game candidate cannot include the compile-owned image path.
#endif
#ifdef RIZ_ENGINE_TRANSPORT_COMMIT_FIXTURE
#error The V5 publisher cannot contain the independent source-write fault selector.
#endif

// Produced in a fresh build directory, never in the repository. It defines
// namespace RizEngineTransportGeneratedProfile with Version, Profile,
// Images[5], Edges[44], OwnedMarkerRva and OwnedMarker[16]. Images/Edges use the
// above contract types. Only image zero has ObserveHeldSelf and zero unknown
// header/hash fields; final independent CLR facts must pin all five disk SHA.
#include "EngineTransportProfile.Generated.h"

namespace RizEngineTransportProfile {
namespace Generated=RizEngineTransportGeneratedProfile;
namespace Admission=RizEngineTransportAdmissionFacts;
inline constexpr std::uint32_t Slot=28;
#if defined(RIZ_ENGINE_TRANSPORT_OWNED)
inline constexpr bool Owned=true, Production=false;
inline constexpr std::uint32_t Profile=1;
#else
inline constexpr bool Owned=false, Production=true;
inline constexpr std::uint32_t Profile=2;
#endif
// Production admission requires verified Stage A interior-entry closure and
// write-free cold-start trace bindings pinned in Generated + AdmissionFacts.
inline constexpr bool ProductionFirstInstallAuthorized=
    Production&&!Owned
    &&Generated::ProductionFirstInstallAuthorized
    &&Generated::InteriorEntryClosureVerified
    &&Generated::ProductionColdTraceVerified
    &&Generated::ProductionAdmissionVerified
    &&Admission::InteriorEntryClosureVerified
    &&Admission::ProductionColdTraceVerified
    &&Admission::ProductionAdmissionVerified
    &&Admission::CompleteStartupAdmissionKnown
    &&Admission::Detail::SameHex(Generated::FixedContractSha256,RizEngineTransportGameProfile::FactsSha256)
    &&Admission::Detail::SameHex(Generated::InteriorClosureSha256,Admission::InteriorClosureSha256)
    &&Admission::Detail::SameHex(Generated::ColdTraceSha256,Admission::ColdTraceSha256);
inline constexpr const auto& Images=Generated::Images;
inline constexpr const auto& Edges=Generated::Edges;
inline constexpr std::uint32_t OwnedMarkerRva=Generated::OwnedMarkerRva;
inline constexpr const auto& OwnedMarker=Generated::OwnedMarker;
static_assert(Generated::Version==RizEngineTransportCompileContract::Version);
static_assert(Generated::Profile==Profile);
static_assert(sizeof(Images)/sizeof(Images[0])==5&&sizeof(Edges)/sizeof(Edges[0])==44);
static_assert(sizeof(OwnedMarker)==16);
static_assert(!Owned||OwnedMarkerRva!=0);
static_assert(!Owned||!ProductionFirstInstallAuthorized);
static_assert(!ProductionFirstInstallAuthorized||(Generated::InteriorEntryClosureVerified&&Generated::ProductionColdTraceVerified&&Generated::ProductionAdmissionVerified));
}
