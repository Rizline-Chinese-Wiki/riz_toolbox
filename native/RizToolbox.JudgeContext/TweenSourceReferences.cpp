#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <array>
#include <cstring>
#include <limits>
#include "TweenSourceReferences.h"
#include "TweenRetirementContext.h"
#include "TweenSourceContext.h"

namespace {
constexpr unsigned ScopeLimit = 128, ReferenceLimit = 16;
constexpr std::uint32_t AllRoles = RizTweenReferenceManager | RizTweenReferenceTween | RizTweenReferenceContainer
    | RizTweenReferenceArray | RizTweenReferencePrior | RizTweenReferenceEvent;
SRWLOCK gate = SRWLOCK_INIT;
struct Guard {
    Guard() { AcquireSRWLockExclusive(&gate); }
    ~Guard() { ReleaseSRWLockExclusive(&gate); }
};
struct Scope {
    RizTweenRetirementReservation retirement{};
    bool used{}, active{}, failed{};
    bool beforeReferences{}, afterReferences{};
    bool beforeAttempted{}, beforeCompleted{};
    bool afterAttempted{}, afterCompleted{}, abnormalAttempted{};
    unsigned delivering{};
    RizTweenSourceReferencesTicketV1 ticket{};
    RizTweenSourceReferenceOriginV1 origin{};
    RizTweenSourceReferencesObserverV1 observer{};
};
struct CurrentDelivery {
    const RizTweenSourceReferenceNoticeV1* notice{};
    CurrentDelivery* parent{};
};
thread_local CurrentDelivery* currentDelivery{};
std::array<Scope, ScopeLimit> scopes{};
RizTweenSourceReferencesObserverV1 observer{};
std::uint32_t attached{}, unknown{}, activeScopes{}, retainedScopes{}, callbackInFlight{};
std::uint64_t sourceEpoch{}, observerEpoch{}, scopeSequence{}, scopesEnded{}, scopesCompleted{};
std::uint64_t deliveryAttempts{}, deliveriesCompleted{}, callbackFaults{}, rejectedReads{};

template<class T> bool Read(const T* pointer, T& output)
{
    if (!pointer) return false;
    __try { std::memcpy(&output, pointer, sizeof(output)); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
template<class T> bool Write(T* pointer, const T& value)
{
    if (!pointer) return false;
    __try { std::memcpy(pointer, &value, sizeof(value)); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void Fault(Scope* scope = nullptr) { unknown = 1; RizTweenRetirementFault(); if (scope) scope->failed = true; }
bool Advance(std::uint64_t& value)
{
    if (value == (std::numeric_limits<std::uint64_t>::max)()) { Fault(); return false; }
    ++value; return true;
}
bool OriginShape(const RizTweenSourceReferenceOriginV1& value, bool initial)
{
    if (value.abiVersion != 1 || value.byteSize != sizeof(value) || value.operation > RizTweenSourceExecute
        || !value.threadId || value.threadId != GetCurrentThreadId() || !value.depth || value.depth > 16
        || value.site >= RizTweenSourceSiteCount || value.stage > RizTweenSourceDecision
        || (value.originRefRoles & ~AllRoles) || !value.sourceEpoch || !value.operationSequence
        || value.operationSequence == value.parentOperationSequence || !value.transaction
        || !value.sourcePc || !value.relayReturnPc) return false;
    const auto operation = value.site <= RizTweenUpdateOnStartAppend ? RizTweenSourceUpdate
        : value.site <= RizTweenPlayOnComplete ? RizTweenSourcePlay : RizTweenSourceExecute;
    if (value.operation != operation) return false;
    if (value.site == RizTweenTailDeadDecision) return value.stage == RizTweenSourceDecision;
    return value.stage != RizTweenSourceDecision && (!initial || value.stage == RizTweenSourceBefore);
}
bool SameOrigin(const RizTweenSourceReferenceOriginV1& left, const RizTweenSourceReferenceOriginV1& right)
{
    // Stage changes at the actual After. Every source/parent/role identity stays
    // fixed for this source transaction; a borrowed token never changes origin.
    return left.abiVersion == right.abiVersion && left.byteSize == right.byteSize
        && left.operation == right.operation && left.threadId == right.threadId && left.depth == right.depth
        && left.site == right.site && left.originRefRoles == right.originRefRoles
        && left.sourceEpoch == right.sourceEpoch && left.operationSequence == right.operationSequence
        && left.parentOperationSequence == right.parentOperationSequence && left.transaction == right.transaction
        && left.sourcePc == right.sourcePc && left.relayReturnPc == right.relayReturnPc;
}
bool TicketShape(const RizTweenSourceReferencesTicketV1& value)
{
    return value.abiVersion == 1 && value.byteSize == sizeof(value) && value.threadId == GetCurrentThreadId()
        && value.reserved == 0 && value.observerEpoch && value.scopeId && value.sourceEpoch
        && value.operationSequence && value.transaction;
}
Scope* Find(const RizTweenSourceReferencesTicketV1& ticket)
{
    for (auto& scope : scopes)
        if (scope.used && std::memcmp(&scope.ticket, &ticket, sizeof(ticket)) == 0) return &scope;
    return nullptr;
}
std::uint32_t RejectDelivery(const RizTweenSourceReferencesTicketV1& ticket, std::uint32_t result)
{
    Guard guard;
    // A bad private delivery cannot later be normalized into an acknowledged
    // pair. Keep the actual reserved scope even if the caller continues After.
    Fault(Find(ticket)); return result;
}
struct BoundaryResult { std::uint32_t result, exception; };
BoundaryResult InvokeCpp(RizTweenSourceReferencesNotifyV1 notify,
    const RizTweenSourceReferenceNoticeV1* notice, void* user)
{
    try { return {notify(notice, user), 0}; }
    catch (...) { return {5, 0xE06D7363u}; }
}
BoundaryResult InvokeBoundary(RizTweenSourceReferencesNotifyV1 notify,
    const RizTweenSourceReferenceNoticeV1* notice, void* user)
{
    // This boundary encloses only the observer. The actual game CALL/replay and
    // its exception are outside it and are never caught or replaced here.
    __try { return InvokeCpp(notify, notice, user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return {5, GetExceptionCode()}; }
}
bool Overlap(const void* first, std::size_t firstBytes, const void* second, std::size_t secondBytes)
{
    if (!firstBytes || !secondBytes) return false;
    const auto a = reinterpret_cast<std::uintptr_t>(first), b = reinterpret_cast<std::uintptr_t>(second);
    if (firstBytes > (std::numeric_limits<std::uintptr_t>::max)() - a
        || secondBytes > (std::numeric_limits<std::uintptr_t>::max)() - b) return true;
    return a < b + secondBytes && b < a + firstBytes;
}
bool CopyCurrent(const RizTweenSourceReferenceNoticeV1& actual,
    RizTweenSourceReferenceNoticeV1* noticeCopy, RizTweenSourceReferenceOriginV1* originCopy,
    RizTweenSourceBorrowedReferenceV1* referencesCopy, std::uint32_t capacity)
{
    if (!noticeCopy || !originCopy || capacity < actual.referenceCount || (actual.referenceCount && !referencesCopy)) return false;
    const auto bytes = actual.referenceCount * sizeof(RizTweenSourceBorrowedReferenceV1);
    // Never let a diagnostic copy overwrite the current immutable source view,
    // or alias its own output fields into a misleading partial object graph.
    const void* outputs[]{noticeCopy, originCopy, referencesCopy};
    const std::size_t sizes[]{sizeof(*noticeCopy), sizeof(*originCopy), bytes};
    for (unsigned i = 0; i < 3; ++i) {
        if (Overlap(outputs[i], sizes[i], &actual, sizeof(actual))
            || Overlap(outputs[i], sizes[i], actual.origin, sizeof(*actual.origin))
            || Overlap(outputs[i], sizes[i], actual.references, bytes)) return false;
        for (unsigned j = 0; j < i; ++j) if (Overlap(outputs[i], sizes[i], outputs[j], sizes[j])) return false;
    }
    __try {
        if (noticeCopy->abiVersion != 1 || noticeCopy->byteSize != sizeof(*noticeCopy)
            || originCopy->abiVersion != 1 || originCopy->byteSize != sizeof(*originCopy)) return false;
        if (bytes) std::memcpy(referencesCopy, actual.references, bytes);
        *originCopy = *actual.origin;
        *noticeCopy = actual;
        noticeCopy->origin = originCopy;
        noticeCopy->references = bytes ? referencesCopy : nullptr;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
std::uint32_t RejectRead(std::uint32_t result)
{
    Guard guard; Advance(rejectedReads); RizTweenRetirementFault(); return result;
}
}

std::uint32_t RizTweenSourceReferencesAttach(std::uint64_t actualEpoch,
    const RizTweenSourceReferencesObserverV1* raw)
{
    RizTweenSourceReferencesObserverV1 copy{};
    if (!actualEpoch || !Read(raw, copy) || copy.abiVersion != 1 || copy.byteSize != sizeof(copy) || !copy.notify) return 1;
    Guard guard;
    if (attached) return 2;
    // No Ready boolean or metadata substitute exists in this component. Only
    // the private same-helper FirstInstall Ready gate may reach this function.
    observer = copy; sourceEpoch = actualEpoch; observerEpoch = 1; attached = 1;
    return 0;
}
std::uint32_t RizTweenSourceReferencesBeginFromSource(
    const RizTweenSourceReferenceOriginV1* raw, RizTweenSourceReferencesTicketV1* output)
{
    RizTweenRetirementScope retirement(RizTweenRetirementReferenceScope);
    RizTweenSourceReferenceOriginV1 origin{}; RizTweenSourceReferencesTicketV1 header{};
    if (!Read(raw, origin) || !OriginShape(origin, true) || !Read(output, header)
        || header.abiVersion != 1 || header.byteSize != sizeof(header) || header.reserved) return 1;
    Guard guard;
    if (!attached) return 2;
    if (origin.sourceEpoch != sourceEpoch) return 3;
    for (auto& scope : scopes) if (scope.used && SameOrigin(scope.origin, origin)) { Fault(&scope); return 3; }
    for (auto& scope : scopes) if (!scope.used) {
        if (!Advance(scopeSequence)) return 5;
        scope = {}; scope.used = true; scope.active = true; scope.origin = origin; scope.observer = observer;
        scope.retirement = retirement.Take();
        scope.ticket = {1, sizeof(scope.ticket), origin.threadId, 0, observerEpoch, scopeSequence,
            sourceEpoch, origin.operationSequence, origin.transaction};
        ++activeScopes; ++retainedScopes;
        if (!Write(output, scope.ticket)) { Fault(&scope); return 5; }
        return 0;
    }
    Fault(); return 5;
}
std::uint32_t RizTweenSourceReferencesDeliverFromSource(
    const RizTweenSourceReferencesTicketV1* rawTicket, std::uint32_t phase,
    const RizTweenSourceReferenceOriginV1* rawOrigin,
    const RizTweenSourceBorrowedReferenceV1* rawReferences, std::uint32_t referenceCount)
{
    RizTweenRetirementScope retirement(RizTweenRetirementReferenceNotify);
    RizTweenSourceReferencesTicketV1 ticket{}; RizTweenSourceReferenceOriginV1 origin{};
    std::array<RizTweenSourceBorrowedReferenceV1, ReferenceLimit> references{};
    if (!Read(rawTicket, ticket) || !TicketShape(ticket)) return 1;
    if (!Read(rawOrigin, origin) || !OriginShape(origin, false)
        || phase > RizTweenReferencesAbnormal || referenceCount > ReferenceLimit
        || (phase == RizTweenReferences ? referenceCount == 0 : referenceCount != 0)) return RejectDelivery(ticket, 1);
    bool hasReference = false;
    for (unsigned i = 0; i < referenceCount; ++i) {
        if (!Read(rawReferences ? rawReferences + i : nullptr, references[i])) return RejectDelivery(ticket, 1);
        const auto role = references[i].role;
        if (!role || (role & (role - 1u)) || (role & ~origin.originRefRoles)) return RejectDelivery(ticket, 1);
        hasReference = hasReference || references[i].value != nullptr;
    }
    Scope* scope{}; std::uint64_t delivery{};
    RizTweenSourceReferencesObserverV1 captured{};
    {
        Guard guard; scope = Find(ticket);
        if (!scope || !scope->active || !SameOrigin(scope->origin, origin)) { Fault(scope); return 3; }
        if (scope->delivering) { Fault(scope); return 5; }
        const bool decision = scope->origin.stage == RizTweenSourceDecision;
        if (phase == RizTweenReferences) {
            if (origin.stage != scope->origin.stage
                && (decision || origin.stage != RizTweenSourceAfter || !scope->beforeAttempted)) { Fault(scope); return 4; }
            if (origin.stage == scope->origin.stage) scope->beforeReferences = scope->beforeReferences || hasReference;
            else scope->afterReferences = scope->afterReferences || hasReference;
        } else if (phase == RizTweenReferencesBeforeCommitted) {
            if (scope->beforeAttempted || !scope->beforeReferences || origin.stage != scope->origin.stage) { Fault(scope); return 4; }
            scope->beforeAttempted = true;
        } else if (phase == RizTweenReferencesAfterCommitted) {
            if (decision || !scope->beforeAttempted || !scope->afterReferences || scope->afterAttempted
                || origin.stage != RizTweenSourceAfter) { Fault(scope); return 4; }
            scope->afterAttempted = true;
        } else {
            if (scope->abnormalAttempted) { Fault(scope); return 4; }
            scope->abnormalAttempted = true;
        }
        if (!Advance(deliveryAttempts)) { Fault(scope); return 5; }
        delivery = deliveryAttempts; captured = scope->observer; scope->delivering = 1; ++callbackInFlight;
    }
    // Native responsibility and immutable observer are registered above. NO
    // native lock is held across this synchronous unmanaged->managed CALL.
    const RizTweenSourceReferenceNoticeV1 notice{1, sizeof(notice), phase, referenceCount,
        ticket.observerEpoch, ticket.scopeId, delivery, &origin, referenceCount ? references.data() : nullptr};
    CurrentDelivery current{&notice, currentDelivery}; currentDelivery = &current;
    const auto result = InvokeBoundary(captured.notify, &notice, captured.user);
    currentDelivery = current.parent;
    {
        Guard guard; scope->delivering = 0; --callbackInFlight;
        if (result.result || result.exception) { Fault(scope); Advance(callbackFaults); }
        else {
            Advance(deliveriesCompleted);
            if (phase == RizTweenReferencesBeforeCommitted) scope->beforeCompleted = true;
            if (phase == RizTweenReferencesAfterCommitted) scope->afterCompleted = true;
        }
    }
    return result.result || result.exception ? 5u : 0u;
}
std::uint32_t RizTweenSourceReferencesEndFromSource(
    const RizTweenSourceReferencesTicketV1* raw, bool returned)
{
    RizTweenSourceReferencesTicketV1 ticket{}; RizTweenSourceReferenceOriginV1 origin{}; bool abnormal{};
    if (!Read(raw, ticket) || !TicketShape(ticket)) return 1;
    {
        Guard guard; auto* scope = Find(ticket);
        if (!scope || !scope->active) return 3;
        if (scope->delivering) { Fault(scope); return 5; }
        origin = scope->origin; abnormal = !returned && !scope->abnormalAttempted;
    }
    if (abnormal) RizTweenSourceReferencesDeliverFromSource(&ticket, RizTweenReferencesAbnormal, &origin, nullptr, 0);
    RizTweenRetirementReservation retirement{};
    {
        Guard guard; auto* scope = Find(ticket);
        if (!scope || !scope->active || scope->delivering) { Fault(scope); return 5; }
        scope->active = false; --activeScopes; Advance(scopesEnded);
        const bool complete = returned && !scope->failed && !scope->abnormalAttempted && scope->beforeReferences && scope->beforeCompleted
            && (scope->origin.stage == RizTweenSourceDecision || scope->afterReferences && scope->afterCompleted);
        if (!complete) { Fault(scope); return 5; }
        scope->used = false; --retainedScopes; Advance(scopesCompleted);
        retirement=scope->retirement;scope->retirement={};
    }
    RizTweenRetirementExit(retirement);return 0;
}
RIZTSR_EXPORT std::uint32_t __cdecl RizTweenSourceReferences_GetInfo(RizTweenSourceReferencesInfoV1* output)
{
    RizTweenSourceReferencesInfoV1 header{};
    if (!Read(output, header) || header.abiVersion != 1 || header.byteSize != sizeof(header)) return 1;
    Guard guard;
    const RizTweenSourceReferencesInfoV1 value{1, sizeof(value), attached, unknown, ScopeLimit, activeScopes,
        retainedScopes, callbackInFlight, sourceEpoch, observerEpoch, scopeSequence, scopesEnded, scopesCompleted,
        deliveryAttempts, deliveriesCompleted, callbackFaults, rejectedReads};
    return Write(output, value) ? 0u : 1u;
}
RIZTSR_EXPORT std::uint32_t __cdecl RizTweenSourceReferences_ReadCurrentNotice(
    const RizTweenSourceReferenceNoticeV1* actual, std::uint64_t expectedObserverEpoch,
    std::uint64_t expectedScope, std::uint64_t expectedDelivery,
    RizTweenSourceReferenceNoticeV1* noticeCopy, RizTweenSourceReferenceOriginV1* originCopy,
    RizTweenSourceBorrowedReferenceV1* referencesCopy, std::uint32_t capacity)
{
    // Only compare the caller address until the current real native TLS scope
    // proves it. Never read arbitrary/stale caller DTO reference pointers.
    if (!currentDelivery || actual != currentDelivery->notice) return RejectRead(3);
    const auto& notice = *currentDelivery->notice;
    if (notice.observerEpoch != expectedObserverEpoch || notice.scopeId != expectedScope
        || notice.deliverySequence != expectedDelivery || notice.origin->threadId != GetCurrentThreadId()) return RejectRead(3);
    if (!CopyCurrent(notice, noticeCopy, originCopy, referencesCopy, capacity)) return RejectRead(1);
    return 0;
}
