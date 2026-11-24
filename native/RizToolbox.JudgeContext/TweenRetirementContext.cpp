#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <array>
#include <algorithm>
#include <cstring>
#include <limits>
#include "TweenRetirementContext.h"
#include "OperationContext.h"
#if !defined(RIZ_TWEEN_RETIREMENT_RESERVATIONS_ONLY)
#include "TweenSourceContext.h"
#include "TweenSourceReferences.h"
#include "TweenSingleEventContext.h"
#endif
#if defined(RIZ_TWEEN_RETIREMENT_FIXTURE)
extern "C" void RizTweenRetirementFixture_AfterEnter(std::uint32_t domain);
#endif

namespace {
constexpr unsigned Capacity = 8192;
SRWLOCK gate = SRWLOCK_INIT;
struct Guard { Guard(){AcquireSRWLockExclusive(&gate);} ~Guard(){ReleaseSRWLockExclusive(&gate);} };
struct Reservation { std::uint64_t serial{}, generation{}; std::uint32_t domain{}, thread{}; };
std::array<Reservation, Capacity> reservations{};
std::uint64_t generation = 1, serial = 0, active = 0, entered = 0, exited = 0, cuts = 0, commits = 0;
volatile LONG unknown = 0;
#if !defined(RIZ_TWEEN_RETIREMENT_RESERVATIONS_ONLY)
RizTweenRetirementCutV1 current{};
std::array<RizTweenRetirementClaimV1,128> currentClaims{};
// Private caller thread identity is frozen at Acquire. A foreign caller can
// neither invalidate another thread's loan nor commit/release its group.
bool Same(const RizTweenRetirementCutV1& cut)
{
    auto expected = current; expected.phase = cut.phase;
    return current.token && cut.threadId == GetCurrentThreadId() && cut.phase == 1
        && std::memcmp(&cut,&expected,sizeof(cut)) == 0;
}
bool Headers(const RizTweenRetirementRequestV1& request)
{
    return request.abiVersion == 1 && request.byteSize == sizeof(request) && request.configurationEpoch
        && request.sourceEpoch && request.referenceObserverEpoch && request.referenceReaderGeneration;
}
bool Shape(std::uint32_t count,const RizTweenRetirementClaimV1* claims)
{
    if (count > currentClaims.size() || count && !claims) return false;
    for (unsigned i = 0; i != count; ++i) {
        if (claims[i].slot >= 128 || claims[i].reserved || !claims[i].epoch || !claims[i].object || !claims[i].actualClass) return false;
        for (unsigned j = 0; j != i; ++j)
            if (claims[j].slot == claims[i].slot || claims[j].object == claims[i].object) return false;
    }
    return true;
}
unsigned SourceState(const RizTweenRetirementRequestV1& request)
{
    constexpr unsigned kinds[]{0,1,1,0,0,3},slots[]{25,20,19,24,23,31};
    for(unsigned i=0;i!=6;++i){
        RizOperationBootstrapInfoV1 lane{1,sizeof(lane)};
        if(RizOperationBootstrap_GetInfo(kinds[i],slots[i],&lane) || lane.unknown || lane.enterFaults || lane.exitFaults
            || !lane.epoch || !lane.entry || !lane.original)return 5;
    }
    RizTweenSourceInfoV1 source{1,sizeof(source)};
    RizTweenSourceReferencesInfoV1 references{1,sizeof(references)};
    RizTweenSingleEventInfoV1 single{1,sizeof(single)};
    if(RizTweenSingleEventContext_GetInfo(&single)||!single.configured||!single.attached||single.unknown||!single.epoch
        ||single.callbackFaults||single.rejectedReads||single.missingPosts||single.retainedParents||single.retainedChildren||single.overflow)return 5;
    if(single.parentsInFlight||single.childrenInFlight||single.parentsEntered!=single.parentsExited||single.childrenEntered!=single.childrenExited)return 3;
    if (RizTweenSourceContext_GetInfo(&source) || RizTweenSourceReferences_GetInfo(&references)
        || !source.configured || !source.metadataBound || source.unknown || source.faults
        || source.bindingCount != 43 || source.journalCapacity != 8192 || !references.attached
        || references.unknown || references.callbackFaults || references.rejectedReads) return 5;
    if (source.epoch != request.sourceEpoch || references.sourceEpoch != request.sourceEpoch
        || references.observerEpoch != request.referenceObserverEpoch
        || source.events < request.journalCutoff || references.deliveryAttempts < request.referenceDeliveryCutoff) return 2;
    // AfterCommitted has not ended the real native scope. RetainedScopes must
    // be zero too; a callback returning successfully alone cannot satisfy this.
    if (source.activeUpdates || source.pendingTransactions || references.activeScopes || references.retainedScopes
        || references.callbackInFlight || references.scopesBegun != references.scopesCompleted
        || references.deliveriesCompleted != references.deliveryAttempts) return 3;
    return 0;
}
#endif
}

