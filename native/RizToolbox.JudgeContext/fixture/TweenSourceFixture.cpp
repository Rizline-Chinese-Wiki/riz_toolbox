#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <memory>
#include <new>
#include <unordered_map>
#include <utility>
#include "../MidContext.h"
#include "../TweenSourceContext.h"
#include "../TweenSourceProfile.h"
#include "../TweenSourceReferences.h"
#include "../TweenSourceContext.References.h"
#include "../TweenRetirementContext.h"

#if !defined(RIZ_TWEEN_SOURCE_FIXTURE)
#error Build this independent owned executable with RIZ_TWEEN_SOURCE_FIXTURE.
#endif

// This executable owns every object, array, list, stack, continuation and CALL
// below. The only production prerequisite substituted by the fixture macro is
// official metadata/admission validation. It links the actual journal and ASM
// entry; it never loads GameAssembly or calls/patches a real game function.
namespace {
namespace P = RizTweenSourceProfile;
constexpr unsigned BindingCount = 43;
constexpr std::uint64_t Canary = 0xAABBCCDDEEFF1122ULL;
std::uint64_t checks{}, failures{}, machineCalls{}, sourcePairs{}, modeledEffects{};
std::array<bool, BindingCount> covered{};
std::uint32_t classTween{}, classManager{}, classArray{}, classCollection{}, classForeign{}, cachedField{};

struct Tween {
    const void* klass{&classTween}; void* monitor{}; void* cached{this};
    std::array<unsigned char, 0x88 - 24> fields{};
    void* onStart{}; void* onComplete{};
    bool fixtureSealed{}; // Deliberately irrelevant to the source dead policy.
};
struct Array {
    const void* klass{&classArray}; void* monitor{}; void* bounds{};
    std::uint64_t length{8}; std::array<void*, 8> values{};
};
struct Collection {
    const void* klass{&classCollection}; void* monitor{}; Array* array{};
    std::int32_t count{}, version{};
};
struct Manager {
    const void* klass{&classManager}; void* monitor{}; std::uint64_t other[2]{};
    Array* playing{}; Collection* scheduled{}; Collection* ready{};
    std::int32_t maximum{8}, working{};
};
struct BarrierState { std::uint64_t calls{}, observedStores{}, mismatches{}; } barriers;
struct InvocationResult {
    RizEventMidFrameV1 before{}, after{};
    std::uintptr_t selector{}, realReturnPc{};
    std::uint64_t route{}, home[3]{}, tail[2]{};
};
static_assert(offsetof(Tween, cached) == 16 && offsetof(Tween, onStart) == 0x88 && offsetof(Tween, onComplete) == 0x90);
static_assert(offsetof(Array, length) == 24 && offsetof(Array, values) == 32);
static_assert(offsetof(Collection, array) == 16 && offsetof(Collection, count) == 24 && offsetof(Collection, version) == 28);
static_assert(offsetof(Manager, playing) == 32 && offsetof(Manager, scheduled) == 40
    && offsetof(Manager, ready) == 48 && offsetof(Manager, working) == 60);

void Check(bool value, const char* expression, int line)
{
    ++checks;
    if (!value) { ++failures; std::fprintf(stderr, "FAILED tween source fixture line %d: %s\n", line, expression); }
}
#define CHECK(expression) Check(!!(expression), #expression, __LINE__)
RizTweenRetirementInfoV1 RetirementInfo()
{
    RizTweenRetirementInfoV1 value{1,sizeof(value)};
    CHECK(RizTweenRetirement_GetInfo(&value)==0);return value;
}
std::uintptr_t Address(const void* value) { return reinterpret_cast<std::uintptr_t>(value); }
template<class T> std::uintptr_t Address(T* value) { return reinterpret_cast<std::uintptr_t>(value); }
unsigned Binding(unsigned site, unsigned stage)
{
    CHECK(site < P::SiteCount);
    if (site == RizTweenTailDeadDecision) { CHECK(stage == RizTweenSourceDecision); return 26; }
    CHECK(stage <= RizTweenSourceAfter); return site * 2 - (site > RizTweenTailDeadDecision ? 1u : 0u) + stage;
}
RizTweenSourceInfoV1 Info()
{
    RizTweenSourceInfoV1 result{1, sizeof(result)};
    CHECK(RizTweenSourceContext_GetInfo(&result) == 0);
    CHECK(result.abiVersion == 1 && result.byteSize == 96);
    return result;
}
void CheckFirstFault(RizTweenSourceFault cause, unsigned site = RizTweenSourceSiteCount, unsigned stage = RizTweenSourceBefore)
{
    const auto detail = RizTweenSourceFirstFaultDetail();
    CHECK((detail >> 24) == 1 && ((detail >> 16) & 0xff) == static_cast<unsigned>(cause));
    const bool located = site < RizTweenSourceSiteCount;
    CHECK((detail & 0xff) == (located ? site + 1 : 0) && ((detail >> 8) & 0xff) == (located ? stage + 1 : 0));
}
RizTweenSourceRecordV1 Record(std::uint64_t sequence, std::uint32_t expectedStatus = 0)
{
    RizTweenSourceRecordV1 result{1, sizeof(result)};
    CHECK(RizTweenSourceContext_ReadRecord(Info().epoch, sequence, &result) == expectedStatus);
    CHECK(result.abiVersion == 1 && result.byteSize == 192 && result.eventSequence == sequence);
    return result;
}
RizTweenSourceOperationTicketV1 Enter(unsigned operation, void* receiver, void* manager, std::uint64_t sequence)
{
    const auto before=RetirementInfo().reservations;
    RizTweenSourceOperationTicketV1 ticket{1, sizeof(ticket)};
    CHECK(RizTweenSourceOperationEnter(operation, receiver, manager, sequence, &ticket) == 0);
    CHECK(ticket.operation == operation && ticket.operationSequence == sequence && ticket.receiver == receiver
        && ticket.threadId == GetCurrentThreadId() && ticket.depth > 0);
    CHECK(RetirementInfo().reservations==before+1);
    return ticket;
}
void Exit(const RizTweenSourceOperationTicketV1& ticket)
{const auto before=RetirementInfo().reservations;CHECK(before>0&&RizTweenSourceOperationExit(&ticket, true)==0);
 CHECK(RetirementInfo().reservations==before-1);}
void Version(Collection& value)
{
    const auto bits = static_cast<std::uint32_t>(value.version) + 1u;
    std::memcpy(&value.version, &bits, sizeof(bits));
}
}

extern "C" {
void __cdecl RtTweenSourceFixture_Invoke(unsigned binding, const RizEventMidFrameV1* input, InvocationResult* result);
void __cdecl RtTweenSourceFixture_StoreBarrier(void** slot, void* value, BarrierState* state);
extern const void* const RtTweenSourceFixture_Returns[44];
extern char RtTweenSourceFixture_DeadReadyA, RtTweenSourceFixture_DeadReadyB, RtTweenSourceFixture_DeadTail;
extern void RizTweenSourceContext_Entry();

bool RizTweenSourceFixture_Metadata(const RizTweenSourceMetadataV1* metadata)
{
    return metadata && metadata->exactTweenClass == &classTween && metadata->exactManagerClass == &classManager
        && metadata->cachedPointerField == &cachedField;
}
void __cdecl RtTweenSourceFixture_Barrier(void** slot, void* value, BarrierState* state)
{
    ++state->calls;
    if (slot && *slot == value) ++state->observedStores;
    else ++state->mismatches;
}
void __cdecl RtTweenSourceFixture_Capture(InvocationResult* result,
    const RizEventMidFrameV1* before, const RizEventMidFrameV1* after, const std::uint64_t* sourceStack)
{
    result->before = *before; result->after = *after;
    result->selector = static_cast<std::uintptr_t>(sourceStack[0]);
    result->realReturnPc = static_cast<std::uintptr_t>(sourceStack[0x490 / 8]);
    result->route = sourceStack[0x480 / 8];
    for (unsigned i = 0; i < 3; ++i) result->home[i] = sourceStack[i + 1];
    result->tail[0] = sourceStack[0x498 / 8]; result->tail[1] = sourceStack[0x4a0 / 8];
}
}

