#pragma once
#include <cstddef>
#include <cstdint>
#include "EngineTransportPublication.h"

#if defined(RIZ_ENGINE_TRANSPORT_OWNED_IMAGE) && (RIZ_ENGINE_TRANSPORT_OWNED_IMAGE != 1 || !defined(RIZ_ENGINE_TRANSPORT_OWNED) || RIZ_ENGINE_TRANSPORT_OWNED != 1 || defined(RIZ_ENGINE_TRANSPORT_PRODUCTION))
#error The MEM_IMAGE owned path is restricted to the separately compiled V5 owned publisher.
#endif

// Link-private Configure component. Request is a descriptor, never an admission
// credential. The containing producer independently proves held file hashes,
// fixed module ownership, original symbols/continuation slots, near ownership,
// runtime-table registration, and the startup execution window. Nothing here
// Attaches an observer, patches source, loads a module, or declares Ready.
namespace RizEngineTransportFamilies {
inline constexpr std::uint32_t Version=1, FamilyCount=12, TypedCount=13, SiteCount=23;
inline constexpr std::uint32_t ConfigCapacity=432, InfoCapacity=176, NoIndex=0xffffffffu;
inline constexpr std::uint64_t AllFamilies=(1ull<<FamilyCount)-1;
enum Result:std::uint32_t {
    Success=0, InvalidInput, Busy, AlreadyAttempted, BadImage, BadExports,
    BadImports, BadAbi, BadTarget, BadOriginal, BadSite, SlotNotFresh,
    ConfigureFailed, BadInformation, ReceiptChanged, NativeException, AdmissionUnproven
};
enum Profile:std::uint32_t { Owned=1,FixedGame=2 };
enum State:std::uint32_t { Never=0, Preparing, Configuring, Configured, Failed };
struct Site { std::uint64_t logicalPc,relayReturnPc; };
struct Request {
    std::uint32_t version,byteSize,slot,mainOsTid,profile,reserved;
    std::uint64_t kernelModule,targetModule,originalModule;
    std::uint64_t relayAllocationBase,relayAllocationBytes;
    // Fixed order: Node4, Scheduler6, Wait3. Every target is the actual module
    // base plus its independently generated fixed RVA; originals are RX images.
    std::uint64_t targets[TypedCount],originals[TypedCount];
    // Inline3, DrainRow2, Wait18. logicalPc stays the observation PC even when
    // the physical source block expands; relayReturnPc is a real FF15 CALL end.
    Site sites[SiteCount];
};
struct Receipt {
    std::uint32_t version,byteSize,state,result;
    std::uint32_t failureFamily,lastWin32Error,descriptorsPrepared,inlineEpochBound;
    std::uint32_t configureAttempts,configureSucceeded,apiCount,profile;
    std::uint64_t attemptedFamilies,configuredFamilies,verifiedFamilies,retainedFamilies;
    std::uint64_t kernelModule,targetModule,originalModule,mainOsTid;
    std::uint64_t typedEntries[TypedCount],inlineEntry,drainRowEntry,waitPhaseEntry;
    RizEngineTransportFamilyV5 families[FamilyCount];
    std::uint32_t configureResults[FamilyCount],informationResults[FamilyCount];
    // Exact zero-filled Configure bytes, with unused tails remaining zero.
    // Inline's expectedStartEpoch is the sole deferred field: it is filled
    // only from actual Start Configure/GetInfo before Inline Configure/hash.
    std::uint8_t configurations[FamilyCount][ConfigCapacity];
    // Actual immediately-after-Configure Info snapshots, not synthesized rows.
    // Later Verify permits real counters/Attach transitions but no identity drift.
    std::uint32_t informationBytes[FamilyCount];
    std::uint8_t information[FamilyCount][InfoCapacity];
};
// One process-local permanent attempt. All 12 descriptors/templates, ABI/API,
// exact source/original/site facts and fresh slots are checked before the first
// Configure. A failed/exceptional attempt is never retried. attemptedFamilies
// conservatively retains possible claims even when native return/Info is lost.
// Output must be fresh zeroed writable storage, disjoint from Request/code.
// Busy is nonwaiting. Copies retain all earlier obligations; no rollback/free.
Result PrepareAndConfigure(const Request* request,Receipt* output);
// Requires the exact privately retained Request+Receipt, not a caller-created
// result DTO. Validates all 12 live immutable identities and monotonic Attach.
// No Configure/Attach or source write occurs. Failure is sticky process-local.
Result Verify(const Request* request,const Receipt* receipt);
// Default owned tests accept only a MEM_PRIVATE exact fixed-image disk mapping.
// The compile-owned-image publisher additionally binds a distinct real loaded
// target to its held whole-file SHA and immutable owned marker; no runtime flag
// can select that path. Fixed game requests remain independently refused.
static_assert(sizeof(Site)==16&&sizeof(Request)==640&&alignof(Request)==8);
static_assert(sizeof(RizEngineTransportFamilyV5)==176);
static_assert(offsetof(Receipt,configurations)%8==0);
}