RizTweenRetirementReservation RizTweenRetirementEnter(std::uint32_t domain)
{
    RizTweenRetirementReservation ticket{};
    {
        Guard guard;
        if (domain == RizTweenRetirementDomainCount) return {};
        if (domain > RizTweenRetirementDomainCount || serial == (std::numeric_limits<std::uint64_t>::max)()
            || entered == (std::numeric_limits<std::uint64_t>::max)()) { unknown=1; return {}; }
        for (unsigned i=0; i != reservations.size(); ++i) if (!reservations[i].serial) {
            auto& value=reservations[i]; value={++serial,generation,domain,GetCurrentThreadId()}; ++active; ++entered;
            ticket={value.serial,value.generation,value.domain,i};break;
        }
        if(!ticket.serial)unknown=1;
    }
#if defined(RIZ_TWEEN_RETIREMENT_FIXTURE)
    if(ticket.serial)RizTweenRetirementFixture_AfterEnter(domain);
#endif
    return ticket;
}
void RizTweenRetirementExit(RizTweenRetirementReservation& ticket,bool failed)
{
    if (!ticket.serial) return;
    Guard guard;
    if (ticket.slot >= reservations.size()) { unknown=1; return; }
    auto& value=reservations[ticket.slot];
    if (value.serial != ticket.serial || value.generation != ticket.generation || value.domain != ticket.domain
        || value.thread != GetCurrentThreadId() || !active) { unknown=1; return; }
    if (failed) unknown=1;
    value={}; --active; ++exited; ticket={};
}
// Fault can be raised while a source/reference lock is held. It only sets a
// sticky atomic bit, so no reverse lock acquisition or callback is possible.
void RizTweenRetirementFault() { InterlockedExchange(&unknown,1); }
std::uint32_t RizTweenRetirementOperationDomain(std::uint32_t kind,std::uint32_t slot)
{
    if(kind==0&&slot==25)return RizTweenRetirementPlay;
    if(kind==1&&slot==20)return RizTweenRetirementAdd;
    if(kind==1&&slot==19)return RizTweenRetirementExecute;
    if(kind==0&&slot==24)return RizTweenRetirementUpdate;
    if(kind==0&&slot==23)return RizTweenRetirementAwake;
    if(kind==3&&slot==31)return RizTweenRetirementSingle;
    return RizTweenRetirementDomainCount;
}