namespace {
struct Owned {
    std::uint64_t eventA[4]{}, eventB[4]{};
    Tween a{}, b{}, dead{};
    Array readyArray{}, playingArray{}, scheduledArray{}, grownReadyArray{};
    Collection ready{}, scheduled{};
    Manager manager{};
    alignas(8) unsigned char activation[0x90]{};
    Owned()
    {
        a.onStart = eventA; a.onComplete = eventB; a.fixtureSealed = true;
        b.onStart = eventB; b.onComplete = eventA;
        dead.onStart = eventA; dead.onComplete = eventB; dead.cached = nullptr;
        readyArray.length = 2;
        ready.array = &readyArray; scheduled.array = &scheduledArray;
        manager.playing = &playingArray; manager.ready = &ready; manager.scheduled = &scheduled;
        playingArray.values[0] = &a;
        std::memcpy(activation + 0x67, &playingArray.values[0], sizeof(void*));
        std::memcpy(activation + 0x77, &playingArray.values[0], sizeof(void*));
    }
};
RizEventMidFrameV1 Input(Owned& owned)
{
    RizEventMidFrameV1 result{1, sizeof(result)};
    // Arithmetic flags and DF are set deliberately. User-mode POPF retains the
    // system-controlled flags; comparison uses the actual pre-CALL snapshot.
    result.rflags = (__readeflags() & ~0xCD5ULL) | 0xCD5ULL;
    for (unsigned i = 0; i < 16; ++i) {
        result.gpr[i] = 0x1111111101234567ULL * (i + 1);
        result.xmm[i] = {0x0123456789ABCDEFULL + i, 0xFEDCBA9876543210ULL - i};
    }
    result.gpr[RizEventMidR14] = Address(&owned.manager);
    result.mxcsr = 0x3F80; result.fpcsr = 0x077F;
    return result;
}
void SameMachine(const RizEventMidFrameV1& left, const RizEventMidFrameV1& right)
{
    CHECK(left.abiVersion == 1 && right.abiVersion == 1 && left.byteSize == sizeof(left) && right.byteSize == sizeof(right));
    CHECK(left.returnPc == right.returnPc && left.entryRsp == right.entryRsp && left.entryRsp + 8 == left.gpr[RizEventMidRsp]);
    CHECK((left.entryRsp & 15u) == 8u && left.rflags == right.rflags && (left.rflags & 0x400u) != 0);
    CHECK(left.mxcsr == right.mxcsr && left.fpcsr == right.fpcsr);
    CHECK(left.reserved0 == 0 && left.reserved1 == 0 && right.reserved0 == 0 && right.reserved1 == 0);
    for (unsigned i = 0; i < 16; ++i) {
        CHECK(left.gpr[i] == right.gpr[i]);
        CHECK(std::memcmp(&left.xmm[i], &right.xmm[i], sizeof(left.xmm[i])) == 0);
    }
}
InvocationResult Call(unsigned binding, const RizEventMidFrameV1& input, std::uintptr_t selector = 0, std::uint64_t route = 0)
{
    CHECK(binding < 44);
    InvocationResult result{}; RtTweenSourceFixture_Invoke(binding, &input, &result); ++machineCalls;
    SameMachine(result.before, result.after);
    CHECK(result.before.returnPc == Address(RtTweenSourceFixture_Returns[binding]) && result.realReturnPc == result.before.returnPc);
    CHECK(result.selector == selector && result.route == route);
    CHECK(result.before.mxcsr == input.mxcsr && result.before.fpcsr == input.fpcsr);
    for (unsigned i = 0; i < 16; ++i) {
        if (i != RizEventMidRsp) CHECK(result.before.gpr[i] == input.gpr[i]);
        CHECK(std::memcmp(&result.before.xmm[i], &input.xmm[i], sizeof(input.xmm[i])) == 0);
    }
    for (const auto cell : result.home) CHECK(cell == Canary);
    for (const auto cell : result.tail) CHECK(cell == Canary);
    if (binding < BindingCount) covered[binding] = true;
    return result;
}
RizTweenSourceRecordV1 Source(unsigned site, unsigned stage, const RizEventMidFrameV1& input,
    std::uintptr_t selector = 0, std::uint64_t route = 0)
{
    const auto before = Info();const auto reservedBefore=RetirementInfo().reservations;
    Call(Binding(site, stage), input, selector, route); const auto after = Info();
    const auto reservedAfter=RetirementInfo().reservations;
    CHECK(stage==RizTweenSourceBefore?reservedAfter==reservedBefore+1
        :stage==RizTweenSourceAfter?reservedBefore>0&&reservedAfter==reservedBefore-1:reservedAfter==reservedBefore);
    CHECK(after.events == before.events + 1 && !after.unknown);
    auto record = Record(after.events);
    CHECK(record.site == site && record.stage == stage && record.sourcePc != nullptr && record.relayReturnPc == RtTweenSourceFixture_Returns[Binding(site, stage)]);
    CHECK((record.flags & (RizTweenSourceKnown | RizTweenSourceParentMatched)) == (RizTweenSourceKnown | RizTweenSourceParentMatched));
    CHECK((record.flags & (RizTweenSourceUnknown | RizTweenSourceReadFault | RizTweenSourceAbnormal)) == 0);
    return record;
}
void Pair(const RizTweenSourceRecordV1& before, const RizTweenSourceRecordV1& after)
{
    ++sourcePairs;
    CHECK(before.stage == RizTweenSourceBefore && after.stage == RizTweenSourceAfter
        && before.site == after.site && before.transaction == after.transaction && before.occurrence == after.occurrence
        && before.updateSequence == after.updateSequence && before.parentUpdateSequence == after.parentUpdateSequence
        && before.operation == after.operation && before.threadId == after.threadId && before.depth == after.depth
        && before.manager == after.manager && before.container == after.container && before.sourceIndex == after.sourceIndex
        && before.destinationIndex == after.destinationIndex && before.eventSequence < after.eventSequence);
    CHECK((after.flags & RizTweenSourcePaired) != 0);
}
void Store(void** slot, void* value) { RtTweenSourceFixture_StoreBarrier(slot, value, &barriers); }
bool TryPop(Collection& stack, void*& result)
{
    if (!stack.count) { result = nullptr; return false; }
    result = stack.array->values[static_cast<unsigned>(stack.count - 1)];
    Store(&stack.array->values[static_cast<unsigned>(--stack.count)], nullptr); Version(stack); return true;
}
void Push(Collection& stack, void* value, Array* replacement = nullptr)
{
    if (replacement) {
        CHECK(static_cast<std::uint64_t>(stack.count) == stack.array->length && replacement->length > stack.array->length);
        for (int i = 0; i < stack.count; ++i) Store(&replacement->values[static_cast<unsigned>(i)], stack.array->values[static_cast<unsigned>(i)]);
        stack.array = replacement;
    }
    CHECK(stack.count >= 0 && static_cast<std::uint64_t>(stack.count) < stack.array->length);
    Store(&stack.array->values[static_cast<unsigned>(stack.count++)], value); Version(stack);
}
void Set(Collection& list, int index, void* value) { Store(&list.array->values[static_cast<unsigned>(index)], value); Version(list); }
void Remove(Collection& list, int index)
{
    CHECK(index >= 0 && index < list.count);
    for (int i = index; i < list.count - 1; ++i) Store(&list.array->values[static_cast<unsigned>(i)], list.array->values[static_cast<unsigned>(i + 1)]);
    Store(&list.array->values[static_cast<unsigned>(--list.count)], nullptr); Version(list);
}
void Configure(Owned& owned)
{
    // These are actual owned ASM labels, with fixed RVA-relative spacing. The
    // synthetic base is only an identity for private Configure; it is not a
    // loaded PE, an accepted production publisher or game metadata proof.
    const auto base = Address(&RtTweenSourceFixture_DeadReadyA) - P::Sites[0].deadContinuationRva;
    CHECK(base + P::Sites[1].deadContinuationRva == Address(&RtTweenSourceFixture_DeadReadyB));
    CHECK(base + P::Sites[13].deadContinuationRva == Address(&RtTweenSourceFixture_DeadTail));
    std::array<RizTweenSourceSiteBindingV1, BindingCount> bindings{};
    for (unsigned site = 0; site < P::SiteCount; ++site) {
        const unsigned first = site == RizTweenTailDeadDecision ? RizTweenSourceDecision : RizTweenSourceBefore;
        const unsigned last = site == RizTweenTailDeadDecision ? RizTweenSourceDecision : RizTweenSourceAfter;
        for (unsigned stage = first; stage <= last; ++stage) {
            const auto index = Binding(site, stage);
            const bool redirect = (site <= 1 && stage == RizTweenSourceAfter) || site == RizTweenTailDeadDecision;
            bindings[index] = {site, stage, reinterpret_cast<void*>(base + P::Sites[site].beginRva),
                RtTweenSourceFixture_Returns[index], redirect ? reinterpret_cast<void*>(base + P::Sites[site].deadContinuationRva) : nullptr};
        }
    }
    RizTweenSourceConfigV1 config{1, sizeof(config), reinterpret_cast<void*>(base), nullptr, nullptr, BindingCount, 0, bindings.data()};
    auto invalid = config; invalid.reservedTweenClass = &classTween; CHECK(RizTweenSourceConfigure(&invalid) == 1);
    invalid = config; invalid.siteBindingCount = BindingCount - 1; CHECK(RizTweenSourceConfigure(&invalid) == 1);
    const auto savedPc = bindings[1].relayReturnPc; bindings[1].relayReturnPc = bindings[0].relayReturnPc;
    CHECK(RizTweenSourceConfigure(&config) == 1); bindings[1].relayReturnPc = savedPc;
    CHECK(RizTweenSourceConfigure(&config) == 0 && RizTweenSourceConfigure(&config) == 4);
    auto configured = Info(); CHECK(configured.configured == 1 && configured.metadataBound == 0 && configured.bindingCount == BindingCount);
    CHECK(RizTweenSourceContext_GetEntry() == reinterpret_cast<void*>(&RizTweenSourceContext_Entry));
    RizTweenSourceOperationTicketV1 early{1, sizeof(early)};
    CHECK(RizTweenSourceOperationEnter(RizTweenSourceUpdate, &owned.manager, &owned.manager, 99, &early) == 2);
    Call(Binding(RizTweenPlayingExecuteFront, RizTweenSourceBefore), Input(owned));
    CHECK(Info().events == 0 && Info().unknown == 0);
    RizTweenSourceMetadataV1 metadata{1, sizeof(metadata), &classTween, &classManager, &cachedField};
    auto wrong = metadata; wrong.exactManagerClass = &classForeign;
    CHECK(RizTweenSourceBindMetadata(&wrong) == 1);
    CHECK(RizTweenSourceBindMetadata(&metadata) == 0 && RizTweenSourceBindMetadata(&metadata) == 4);
    CHECK(RizTweenSourceBindMetadata(&wrong) == 1); // A different tuple is never status-4 adopted.
    wrong = metadata; wrong.cachedPointerField = &classForeign; CHECK(RizTweenSourceBindMetadata(&wrong) == 1);
    CHECK(Info().metadataBound == 1 && !Info().unknown);
}
std::pair<RizTweenSourceRecordV1, RizTweenSourceRecordV1> Pop(Owned& owned, unsigned site)
{
    auto input = Input(owned); void* output = reinterpret_cast<void*>(Canary);
    input.gpr[RizEventMidRcx] = Address(&owned.ready); input.gpr[RizEventMidRdx] = Address(&output);
    const auto before = Source(site, RizTweenSourceBefore, input);
    const bool succeeded = TryPop(owned.ready, output); input.gpr[RizEventMidRax] = succeeded ? 1 : 0;
    const bool dead = succeeded && output == &owned.dead;
    const auto selector = dead ? Address(site == 0 ? &RtTweenSourceFixture_DeadReadyA : &RtTweenSourceFixture_DeadReadyB) : 0;
    const auto after = Source(site, RizTweenSourceAfter, input, selector, dead ? site + 1 : 0);
    Pair(before, after);
    CHECK(((after.flags & RizTweenSourcePopSucceeded) != 0) == succeeded);
    CHECK(((after.flags & RizTweenSourceExactDead) != 0) == dead);
    return {before, after};
}
void PlainEvent(Owned& owned, unsigned site, const RizTweenSourceOperationTicketV1& operation)
{
    auto input = Input(owned); input.gpr[RizEventMidRsi] = Address(operation.receiver);
    auto* tween = static_cast<Tween*>(operation.receiver);
    input.gpr[RizEventMidRcx] = Address(site == RizTweenPlayOnStart ? tween->onStart : tween->onComplete);
    const auto before = Source(site, RizTweenSourceBefore, input); ++modeledEffects;
    const auto after = Source(site, RizTweenSourceAfter, input); Pair(before, after);
    CHECK(before.operation == operation.operation && before.updateSequence == operation.operationSequence
        && before.parentUpdateSequence == operation.parentOperationSequence && before.tween == operation.receiver);
}
void Insert(Owned& owned, unsigned site, int destination, Tween* tween, std::uint64_t poppedTransaction)
{
    auto input = Input(owned); input.gpr[RizEventMidRbx] = Address(&owned.playingArray);
    input.gpr[RizEventMidRsi] = static_cast<std::uint64_t>(destination); input.gpr[RizEventMidRdi] = Address(tween);
    const auto before = Source(site, RizTweenSourceBefore, input);
    Store(&owned.playingArray.values[static_cast<unsigned>(destination)], tween);
    const auto after = Source(site, RizTweenSourceAfter, input); Pair(before, after);
    CHECK(after.occurrence == poppedTransaction && after.destinationIndex == destination && after.tween == tween);
}
void ExecuteSource(Owned& owned, unsigned site, int source, std::uint64_t executeSequence = 0)
{
    auto input = Input(owned); input.gpr[RizEventMidRcx] = Address(&owned.manager);
    input.gpr[RizEventMidRdx] = Address(owned.playingArray.values[static_cast<unsigned>(source)]);
    // Deliberately make ESI and R15D differ. Site 15 must use the old tail R15D.
    input.gpr[RizEventMidRsi] = site == RizTweenPlayingExecuteExisting ? 0 : static_cast<std::uint64_t>(source);
    input.gpr[RizEventMidR15] = site == RizTweenPlayingExecuteExisting ? static_cast<std::uint64_t>(source) : 7;
    const auto before = Source(site, RizTweenSourceBefore, input);
    CHECK(before.container == &owned.manager && before.arrayBefore == &owned.playingArray && before.sourceIndex == source);
    ++modeledEffects;
    if (executeSequence) {
        auto execute = Enter(RizTweenSourceExecute, reinterpret_cast<void*>(input.gpr[RizEventMidRdx]), &owned.manager, executeSequence);
        PlainEvent(owned, RizTweenExecuteOnComplete, execute); Exit(execute);
    }
    const auto after = Source(site, RizTweenSourceAfter, input); Pair(before, after);
}
void Happy(Owned& owned)
{
    owned.playingArray.values = {}; owned.playingArray.values[2] = &owned.dead;
    owned.readyArray.values[0] = &owned.a; owned.readyArray.values[1] = &owned.a;
    owned.ready.count = 2; owned.ready.version = (std::numeric_limits<std::int32_t>::max)() - 1;
    auto update = Enter(RizTweenSourceUpdate, &owned.manager, &owned.manager, 1);
    const auto firstPop = Pop(owned, RizTweenReadyPopFront);
    CHECK(firstPop.second.sourceIndex == 1 && firstPop.second.tween == &owned.a);
    auto event = Input(owned); event.gpr[RizEventMidRbp] = Address(owned.activation);
    event.gpr[RizEventMidRcx] = Address(owned.a.onStart);
    const auto eventBefore = Source(RizTweenUpdateOnStartFront, RizTweenSourceBefore, event);
    auto play = Enter(RizTweenSourcePlay, &owned.a, nullptr, 2);
    CHECK(play.depth == 2 && play.parentOperationSequence == update.operationSequence);
    auto nested = Enter(RizTweenSourceExecute, &owned.a, &owned.manager, 3);
    CHECK(nested.depth == 3 && nested.parentOperationSequence == play.operationSequence);
    PlainEvent(owned, RizTweenExecuteOnComplete, nested); Exit(nested);
    PlainEvent(owned, RizTweenPlayOnStart, play); PlainEvent(owned, RizTweenPlayOnComplete, play); Exit(play);
    const auto eventAfter = Source(RizTweenUpdateOnStartFront, RizTweenSourceAfter, event); Pair(eventBefore, eventAfter);
    CHECK(eventBefore.occurrence == firstPop.second.transaction);
    Insert(owned, RizTweenPlayingInsertFront, 0, &owned.a, firstPop.second.transaction);
    ExecuteSource(owned, RizTweenPlayingExecuteFront, 0, 4);
    const auto secondPop = Pop(owned, RizTweenReadyPopAppend);
    CHECK(secondPop.second.sourceIndex == 0 && secondPop.second.transaction != firstPop.second.transaction
        && secondPop.second.tween == firstPop.second.tween && owned.ready.version == (std::numeric_limits<std::int32_t>::min)());
    const auto secondEventBefore = Source(RizTweenUpdateOnStartAppend, RizTweenSourceBefore, event);
    ++modeledEffects;
    const auto secondEventAfter = Source(RizTweenUpdateOnStartAppend, RizTweenSourceAfter, event); Pair(secondEventBefore, secondEventAfter);
    CHECK(secondEventBefore.occurrence == secondPop.second.transaction);
    Insert(owned, RizTweenPlayingInsertAppend, 1, &owned.a, secondPop.second.transaction);
    ExecuteSource(owned, RizTweenPlayingExecuteAppend, 1);
    ExecuteSource(owned, RizTweenPlayingExecuteExisting, 1);
    CHECK(owned.a.fixtureSealed && owned.a.cached != nullptr && modeledEffects > 0);

    auto move = Input(owned); move.gpr[RizEventMidRbx] = Address(&owned.playingArray);
    move.gpr[RizEventMidRsi] = 0; move.gpr[RizEventMidR15] = 1; move.gpr[RizEventMidRdi] = Address(&owned.a);
    const auto moveBefore = Source(RizTweenPlayingMoveWrite, RizTweenSourceBefore, move);
    Store(&owned.playingArray.values[0], &owned.a);
    const auto moveAfter = Source(RizTweenPlayingMoveWrite, RizTweenSourceAfter, move); Pair(moveBefore, moveAfter);
    auto clear = Input(owned); clear.gpr[RizEventMidRcx] = 1; clear.gpr[RizEventMidRdx] = Address(&owned.playingArray);
    clear.gpr[RizEventMidRsi] = 1; clear.gpr[RizEventMidR15] = 0;
    const auto clearBefore = Source(RizTweenPlayingMoveClear, RizTweenSourceBefore, clear);
    Store(&owned.playingArray.values[1], nullptr);
    const auto clearAfter = Source(RizTweenPlayingMoveClear, RizTweenSourceAfter, clear); Pair(clearBefore, clearAfter);
    CHECK(clearAfter.sourceIndex == 1 && owned.playingArray.values[0] == &owned.a && owned.playingArray.values[1] == nullptr);

    auto tail = Input(owned); tail.gpr[RizEventMidRbx] = Address(&owned.dead); tail.gpr[RizEventMidR15] = 2;
    tail.gpr[RizEventMidRax] = 0;
    const auto decision = Source(RizTweenTailDeadDecision, RizTweenSourceDecision, tail, Address(&RtTweenSourceFixture_DeadTail), 13);
    CHECK(decision.tween == &owned.dead && decision.sourceIndex == 2 && (decision.flags & RizTweenSourceExactDead));
    auto tailClear = Input(owned); tailClear.gpr[RizEventMidRcx] = Address(&owned.playingArray);
    tailClear.gpr[RizEventMidRax] = 2; tailClear.gpr[RizEventMidR15] = 2; tailClear.gpr[RizEventMidR13] = 0;
    const auto tailBefore = Source(RizTweenPlayingTailClear, RizTweenSourceBefore, tailClear);
    Store(&owned.playingArray.values[2], nullptr);
    const auto tailAfter = Source(RizTweenPlayingTailClear, RizTweenSourceAfter, tailClear); Pair(tailBefore, tailAfter);
    CHECK(owned.playingArray.values[2] == nullptr && tailAfter.prior == &owned.dead);
    tail.gpr[RizEventMidRbx] = Address(&owned.a); tail.gpr[RizEventMidR15] = 0; tail.gpr[RizEventMidRax] = 1;
    CHECK((Source(RizTweenTailDeadDecision, RizTweenSourceDecision, tail).flags & RizTweenSourceRedirected) == 0);
    tail.gpr[RizEventMidRbx] = 0; tail.gpr[RizEventMidR15] = 3; tail.gpr[RizEventMidRax] = 0;
    CHECK((Source(RizTweenTailDeadDecision, RizTweenSourceDecision, tail).flags & RizTweenSourceRedirected) == 0);

    // Real successful dead pops exit their transient before either onStart or
    // playing insertion; the two selector branches return to different loops.
    for (unsigned site = 0; site < 2; ++site) {
        owned.ready.array->values[0] = &owned.dead; owned.ready.count = 1;
        const auto effectsBefore = modeledEffects; const auto pop = Pop(owned, site);
        CHECK(pop.second.flags & RizTweenSourceRedirected); CHECK(modeledEffects == effectsBefore);
        Pop(owned, site); // Actual empty TryPop: no stale output/slot mutation.
    }
    // A non-dead activation rejected by the original initializer is proven by
    // the next actual pop in that lane. The old pop token must not survive it.
    owned.ready.array->values[0] = &owned.b; owned.ready.count = 1;
    Pop(owned, RizTweenReadyPopFront); Pop(owned, RizTweenReadyPopFront);

    owned.scheduledArray.values[0] = &owned.a; owned.scheduledArray.values[1] = &owned.a;
    owned.scheduledArray.values[2] = &owned.b; owned.scheduled.count = 3;
    auto push = Input(owned); push.gpr[RizEventMidRcx] = Address(&owned.ready); push.gpr[RizEventMidRdx] = Address(&owned.a);
    push.gpr[RizEventMidRsi] = 0; push.gpr[RizEventMidRbx] = 1;
    const auto pushBefore = Source(RizTweenScheduledPushFront, RizTweenSourceBefore, push);
    Push(owned.ready, &owned.a);
    const auto pushAfter = Source(RizTweenScheduledPushFront, RizTweenSourceAfter, push); Pair(pushBefore, pushAfter);
    CHECK(pushAfter.arrayBefore == &owned.readyArray && pushAfter.arrayAfter == &owned.readyArray && pushAfter.sourceIndex == 0);
    const auto pushTailBefore = Source(RizTweenScheduledPushTail, RizTweenSourceBefore, push);
    Push(owned.ready, &owned.a);
    const auto pushTailAfter = Source(RizTweenScheduledPushTail, RizTweenSourceAfter, push); Pair(pushTailBefore, pushTailAfter);
    CHECK(pushTailAfter.sourceIndex == 1 && pushTailAfter.destinationIndex == 1 && owned.ready.count == 2);
    push.gpr[RizEventMidRsi] = 2; push.gpr[RizEventMidRdx] = Address(&owned.b);
    const auto resizeBefore = Source(RizTweenScheduledPushFront, RizTweenSourceBefore, push);
    Push(owned.ready, &owned.b, &owned.grownReadyArray);
    const auto resizeAfter = Source(RizTweenScheduledPushFront, RizTweenSourceAfter, push); Pair(resizeBefore, resizeAfter);
    CHECK(resizeAfter.arrayBefore == &owned.readyArray && resizeAfter.arrayAfter == &owned.grownReadyArray
        && resizeAfter.capacityBefore == 2 && resizeAfter.capacityAfter == 8 && resizeAfter.destinationIndex == 2 && owned.ready.count == 3);
    auto set = Input(owned); set.gpr[RizEventMidRcx] = Address(&owned.scheduled); set.gpr[RizEventMidRdx] = 0;
    set.gpr[RizEventMidRbx] = 2; set.gpr[RizEventMidR8] = Address(&owned.b);
    const auto setBefore = Source(RizTweenScheduledSetItem, RizTweenSourceBefore, set); Set(owned.scheduled, 0, &owned.b);
    const auto setAfter = Source(RizTweenScheduledSetItem, RizTweenSourceAfter, set); Pair(setBefore, setAfter);
    CHECK(setAfter.sourceIndex == 2 && setAfter.destinationIndex == 0 && owned.scheduled.array->values[0] == &owned.b);
    for (unsigned site = RizTweenScheduledRemoveAdvance; site <= RizTweenScheduledRemoveCurrent; ++site) {
        const int index = site == RizTweenScheduledRemoveCurrent ? 0 : 1;
        auto remove = Input(owned); remove.gpr[RizEventMidRcx] = Address(&owned.scheduled); remove.gpr[RizEventMidRdx] = static_cast<std::uint64_t>(index);
        const auto before = Source(site, RizTweenSourceBefore, remove); Remove(owned.scheduled, index);
        const auto after = Source(site, RizTweenSourceAfter, remove); Pair(before, after);
        CHECK(after.sourceIndex == index && after.countAfter == after.countBefore - 1);
    }
    CHECK(owned.scheduled.count == 0 && owned.scheduled.array->values[0] == nullptr && owned.scheduled.array->values[1] == nullptr);
    Exit(update);

    // Direct Execute has its own actual manager even without an Update parent.
    auto direct = Enter(RizTweenSourceExecute, &owned.a, &owned.manager, 5);
    CHECK(direct.parentOperationSequence == 0); PlainEvent(owned, RizTweenExecuteOnComplete, direct); Exit(direct);
    auto directPlay = Enter(RizTweenSourcePlay, &owned.a, nullptr, 6);
    PlainEvent(owned, RizTweenPlayOnStart, directPlay); Exit(directPlay);

    // More than a full 8192-record window of paired, normal traffic must remain
    // usable. A lagging reader gets an explicit expired result, never row reuse.
    update = Enter(RizTweenSourceUpdate, &owned.manager, &owned.manager, 7);
    const auto first = Info().events + 1;
    for (unsigned i = 0; i < 4097 && !Info().unknown; ++i) ExecuteSource(owned, RizTweenPlayingExecuteFront, 0);
    Exit(update);
    const auto final = Info();
    CHECK(final.events > 8192 && final.retainedRecords == 8192 && final.pendingTransactions == 0
        && final.activeUpdates == 0 && final.unknown == 0 && final.faults == 0 && final.redirects == 3);
    CHECK(RizTweenSourceFirstFaultDetail() == 0);
    RizTweenSourceRecordV1 expired{1, sizeof(expired)}; expired.transaction = Canary; const auto original = expired;
    CHECK(RizTweenSourceContext_ReadRecord(final.epoch, first, &expired) == 3);
    CHECK(std::memcmp(&expired, &original, sizeof(expired)) == 0);
    CHECK(Record(final.events).eventSequence == final.events);
    for (const bool seen : covered) CHECK(seen);
    CHECK(barriers.calls > 0 && barriers.calls == barriers.observedStores && barriers.mismatches == 0);
}
void ExpectedUnknown(Owned& owned, const char* mode)
{
    const auto update = Enter(RizTweenSourceUpdate, &owned.manager, &owned.manager, 1);
    auto input = Input(owned); input.gpr[RizEventMidRcx] = Address(&owned.manager);
    input.gpr[RizEventMidRdx] = Address(&owned.a); input.gpr[RizEventMidRsi] = 0;
    const auto beforeBinding = Binding(RizTweenPlayingExecuteFront, RizTweenSourceBefore);
    if (std::strcmp(mode, "capacity") == 0) {
        owned.playingArray.length = 4097; Call(beforeBinding, input);
        CHECK(Info().pendingTransactions == 1 && Info().events == 1);
        CheckFirstFault(RizTweenSourceFault::SourceBeforeMismatch, RizTweenPlayingExecuteFront);
    } else if (std::strcmp(mode, "broken-pair") == 0) {
        Call(Binding(RizTweenPlayingExecuteFront, RizTweenSourceAfter), input);
        CHECK(Info().events == 0 && Info().pendingTransactions == 0);
        CheckFirstFault(RizTweenSourceFault::SourceAfterMissingBefore, RizTweenPlayingExecuteFront, RizTweenSourceAfter);
    } else if (std::strcmp(mode, "wrong-parent") == 0) {
        Manager other = owned.manager; input.gpr[RizEventMidR14] = Address(&other); Call(beforeBinding, input);
        CHECK(Info().pendingTransactions == 1);
        CheckFirstFault(RizTweenSourceFault::SourceBeforeMismatch, RizTweenPlayingExecuteFront);
    } else if (std::strcmp(mode, "foreign-tail") == 0) {
        owned.b.klass = &classForeign; owned.playingArray.values[2] = &owned.b;
        input.gpr[RizEventMidRbx] = Address(&owned.b); input.gpr[RizEventMidR15] = 2;
        Call(Binding(RizTweenTailDeadDecision, RizTweenSourceDecision), input);
        CHECK(owned.playingArray.values[2] == &owned.b && Info().redirects == 0 && Info().events == 1);
        CheckFirstFault(RizTweenSourceFault::SourceBeforeMismatch, RizTweenTailDeadDecision, RizTweenSourceDecision);
    } else if (std::strcmp(mode, "abnormal-exit") == 0) {
        Source(RizTweenPlayingExecuteFront, RizTweenSourceBefore, input);
        CHECK(RizTweenSourceFirstFaultDetail() == 0);
    } else if (std::strcmp(mode, "unbound") == 0) {
        Call(43, input); CHECK(Info().events == 0);
        CheckFirstFault(RizTweenSourceFault::UnboundSourceReturn);
    } else if (std::strcmp(mode, "unfinished-window") == 0) {
        const auto outer = Source(RizTweenPlayingExecuteFront, RizTweenSourceBefore, input);
        for (unsigned i = 0; i < 4095 && !Info().unknown; ++i) ExecuteSource(owned, RizTweenPlayingExecuteFront, 0);
        Source(RizTweenPlayingExecuteFront, RizTweenSourceBefore, input);
        CHECK(Info().events == 8192 && Info().pendingTransactions == 2);
        Call(Binding(RizTweenPlayingExecuteFront, RizTweenSourceAfter), input);
        const auto retained = Record(outer.eventSequence, 5);
        CHECK(retained.transaction == outer.transaction && retained.eventSequence == outer.eventSequence);
        CHECK(Info().events == 8192 && Info().pendingTransactions == 2 && Info().retainedRecords == 8192);
        CheckFirstFault(RizTweenSourceFault::JournalPendingEviction, RizTweenPlayingExecuteFront);
    } else {
        CHECK(false); // main validates all mode names before setup.
    }
    const auto beforeExit = RizTweenSourceFirstFaultDetail();
    CHECK(RizTweenSourceOperationExit(&update, false) == 5);
    const auto state = Info(); CHECK(state.unknown == 1 && state.faults > 0 && state.activeUpdates == 1);
    if (beforeExit) CHECK(RizTweenSourceFirstFaultDetail() == beforeExit);
    if (std::strcmp(mode, "abnormal-exit") == 0) {
        CheckFirstFault(RizTweenSourceFault::OperationAbnormalExit);
        CHECK(state.pendingTransactions == 1 && state.events == 2);
        CHECK((Record(state.events, 5).flags & (RizTweenSourceUnknown | RizTweenSourceAbnormal))
            == (RizTweenSourceUnknown | RizTweenSourceAbnormal));
    }
}

// Joint source-core / references integration. The protected owned replacement
// array would make the real native Capture fail before publication. Its owned
// observer records an independent shared_ptr root first, then restores readable
// access and checks controlled metadata. This proves ordering in the actual
// SourceContext path, without claiming IL2CPP handles or a production Ready gate.
struct ReferenceRoot {
    void* identity{}; unsigned kind{}; bool attempted{}, recorded{};
    std::shared_ptr<void> independent;
};
struct ReferenceHarness {
    std::unordered_map<void*, std::pair<std::weak_ptr<void>, unsigned>> registry;
    std::array<ReferenceRoot, 64> roots{};
    unsigned rootCount{}, callbacks{}, beforeNotices{}, afterNotices{}, abnormalNotices{}, metadataReads{};
    unsigned activeDepth{}, maximumDepth{}, nestedOperations{}, releasedProtectedArrays{};
    bool controlledMetadata{}, failBefore{}, recurse{}, didFail{}, protectedReleased{}, rootFirst=true;
    bool mutateBeforeCommitted{},beforeCommittedMutationChecked{};
    bool mutateAfterCommitted{},afterCommittedMutationChecked{},originalPopTokenPreserved{},originalRaxPreserved{};
    void* protectedArray{};
    std::array<RizTweenSourceOperationTicketV1, 16> operations{};
    unsigned operationDepth{};
    Owned* owned{};
} referenceHarness;
template<class T, class U> void RegisterReference(const std::shared_ptr<T>& owner, U* pointer, unsigned kind)
{
    std::shared_ptr<void> alias(owner, static_cast<void*>(pointer));
    referenceHarness.registry.emplace(static_cast<void*>(pointer), std::make_pair(std::weak_ptr<void>(alias), kind));
}
RizTweenSourceOperationTicketV1 ReferenceEnter(unsigned operation, void* receiver, void* manager, std::uint64_t sequence)
{
    const auto ticket = Enter(operation, receiver, manager, sequence);
    CHECK(referenceHarness.operationDepth < referenceHarness.operations.size());
    referenceHarness.operations[referenceHarness.operationDepth++] = ticket; return ticket;
}
void ReferenceExit(const RizTweenSourceOperationTicketV1& ticket, bool returned, unsigned expected)
{
    CHECK(referenceHarness.operationDepth && referenceHarness.operations[referenceHarness.operationDepth - 1].operationSequence == ticket.operationSequence);
    CHECK(RizTweenSourceOperationPrepareExitReferences(&ticket) == expected);
    CHECK(RizTweenSourceOperationExit(&ticket, returned) == expected);
    --referenceHarness.operationDepth;
}
void ReferenceNestedEvent()
{
    auto& owned = *referenceHarness.owned;
    const auto play = ReferenceEnter(RizTweenSourcePlay, &owned.a, nullptr, 102);
    auto input = Input(owned); input.gpr[RizEventMidRsi] = Address(&owned.a); input.gpr[RizEventMidRcx] = Address(owned.a.onStart);
    Call(Binding(RizTweenPlayOnStart, RizTweenSourceBefore), input);
    Call(Binding(RizTweenPlayOnStart, RizTweenSourceAfter), input);
    ReferenceExit(play, true, 0); ++referenceHarness.nestedOperations;
}
std::uint32_t __cdecl ReferenceIntegrationNotify(const RizTweenSourceReferenceNoticeV1* actual, void* user)
{
    auto& state = referenceHarness;
    CHECK(user == &state && state.controlledMetadata && actual != nullptr && state.operationDepth > 0);
    if (!actual || !state.operationDepth) return 5;
    RizTweenSourceReferenceNoticeV1 notice{1, sizeof(notice)};
    RizTweenSourceReferenceOriginV1 origin{1, sizeof(origin)};
    std::array<RizTweenSourceBorrowedReferenceV1, 16> references{};
    CHECK(RizTweenSourceReferences_ReadCurrentNotice(actual, actual->observerEpoch, actual->scopeId, actual->deliverySequence,
        &notice, &origin, references.data(), static_cast<unsigned>(references.size())) == 0);
    const auto& parent = state.operations[state.operationDepth - 1];
    CHECK(origin.operationSequence == parent.operationSequence && origin.parentOperationSequence == parent.parentOperationSequence
        && origin.operation == parent.operation && origin.threadId == parent.threadId && origin.depth == parent.depth);
    CHECK(origin.relayReturnPc == RtTweenSourceFixture_Returns[Binding(origin.site,
        origin.site == RizTweenTailDeadDecision ? RizTweenSourceDecision : RizTweenSourceBefore)]);
    ++state.callbacks; ++state.activeDepth;
    if (state.activeDepth > state.maximumDepth) state.maximumDepth = state.activeDepth;
    // This is the real SourceContext lock, in addition to the references gate.
    // A callback under either native lock would deadlock this owned execution.
    const auto source = Info(); CHECK(source.activeUpdates > 0 && source.pendingTransactions > 0);
    // Terminal abnormal delivery may be flushed by the operation's finally
    // after its preserving relay returned; it still owns source+Pending+scope+
    // Notify. Normal source callbacks additionally own the actual relay.
    CHECK(RetirementInfo().reservations>=(notice.phase==RizTweenReferencesAbnormal?4u:5u));
    if (notice.phase == RizTweenReferences) {
        for (unsigned i = 0; i < notice.referenceCount; ++i) {
            const auto& reference = references[i]; if (!reference.value) continue;
            ReferenceRoot* root{};
            for (unsigned at = 0; at < state.rootCount; ++at) if (state.roots[at].identity == reference.value) { root = &state.roots[at]; break; }
            if (!root) {
                CHECK(state.rootCount < state.roots.size()); if (state.rootCount >= state.roots.size()) { --state.activeDepth; return 5; }
                root = &state.roots[state.rootCount++]; root->identity = reference.value;
                root->kind = reference.role == RizTweenReferencePrior ? RizTweenReferenceTween : reference.role;
                root->attempted = true; // Reserve responsibility before native/owned root adoption.
                const auto registered = state.registry.find(reference.value);
                CHECK(registered != state.registry.end());
                if (registered == state.registry.end()) { --state.activeDepth; return 5; }
                root->independent = registered->second.first.lock();
                root->recorded = static_cast<bool>(root->independent);
                CHECK(root->recorded && root->independent.get() == reference.value);
                if (!root->recorded) { --state.activeDepth; return 5; }
            }
        }
        // Every root is already recorded before any object role/class read.
        for (unsigned i = 0; i < notice.referenceCount; ++i) {
            const auto& reference = references[i]; if (!reference.value) continue;
            ReferenceRoot* root{};
            for (unsigned at = 0; at < state.rootCount; ++at) if (state.roots[at].identity == reference.value) { root = &state.roots[at]; break; }
            if (!root || !root->recorded || !root->independent) { state.rootFirst = false; --state.activeDepth; return 5; }
            if (reference.value == state.protectedArray && !state.protectedReleased) {
                DWORD previous{};
                CHECK(VirtualProtect(state.protectedArray, 4096, PAGE_READWRITE, &previous) && previous == PAGE_NOACCESS);
                state.protectedReleased = true; ++state.releasedProtectedArrays;
            }
            const auto registered = state.registry.find(reference.value);
            const auto kind = reference.role == RizTweenReferencePrior ? RizTweenReferenceTween : reference.role;
            CHECK(registered != state.registry.end() && registered->second.second == kind && root->kind == kind);
            if (kind == RizTweenReferenceManager) CHECK(*static_cast<const void**>(reference.value) == &classManager);
            else if (kind == RizTweenReferenceTween) CHECK(*static_cast<const void**>(reference.value) == &classTween);
            else if (kind == RizTweenReferenceArray) CHECK(*static_cast<const void**>(reference.value) == &classArray);
            else if (kind == RizTweenReferenceContainer) CHECK(*static_cast<const void**>(reference.value) == &classCollection);
            ++state.metadataReads;
        }
        if (state.recurse) {
            state.recurse = false; ReferenceNestedEvent();
            // Same-thread nested real source callbacks restore the outer TLS.
            RizTweenSourceReferenceNoticeV1 restored{1, sizeof(restored)};
            RizTweenSourceReferenceOriginV1 restoredOrigin{1, sizeof(restoredOrigin)};
            CHECK(RizTweenSourceReferences_ReadCurrentNotice(actual, notice.observerEpoch, notice.scopeId, notice.deliverySequence,
                &restored, &restoredOrigin, references.data(), static_cast<unsigned>(references.size())) == 0);
        }
        if (state.failBefore && origin.stage == RizTweenSourceBefore) {
            state.failBefore = false; state.didFail = true; --state.activeDepth; return 19;
        }
    } else if (notice.phase == RizTweenReferencesBeforeCommitted) {
        ++state.beforeNotices;
        if(state.mutateBeforeCommitted&&origin.site==RizTweenScheduledPushFront){
            state.mutateBeforeCommitted=false;
            // This real observer-side store is after the native Before row was
            // appended. Both objects are already independently rooted. The
            // original Push still runs, but its old prior/vector is now stale.
            Store(&state.owned->readyArray.values[0],&state.owned->b);
            state.beforeCommittedMutationChecked=true;
        }
    }
    else if (notice.phase == RizTweenReferencesAfterCommitted) {
        ++state.afterNotices;
        if(state.mutateAfterCommitted&&origin.site==RizTweenReadyPopFront){
            state.mutateAfterCommitted=false;
            CHECK(state.owned->dead.cached==nullptr);
            state.owned->dead.cached=&state.owned->manager;
            state.afterCommittedMutationChecked=true;
        }
    }
    else { CHECK(notice.phase == RizTweenReferencesAbnormal); ++state.abnormalNotices; }
    --state.activeDepth; return 0;
}
void ReferenceIntegration(const char* selectedMode)
{
    auto owner = std::make_shared<Owned>(); auto& owned = *owner; Configure(owned);
    auto& state = referenceHarness; state.owned = &owned;
    RegisterReference(owner, &owned.manager, RizTweenReferenceManager);
    RegisterReference(owner, &owned.ready, RizTweenReferenceContainer); RegisterReference(owner, &owned.scheduled, RizTweenReferenceContainer);
    RegisterReference(owner, &owned.readyArray, RizTweenReferenceArray); RegisterReference(owner, &owned.playingArray, RizTweenReferenceArray);
    RegisterReference(owner, &owned.scheduledArray, RizTweenReferenceArray); RegisterReference(owner, &owned.grownReadyArray, RizTweenReferenceArray);
    RegisterReference(owner, &owned.a, RizTweenReferenceTween); RegisterReference(owner, &owned.b, RizTweenReferenceTween);
    RegisterReference(owner, &owned.dead, RizTweenReferenceTween);
    RegisterReference(owner, owned.eventA, RizTweenReferenceEvent); RegisterReference(owner, owned.eventB, RizTweenReferenceEvent);
    const auto initial = Info(); CHECK(initial.configured && initial.metadataBound && !initial.unknown && initial.events == 0);
    state.controlledMetadata = true;
    const RizTweenSourceReferencesObserverV1 observer{1, sizeof(observer), ReferenceIntegrationNotify, &state};
    CHECK(RizTweenSourceReferencesAttach(initial.epoch, &observer) == 0);
    state.failBefore = std::strcmp(selectedMode, "references-fault") == 0;
    const bool abnormal = std::strcmp(selectedMode, "references-abnormal") == 0;
    const bool changed = std::strcmp(selectedMode,"references-before-changed") == 0;
    const bool afterChanged = std::strcmp(selectedMode,"references-after-changed") == 0;
    state.mutateBeforeCommitted=changed;
    state.mutateAfterCommitted=afterChanged;
    state.recurse = !state.failBefore && !abnormal && !changed && !afterChanged;
    owned.readyArray.values[0] = &owned.a; owned.readyArray.values[1] = &owned.a; owned.ready.count = 2;
    owned.scheduledArray.values[0] = &owned.b; owned.scheduled.count = 1;
    const auto update = ReferenceEnter(RizTweenSourceUpdate, &owned.manager, &owned.manager, 101);
    auto input = Input(owned); input.gpr[RizEventMidRcx] = Address(&owned.ready);
    input.gpr[RizEventMidRdx] = Address(&owned.b); input.gpr[RizEventMidRsi] = 0;
    if(afterChanged){
        owned.readyArray.values={};owned.readyArray.values[0]=&owned.dead;owned.ready.count=1;
        void* output{};input.gpr[RizEventMidRdx]=Address(&output);
        Call(Binding(RizTweenReadyPopFront,RizTweenSourceBefore),input);
        const auto before=Record(Info().events);
        const bool popped=TryPop(owned.ready,output);++modeledEffects;
        CHECK(popped&&output==&owned.dead&&owned.ready.count==0&&owned.readyArray.values[0]==nullptr);
        input.gpr[RizEventMidRax]=popped?1u:0u;
        const auto machine=Call(Binding(RizTweenReadyPopFront,RizTweenSourceAfter),input);
        const auto after=Record(Info().events,5);
        state.originalRaxPreserved=machine.before.gpr[RizEventMidRax]==1&&machine.after.gpr[RizEventMidRax]==1;
        CHECK(state.afterCommittedMutationChecked&&owned.dead.cached!=nullptr&&state.originalRaxPreserved
            &&machine.selector==0&&machine.route==0&&Info().redirects==0);
        CHECK(before.transaction==after.transaction&&after.rawResult==1&&after.tween==&owned.dead
            &&(after.flags&RizTweenSourcePopSucceeded)!=0
            &&(after.flags&(RizTweenSourceExactDead|RizTweenSourceRedirected))==0);
        const auto faultsBeforeExit=Info().faults;
        ReferenceExit(update,true,5);
        // With the active source stack already paired and no move pending, the
        // extra native Exit fault proves its original popped transaction is
        // still retained. A premature dead-discard would have erased it.
        state.originalPopTokenPreserved=output==&owned.dead&&Info().faults==faultsBeforeExit+1;
        CHECK(state.originalPopTokenPreserved&&state.beforeNotices==1&&state.afterNotices==1&&state.abnormalNotices==1);
    }else{
    Call(Binding(RizTweenScheduledPushFront, RizTweenSourceBefore), input);
    const auto before = Record(Info().events, state.didFail||changed ? 5u : 0u);
    CHECK(before.site == RizTweenScheduledPushFront && before.stage == RizTweenSourceBefore && state.beforeNotices >= 1);
    if (abnormal) {
        struct OriginalFailure { std::uint64_t payload; };
        try { ++modeledEffects; throw OriginalFailure{Canary}; }
        catch (const OriginalFailure& error) { CHECK(error.payload == Canary); ReferenceExit(update, false, 5); }
        CHECK(state.abnormalNotices == 1 && state.afterNotices == 0 && Info().pendingTransactions == 1);
    } else {
        void* const allocation = VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        CHECK(allocation != nullptr); if (!allocation) { ReferenceExit(update, false, 5); return; }
        auto grown = std::shared_ptr<Array>(new(allocation) Array(), [](Array* value) { value->~Array(); VirtualFree(value, 0, MEM_RELEASE); });
        RegisterReference(grown, grown.get(), RizTweenReferenceArray); std::weak_ptr<Array> weak = grown;
        Push(owned.ready, &owned.b, grown.get()); ++modeledEffects;
        state.protectedArray = grown.get(); DWORD previous{};
        CHECK(VirtualProtect(grown.get(), 4096, PAGE_NOACCESS, &previous));
        Call(Binding(RizTweenScheduledPushFront, RizTweenSourceAfter), input);
        const auto after = Record(Info().events, state.didFail||changed ? 5u : 0u);
        CHECK(after.site == RizTweenScheduledPushFront && after.stage == RizTweenSourceAfter && state.afterNotices >= 1);
        CHECK(state.protectedReleased && state.releasedProtectedArrays == 1 && state.rootFirst);
        if (!state.didFail&&!changed) {
            Pair(before, after);
            CHECK(after.arrayBefore == &owned.readyArray && after.arrayAfter == grown.get()
                && after.countBefore == 2 && after.countAfter == 3 && after.capacityBefore == 2 && after.capacityAfter == 8);
            CHECK(state.maximumDepth >= 2 && state.nestedOperations == 1);
        }
        grown.reset(); CHECK(!weak.expired()); // Newly published independent root is retained.
        ReferenceExit(update, true, state.didFail||changed ? 5u : 0u);
        CHECK(state.afterNotices >= 1 && state.abnormalNotices == (changed?1u:0u));
        if(changed){
            CHECK(state.beforeCommittedMutationChecked&&owned.ready.count==3&&owned.ready.array->values[0]==&owned.b
                &&owned.ready.array->values[1]==&owned.a&&owned.ready.array->values[2]==&owned.b&&modeledEffects==1);
        }
    }
    }
    RizTweenSourceReferencesInfoV1 info{1, sizeof(info)};
    CHECK(RizTweenSourceReferences_GetInfo(&info) == 0 && info.callbackInFlight == 0 && info.activeScopes == 0);
    CHECK(state.callbacks == info.deliveryAttempts && state.operationDepth == 0 && state.activeDepth == 0);
    CHECK(state.rootFirst && state.rootCount > 0 && state.metadataReads > 0);
    if (state.didFail || abnormal || changed || afterChanged) CHECK(info.unknown == 1 && info.retainedScopes == 1 && Info().unknown == 1 && Info().pendingTransactions == 1);
    else CHECK(info.unknown == 0 && info.retainedScopes == 0 && Info().unknown == 0 && Info().pendingTransactions == 0);
    if (state.didFail) CheckFirstFault(RizTweenSourceFault::ReferencesDeliveryRejected, RizTweenScheduledPushFront);
    else if (abnormal) CheckFirstFault(RizTweenSourceFault::OperationUnfinishedSource, RizTweenScheduledPushFront);
    else if (changed) CheckFirstFault(RizTweenSourceFault::SourceBeforeChanged, RizTweenScheduledPushFront);
    else if (afterChanged) CheckFirstFault(RizTweenSourceFault::SourceAfterChanged, RizTweenReadyPopFront, RizTweenSourceAfter);
    else CHECK(RizTweenSourceFirstFaultDetail() == 0);
    std::printf("{\"kind\":\"owned-joint-tween-source-references-fixture\",\"mode\":\"%s\",\"passed\":%s,\"checks\":%llu,\"failures\":%llu,"
        "\"machineCalls\":%llu,\"referenceCallbacks\":%u,\"referencesRooted\":%u,\"metadataReads\":%u,\"growthReadRestored\":%u,"
        "\"nestedOperations\":%u,\"maximumCallbackDepth\":%u,\"beforeNotices\":%u,\"afterNotices\":%u,\"abnormalNotices\":%u,"
        "\"retainedReferenceScopes\":%u,\"retainedSourceTransactions\":%llu,\"unknown\":%u,\"modeledEffects\":%llu,"
        "\"beforeCommittedMutationChecked\":%s,"
        "\"afterCommittedMutationChecked\":%s,\"originalPopTokenPreserved\":%s,\"originalRaxPreserved\":%s,\"redirects\":%llu,"
        "\"gameCodeExecuted\":false,\"gameRuntimeVerified\":false,\"officialMetadataVerified\":false,\"actualPublisherReadyVerified\":false}\n",
        selectedMode, failures ? "false" : "true", checks, failures, machineCalls, state.callbacks, state.rootCount, state.metadataReads,
        state.releasedProtectedArrays, state.nestedOperations, state.maximumDepth, state.beforeNotices, state.afterNotices, state.abnormalNotices,
        info.retainedScopes, Info().pendingTransactions, Info().unknown, modeledEffects,state.beforeCommittedMutationChecked?"true":"false",
        state.afterCommittedMutationChecked?"true":"false",state.originalPopTokenPreserved?"true":"false",state.originalRaxPreserved?"true":"false",Info().redirects);
}
}

