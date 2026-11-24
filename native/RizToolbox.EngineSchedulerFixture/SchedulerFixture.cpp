#include "SchedulerOwned.h"
#include <intrin.h>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <xmmintrin.h>
#include "../RizToolbox.JudgeContext/MidContext.h"
#include "../RizToolbox.JudgeContext/EngineSchedulerContext.h"
extern "C" {
extern unsigned char RtSchedulerFixture_DrainBeginSite, RtSchedulerFixture_DrainBeginContinuation;
extern unsigned char RtSchedulerFixture_DrainNoCallback, RtSchedulerFixture_DrainEndSite;
extern unsigned char RtSchedulerFixture_DrainEndContinuation, RtSchedulerFixture_DrainEnd;
__declspec(align(16)) unsigned char RtSchedulerFixture_XmmInput[16][16]{};
std::uint32_t RtSchedulerFixture_InputMxcsr = 0x3f80;
std::uint16_t RtSchedulerFixture_InputFpcsr = 0x077f;
std::uint64_t RtSchedulerFixture_TestFlags(const void*, std::uint64_t);
std::uint64_t RtSchedulerFixture_CmpFlags(const void*, std::uint64_t);
std::uint64_t RtSchedulerFixture_CallbackTarget(void*,void*,void*,void*);
}

