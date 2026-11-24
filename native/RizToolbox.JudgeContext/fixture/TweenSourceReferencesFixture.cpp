#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <iterator>
#include <memory>
#include <unordered_map>
#include "../TweenSourceContext.h"
#include "../TweenSourceReferences.h"

// Independent owned executable. This links the actual private references
// primitive. The controlled metadata gate and shared_ptr roots below model only
// fixture-owned objects: no IL2CPP handle, game Ready receipt or game execution
// is claimed. Ordinary C++ CALL/RET is used; no return slot is rewritten.
namespace {
constexpr std::uint64_t SourceEpoch = 7, Payload = 0xF123456789ABCDEFULL;
constexpr DWORD ObserverSeh = 0xE0545246u, OriginalSeh = 0xE0545247u;
constexpr unsigned AllRoles = 63;
std::uint64_t checks{}, failures{}, callbacks{}, referencesNotices{}, beforeNotices{}, afterNotices{}, abnormalNotices{};
std::uint64_t metadataReads{}, rootsRecorded{}, originalCalls{}, originalEffects{}, nestedCalls{}, workerRejections{}, returnChecks{};
std::uint64_t transaction{}, operationSequence{};
unsigned maximumCallbacks{}, cppUnwinds{};
bool controlledMetadataBound{}, failureArmed{}, nestedArmed{}, workerArmed{};
const char* mode{};

void Check(bool value, const char* expression, int line)
{
    ++checks;
    if (!value) { ++failures; std::fprintf(stderr, "FAILED source references fixture line %d: %s\n", line, expression); }
}
#define CHECK(expression) Check(!!(expression), #expression, __LINE__)

struct Object { unsigned exactRole; unsigned mutations{}; };
std::unordered_map<void*, std::weak_ptr<Object>> objects;
struct RootRecord {
    bool attempted{}, recorded{};
    std::uint64_t scope{};
    void* identity{};
    unsigned role{};
    std::shared_ptr<Object> independent;
};
// Reservation exists before any root adoption. Unknown callback records are
// process-held and never silently erased or retried by transport completion.
std::array<RootRecord, 2048> roots{};
unsigned rootCount{};
std::shared_ptr<Object> manager, tween, container, arrayBefore, eventObject;
std::weak_ptr<Object> publishedAfterArray;
std::shared_ptr<Object> NewObject(unsigned role)
{
    auto value = std::make_shared<Object>(); value->exactRole = role;
    objects.emplace(value.get(), value); return value;
}
struct Driver {
    Driver* parent{};
    RizTweenSourceReferenceOriginV1 origin{};
    RizTweenSourceReferencesTicketV1 ticket{1, sizeof(ticket)};
};
thread_local Driver* driverTop{};
struct SavedNotice {
    const RizTweenSourceReferenceNoticeV1* actual{};
    std::uint64_t epoch{}, scope{}, delivery{};
};
SavedNotice lastNotice{}, outerNotice{}, workerNotice{};
struct ObserverCpp { std::uint64_t payload; };
struct OriginalCpp { std::uint64_t payload; };
struct CppUnwind { ~CppUnwind() { ++cppUnwinds; } };

RizTweenSourceReferencesInfoV1 Info()
{
    RizTweenSourceReferencesInfoV1 result{1, sizeof(result)};
    CHECK(RizTweenSourceReferences_GetInfo(&result) == 0);
    CHECK(result.abiVersion == 1 && result.byteSize == 104 && result.scopeCapacity == 128);
    return result;
}
unsigned ReadSaved(const SavedNotice& saved)
{
    RizTweenSourceReferenceNoticeV1 notice{1, sizeof(notice)};
    RizTweenSourceReferenceOriginV1 origin{1, sizeof(origin)};
    std::array<RizTweenSourceBorrowedReferenceV1, 16> references{};
    return RizTweenSourceReferences_ReadCurrentNotice(saved.actual, saved.epoch, saved.scope, saved.delivery,
        &notice, &origin, references.data(), static_cast<unsigned>(references.size()));
}
DWORD WINAPI WorkerRead(void* pointer)
{
    const auto* saved = static_cast<const SavedNotice*>(pointer);
    return ReadSaved(*saved);
}
RizTweenSourceReferenceOriginV1 Origin(unsigned site = RizTweenScheduledPushFront, unsigned operation = RizTweenSourceUpdate,
    std::uint64_t parent = 0, unsigned depth = 1)
{
    // These scalar PCs are owned fixture identity anchors, not game addresses.
    static unsigned char source[22]{}, returns[22]{};
    return {1, sizeof(RizTweenSourceReferenceOriginV1), operation, GetCurrentThreadId(), depth, site,
        site == RizTweenTailDeadDecision ? RizTweenSourceDecision : RizTweenSourceBefore, AllRoles,
        SourceEpoch, ++operationSequence, parent, ++transaction, source + site, returns + site};
}
void Begin(Driver& driver, const RizTweenSourceReferenceOriginV1& origin)
{
    driver.origin = origin; driver.parent = driverTop; driverTop = &driver;
    CHECK(RizTweenSourceReferencesBeginFromSource(&driver.origin, &driver.ticket) == 0);
    CHECK(driver.ticket.scopeId > 0 && driver.ticket.threadId == GetCurrentThreadId()
        && driver.ticket.sourceEpoch == SourceEpoch && driver.ticket.operationSequence == origin.operationSequence
        && driver.ticket.transaction == origin.transaction);
}
unsigned Deliver(Driver& driver, unsigned phase, const RizTweenSourceBorrowedReferenceV1* references = nullptr, unsigned count = 0)
{
    CHECK(driverTop == &driver);
    return RizTweenSourceReferencesDeliverFromSource(&driver.ticket, phase, &driver.origin, references, count);
}
void End(Driver& driver, bool returned, unsigned expected)
{
    CHECK(driverTop == &driver);
    CHECK(RizTweenSourceReferencesEndFromSource(&driver.ticket, returned) == expected);
    driverTop = driver.parent;
}
void RecordIndependentRoots(const RizTweenSourceReferenceNoticeV1& notice)
{
    const auto first = rootCount;
    // Root every direct input before checking any object's exact role/metadata.
    for (unsigned i = 0; i < notice.referenceCount; ++i) {
        const auto& reference = notice.references[i];
        if (!reference.value) continue;
        CHECK(rootCount < roots.size()); if (rootCount >= roots.size()) throw ObserverCpp{Payload};
        auto& root = roots[rootCount++];
        root.attempted = true; root.scope = notice.scopeId; root.identity = reference.value; root.role = reference.role;
        const auto found = objects.find(reference.value); // Identity lookup reads no object metadata.
        if (found == objects.end()) throw ObserverCpp{Payload};
        root.independent = found->second.lock();
        root.recorded = static_cast<bool>(root.independent);
        CHECK(root.recorded); if (!root.recorded) throw ObserverCpp{Payload};
        ++rootsRecorded;
    }
    if (failureArmed && (std::strcmp(mode, "cpp") == 0 || std::strcmp(mode, "seh") == 0)) {
        failureArmed = false;
        if (std::strcmp(mode, "cpp") == 0) { CppUnwind unwind; throw ObserverCpp{Payload}; }
        if (std::strcmp(mode, "seh") == 0) {
            const ULONG_PTR parameters[]{static_cast<ULONG_PTR>(Payload)};
            RaiseException(ObserverSeh, 0, 1, parameters);
        }
    }
    for (unsigned i = first; i < rootCount; ++i) {
        const auto& root = roots[i]; CHECK(root.attempted && root.recorded && root.independent);
        CHECK(root.independent.get() == root.identity);
        const unsigned expected = root.role == RizTweenReferencePrior ? RizTweenReferenceTween : root.role;
        CHECK(root.independent->exactRole == expected); ++metadataReads;
    }
}
void EmptyPair(unsigned site = RizTweenPlayingExecuteFront)
{
    Driver driver;
    const auto kind = site <= RizTweenUpdateOnStartAppend ? RizTweenSourceUpdate
        : site <= RizTweenPlayOnComplete ? RizTweenSourcePlay : RizTweenSourceExecute;
    Begin(driver, Origin(site, kind, driverTop ? driverTop->origin.operationSequence : 0, driverTop ? driverTop->origin.depth + 1 : 1));
    const RizTweenSourceBorrowedReferenceV1 before{tween.get(), RizTweenReferenceTween, -1};
    CHECK(Deliver(driver, RizTweenReferences, &before, 1) == 0);
    CHECK(Deliver(driver, RizTweenReferencesBeforeCommitted) == 0);
    if (driver.origin.stage != RizTweenSourceDecision) {
        driver.origin.stage = RizTweenSourceAfter;
        const RizTweenSourceBorrowedReferenceV1 after{tween.get(), RizTweenReferenceTween, -1};
        CHECK(Deliver(driver, RizTweenReferences, &after, 1) == 0);
        CHECK(Deliver(driver, RizTweenReferencesAfterCommitted) == 0);
    }
    End(driver, true, 0);
}
void NestedCallback()
{
    CHECK(driverTop != nullptr);
    Driver nested;
    Begin(nested, Origin(RizTweenPlayOnStart, RizTweenSourcePlay, driverTop->origin.operationSequence, driverTop->origin.depth + 1));
    RizTweenSourceBorrowedReferenceV1 reference{tween.get(), RizTweenReferenceTween, -1};
    CHECK(Deliver(nested, RizTweenReferences, &reference, 1) == 0);
    CHECK(Deliver(nested, RizTweenReferencesBeforeCommitted) == 0);
    nested.origin.stage = RizTweenSourceAfter;
    const RizTweenSourceBorrowedReferenceV1 after{tween.get(), RizTweenReferenceTween, -1};
    CHECK(Deliver(nested, RizTweenReferences, &after, 1) == 0);
    CHECK(Deliver(nested, RizTweenReferencesAfterCommitted) == 0);
    End(nested, true, 0); ++nestedCalls;
}
std::uint32_t __cdecl Notify(const RizTweenSourceReferenceNoticeV1* actual, void* user)
{
    CHECK(user == &controlledMetadataBound && controlledMetadataBound);
    CHECK(driverTop != nullptr);
    RizTweenSourceReferenceNoticeV1 notice{1, sizeof(notice)};
    RizTweenSourceReferenceOriginV1 origin{1, sizeof(origin)};
    std::array<RizTweenSourceBorrowedReferenceV1, 16> references{};
    CHECK(actual != nullptr); if (!actual) return 5;
    CHECK(RizTweenSourceReferences_ReadCurrentNotice(actual, actual->observerEpoch, actual->scopeId, actual->deliverySequence,
        &notice, &origin, references.data(), static_cast<unsigned>(references.size())) == 0);
    CHECK(notice.origin == &origin && (!notice.referenceCount || notice.references == references.data()));
    CHECK(driverTop && driverTop->origin.operationSequence == origin.operationSequence
        && driverTop->origin.parentOperationSequence == origin.parentOperationSequence
        && driverTop->origin.transaction == origin.transaction && origin.threadId == GetCurrentThreadId());
    CHECK(notice.scopeId == driverTop->ticket.scopeId && notice.observerEpoch == driverTop->ticket.observerEpoch);
    ++callbacks;
    const auto activity = Info(); // Proves callback is outside the native gate.
    CHECK(activity.callbackInFlight > 0 && activity.activeScopes > 0 && activity.retainedScopes > 0);
    if (activity.callbackInFlight > maximumCallbacks) maximumCallbacks = activity.callbackInFlight;
    lastNotice = {actual, notice.observerEpoch, notice.scopeId, notice.deliverySequence};

    // A DTO with identical values has no current native ADDRESS identity.
    auto dto = *actual;
    CHECK(RizTweenSourceReferences_ReadCurrentNotice(&dto, notice.observerEpoch, notice.scopeId, notice.deliverySequence,
        &notice, &origin, references.data(), static_cast<unsigned>(references.size())) == 3);
    CHECK(RizTweenSourceReferences_ReadCurrentNotice(actual, notice.observerEpoch + 1, notice.scopeId, notice.deliverySequence,
        &notice, &origin, references.data(), static_cast<unsigned>(references.size())) == 3);
    if (outerNotice.actual && outerNotice.scope != notice.scopeId) CHECK(ReadSaved(outerNotice) == 3);

    if (workerArmed) {
        workerArmed = false; workerNotice = lastNotice;
        const auto worker = CreateThread(nullptr, 0, WorkerRead, &workerNotice, 0, nullptr);
        CHECK(worker != nullptr);
        if (worker) {
            CHECK(WaitForSingleObject(worker, 5000) == WAIT_OBJECT_0);
            DWORD result{}; CHECK(GetExitCodeThread(worker, &result)); CHECK(result == 3); CloseHandle(worker); ++workerRejections;
        }
    }
    if (notice.phase == RizTweenReferences) {
        ++referencesNotices;
        RecordIndependentRoots(notice);
        if (nestedArmed) {
            nestedArmed = false; outerNotice = lastNotice;
            NestedCallback(); CHECK(ReadSaved(outerNotice) == 0); outerNotice = {};
        }
        if (failureArmed && std::strcmp(mode, "nonzero") == 0) { failureArmed = false; return 27; }
    } else if (notice.phase == RizTweenReferencesBeforeCommitted) ++beforeNotices;
    else if (notice.phase == RizTweenReferencesAfterCommitted) ++afterNotices;
    else { CHECK(notice.phase == RizTweenReferencesAbnormal); ++abnormalNotices; }
    return 0;
}
unsigned ControlledAttach()
{
    // Deliberately controlled fixture premise. Production must instead use the
    // actual FirstInstall Ready plus official metadata gate in its own helper.
    if (!controlledMetadataBound) return 2;
    const RizTweenSourceReferencesObserverV1 descriptor{1, sizeof(descriptor), Notify, &controlledMetadataBound};
    return RizTweenSourceReferencesAttach(SourceEpoch, &descriptor);
}
void Setup()
{
    auto info = Info(); CHECK(info.attached == 0 && info.unknown == 0);
    CHECK(ControlledAttach() == 2 && Info().attached == 0);
    auto origin = Origin(); RizTweenSourceReferencesTicketV1 early{1, sizeof(early)};
    CHECK(RizTweenSourceReferencesBeginFromSource(&origin, &early) == 2);
    RizTweenSourceReferencesObserverV1 invalid{1, sizeof(invalid)};
    CHECK(RizTweenSourceReferencesAttach(SourceEpoch, &invalid) == 1);
    controlledMetadataBound = true;
    CHECK(ControlledAttach() == 0 && ControlledAttach() == 2);
    info = Info(); CHECK(info.attached == 1 && info.sourceEpoch == SourceEpoch && info.observerEpoch != 0);
    manager = NewObject(RizTweenReferenceManager); tween = NewObject(RizTweenReferenceTween);
    container = NewObject(RizTweenReferenceContainer); arrayBefore = NewObject(RizTweenReferenceArray); eventObject = NewObject(RizTweenReferenceEvent);
}
void PairWithOwnedReferences(bool expectObserverFailure)
{
    Driver driver; Begin(driver, Origin());
    const RizTweenSourceBorrowedReferenceV1 input[]{
        {manager.get(), RizTweenReferenceManager, -1}, {tween.get(), RizTweenReferenceTween, 1},
        {container.get(), RizTweenReferenceContainer, -1}, {arrayBefore.get(), RizTweenReferenceArray, -1},
        {tween.get(), RizTweenReferencePrior, 0}, {eventObject.get(), RizTweenReferenceEvent, -1},
        {nullptr, RizTweenReferencePrior, 2}};
    void* const returnBefore = *static_cast<void**>(_AddressOfReturnAddress());
    CHECK(Deliver(driver, RizTweenReferences, input, static_cast<unsigned>(std::size(input))) == (expectObserverFailure ? 5u : 0u));
    CHECK(Deliver(driver, RizTweenReferencesBeforeCommitted) == 0);
    // Actual owned original side effect lies outside the observer catch boundary.
    ++originalCalls; ++container->mutations; ++originalEffects;
    auto grown = NewObject(RizTweenReferenceArray); publishedAfterArray = grown;
    driver.origin.stage = RizTweenSourceAfter;
    const RizTweenSourceBorrowedReferenceV1 output{grown.get(), RizTweenReferenceArray, -1};
    CHECK(Deliver(driver, RizTweenReferences, &output, 1) == 0);
    grown.reset(); CHECK(!publishedAfterArray.expired()); // Independent publisher root really retains it.
    CHECK(Deliver(driver, RizTweenReferencesAfterCommitted) == 0);
    End(driver, true, expectObserverFailure ? 5u : 0u);
    CHECK(*static_cast<void**>(_AddressOfReturnAddress()) == returnBefore); ++returnChecks;
    CHECK(ReadSaved(lastNotice) == 3); // Callback address is stale after real return.
}
void OriginalCppCase()
{
    Driver driver; Begin(driver, Origin());
    try {
        const RizTweenSourceBorrowedReferenceV1 reference{tween.get(), RizTweenReferenceTween, -1};
        CHECK(Deliver(driver, RizTweenReferences, &reference, 1) == 0);
        CHECK(Deliver(driver, RizTweenReferencesBeforeCommitted) == 0);
        ++originalCalls; ++originalEffects;
        throw OriginalCpp{Payload};
    } catch (const OriginalCpp& error) {
        End(driver, false, 5); CHECK(error.payload == Payload); return;
    }
}
void RaiseOriginal()
{
    const ULONG_PTR parameters[]{static_cast<ULONG_PTR>(Payload)};
    ++originalCalls; ++originalEffects; RaiseException(OriginalSeh, 0, 1, parameters);
}
void OriginalSehWindow(Driver* driver)
{
    __try {
        const RizTweenSourceBorrowedReferenceV1 reference{tween.get(), RizTweenReferenceTween, -1};
        CHECK(Deliver(*driver, RizTweenReferences, &reference, 1) == 0);
        CHECK(Deliver(*driver, RizTweenReferencesBeforeCommitted) == 0);
        RaiseOriginal();
    } __finally {
        End(*driver, false, 5);
    }
}
LONG OriginalFilter(EXCEPTION_POINTERS* error)
{
    CHECK(error->ExceptionRecord->ExceptionCode == OriginalSeh);
    CHECK(error->ExceptionRecord->NumberParameters == 1 && error->ExceptionRecord->ExceptionInformation[0] == Payload);
    return error->ExceptionRecord->ExceptionCode == OriginalSeh ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH;
}
void OriginalSehCase(Driver* driver)
{
    __try { OriginalSehWindow(driver); }
    __except (OriginalFilter(GetExceptionInformation())) { CHECK(driverTop == nullptr); }
}
void CapacityCase()
{
    std::array<Driver, 128> held{};
    for (auto& value : held) Begin(value, Origin());
    Driver overflow; overflow.origin = Origin();
    CHECK(RizTweenSourceReferencesBeginFromSource(&overflow.origin, &overflow.ticket) == 5);
    CHECK(Info().activeScopes == 128 && Info().retainedScopes == 128 && Info().unknown == 1);
    // Unknown never suppresses captured Abnormal responsibility.
    for (unsigned i = static_cast<unsigned>(held.size()); i > 0; --i) End(held[i - 1], false, 5);
    CHECK(abnormalNotices == held.size());
}
}

