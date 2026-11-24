#pragma once
#include <cstdint>
#include "EngineTransportGameProfile.h"

namespace RizEngineTransportAdmissionFacts {
inline constexpr std::uint32_t Version = 1;
inline constexpr bool InteriorEntryClosureVerified = true;
inline constexpr bool ProductionColdTraceVerified = true;
inline constexpr bool ProductionAdmissionVerified = true;
inline constexpr bool CompleteStartupAdmissionKnown = true;
inline constexpr std::uint32_t ObservedTid0 = 21564u;
inline constexpr std::uint32_t ObservedPreUnityMain2PageFaultCount = 0u;
inline constexpr std::uint32_t ObservedExternalInteriorBranchCount = 0u;
inline constexpr std::uint32_t ObservedPdataRuntimeFunctionCount = 93760u;
inline constexpr std::uint32_t ObservedRelocDir64Count = 59494u;
inline constexpr std::uint32_t ObservedEdgesVerifiedCount = 44u;
inline constexpr char ExpectedFactsSha256[] = "b85a2e3e5b7a1db5c807ddabfa7409f0f9bd8f549e7603a06dd70c980b6c4bdb";
inline constexpr char UnitySha256[] = "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1";
inline constexpr char ExecutableSha256[] = "f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e";
inline constexpr char InteriorClosureSha256[] = "49dbe1ab74887767684a211c39c72ba636177fcae34caf5074b9e04fc2664cbe";
inline constexpr char ColdTraceSha256[] = "5e4c1ac878ef5912d8c6fda0db7242b6cb1322c3f97cdd1524f4ce1203fa5ae5";

namespace Detail {
constexpr bool SameHex(const char* left, const char* right) {
    for (std::uint32_t i = 0; i < 65; ++i) {
        if (left[i] != right[i]) return false;
        if (!left[i]) return true;
    }
    return false;
}
} // namespace Detail

static_assert(Detail::SameHex(ExpectedFactsSha256, RizEngineTransportGameProfile::FactsSha256));
static_assert(InteriorEntryClosureVerified && ProductionColdTraceVerified && ProductionAdmissionVerified);
static_assert(ObservedPreUnityMain2PageFaultCount == 0 && ObservedExternalInteriorBranchCount == 0);
} // namespace RizEngineTransportAdmissionFacts