int main(int argc, char** argv)
{
    const char* mode = argc == 2 ? argv[1] : "happy";
    const bool references = std::strcmp(mode, "references-grow") == 0 || std::strcmp(mode, "references-fault") == 0
        || std::strcmp(mode, "references-abnormal") == 0 || std::strcmp(mode,"references-before-changed") == 0
        || std::strcmp(mode,"references-after-changed") == 0;
    const bool happy = std::strcmp(mode, "happy") == 0;
    const bool known = happy || references || std::strcmp(mode, "capacity") == 0 || std::strcmp(mode, "broken-pair") == 0
        || std::strcmp(mode, "wrong-parent") == 0 || std::strcmp(mode, "foreign-tail") == 0
        || std::strcmp(mode, "abnormal-exit") == 0 || std::strcmp(mode, "unbound") == 0 || std::strcmp(mode, "unfinished-window") == 0;
    if (!known || argc > 2) {
        std::fprintf(stderr, "Usage: TweenSourceFixture.exe [happy|capacity|broken-pair|wrong-parent|foreign-tail|abnormal-exit|unbound|unfinished-window|references-grow|references-fault|references-abnormal|references-before-changed|references-after-changed]\n");
        return 2;
    }
    if (references) { ReferenceIntegration(mode); return failures ? 1 : 0; }
    Owned owned; Configure(owned);
    if (happy) Happy(owned); else ExpectedUnknown(owned, mode);
    const auto state = Info();
    unsigned coverage = 0; for (const bool seen : covered) if (seen) ++coverage;
    std::printf("{\"kind\":\"owned-native-tween-source-fixture\",\"mode\":\"%s\",\"passed\":%s,\"checks\":%llu,\"failures\":%llu,"
        "\"siteCount\":22,\"bindingCount\":43,\"coveredBindings\":%u,\"recordBytes\":192,\"infoBytes\":96,\"machineCalls\":%llu,"
        "\"pairedOperations\":%llu,\"modeledEffects\":%llu,\"modeledGcBarrierCalls\":%llu,\"events\":%llu,\"retainedRecords\":%llu,"
        "\"pendingTransactions\":%llu,\"unknown\":%u,\"faults\":%llu,\"gameCodeExecuted\":false,\"gameRuntimeVerified\":false,"
        "\"officialMetadataVerified\":false,\"fullStackAliasProof\":false}\n",
        mode, failures ? "false" : "true", checks, failures, coverage, machineCalls, sourcePairs, modeledEffects, barriers.calls,
        state.events, state.retainedRecords, state.pendingTransactions, state.unknown, state.faults);
    return failures ? 1 : 0;
}