#if !defined(RIZ_TWEEN_RETIREMENT_RESERVATIONS_ONLY)
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_Acquire(const RizTweenRetirementRequestV1* raw,
    std::uint32_t count,const RizTweenRetirementClaimV1* claims,RizTweenRetirementCutV1* output)
{
    if (!raw || !output || output->abiVersion != 1 || output->byteSize != sizeof(*output)) return 1;
    const auto request=*raw;
    if (!Headers(request) || !Shape(count,claims)) return 1;
    Guard guard;
    if (unknown) return 5;
    if (current.token || active) return 3;
    const auto state=SourceState(request); if (state) {if(state==5)unknown=1;return state;}
    const auto admitted=RizTweenAdmissionRetirementBatch(request.configurationEpoch,count,claims,nullptr,false);
    if (admitted) {if(admitted==5)unknown=1;return admitted;}
    if (generation == (std::numeric_limits<std::uint64_t>::max)() || cuts == (std::numeric_limits<std::uint64_t>::max)()) {unknown=1;return 5;}
    current={1,sizeof(current),GetCurrentThreadId(),1,++cuts,generation++,request.configurationEpoch,
        request.sourceEpoch,request.journalCutoff,request.referenceObserverEpoch,request.referenceDeliveryCutoff,
        request.referenceReaderGeneration,count,0};
    if(count)std::copy_n(claims,count,currentClaims.begin()); *output=current; return 0;
}
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_Verify(const RizTweenRetirementCutV1* raw)
{
    if (!raw || raw->abiVersion != 1 || raw->byteSize != sizeof(*raw)) return 1;
    Guard guard; if (!Same(*raw)) return 2; if(unknown)return 5;
    // A new generation may run foreign originals. It cannot obtain the closed
    // run's managed enrollment or its root generations. Never wait for it or
    // suppress its game execution. No old reservation may reappear.
    for(const auto& value:reservations)if(value.serial && value.generation<=current.generation){unknown=1;return 5;}
    return 0;
}
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_UnregisterBatch(const RizTweenRetirementCutV1* raw,
    std::uint32_t count,const RizTweenRetirementClaimV1* claims,RizTweenAdmissionReceiptV1* receipts)
{
    if (!raw || count && !receipts || !Shape(count,claims)) return 1;
    Guard guard;if(!Same(*raw))return 2;if(unknown)return 5;if(current.phase==2)return 6;
    if(count!=current.claimCount || count&&std::memcmp(claims,currentClaims.data(),count*sizeof(*claims)))return 2;
    // Reservations from the next generation are unrelated to the permanently
    // withdrawn run. Admission's same lock serializes their exact dead policy
    // with this all-or-nothing registry mutation.
    const auto result=RizTweenAdmissionRetirementBatch(current.configurationEpoch,count,currentClaims.data(),receipts,true);
    if(result){if(result==5)unknown=1;return result;}
    current.phase=2;++commits;return 0;
}
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_VerifyReferenceRoots(const RizTweenRetirementCutV1* raw)
{
    if(!raw)return 1;
    Guard guard;if(!Same(*raw))return 2;if(unknown)return 5;
    RizTweenSourceReferencesInfoV1 references{1,sizeof(references)};
    if(RizTweenSourceReferences_GetInfo(&references) || !references.attached || references.unknown
        || references.callbackFaults || references.rejectedReads){unknown=1;return 5;}
    if(references.sourceEpoch!=current.sourceEpoch || references.observerEpoch!=current.referenceObserverEpoch
        || references.deliveryAttempts<current.referenceDeliveryCutoff)return 2;
    return references.activeScopes || references.retainedScopes || references.callbackInFlight
        || references.scopesBegun!=references.scopesCompleted || references.deliveryAttempts!=references.deliveriesCompleted ? 3u : 0u;
}
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_Release(const RizTweenRetirementCutV1* raw)
{
    if(!raw)return 1;Guard guard;if(!Same(*raw))return 2;
    const auto result=unknown?5u:0u;current={};currentClaims={};return result;
}
#endif
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_GetInfo(RizTweenRetirementInfoV1* info)
{
    if(!info || info->abiVersion!=1 || info->byteSize!=sizeof(*info))return 1;
    Guard guard;
#if defined(RIZ_TWEEN_RETIREMENT_RESERVATIONS_ONLY)
    constexpr unsigned cutActive=0;
#else
    const unsigned cutActive=current.token?1u:0u;
#endif
    *info={1,sizeof(*info),static_cast<unsigned>(unknown),cutActive,generation,active,entered,exited,cuts,commits};return 0;
}