int main(int argc, char** argv)
{
    mode = argc == 2 ? argv[1] : "happy";
    const bool happy = std::strcmp(mode, "happy") == 0;
    const bool callbackFailure = std::strcmp(mode, "cpp") == 0 || std::strcmp(mode, "seh") == 0 || std::strcmp(mode, "nonzero") == 0;
    const bool originalCpp = std::strcmp(mode, "original-cpp") == 0, originalSeh = std::strcmp(mode, "original-seh") == 0;
    const bool capacity = std::strcmp(mode, "capacity") == 0, unpaired = std::strcmp(mode, "unpaired") == 0;
    const bool empty = std::strcmp(mode, "empty") == 0;
    if (argc > 2 || !(happy || callbackFailure || originalCpp || originalSeh || capacity || unpaired || empty)) {
        std::fprintf(stderr, "Usage: TweenSourceReferencesFixture.exe [happy|cpp|seh|nonzero|original-cpp|original-seh|capacity|unpaired|empty]\n"); return 2;
    }
    Setup();
    if (happy) {
        nestedArmed = true; workerArmed = true; PairWithOwnedReferences(false);
        EmptyPair(RizTweenTailDeadDecision);
        for (unsigned i = 0; i < 260; ++i) EmptyPair();
        CHECK(nestedCalls == 1 && maximumCallbacks >= 2 && workerRejections == 1 && rootsRecorded == metadataReads);
    } else if (callbackFailure) {
        failureArmed = true; PairWithOwnedReferences(true);
        CHECK(beforeNotices == 1 && afterNotices == 1 && originalCalls == 1 && originalEffects == 1 && !publishedAfterArray.expired());
        if (std::strcmp(mode, "cpp") == 0) CHECK(cppUnwinds == 1);
        const auto retained = Info(); CHECK(retained.unknown == 1 && retained.retainedScopes == 1 && retained.callbackFaults == 1);
        EmptyPair(); // A previous failure does not suppress later actual duties.
    } else if (originalCpp) OriginalCppCase();
    else if (originalSeh) { Driver driver; Begin(driver, Origin()); OriginalSehCase(&driver); }
    else if (capacity) CapacityCase();
    else if (unpaired) {
        Driver driver; Begin(driver, Origin());
        const RizTweenSourceBorrowedReferenceV1 reference{tween.get(), RizTweenReferenceTween, -1};
        CHECK(Deliver(driver, RizTweenReferences, &reference, 1) == 0);
        CHECK(Deliver(driver, RizTweenReferencesBeforeCommitted) == 0);
        driver.origin.stage = RizTweenSourceAfter;
        const RizTweenSourceBorrowedReferenceV1 missingAfter{nullptr, RizTweenReferenceArray, -1};
        CHECK(Deliver(driver, RizTweenReferences, &missingAfter, 1) == 0);
        CHECK(Deliver(driver, RizTweenReferencesAfterCommitted) == 4);
        End(driver, true, 5); CHECK(Info().retainedScopes == 1);
    }
    else {
        Driver driver; Begin(driver, Origin());
        const RizTweenSourceBorrowedReferenceV1 missing{nullptr, RizTweenReferenceManager, -1};
        CHECK(Deliver(driver, RizTweenReferences, &missing, 1) == 0);
        CHECK(Deliver(driver, RizTweenReferencesBeforeCommitted) == 4);
        driver.origin.stage = RizTweenSourceAfter;
        CHECK(Deliver(driver, RizTweenReferencesAfterCommitted) == 4);
        End(driver, true, 5); CHECK(Info().retainedScopes == 1);
    }
    const auto final = Info();
    CHECK(final.activeScopes == 0 && final.callbackInFlight == 0 && driverTop == nullptr);
    CHECK(final.deliveryAttempts == callbacks && final.deliveriesCompleted + final.callbackFaults == final.deliveryAttempts);
    CHECK(final.scopesCompleted + final.retainedScopes == final.scopesBegun && final.scopesEnded == final.scopesBegun);
    if (happy) CHECK(final.unknown == 0 && final.retainedScopes == 0 && final.scopesCompleted > final.scopeCapacity);
    else CHECK(final.unknown == 1 && final.retainedScopes > 0);
    if (originalCpp || originalSeh) CHECK(originalCalls == 1 && originalEffects == 1 && abnormalNotices == 1 && afterNotices == 0);
    for (unsigned i = 0; i < rootCount; ++i) CHECK(roots[i].attempted && roots[i].recorded && roots[i].independent);
    std::printf("{\"kind\":\"owned-native-tween-source-references-fixture\",\"mode\":\"%s\",\"passed\":%s,\"checks\":%llu,\"failures\":%llu,"
        "\"callbacks\":%llu,\"callbackAttempts\":%llu,\"callbacksCompleted\":%llu,\"callbackFaults\":%llu,\"rootsRecorded\":%llu,\"metadataReads\":%llu,"
        "\"beforeNotices\":%llu,\"afterNotices\":%llu,\"abnormalNotices\":%llu,\"originalCalls\":%llu,\"originalEffects\":%llu,"
        "\"nestedCalls\":%llu,\"workerRejections\":%llu,\"returnChecks\":%llu,\"scopesBegun\":%llu,\"scopesCompleted\":%llu,\"retainedScopes\":%u,"
        "\"unknown\":%u,\"controlledOwnedRoots\":true,\"gameCodeExecuted\":false,\"officialMetadataVerified\":false,\"actualPublisherReadyVerified\":false,\"hardwareCetVerified\":false}\n",
        mode, failures ? "false" : "true", checks, failures, callbacks, final.deliveryAttempts, final.deliveriesCompleted, final.callbackFaults,
        rootsRecorded, metadataReads, beforeNotices, afterNotices, abnormalNotices, originalCalls, originalEffects,
        nestedCalls, workerRejections, returnChecks, final.scopesBegun, final.scopesCompleted, final.retainedScopes, final.unknown);
    return failures ? 1 : 0;
}