// The transport/header adoption below is completed only against the native
// owner's issued ABI. Owned records and local validation state do not issue
// scheduler scope identities or native completion frames.
namespace {
constexpr std::uint64_t RegisterTag = 0xa1a2a3a4a5a6a7a8ULL;
constexpr std::uint64_t RemoveReturn = 0x823456781234abcdULL;
constexpr std::uint64_t ConsumeReturn = 0x934567892345bcdeULL;
constexpr std::uint64_t PumpReturn = 0xa456789a3456cdefULL;
constexpr std::uint64_t DrainReturn = 0xb56789ab4567def0ULL;
constexpr DWORD InputError = 0x12345678, OriginalError = 0x76543210;
constexpr DWORD OriginalException = 0xe04d5345, ObserverException = 0xe04d534f;
constexpr unsigned RowLimit = 16, CallLimit = 128;
using CarrierFunction = std::uint64_t(__cdecl*)(void*, void*, void*, void*);
using CleanupFunction = void(__cdecl*)(void*);
struct CppException { std::uint64_t seal; };
struct OwnedRow {
    SchedulerOwnedEntry* entry{};
    SchedulerOwnedSource source{};
    SchedulerOwnedManager* manager{};
    std::uint64_t localAllocationId{};
    unsigned registered{}, consumed{}, removed{}, callbackOpen{}, callbackDone{}, cleanupDone{}, freed{};
};
struct CallReceipt {
    RizEngineSchedulerFrameV1 frame{};
    RizEngineSchedulerCompletionV1 completion{};
    unsigned exited{}, lateUnobserved{};
};
struct SiteReceipt {
    std::uint64_t site{}, relay{}, returnPc{}, continuation{}, branchTarget{};
    unsigned span{}, codeBytes{};
    unsigned char original[8]{}, replacement[8]{}, code[64]{}, unwind[16]{};
    RUNTIME_FUNCTION function{};
};
struct FixtureReceipt {
    std::uint32_t version = 1, byteSize = sizeof(FixtureReceipt), checks{}, failures{};
    std::uint32_t registrations{}, consumes{}, removals{}, callbacks{}, cleanups{}, drains{}, pumps{}, gapChecks{};
    std::uint32_t actualFrees{}, addressReuses{}, fullEaxCases{}, repeatOrderChecks{}, originalSeh{}, originalCpp{};
    std::uint32_t workerCalls{}, unobservedCalls{}, observerFaults{}, rawReturnChecks{}, lastErrorChecks{}, reserved{};
    std::uint64_t firstEntry{}, reusedEntry{}, nextAllocationId{}, rawReturns[16]{};
    unsigned callCount{}, rowEventCount{}, statePairs{}, rowUnwindWalks{}, rowVirtualUnwinds{}, rowCanaries{};
    std::uint64_t imageBase{}, relayBase{}, rowEntry{}, sourceBegin{}, sourceEnd{};
    RUNTIME_FUNCTION sourceFunction{};
    SiteReceipt sites[2]{};
    RizEngineSchedulerInfoV1 information[6]{}, foreignInformation{};
    CallReceipt calls[CallLimit]{};
    RizEngineSchedulerRowFrameV1 rowEvents[RowLimit * 3]{};
    RizEventMidFrameV1 machines[RowLimit][4]{};
    OwnedRow rows[RowLimit]{};
} receipt;
SchedulerOwnedManager manager{}, foreignManager{};
__declspec(align(16)) unsigned char ownedPayload[2][0x70]{};
unsigned rowCount{};
const wchar_t* mode{};
CarrierFunction registerEntry{}, removeEntry{}, consumeEntry{}, pumpEntry{}, drainEntry{}, callbackEntry{}, foreignPumpEntry{};
void* preferredEntry{};
OwnedRow* currentCallbackRow{};
OwnedRow* currentDrainRow{};
bool unobserved{}, lateAttachDone{}, nestedForeignDone{};
HMODULE helper{};
HANDLE helperLock = INVALID_HANDLE_VALUE;
decltype(&RizEngineSchedulerContext_GetInfo) getInfo{};
decltype(&RizEngineSchedulerContext_Attach) attachObserver{};
RizEngineSchedulerObserverV1 observers[6]{};
void* rowEntry{};
unsigned char* relayBlock{};
PRUNTIME_FUNCTION relayFunctions{};
std::uint64_t drainRsp{}, drainReturnPc{};
DWORD mainThread{};
void AttachCallbackLate();
bool observerReentry{}, observerReentered{};
std::uint64_t __cdecl ForeignCallback(void*,void*,void*,void*);

void Check(bool value, const char* expression, int line)
{
    ++receipt.checks;
    if (!value) { ++receipt.failures; std::fprintf(stderr, "FAILED SchedulerFixture line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
bool Named(const wchar_t* name) { return std::wcscmp(mode, name) == 0; }
OwnedRow* Find(SchedulerOwnedEntry* entry)
{
    for (unsigned i = rowCount; i != 0; --i)
        if (receipt.rows[i - 1].entry == entry && !receipt.rows[i - 1].freed) return &receipt.rows[i - 1];
    return nullptr;
}
void Erase(OwnedRow* row)
{
    CHECK(row && row->manager && row->entry && !row->freed);
    if (!row || !row->manager) return;
    auto* owner = row->manager; unsigned index = RowLimit;
    for (unsigned i = 0; i != owner->count; ++i) if (owner->rows[i] == row->entry) { index = i; break; }
    CHECK(index != RowLimit && owner->count > 0);
    if (index == RowLimit) return;
    for (unsigned i = index; i + 1 < owner->count; ++i) owner->rows[i] = owner->rows[i + 1];
    --owner->count; owner->rows[owner->count] = nullptr;
    CHECK(VirtualFree(row->entry, 0, MEM_RELEASE)); row->freed = 1; ++receipt.actualFrees;
    MEMORY_BASIC_INFORMATION state{};
    CHECK(VirtualQuery(row->entry, &state, sizeof(state)) == sizeof(state) && state.State == MEM_FREE);
}
void __cdecl Cleanup(void* payload)
{
    auto* row = currentCallbackRow ? currentCallbackRow : currentDrainRow;
    CHECK(row && row->source.payload == payload && row->cleanupDone == 0);
    if (row) ++row->cleanupDone;
    ++receipt.cleanups;
}
SchedulerOwnedSource Source(unsigned index = 0)
{
    SchedulerOwnedSource source{};
    source.dueTimeBits = 0x400921fb54442d18ULL; source.frame = -1234567890123LL;
    source.intervalBits = 0x3f123456; source.repeat = Named(L"repeat") ? 1 : 0;
    source.payload = &ownedPayload[index]; source.callback = reinterpret_cast<void*>(&RtSchedulerFixture_CallbackTarget);
    source.cleanup = reinterpret_cast<void*>(&Cleanup); source.ownerId = 0x12345678;
    source.category = 0x40; source.creationTag = 0x87654321;
    if (Named(L"foreign-callback")) {
        source.payload = reinterpret_cast<void*>(1);
        source.callback = reinterpret_cast<void*>(&ForeignCallback);
    }
    return source;
}
std::uint64_t __cdecl ForeignCallback(void* owner,void* payload,void* arg2,void* arg3)
{
    CHECK(currentCallbackRow && currentCallbackRow->manager == owner && currentCallbackRow->source.payload == payload
        && arg2 == reinterpret_cast<void*>(RegisterTag) && arg3 == reinterpret_cast<void*>(RegisterTag));
    ++receipt.callbacks; if (currentCallbackRow) currentCallbackRow->callbackDone = 1;
    SetLastError(OriginalError); return 0xabcdef0100000001ULL;
}
OwnedRow* RegisterOne(SchedulerOwnedManager* owner, SchedulerOwnedSource* source)
{
    SchedulerOwnedEntry* output{};
    SetLastError(InputError);
    const auto result = registerEntry(owner->tree, &output, source, reinterpret_cast<void*>(RegisterTag));
    CHECK(result == reinterpret_cast<std::uint64_t>(&output)); ++receipt.rawReturnChecks;
    CHECK(GetLastError() == OriginalError); ++receipt.lastErrorChecks;
    auto* row = Find(output); CHECK(row && row->registered && row->source.payload == source->payload);
    return row;
}
void CallbackAndCleanup(SchedulerOwnedManager* owner, OwnedRow* row, bool conditional)
{
    currentCallbackRow = row;
    CHECK(row && row->consumed && !row->callbackOpen && !row->callbackDone);
    // The physical consume has returned, the entry allocation is MEM_FREE,
    // and callback has not entered yet. This is a retained delivery duty.
    CHECK(row->freed && row->manager == owner && row->source.callback);
    if (!unobserved) {
        unsigned completed = 0;
        for (unsigned i = 0; i != receipt.callCount; ++i)
            if (receipt.calls[i].frame.kind == RizEngineSchedulerConsume && receipt.calls[i].exited
                && receipt.calls[i].frame.entry == row->entry) ++completed;
        CHECK(completed > 0 && row->callbackDone == 0);
    }
    ++receipt.gapChecks;
    const auto result = reinterpret_cast<CarrierFunction>(row->source.callback)(owner, row->source.payload,
        reinterpret_cast<void*>(RegisterTag), reinterpret_cast<void*>(RegisterTag));
    const auto eax = static_cast<std::uint32_t>(result);
    CHECK(result == (Named(L"high-eax") ? 0xabcdef0112340001ULL : 0xabcdef0100000001ULL)); ++receipt.rawReturnChecks;
    if (conditional && eax == 1 && row->source.cleanup)
        reinterpret_cast<CleanupFunction>(row->source.cleanup)(row->source.payload);
    currentCallbackRow = nullptr;
}
}

extern "C" std::uint64_t RtSchedulerFixture_Register(void* tree, void* outputSlot, void* sourcePointer, void* arg3)
{
    CHECK(GetLastError() == InputError && arg3 == reinterpret_cast<void*>(RegisterTag));
    auto* owner = tree == manager.tree ? &manager : tree == foreignManager.tree ? &foreignManager : nullptr;
    auto* source = static_cast<SchedulerOwnedSource*>(sourcePointer);
    CHECK(owner && source && outputSlot && owner->count < RowLimit && rowCount < RowLimit);
    if (!owner || !source || !outputSlot || rowCount == RowLimit) RaiseFailFastException(nullptr, nullptr, 0);
    auto* entry = static_cast<SchedulerOwnedEntry*>(VirtualAlloc(preferredEntry, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    CHECK(entry && (!preferredEntry || preferredEntry == entry));
    if (!entry) RaiseFailFastException(nullptr, nullptr, 0);
    auto& row = receipt.rows[rowCount++]; row.entry = entry; row.source = *source; row.manager = owner;
    row.localAllocationId = ++receipt.nextAllocationId; row.registered = 1;
    if (preferredEntry) { ++receipt.addressReuses; receipt.reusedEntry = reinterpret_cast<std::uint64_t>(entry); preferredEntry = nullptr; }
    if (!receipt.firstEntry) receipt.firstEntry = reinterpret_cast<std::uint64_t>(entry);
    std::memcpy(&entry->record, source, sizeof(*source));
    owner->rows[owner->count++] = entry; ++receipt.registrations;
    *static_cast<SchedulerOwnedEntry**>(outputSlot) = entry;
    SetLastError(OriginalError);
    return reinterpret_cast<std::uint64_t>(outputSlot);
}
extern "C" std::uint64_t RtSchedulerFixture_Consume(void* opaque, void* unused, void* entryPointer, void* arg3)
{
    CHECK(GetLastError() == InputError && unused == reinterpret_cast<void*>(RegisterTag) && arg3 == reinterpret_cast<void*>(RegisterTag));
    auto* row = Find(static_cast<SchedulerOwnedEntry*>(entryPointer));
    CHECK(row && row->manager == opaque && !row->consumed && !row->removed);
    if (row) { row->consumed = 1; Erase(row); }
    ++receipt.consumes; SetLastError(OriginalError); return ConsumeReturn;
}
extern "C" std::uint64_t RtSchedulerFixture_Remove(void* opaque, void* record, void* entryPointer, void* arg3)
{
    CHECK(GetLastError() == InputError && arg3 == reinterpret_cast<void*>(RegisterTag));
    auto* row = Find(static_cast<SchedulerOwnedEntry*>(entryPointer));
    CHECK(row && row->manager == opaque && record == reinterpret_cast<unsigned char*>(entryPointer) + 0x20);
    if (row) {
        row->removed = 1; Erase(row); currentCallbackRow = row;
        if (row->source.cleanup) reinterpret_cast<CleanupFunction>(row->source.cleanup)(row->source.payload);
        currentCallbackRow = nullptr;
    }
    ++receipt.removals; SetLastError(OriginalError); return RemoveReturn;
}
extern "C" std::uint64_t RtSchedulerFixture_Callback(void* owner, void* payload, void* arg2, void* arg3)
{
    auto* row = currentCallbackRow ? currentCallbackRow : currentDrainRow;
    CHECK(row && row->manager == owner && row->source.payload == payload && !row->callbackOpen
        && arg2 == reinterpret_cast<void*>(RegisterTag) && arg3 == reinterpret_cast<void*>(RegisterTag));
    if (row) row->callbackOpen = 1;
    ++receipt.callbacks;
    if (Named(L"late-attach") && !lateAttachDone) AttachCallbackLate();
    if (Named(L"original-seh")) RaiseException(OriginalException, 0, 0, nullptr);
    if (Named(L"original-cpp")) throw CppException{ 0xcafebabedeadbeefULL };
    if (Named(L"nested-foreign") && !nestedForeignDone) {
        nestedForeignDone = true; auto foreign = Source(1); foreign.repeat = 0;
        auto* outer = currentCallbackRow;
        RegisterOne(&foreignManager, &foreign);
        SetLastError(InputError); CHECK(foreignPumpEntry(&foreignManager, reinterpret_cast<void*>(0x40),
            reinterpret_cast<void*>(RegisterTag), reinterpret_cast<void*>(RegisterTag)) == PumpReturn);
        currentCallbackRow = outer;
    }
    if (row) { row->callbackOpen = 0; row->callbackDone = 1; }
    SetLastError(OriginalError); return Named(L"high-eax") ? 0xabcdef0112340001ULL : 0xabcdef0100000001ULL;
}
extern "C" std::uint64_t RtSchedulerFixture_Pump(void* opaque, void* mask, void* arg2, void* arg3)
{
    CHECK(GetLastError() == InputError && mask == reinterpret_cast<void*>(0x40)
        && arg2 == reinterpret_cast<void*>(RegisterTag) && arg3 == reinterpret_cast<void*>(RegisterTag));
    auto* owner = static_cast<SchedulerOwnedManager*>(opaque);
    CHECK(owner && owner->count > 0); ++receipt.pumps;
    auto* row = Find(owner->rows[0]); CHECK(row); if (!row) return PumpReturn;
    if (row->source.repeat) {
        auto copy = row->source; copy.repeat = 0;
        auto* next = RegisterOne(owner, &copy);
        CHECK(next && next->localAllocationId > row->localAllocationId && !row->consumed && owner->count >= 2);
        ++receipt.repeatOrderChecks;
    }
    SetLastError(InputError);
    CHECK(consumeEntry(owner, reinterpret_cast<void*>(RegisterTag), row->entry, reinterpret_cast<void*>(RegisterTag)) == ConsumeReturn); ++receipt.rawReturnChecks;
    CHECK(GetLastError() == OriginalError); ++receipt.lastErrorChecks;
    CallbackAndCleanup(owner, row, !row->source.repeat);
    SetLastError(OriginalError); return PumpReturn;
}

// DrainCaller.asm supplies the complete owned native function and two actual
// instruction windows. C++ helpers below perform each row's real operations.
extern "C" void* RtSchedulerFixture_DrainNext(void* opaque)
{
    auto* owner = static_cast<SchedulerOwnedManager*>(opaque);
    CHECK(owner == &manager || owner == &foreignManager);
    if (!owner->count) { currentDrainRow = nullptr; return nullptr; }
    auto* row = Find(owner->rows[0]); CHECK(row); currentDrainRow = row;
    owner->iterator = row->entry; return row->entry;
}
extern "C" void RtSchedulerFixture_DrainBody(void* opaque, void* entryPointer, std::uint32_t invokeCallback)
{
    auto* row = currentDrainRow;
    CHECK(row && row->manager == opaque && row->entry == entryPointer && !row->freed);
    if (!row) return;
    // The category callback runs before erase, matching the documented drain
    // ordering. Row End follows actual erase and any normal cleanup return.
    CHECK(invokeCallback == ((row->source.category & 0x40) ? 1u : 0u));
    if (invokeCallback) {
        currentCallbackRow = row;
        CHECK(reinterpret_cast<CarrierFunction>(row->source.callback)(opaque, row->source.payload,
            reinterpret_cast<void*>(RegisterTag), reinterpret_cast<void*>(RegisterTag))
            == (Named(L"high-eax") ? 0xabcdef0112340001ULL : 0xabcdef0100000001ULL));
        currentCallbackRow = nullptr;
    }
    row->removed = 1; Erase(row);
    if (row->source.cleanup) reinterpret_cast<CleanupFunction>(row->source.cleanup)(row->source.payload);
}
extern "C" std::uint64_t RtSchedulerFixture_DrainFinished(void* opaque)
{
    CHECK(static_cast<SchedulerOwnedManager*>(opaque)->count == 0); ++receipt.drains;
    currentDrainRow = nullptr; SetLastError(OriginalError); return DrainReturn;
}

namespace {
template<class T> T Symbol(const char* name)
{
    const auto address = GetProcAddress(helper, name); CHECK(address != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(address)); std::memcpy(&result, &address, sizeof(result)); return result;
}
void MatchRecord(const RizEngineSchedulerRecordV1& actual, const SchedulerOwnedSource& expected)
{
    CHECK(actual.dueTimeBits == expected.dueTimeBits && actual.frame == expected.frame
        && actual.intervalBits == expected.intervalBits && actual.repeat == expected.repeat
        && actual.payload == expected.payload && actual.callback == expected.callback && actual.cleanup == expected.cleanup
        && actual.ownerId == expected.ownerId && actual.category == expected.category
        && actual.creationTag == expected.creationTag && actual.reserved == 0);
}
std::uintptr_t __cdecl ObserveEnter(const RizEngineSchedulerFrameV1* frame, void* user)
{
    CHECK(user == &receipt && frame && frame->abiVersion == 1 && frame->byteSize == 256
        && frame->kind < 6 && frame->slot == 0 && frame->sequence && frame->epoch
        && frame->reserved == 0 && frame->reserved1 == 0 && receipt.callCount < CallLimit);
    if (!frame || receipt.callCount == CallLimit) return 0;
    const auto index = receipt.callCount++; auto& call = receipt.calls[index]; call.frame = *frame;
    for (unsigned i = 0; i != index; ++i) CHECK(receipt.calls[i].frame.sequence != frame->sequence);
    CHECK(frame->threadId == GetCurrentThreadId() && frame->depth > 0 && frame->rootSequence > 0);
    if (observerReentry) CHECK(frame->sourceSequence == 0 && (frame->flags & RizEngineSchedulerSourceDrainRow) == 0);
    if (!Named(L"worker")) CHECK((frame->flags & RizEngineSchedulerMainThread) != 0);
    if (frame->kind == RizEngineSchedulerRegister) {
        CHECK(frame->sourceRecord == frame->arg2 && frame->tree == frame->arg0 && frame->outputSlot == frame->arg1);
        MatchRecord(frame->record, *static_cast<SchedulerOwnedSource*>(frame->arg2));
        if (Named(L"foreign-callback")) CHECK(frame->nodeSnapshotFlags == 0);
    } else if (frame->kind == RizEngineSchedulerConsume || frame->kind == RizEngineSchedulerRemove) {
        auto* row = Find(static_cast<SchedulerOwnedEntry*>(frame->arg2)); CHECK(row);
        if (row) MatchRecord(frame->record, row->source);
        if (Named(L"foreign-callback")) CHECK(frame->nodeSnapshotFlags == 0);
        CHECK(frame->entry == frame->arg2 && frame->manager == frame->arg0
            && frame->sourceRecord == reinterpret_cast<unsigned char*>(frame->arg2) + 0x20);
    } else if (frame->kind == RizEngineSchedulerCallback) {
        CHECK(frame->arg0 && frame->arg1 && frame->nodeSnapshotFlags == 3);
        auto* row = currentCallbackRow ? currentCallbackRow : currentDrainRow;
        CHECK(row && row->source.payload == frame->arg1 && row->manager == frame->arg0);
        if (row && row->consumed) {
            CHECK(frame->sourceSequence != 0 && (frame->flags & RizEngineSchedulerSourceConsume) != 0);
            bool matched = false;
            for (unsigned i = 0; i != index; ++i) {
                const auto& source = receipt.calls[i];
                if (source.frame.sequence == frame->sourceSequence) {
                    CHECK(source.frame.kind == RizEngineSchedulerConsume && source.exited
                        && source.frame.entry == row->entry && source.frame.parentSequence == frame->parentSequence
                        && source.frame.record.payload == frame->arg1);
                    matched = true;
                }
            }
            CHECK(matched);
        } else if (row && currentDrainRow) {
            CHECK(frame->sourceSequence != 0 && (frame->flags & RizEngineSchedulerSourceDrainRow) != 0);
        }
        if (Named(L"observer-reenter-drain") && !observerReentered) {
            observerReentered = true; observerReentry = true;
            auto* outer = currentCallbackRow;
            auto source = Source(1); source.payload = reinterpret_cast<void*>(1);
            source.callback = reinterpret_cast<void*>(&ForeignCallback); source.cleanup = nullptr;
            auto* inner = RegisterOne(&foreignManager,&source); CHECK(inner);
            if (inner) {
                SetLastError(InputError);
                CHECK(removeEntry(&foreignManager,&inner->entry->record,inner->entry,reinterpret_cast<void*>(RegisterTag)) == RemoveReturn);
            }
            currentCallbackRow = outer; observerReentry = false;
        }
        if (Named(L"observer-seh") || Named(L"observer-cpp")) {
            ++receipt.observerFaults;
            if (Named(L"observer-seh")) RaiseException(ObserverException, 0, 0, nullptr);
            else throw CppException{ 0x1111222233334444ULL };
        }
    }
    SetLastError(0xf0e0d0c0); return index + 1;
}
void __cdecl ObserveExit(const RizEngineSchedulerFrameV1* frame, std::uintptr_t token,
    const RizEngineSchedulerCompletionV1* completion, void* user)
{
    CHECK(user == &receipt && completion->abiVersion == 1 && completion->byteSize == 48);
    CallReceipt* call = nullptr;
    for (unsigned i = 0; i != receipt.callCount; ++i)
        if (receipt.calls[i].frame.sequence == frame->sequence) { call = &receipt.calls[i]; break; }
    CHECK(call && !call->exited); if (!call) return;
    CHECK(std::memcmp(&call->frame, frame, sizeof(*frame)) == 0);
    const bool observerFault = frame->kind == RizEngineSchedulerCallback && (Named(L"observer-seh") || Named(L"observer-cpp"));
    CHECK(token == (observerFault ? 0u : static_cast<std::uintptr_t>(call - receipt.calls + 1)));
    call->completion = *completion; call->exited = 1;
    const bool abnormal = (completion->flags & RizEngineSchedulerAbnormal) != 0;
    const bool shouldAbnormal = (Named(L"original-seh") || Named(L"original-cpp"))
        && (frame->kind == RizEngineSchedulerPump || frame->kind == RizEngineSchedulerCallback);
    CHECK(abnormal == shouldAbnormal);
    if (!abnormal) {
        CHECK((completion->flags & (RizEngineSchedulerReturned | RizEngineSchedulerRawRaxValid)) == 9);
        const std::uint64_t expected[] = { reinterpret_cast<std::uint64_t>(frame->outputSlot), RemoveReturn,
            ConsumeReturn, PumpReturn, DrainReturn, Named(L"high-eax") ? 0xabcdef0112340001ULL : 0xabcdef0100000001ULL };
        CHECK(completion->rawRax == expected[frame->kind]); ++receipt.rawReturnChecks;
        if (frame->kind == RizEngineSchedulerRegister) {
            CHECK(completion->outputFlags == 3 && completion->outputEntry != nullptr);
            CHECK(Find(static_cast<SchedulerOwnedEntry*>(completion->outputEntry)) != nullptr);
        }
        if (frame->kind == RizEngineSchedulerCallback && Named(L"high-eax")) ++receipt.fullEaxCases;
    }
    SetLastError(0xfedcba98);
}
void WalkRowUnwind()
{
    CONTEXT context{}; RtlCaptureContext(&context);
    bool sawProbe = false, sawRelay = false, sawCaller = false;
    DWORD64 helperImage{}; const auto function = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(rowEntry), &helperImage, nullptr);
    CHECK(function); if (!function) return;
    for (unsigned step = 0; step != 64 && context.Rip && context.Rsp; ++step) {
        if (context.Rip >= helperImage + function->BeginAddress && context.Rip < helperImage + function->EndAddress) sawProbe = true;
        if (context.Rip == drainReturnPc) { sawCaller = true; break; }
        bool relay = false;
        for (unsigned i = 0; i != 2; ++i) if (context.Rip == receipt.sites[i].returnPc) relay = true;
        DWORD64 image{}; const auto rf = RtlLookupFunctionEntry(context.Rip, &image, nullptr);
        if (relay) { CHECK(image == receipt.imageBase && context.Rsp == drainRsp); sawRelay = true; }
        if (rf) {
            PVOID data{}; DWORD64 establisher{};
            RtlVirtualUnwind(UNW_FLAG_NHANDLER, image, context.Rip, rf, &context, &data, &establisher, nullptr);
            ++receipt.rowVirtualUnwinds;
        } else { context.Rip = *reinterpret_cast<const DWORD64*>(context.Rsp); context.Rsp += 8; }
        if (relay) {
            CHECK(context.Rip == drainReturnPc && context.Rsp == drainRsp + 0x820);
            const auto* saved = reinterpret_cast<const DWORD64*>(drainRsp + 0x7d8);
            CHECK(context.R15 == saved[0] && context.R14 == saved[1] && context.R13 == saved[2] && context.R12 == saved[3]
                && context.Rdi == saved[4] && context.Rsi == saved[5] && context.Rbp == saved[6] && context.Rbx == saved[7]);
            for (unsigned i = 6; i != 16; ++i)
                CHECK(std::memcmp(reinterpret_cast<const unsigned char*>(&context.Xmm0) + i * 16,
                    reinterpret_cast<const void*>(drainRsp + 0x700 + (i - 6) * 16),16) == 0);
        }
    }
    CHECK(sawProbe && sawRelay && sawCaller); ++receipt.rowUnwindWalks;
}
void __cdecl ObserveRow(const RizEngineSchedulerRowFrameV1* frame, void* user)
{
    CHECK(user == &receipt && frame->abiVersion == 1 && frame->byteSize == 224 && frame->slot == 0
        && frame->rowSequence > 0 && frame->dispatchSequence > 0 && frame->eventSequence > 0
        && frame->manager == &manager && frame->reserved == 0 && frame->reserved1 == 0
        && receipt.rowEventCount < RowLimit * 3);
    if (receipt.rowEventCount == RowLimit * 3) return;
    receipt.rowEvents[receipt.rowEventCount++] = *frame;
    CHECK(currentDrainRow && frame->entry == currentDrainRow->entry && frame->tree == manager.tree);
    if (currentDrainRow) MatchRecord(frame->record, currentDrainRow->source);
    CHECK(frame->nodeSnapshotFlags == 3 && frame->nodeOwner == &manager && frame->enumType == 2
        && frame->referenceCount == 1 && (frame->flags & 3) == 3);
    const auto site = frame->kind == RizEngineSchedulerRowBegin ? 0u : 1u;
    if (frame->kind != RizEngineSchedulerRowAbnormal) {
        CHECK(frame->sitePc == reinterpret_cast<void*>(receipt.sites[site].site)
            && frame->relayReturnPc == reinterpret_cast<void*>(receipt.sites[site].returnPc));
        if (site == 1) {
            MEMORY_BASIC_INFORMATION state{};
            CHECK(currentDrainRow->freed && currentDrainRow->cleanupDone
                && VirtualQuery(frame->entry, &state, sizeof(state)) == sizeof(state) && state.State == MEM_FREE);
        }
        WalkRowUnwind();
    }
    // Force volatile registers and status/control state away from source
    // values using ordinary calls; the probe must restore them afterward.
    _mm_setcsr(0x1f80); SetLastError(0xa5a5a5a5);
}
void AttachCallbackLate()
{
    CHECK(attachObserver(RizEngineSchedulerCallback, 0, receipt.information[5].epoch, &observers[5]) == 0);
    lateAttachDone = true; ++receipt.unobservedCalls;
}
__declspec(noinline) std::uint64_t __cdecl Original0(void* a, void* b, void* c, void* d)
{ const auto r = RtSchedulerFixture_Register(a,b,c,d); _ReadWriteBarrier(); return r; }
__declspec(noinline) std::uint64_t __cdecl Original1(void* a, void* b, void* c, void* d)
{ const auto r = RtSchedulerFixture_Remove(a,b,c,d); _ReadWriteBarrier(); return r; }
__declspec(noinline) std::uint64_t __cdecl Original2(void* a, void* b, void* c, void* d)
{ const auto r = RtSchedulerFixture_Consume(a,b,c,d); _ReadWriteBarrier(); return r; }
__declspec(noinline) std::uint64_t __cdecl Original3(void* a, void* b, void* c, void* d)
{ const auto r = RtSchedulerFixture_Pump(a,b,c,d); _ReadWriteBarrier(); return r; }
__declspec(noinline) std::uint64_t __cdecl Original4(void* a, void* b, void* c, void* d)
{ const auto r = RtSchedulerFixture_Drain(a,b,c,d); _ReadWriteBarrier(); return r; }
__declspec(noinline) std::uint64_t __cdecl Original5(void* a, void* b, void* c, void* d)
{ const auto r = RtSchedulerFixture_Callback(a,b,c,d); _ReadWriteBarrier(); return r; }
__declspec(noinline) std::uint64_t __cdecl ForeignTarget(void* a, void* b, void* c, void* d)
{ const auto r = RtSchedulerFixture_Pump(a,b,c,d); _ReadWriteBarrier(); return r ^ 0x5555; }
__declspec(noinline) std::uint64_t __cdecl ForeignOriginal(void* a, void* b, void* c, void* d)
{ const auto r = RtSchedulerFixture_Pump(a,b,c,d); _ReadWriteBarrier(); return r; }
bool RelativeJump(unsigned char* at, std::uint64_t instructionRva, std::uint64_t target)
{
    const auto delta = static_cast<std::int64_t>(target) - static_cast<std::int64_t>(instructionRva + 5);
    if (delta < INT32_MIN || delta > INT32_MAX) return false;
    const auto relative = static_cast<std::int32_t>(delta); at[0] = 0xe9; std::memcpy(at + 1, &relative, 4); return true;
}
bool PrepareRelays()
{
    receipt.imageBase = reinterpret_cast<DWORD64>(GetModuleHandleW(nullptr));
    receipt.sourceBegin = reinterpret_cast<DWORD64>(&RtSchedulerFixture_Drain);
    receipt.sourceEnd = reinterpret_cast<DWORD64>(&RtSchedulerFixture_DrainEnd);
    receipt.rowEntry = reinterpret_cast<DWORD64>(rowEntry);
    DWORD64 image{}; const auto source = RtlLookupFunctionEntry(receipt.sourceBegin, &image, nullptr);
    CHECK(source && image == receipt.imageBase && source->BeginAddress + image == receipt.sourceBegin
        && source->EndAddress + image == receipt.sourceEnd); if (!source) return false;
    receipt.sourceFunction = *source;
    auto next = (receipt.imageBase + 0x200000 + 0xffff) & ~std::uint64_t(0xffff);
    for (; next < receipt.imageBase + 0x70000000; next += 0x10000) {
        MEMORY_BASIC_INFORMATION state{};
        if (!VirtualQuery(reinterpret_cast<void*>(next), &state, sizeof(state))) break;
        if (state.State == MEM_FREE && state.RegionSize >= 4096) {
            relayBlock = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(next), 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
            if (relayBlock) break;
        }
    }
    CHECK(relayBlock); if (!relayBlock) return false;
    receipt.relayBase = reinterpret_cast<DWORD64>(relayBlock);
    relayFunctions = reinterpret_cast<PRUNTIME_FUNCTION>(relayBlock + 0x300);
    const void* sites[]{ &RtSchedulerFixture_DrainBeginSite, &RtSchedulerFixture_DrainEndSite };
    const void* continuations[]{ &RtSchedulerFixture_DrainBeginContinuation, &RtSchedulerFixture_DrainEndContinuation };
    for (unsigned i = 0; i != 2; ++i) {
        auto& site = receipt.sites[i]; site.site = reinterpret_cast<DWORD64>(sites[i]);
        site.continuation = reinterpret_cast<DWORD64>(continuations[i]); site.span = i ? 5u : 6u;
        site.branchTarget = i ? 0 : reinterpret_cast<DWORD64>(&RtSchedulerFixture_DrainNoCallback);
        site.relay = reinterpret_cast<DWORD64>(relayBlock + i * 64); site.returnPc = site.relay + 6;
        std::memcpy(site.original, sites[i], site.span);
        const unsigned char begin[]{0xf6,0x43,0x54,0x40}, end[]{0x48,0x83,0x7d,0x38,0};
        if (i == 0) CHECK(std::memcmp(site.original, begin, 4) == 0 && site.original[4] == 0x74
            && site.site + 6 + static_cast<std::int8_t>(site.original[5]) == site.branchTarget);
        else CHECK(std::memcmp(site.original, end, sizeof(end)) == 0);
        auto* code = relayBlock + i * 64;
        const unsigned char call[]{0xff,0x15,0x2a,0,0,0}; std::memcpy(code, call, sizeof(call));
        std::memcpy(code + 48, &rowEntry, 8); unsigned cursor = 6;
        if (i == 0) {
            std::memcpy(code + cursor, begin, 4); cursor += 4;
            code[cursor] = 0x0f; code[cursor + 1] = 0x84;
            const auto delta = static_cast<std::int64_t>(site.branchTarget) - static_cast<std::int64_t>(site.relay + cursor + 6);
            CHECK(delta >= INT32_MIN && delta <= INT32_MAX); const auto rel = static_cast<std::int32_t>(delta);
            std::memcpy(code + cursor + 2, &rel, 4); cursor += 6;
        } else { std::memcpy(code + cursor, end, 5); cursor += 5; }
        CHECK(RelativeJump(code + cursor, site.relay + cursor, site.continuation)); cursor += 5;
        site.codeBytes = cursor; std::memcpy(site.code, code, 64);
        auto* unwind = relayBlock + 0x200 + i * 32;
        unwind[0] = 1 | (UNW_FLAG_CHAININFO << 3); std::memcpy(unwind + 4, source, sizeof(*source));
        relayFunctions[i] = {static_cast<DWORD>(site.relay - image), static_cast<DWORD>(site.relay + cursor - image),
            static_cast<DWORD>(reinterpret_cast<DWORD64>(unwind) - image)};
        site.function = relayFunctions[i]; std::memcpy(site.unwind, unwind, 16);
        std::memset(site.replacement, 0x90, site.span);
        CHECK(RelativeJump(site.replacement, site.site, site.relay));
    }
    DWORD previous{}; CHECK(VirtualProtect(relayBlock, 4096, PAGE_EXECUTE_READ, &previous));
    CHECK(FlushInstructionCache(GetCurrentProcess(), relayBlock, 4096));
    CHECK(RtlAddFunctionTable(relayFunctions, 2, image));
    return receipt.failures == 0;
}
bool Initialize(const wchar_t* path)
{
    helperLock = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    CHECK(helperLock != INVALID_HANDLE_VALUE);
    helper = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    CHECK(helper); if (!helper) return false;
    auto version = Symbol<decltype(&RizEngineSchedulerContext_GetAbiVersion)>("RizEngineSchedulerContext_GetAbiVersion");
    auto frameSize = Symbol<decltype(&RizEngineSchedulerContext_GetFrameSize)>("RizEngineSchedulerContext_GetFrameSize");
    auto completionSize = Symbol<decltype(&RizEngineSchedulerContext_GetCompletionSize)>("RizEngineSchedulerContext_GetCompletionSize");
    auto rowSize = Symbol<decltype(&RizEngineSchedulerContext_GetRowFrameSize)>("RizEngineSchedulerContext_GetRowFrameSize");
    auto getEntry = Symbol<decltype(&RizEngineSchedulerContext_GetEntry)>("RizEngineSchedulerContext_GetEntry");
    auto getRow = Symbol<decltype(&RizEngineSchedulerContext_GetDrainRowEntry)>("RizEngineSchedulerContext_GetDrainRowEntry");
    auto configure = Symbol<decltype(&RizEngineSchedulerContext_Configure)>("RizEngineSchedulerContext_Configure");
    getInfo = Symbol<decltype(getInfo)>("RizEngineSchedulerContext_GetInfo");
    attachObserver = Symbol<decltype(attachObserver)>("RizEngineSchedulerContext_Attach");
    if (receipt.failures) return false;
    CHECK(version() == 1 && frameSize() == 256 && completionSize() == 48 && rowSize() == 224);
    rowEntry = getRow(); CHECK(rowEntry); if (!PrepareRelays()) return false;
    CarrierFunction targets[]{&RtSchedulerFixture_Register, &RtSchedulerFixture_Remove, &RtSchedulerFixture_Consume,
        &RtSchedulerFixture_Pump, &RtSchedulerFixture_Drain, &RtSchedulerFixture_CallbackTarget};
    CarrierFunction originals[]{&Original0,&Original1,&Original2,&Original3,&Original4,&Original5};
    CarrierFunction* entries[]{&registerEntry,&removeEntry,&consumeEntry,&pumpEntry,&drainEntry,&callbackEntry};
    for (unsigned kind = 0; kind != 6; ++kind) {
        RizEngineSchedulerConfigV1 config{1,sizeof(config),reinterpret_cast<void*>(targets[kind]),
            reinterpret_cast<void*>(originals[kind]),mainThread,0,{}};
        if (kind == RizEngineSchedulerDrain) for (unsigned i = 0; i != 2; ++i)
            config.sites[i] = {reinterpret_cast<void*>(receipt.sites[i].site),reinterpret_cast<void*>(receipt.sites[i].returnPc)};
        CHECK(configure(kind,0,&config) == 0);
        auto& info = receipt.information[kind]; info = {1,sizeof(info)}; CHECK(getInfo(kind,0,&info) == 0);
        *entries[kind] = reinterpret_cast<CarrierFunction>(getEntry(kind,0)); CHECK(*entries[kind]);
        observers[kind] = {1,sizeof(RizEngineSchedulerObserverV1),&ObserveEnter,&ObserveExit,
            kind == RizEngineSchedulerDrain ? &ObserveRow : nullptr,&receipt};
        if (kind != RizEngineSchedulerCallback || !Named(L"late-attach"))
            CHECK(attachObserver(kind,0,info.epoch,&observers[kind]) == 0);
    }
    RizEngineSchedulerConfigV1 foreign{1,sizeof(foreign),reinterpret_cast<void*>(&ForeignTarget),
        reinterpret_cast<void*>(&ForeignOriginal),mainThread,0,{}};
    CHECK(configure(RizEngineSchedulerPump,1,&foreign) == 0);
    foreignPumpEntry = reinterpret_cast<CarrierFunction>(getEntry(RizEngineSchedulerPump,1)); CHECK(foreignPumpEntry);
    for (unsigned i = 0; i != 2; ++i) {
        const auto& site = receipt.sites[i]; auto* pointer = reinterpret_cast<void*>(site.site); DWORD previous{};
        CHECK(std::memcmp(pointer,site.original,site.span) == 0);
        CHECK(VirtualProtect(pointer,site.span,PAGE_EXECUTE_READWRITE,&previous));
        std::memcpy(pointer,site.replacement,site.span); CHECK(FlushInstructionCache(GetCurrentProcess(),pointer,site.span));
        DWORD unused{}; CHECK(VirtualProtect(pointer,site.span,previous,&unused));
    }
    return receipt.failures == 0;
}
}

extern "C" __declspec(noinline) std::uint64_t RtSchedulerFixture_CallbackTarget(void* a,void* b,void* c,void* d)
{
    // The actual source record stores this exact configured target. Its real
    // call forwards to the configured typed entry, which invokes Original5;
    // no record is claimed merely because it stores a helper-entry address.
    const auto result = callbackEntry(a,b,c,d); _ReadWriteBarrier(); return result;
}

extern "C" void RtSchedulerFixture_DrainPrepare(void* opaque, std::uint64_t rsp)
{
    CHECK(opaque == &manager && currentDrainRow); drainRsp = rsp;
    drainReturnPc = *reinterpret_cast<const std::uint64_t*>(rsp + 0x818);
    for (unsigned i = 0; i != 4; ++i) {
        auto* frame = reinterpret_cast<RizEventMidFrameV1*>(rsp + 0x40 + i * 0x1b0);
        std::memset(frame,0,sizeof(*frame)); frame->abiVersion = 1; frame->byteSize = sizeof(*frame);
    }
}
extern "C" void RtSchedulerFixture_DrainCheck(void* opaque, std::uint64_t rsp)
{
    CHECK(opaque == &manager && rsp == drainRsp && currentDrainRow && currentDrainRow->freed);
    if (!currentDrainRow) return;
    const auto index = static_cast<unsigned>(currentDrainRow - receipt.rows); CHECK(index < RowLimit);
    if (index >= RowLimit) return;
    const auto* frames = reinterpret_cast<const RizEventMidFrameV1*>(rsp + 0x40);
    for (unsigned i = 0; i != 4; ++i) receipt.machines[index][i] = frames[i];
    for (unsigned pair = 0; pair != 2; ++pair) {
        const auto& before = frames[pair * 2]; const auto& after = frames[pair * 2 + 1];
        CHECK(before.abiVersion == 1 && after.abiVersion == 1 && before.byteSize == 432 && after.byteSize == 432);
        const auto expectedFlags = pair == 0
            ? RtSchedulerFixture_TestFlags(&currentDrainRow->source.category,before.rflags)
            : RtSchedulerFixture_CmpFlags(&manager.count,before.rflags);
        CHECK(after.rflags == expectedFlags && (before.rflags & 0x400) && (after.rflags & 0x400));
        CHECK(before.mxcsr == 0x3f80 && after.mxcsr == before.mxcsr && before.fpcsr == 0x077f && after.fpcsr == before.fpcsr);
        for (unsigned reg = 0; reg != 16; ++reg) {
            CHECK(before.gpr[reg] == after.gpr[reg]);
            CHECK(std::memcmp(&before.xmm[reg],&after.xmm[reg],16) == 0);
        }
        ++receipt.statePairs;
    }
    const auto* canaries = reinterpret_cast<const std::uint64_t*>(rsp + 0x7c0);
    CHECK(canaries[0] == 0xaabbccddeeff1122ULL && canaries[1] == canaries[0] && canaries[2] == canaries[0]); ++receipt.rowCanaries;
}

namespace {
void RunBody()
{
    auto source = Source();
    auto* first = RegisterOne(&manager,&source); CHECK(first);
    if (Named(L"remove") || Named(L"drain") || Named(L"multirow")) {
        auto second = source;
        if (Named(L"drain")) second.category = 0;
        RegisterOne(&manager,&second);
        if (Named(L"drain")) RegisterOne(&manager,&source);
    }
    if (Named(L"remove")) {
        while (manager.count) {
            auto* entry = manager.rows[0]; SetLastError(InputError);
            CHECK(removeEntry(&manager,&entry->record,entry,reinterpret_cast<void*>(RegisterTag)) == RemoveReturn);
            ++receipt.rawReturnChecks; CHECK(GetLastError() == OriginalError); ++receipt.lastErrorChecks;
        }
    } else if (Named(L"drain") || Named(L"observer-reenter-drain")) {
        SetLastError(InputError);
        CHECK(drainEntry(&manager,reinterpret_cast<void*>(RegisterTag),reinterpret_cast<void*>(RegisterTag),reinterpret_cast<void*>(RegisterTag)) == DrainReturn);
        ++receipt.rawReturnChecks; CHECK(GetLastError() == OriginalError); ++receipt.lastErrorChecks;
    } else {
        while (manager.count) {
            SetLastError(InputError);
            CHECK(pumpEntry(&manager,reinterpret_cast<void*>(0x40),reinterpret_cast<void*>(RegisterTag),reinterpret_cast<void*>(RegisterTag)) == PumpReturn);
            ++receipt.rawReturnChecks; CHECK(GetLastError() == OriginalError); ++receipt.lastErrorChecks;
        }
        if (Named(L"address-reuse") || Named(L"late-attach")) {
            if (Named(L"address-reuse")) preferredEntry = first->entry;
            RegisterOne(&manager,&source); SetLastError(InputError);
            CHECK(pumpEntry(&manager,reinterpret_cast<void*>(0x40),reinterpret_cast<void*>(RegisterTag),reinterpret_cast<void*>(RegisterTag)) == PumpReturn);
            ++receipt.rawReturnChecks; CHECK(GetLastError() == OriginalError); ++receipt.lastErrorChecks;
        }
    }
}
void RunWithSeh()
{
    __try { RunBody(); }
    __except (GetExceptionCode() == OriginalException ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        ++receipt.originalSeh;
    }
}
DWORD WINAPI Worker(void*) { RunBody(); ++receipt.workerCalls; return 0; }
void InitializePayloads()
{
    manager.tree = &manager.tree; foreignManager.tree = &foreignManager.tree;
    for (unsigned i = 0; i != 2; ++i) {
        auto* node = ownedPayload[i]; auto* owner = i ? &foreignManager : &manager;
        std::memcpy(node + 0x58,&owner,sizeof(owner));
        const std::uint64_t handle = 0x1234567890000000ULL + i;
        std::memcpy(node + 0x10,&handle,sizeof(handle));
        const std::uint32_t type = 2; std::memcpy(node + 0x18,&type,sizeof(type));
        auto* cache = &ownedPayload[i]; std::memcpy(node + 0x20,&cache,sizeof(cache));
        const std::int32_t references = 1; std::memcpy(node + 0x60,&references,sizeof(references));
    }
    for (unsigned i = 0; i != 16; ++i) for (unsigned j = 0; j != 16; ++j)
        RtSchedulerFixture_XmmInput[i][j] = static_cast<unsigned char>(0x10 + i * 7 + j);
}
void VerifyCompleted()
{
    const bool expectedUnknown = Named(L"worker") || Named(L"original-seh") || Named(L"original-cpp")
        || Named(L"observer-seh") || Named(L"observer-cpp");
    for (unsigned kind = 0; kind != 6; ++kind) {
        auto& info = receipt.information[kind]; CHECK(getInfo(kind,0,&info) == 0);
        CHECK(info.configured == 1 && info.attached == 1 && info.entered == info.exited && info.inFlight == 0
            && info.rowsInFlight == 0 && info.unknown == (expectedUnknown ? 1u : 0u));
        unsigned captured = 0;
        for (unsigned i = 0; i != receipt.callCount; ++i)
            if (receipt.calls[i].frame.kind == kind) { CHECK(receipt.calls[i].exited == 1); ++captured; }
        CHECK(info.entered == captured + (kind == RizEngineSchedulerCallback ? receipt.unobservedCalls : 0));
    }
    auto& foreign = receipt.foreignInformation; foreign = {1,sizeof(foreign)};
    CHECK(getInfo(RizEngineSchedulerPump,1,&foreign) == 0 && foreign.attached == 0
        && foreign.inFlight == 0 && foreign.entered == foreign.exited
        && foreign.entered == (Named(L"nested-foreign") ? 1u : 0u));
    CHECK(manager.count == 0 && foreignManager.count == 0 && receipt.registrations == rowCount && receipt.actualFrees == rowCount);
    if (Named(L"high-eax")) CHECK(receipt.fullEaxCases == 1 && receipt.cleanups == 0);
    if (Named(L"repeat")) CHECK(receipt.repeatOrderChecks == 1 && receipt.registrations == 2 && receipt.callbacks == 2 && receipt.cleanups == 1);
    if (Named(L"multirow")) CHECK(receipt.registrations == 2 && receipt.callbacks == 2 && receipt.gapChecks == 2
        && receipt.rows[0].source.payload == receipt.rows[1].source.payload
        && receipt.rows[0].localAllocationId != receipt.rows[1].localAllocationId);
    if (Named(L"address-reuse")) CHECK(receipt.addressReuses == 1 && receipt.firstEntry == receipt.reusedEntry);
    if (Named(L"drain")) {
        CHECK(receipt.drains == 1 && receipt.rowEventCount == 6 && receipt.statePairs == 6
            && receipt.rowCanaries == 3 && receipt.rowUnwindWalks == 6 && receipt.cleanups == 3 && receipt.callbacks == 2);
        for (unsigned i = 0; i + 1 < receipt.rowEventCount; i += 2) {
            const auto& begin = receipt.rowEvents[i]; const auto& end = receipt.rowEvents[i + 1];
            CHECK(begin.kind == RizEngineSchedulerRowBegin && end.kind == RizEngineSchedulerRowEnd
                && begin.rowSequence == end.rowSequence && begin.dispatchSequence == end.dispatchSequence
                && begin.entry == end.entry && begin.eventSequence < end.eventSequence
                && begin.nodeOwner == end.nodeOwner && begin.enumHandle == end.enumHandle
                && begin.enumCache == end.enumCache && begin.enumType == end.enumType
                && begin.referenceCount == end.referenceCount && begin.nodeSnapshotFlags == end.nodeSnapshotFlags
                && std::memcmp(&begin.record,&end.record,sizeof(begin.record)) == 0);
        }
    }
    if (Named(L"late-attach")) CHECK(lateAttachDone && receipt.unobservedCalls == 1 && receipt.information[5].entered == 2);
    if (Named(L"foreign-callback")) CHECK(receipt.callbacks == 1 && receipt.information[5].entered == 0
        && receipt.information[0].snapshotFaults == 0 && receipt.information[2].snapshotFaults == 0);
    if (Named(L"observer-reenter-drain")) CHECK(observerReentered && receipt.removals == 1
        && receipt.drains == 1 && receipt.rowEventCount == 2 && receipt.statePairs == 2);
    if (Named(L"original-seh")) CHECK(receipt.originalSeh == 1 && receipt.cleanups == 0);
    if (Named(L"original-cpp")) CHECK(receipt.originalCpp == 1 && receipt.cleanups == 0);
}
}
int wmain(int argc, wchar_t** argv)
{
    if (argc != 4) { std::fprintf(stderr,"usage: SchedulerFixture ABS_KERNEL_DLL MODE ABS_RAW_OUTPUT\n"); return 2; }
    mode = argv[2]; mainThread = GetCurrentThreadId(); InitializePayloads();
    const bool valid = Named(L"normal") || Named(L"high-eax") || Named(L"repeat") || Named(L"multirow")
        || Named(L"remove") || Named(L"drain") || Named(L"address-reuse") || Named(L"nested-foreign")
        || Named(L"worker") || Named(L"original-seh") || Named(L"original-cpp") || Named(L"observer-seh")
        || Named(L"observer-cpp") || Named(L"late-attach") || Named(L"foreign-callback") || Named(L"observer-reenter-drain");
    CHECK(valid); bool initialized = false;
    if (valid) initialized = Initialize(argv[1]);
    if (initialized) {
        if (Named(L"worker")) {
            HANDLE thread = CreateThread(nullptr,0,&Worker,nullptr,0,nullptr); CHECK(thread);
            if (thread) { CHECK(WaitForSingleObject(thread,15000) == WAIT_OBJECT_0); DWORD code{};
                CHECK(GetExitCodeThread(thread,&code) && code == 0); CHECK(CloseHandle(thread)); }
        } else if (Named(L"original-seh")) RunWithSeh();
        else if (Named(L"original-cpp")) {
            try { RunBody(); } catch (const CppException& error) {
                CHECK(error.seal == 0xcafebabedeadbeefULL); ++receipt.originalCpp;
            }
        } else RunBody();
        VerifyCompleted();
    }
    HANDLE output = CreateFileW(argv[3],GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
    CHECK(output != INVALID_HANDLE_VALUE);
    if (output != INVALID_HANDLE_VALUE) {
        DWORD written{};
        const bool okay = WriteFile(output,&receipt,sizeof(receipt),&written,nullptr) && written == sizeof(receipt);
        const bool flushed = FlushFileBuffers(output) != FALSE, closed = CloseHandle(output) != FALSE;
        if (!okay || !flushed || !closed) { std::fprintf(stderr,"Raw receipt persistence failed.\n"); return 3; }
    }
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-engine-scheduler-fixture\",\"mode\":\"%ls\",\"passed\":%s,\"initialized\":%s,\"checks\":%u,\"failures\":%u,\"registrations\":%u,\"consumes\":%u,\"removals\":%u,\"callbacks\":%u,\"cleanups\":%u,\"gapChecks\":%u,\"realFrees\":%u,\"addressReuses\":%u,\"fullEaxCases\":%u,\"rawReturnChecks\":%u,\"lastErrorChecks\":%u,\"capturedCalls\":%u,\"rowEvents\":%u,\"statePairs\":%u,\"rowUnwindWalks\":%u,\"rawBytes\":%zu,\"typedEntryInvoked\":true,\"fixtureOnlyPatch\":true,\"sourceE9RelayCall\":true,\"branchFlagReplay\":true,\"gameRuntimeVerified\":false,\"productionAdmissionVerified\":false,\"engineCancellationCompleted\":false}\n",
        mode,receipt.failures ? "false" : "true",initialized ? "true" : "false",receipt.checks,receipt.failures,
        receipt.registrations,receipt.consumes,receipt.removals,receipt.callbacks,receipt.cleanups,receipt.gapChecks,
        receipt.actualFrees,receipt.addressReuses,receipt.fullEaxCases,receipt.rawReturnChecks,receipt.lastErrorChecks,
        receipt.callCount,receipt.rowEventCount,receipt.statePairs,receipt.rowUnwindWalks,sizeof(receipt));
    // All transport descriptors, observers, modules, relays and function tables
    // are permanent for this self-owned child. No cancellation is inferred.
    return receipt.failures ? 1 : 0;
}
