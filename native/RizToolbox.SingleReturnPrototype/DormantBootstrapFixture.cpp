#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include "SingleReturnContext.h"

// Owned executable only. It loads only the supplied isolated helper and invokes
// typed native originals, observers and readers; no game or production provider.
struct Api {
    decltype(&RizSingleReturnPrototype_GetLimit) limit;
    decltype(&RizSingleReturnPrototype_GetEntry) entry;
    decltype(&RizSingleReturnPrototype_Configure) configure;
    decltype(&RizSingleReturnPrototype_ClaimMusicClock) claim;
    decltype(&RizSingleReturnPrototype_PublishMusicClock) publish;
    decltype(&RizSingleReturnPrototype_ReadClockPolicy) readPolicy;
    decltype(&RizSingleReturnPrototype_Withdraw) withdraw;
    decltype(&RizSingleReturnPrototype_WithdrawMusicClock) withdrawClock;
    decltype(&RizSingleReturnPrototype_MarkFault) fault;
    decltype(&RizSingleReturnPrototype_GetStats) stats;
    decltype(&RizSingleReturnPrototype_PrepareDormantMusicClock) prepare;
    decltype(&RizSingleReturnPrototype_ArmDormantMusicClock) arm;
    decltype(&RizSingleReturnPrototype_CancelDormantMusicClock) cancel;
    decltype(&RizSingleReturnPrototype_ReadDormantMusicClock) read;
} api{};
struct Invocation {
    std::uint32_t returned, bits, exceptionCode, exceptionFlags, exceptionCount;
    std::uintptr_t info[3];
};
struct Owner {
    std::uint32_t slot, originalBits;
    volatile LONG mode, originalCalls, readerCalls, enters, exits;
    std::uint32_t observerFault, readerFault, armOnExit, armOnExitResult;
    void* receivedSelf;
    const void* receivedMethod;
    void* receipt;
    HANDLE entered, release;
    double dspNow;
    RizMusicClockPolicyV1 policy;
    RizSingleFrameV1 frame;
    RizSingleCompletionV1 completion;
    Invocation workerResult;
};
Owner owners[32]{};
std::uint64_t methodTags[32]{};
std::uint32_t checks = 0, failures = 0, cases = 0;
std::array<void*, 32> receiptAddresses{};
std::array<std::uint64_t, 32> receiptEpochs{};
std::uint32_t receiptCount = 0;
const char* currentCase = "startup";
void Check(bool condition, const char* message)
{
    ++checks;
    if (!condition) { ++failures; std::fprintf(stderr, "FAIL [%s] %s\n", currentCase, message); }
}
void Begin(const char* name) { currentCase = name; ++cases; std::printf("CASE %u %s\n", cases, name); }
std::uint32_t Bits(float value) { std::uint32_t bits; std::memcpy(&bits, &value, sizeof(bits)); return bits; }
float Float(std::uint32_t bits) { float value; std::memcpy(&value, &bits, sizeof(value)); return value; }
__declspec(noinline) float __cdecl Original(void* self, const void* method)
{
    auto* owner = static_cast<Owner*>(self);
    InterlockedIncrement(&owner->originalCalls);
    owner->receivedSelf = self; owner->receivedMethod = method;
    const auto mode = InterlockedCompareExchange(&owner->mode, 0, 0);
    if (mode == 1) {
        SetEvent(owner->entered);
        if (WaitForSingleObject(owner->release, 10000) != WAIT_OBJECT_0) RaiseException(0xe05253ffu, 0, 0, nullptr);
    }
    if (mode == 2) {
        const ULONG_PTR args[]{reinterpret_cast<ULONG_PTR>(self), reinterpret_cast<ULONG_PTR>(method), 0xfedcba9876543210ull};
        RaiseException(0xe0525301u, 0, 3, args);
    }
    return Float(owner->originalBits);
}
std::uintptr_t __cdecl Enter(const RizSingleFrameV1* frame, void* user)
{
    auto* owner = static_cast<Owner*>(user); owner->frame = *frame; InterlockedIncrement(&owner->enters);
    if (owner->observerFault == 1) RaiseException(0xe0525302u, 0, 0, nullptr);
    return 0x706f64746f6b656eull;
}
void __cdecl Exit(const RizSingleFrameV1*, const RizSingleCompletionV1* completion, void* user)
{
    auto* owner = static_cast<Owner*>(user); owner->completion = *completion; InterlockedIncrement(&owner->exits);
    if (owner->armOnExit) owner->armOnExitResult = api.arm(owner->receipt);
    if (owner->observerFault == 2) RaiseException(0xe0525303u, 0, 0, nullptr);
}
std::uint32_t __cdecl ClockRead(RizMusicClockSampleV1* sample, void* user)
{
    auto* owner = static_cast<Owner*>(user); InterlockedIncrement(&owner->readerCalls);
    if (owner->readerFault == 1) RaiseException(0xe0525304u, 0, 0, nullptr);
    if (owner->readerFault == 2) { api.withdrawClock(owner->slot, owner->policy.policyGeneration); return 1; }
    const auto& policy = owner->policy;
    *sample = RizMusicClockSampleV1{1, sizeof(*sample), policy.self, policy.method,
        policy.runHigh, policy.runLow, policy.controllerGeneration, policy.policyGeneration,
        policy.phase, 0, owner->dspNow};
    return 0;
}
int Filter(EXCEPTION_POINTERS* exception, Invocation* result)
{
    const auto* record = exception->ExceptionRecord;
    result->exceptionCode = record->ExceptionCode; result->exceptionFlags = record->ExceptionFlags;
    result->exceptionCount = record->NumberParameters;
    for (std::uint32_t index = 0; index < 3 && index < record->NumberParameters; ++index) result->info[index] = record->ExceptionInformation[index];
    return EXCEPTION_EXECUTE_HANDLER;
}
Invocation InvokeAt(Owner& owner, RizSingleOriginal entry)
{
    Invocation result{};
    __try { result.bits = Bits(entry(&owner, owner.policy.method)); result.returned = 1; }
    __except (Filter(GetExceptionInformation(), &result)) {}
    return result;
}
Invocation Invoke(Owner& owner) { return InvokeAt(owner, reinterpret_cast<RizSingleOriginal>(api.entry(owner.slot))); }
DWORD WINAPI Worker(void* user) { auto& owner = *static_cast<Owner*>(user); owner.workerResult = Invoke(owner); return 0; }
Owner& Configure(std::uint32_t slot)
{
    auto& owner = owners[slot]; owner.slot = slot; owner.originalBits = Bits(7.25f); owner.dspNow = 12;
    owner.entered = CreateEventW(nullptr, TRUE, FALSE, nullptr); owner.release = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    Check(owner.entered != nullptr && owner.release != nullptr, "owned synchronization events created");
    owner.policy = RizMusicClockPolicyV1{1, sizeof(owner.policy), &owner, &methodTags[slot], 9, 11, 7, 1,
        RizClockPlaying, 0, 10, 20, 1.5, 0, 1000, &ClockRead, &owner, nullptr};
    const RizSingleConfigV1 config{1, sizeof(config), &Original, &Enter, &Exit, &owner};
    Check(api.configure(slot, &config) == 0, "configured owned original and observers");
    return owner;
}
RizMusicClockBootstrapInfoV1 Read(Owner& owner)
{
    RizMusicClockBootstrapInfoV1 info{1, sizeof(info)};
    Check(api.read(owner.receipt, &info) == 0, "read registered receipt");
    return info;
}
RizSingleStatsV1 Stats(Owner& owner)
{
    RizSingleStatsV1 stats{1, sizeof(stats)};
    Check(api.stats(owner.slot, &stats) == 0, "read actual native stats");
    return stats;
}
void RememberReceipt(Owner& owner)
{
    auto info = Read(owner);
    Check(info.receipt == owner.receipt && info.receipt != nullptr && info.epoch > 0, "native registered receipt and nonzero epoch");
    for (std::uint32_t index = 0; index < receiptCount; ++index) {
        Check(receiptAddresses[index] != info.receipt, "receipt address never reused");
        Check(receiptEpochs[index] < info.epoch, "epoch is unique and monotonic");
    }
    receiptAddresses[receiptCount] = info.receipt; receiptEpochs[receiptCount] = info.epoch; ++receiptCount;
}
void Prepare(Owner& owner)
{
    Check(api.prepare(owner.slot, 0, &owner.policy, &owner.receipt) == 0, "prepared dormant native gate");
    RememberReceipt(owner);
}
HANDLE Block(Owner& owner)
{
    InterlockedExchange(&owner.mode, 1);
    const auto thread = CreateThread(nullptr, 0, &Worker, &owner, 0, nullptr);
    Check(thread != nullptr, "owned native worker created");
    Check(WaitForSingleObject(owner.entered, 5000) == WAIT_OBJECT_0, "actual original is blocked");
    return thread;
}
void Drain(Owner& owner, HANDLE thread)
{
    SetEvent(owner.release);
    Check(WaitForSingleObject(thread, 5000) == WAIT_OBJECT_0, "actual original returned and worker drained");
    DWORD exitCode = 0xffffffffu;
    Check(GetExitCodeThread(thread, &exitCode) != 0 && exitCode == 0, "actual owned worker exit code zero");
    CloseHandle(thread); InterlockedExchange(&owner.mode, 0);
    Check(Stats(owner).inFlight == 0, "native inFlight really drained");
}
void AbiAndBadBaseline()
{
    Begin("ABI and rejected baselines");
    Check(sizeof(RizMusicClockPolicyV1) == 128 && sizeof(RizSingleStatsV1) == 112
        && sizeof(RizSingleFrameV1) == 80 && sizeof(RizSingleCompletionV1) == 48, "existing ABI remains exact");
    Check(sizeof(RizMusicClockBootstrapInfoV1) == 192 && offsetof(RizMusicClockBootstrapInfoV1, policy) == 64, "new info ABI exact");
    Check(api.limit() == 32 && api.entry(32) == nullptr, "slot limit retained");
    auto& owner = owners[0]; owner.slot = 0;
    owner.policy = RizMusicClockPolicyV1{1, sizeof(owner.policy), &owner, &methodTags[0], 9, 11, 7, 1,
        RizClockPlaying, 0, 10, 20, 1.5, 0, 1000, &ClockRead, &owner, nullptr};
    void* receipt = reinterpret_cast<void*>(1);
    Check(api.prepare(0, 0, &owner.policy, &receipt) == 3 && receipt == nullptr, "unconfigured prepare rejected");
    auto& configured = Configure(0);
    Check(api.prepare(0, 1, &configured.policy, &receipt) == 5 && receipt == nullptr, "nonzero expected baseline rejected");
    Check(api.prepare(32, 0, &configured.policy, &receipt) == 1, "bad slot rejected");
    Check(api.prepare(0, 0, nullptr, &receipt) == 1 && api.prepare(0, 0, &configured.policy, nullptr) == 1, "null prepare arguments rejected");
    auto bad = configured.policy; bad.policyGeneration = 0;
    Check(api.prepare(0, 0, &bad, &receipt) == 1 && receipt == nullptr, "zero generation rejected");
    bad = configured.policy; bad.rate = (std::numeric_limits<double>::quiet_NaN)();
    Check(api.prepare(0, 0, &bad, &receipt) == 1, "nonfinite rate rejected");
    bad = configured.policy; bad.reserved = 1;
    Check(api.prepare(0, 0, &bad, &receipt) == 1, "reserved policy bits rejected");
    bad = configured.policy; bad.runHigh = 0; bad.runLow = 0;
    Check(api.prepare(0, 0, &bad, &receipt) == 1, "zero run identity rejected");
    Prepare(configured);
}
void DormantAndArm()
{
    Begin("immutable dormant policy, observer, arm and selected rate");
    auto& owner = Configure(1);
    auto callerPolicy = owner.policy;
    Check(api.prepare(owner.slot, 0, &callerPolicy, &owner.receipt) == 0, "prepare copies caller policy");
    RememberReceipt(owner); callerPolicy.rate = 99; callerPolicy.mediaAnchor = 999;
    const auto dormant = Read(owner); const auto before = Stats(owner);
    Check(dormant.phase == RizMusicClockDormant && dormant.initialGeneration == 1 && dormant.currentGeneration == 1
        && dormant.inFlight == 0 && dormant.unknown == 0 && dormant.reserved == 0 && dormant.slot == owner.slot, "dormant native facts exact");
    Check(std::memcmp(&dormant.policy, &owner.policy, sizeof(owner.policy)) == 0, "full initial policy immutable exact copy");
    Check(before.authorityActive == 0 && before.policyGeneration == 1 && before.observerEnabled == 1, "dormant policy published with authority inactive");
    auto result = Invoke(owner);
    Check(result.returned && result.bits == owner.originalBits && owner.originalCalls == 1, "dormant actual original exactly once and exact result");
    Check(owner.readerCalls == 0 && owner.enters == 1 && owner.exits == 1, "dormant actual observer executes without ClockRead");
    Check(owner.receivedSelf == &owner && owner.receivedMethod == owner.policy.method, "actual typed original receives exact identities");
    Check(owner.frame.policyGeneration == 0 && owner.completion.disposition == RizSingleNoAuthority
        && !(owner.completion.flags & RizSingleAuthorityCaptured) && (owner.completion.flags & RizSingleObserverCaptured), "dormant frame captures no authority");
    auto next = owner.policy; next.policyGeneration = 2;
    void* duplicate = reinterpret_cast<void*>(1);
    Check(api.prepare(owner.slot, 0, &owner.policy, &duplicate) == 2 && duplicate == nullptr, "second prepare cannot replace receipt");
    Check(api.claim(owner.slot, &owner.policy) == 2, "ordinary Claim cannot bypass dormant gate");
    Check(api.publish(owner.slot, 0, &owner.policy) == 2, "ordinary initial Publish cannot bypass dormant gate");
    Check(api.publish(owner.slot, 1, &next) == 2 && Stats(owner).authorityActive == 0, "ordinary successor Publish cannot bypass dormant gate");
    Check(api.arm(owner.receipt) == 0, "actual dormant gate armed");
    Check(api.arm(owner.receipt) == 2, "repeated Arm rejected");
    result = Invoke(owner);
    Check(result.returned && result.bits == Bits(23.0f) && owner.originalCalls == 2 && owner.readerCalls == 1, "actual armed selected-rate result 20+(12-10)*1.5=23");
    Check(owner.completion.originalBits == owner.originalBits && owner.completion.candidateBits == Bits(23.0f)
        && owner.completion.disposition == RizSingleClockCandidate && (owner.completion.flags & RizSingleAuthorityCaptured), "armed completion preserves original and candidate evidence");
    Check(Read(owner).phase == RizMusicClockArmed && Stats(owner).applied == 1, "armed phase and actual applied counter");
}
void BusyArm()
{
    Begin("blocked actual original, busy Arm, drain, successful Arm");
    auto& owner = Configure(2); Prepare(owner); owner.armOnExit = 1; owner.armOnExitResult = 99;
    const auto thread = Block(owner);
    const auto during = Read(owner); const auto stats = Stats(owner);
    Check(during.phase == RizMusicClockDormant && during.inFlight == 1 && stats.inFlight == 1 && stats.originalCalls == 1, "readback and stats reflect actual native frame");
    Check(api.arm(owner.receipt) == 8 && Stats(owner).authorityActive == 0, "real original inFlight forbids arm with status8");
    Check(owner.readerCalls == 0, "blocked dormant frame has no ClockRead");
    Drain(owner, thread);
    Check(owner.workerResult.returned && owner.workerResult.bits == owner.originalBits && owner.readerCalls == 0, "dormant frame never gains authority retroactively");
    Check(owner.armOnExitResult == 8, "Arm in real exit callback is busy until entire frame drains");
    owner.armOnExit = 0;
    Check(api.arm(owner.receipt) == 0, "Arm succeeds only after real native drain");
    Check(Invoke(owner).bits == Bits(23.0f) && owner.readerCalls == 1, "next actual frame uses armed selected rate");
}
void BusyPrepare()
{
    Begin("Prepare cannot admit an actual inFlight baseline");
    auto& owner = Configure(3); const auto thread = Block(owner);
    void* receipt = reinterpret_cast<void*>(1);
    Check(api.prepare(owner.slot, 0, &owner.policy, &receipt) == 8 && receipt == nullptr, "unclaimed but actual busy baseline rejected");
    Drain(owner, thread); Prepare(owner);
    Check(Read(owner).phase == RizMusicClockDormant && owner.readerCalls == 0, "drained baseline admits dormant only");
}
void InvalidReceipts()
{
    Begin("foreign, raw, copied and inaccessible receipts rejected before dereference");
    auto& owner = owners[1]; auto info = Read(owner); auto copy = info;
    std::array<unsigned char, 40> opaqueCopy{};
    std::memcpy(opaqueCopy.data(), owner.receipt, opaqueCopy.size());
    void* inaccessible = VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_NOACCESS);
    Check(inaccessible != nullptr, "owned inaccessible memory allocated");
    void* invalid[]{nullptr, reinterpret_cast<void*>(1), reinterpret_cast<void*>(~std::uintptr_t{0}), &copy, &owner.policy, opaqueCopy.data(), inaccessible};
    for (const auto pointer : invalid) {
        RizMusicClockBootstrapInfoV1 output{1, sizeof(output)};
        Check(api.arm(pointer) == 1, "unregistered Arm rejected safely");
        Check(api.cancel(pointer) == 1, "unregistered Cancel rejected safely");
        Check(api.read(pointer, &output) == 1, "unregistered Read rejected safely");
    }
    Check(api.read(owner.receipt, nullptr) == 1, "null info rejected");
    copy.abiVersion = 2; Check(api.read(owner.receipt, &copy) == 1, "wrong info version rejected");
    copy = info; copy.byteSize = 191; Check(api.read(owner.receipt, &copy) == 1, "wrong info size rejected");
    Check(api.read(owner.receipt, static_cast<RizMusicClockBootstrapInfoV1*>(inaccessible)) == 1, "inaccessible info rejected safely");
    if (inaccessible) Check(VirtualFree(inaccessible, 0, MEM_RELEASE) != 0, "owned inaccessible memory released");
    Check(Read(owner).phase == RizMusicClockArmed && Invoke(owner).bits == Bits(23.0f), "foreign/copy operations leave genuine armed receipt unchanged");
}
void Cancel()
{
    Begin("dormant cancellation retains receipt and immutable policy");
    auto& owner = Configure(4); Prepare(owner); const auto initial = Read(owner);
    const auto thread = Block(owner);
    Check(api.cancel(owner.receipt) == 0, "dormant cancellation closes while original is still live");
    const auto closed = Read(owner);
    Check(closed.phase == RizMusicClockCancelled && closed.inFlight == 1 && closed.receipt == initial.receipt
        && closed.epoch == initial.epoch && std::memcmp(&closed.policy, &initial.policy, sizeof(closed.policy)) == 0, "cancel retains exact policy, receipt, epoch and live count");
    Check(api.arm(owner.receipt) == 4 && api.cancel(owner.receipt) == 2, "closed gate cannot arm and repeated cancellation rejects");
    auto next = owner.policy; next.policyGeneration = 2;
    Check(api.claim(owner.slot, &owner.policy) == 4 && api.publish(owner.slot, 1, &next) == 4, "cancelled binding cannot reopen");
    Drain(owner, thread);
    Check(owner.workerResult.bits == owner.originalBits && owner.readerCalls == 0 && Stats(owner).authorityActive == 0, "cancelled inFlight frame returns exact original");
}
void WithdrawAndFault()
{
    Begin("existing withdrawal and fault are reflected by bootstrap readback");
    auto& withdrawn = Configure(5); Prepare(withdrawn);
    Check(api.withdraw(withdrawn.slot, 1) == 0 && Read(withdrawn).phase == RizMusicClockCancelled, "existing broad authority withdrawal reads phase3");
    Check(api.arm(withdrawn.receipt) == 4, "withdrawn dormant gate cannot arm");
    auto& exact = Configure(6); Prepare(exact);
    Check(api.withdrawClock(exact.slot, 2) == 5 && Read(exact).phase == RizMusicClockDormant, "stale exact withdrawal leaves dormant gate intact");
    Check(api.withdrawClock(exact.slot, 1) == 0 && Read(exact).phase == RizMusicClockCancelled && api.arm(exact.receipt) == 4, "matching exact withdrawal closes dormant gate");
    auto& faulted = Configure(7); Prepare(faulted);
    Check(api.fault(faulted.slot) == 0 && Read(faulted).unknown == 1 && api.arm(faulted.receipt) == 3, "MarkFault is visible and blocks arm");
    Check(Invoke(faulted).bits == faulted.originalBits && faulted.readerCalls == 0, "faulted dormant invocation still runs original without ClockRead");
    Check(api.cancel(faulted.receipt) == 0 && Read(faulted).phase == RizMusicClockCancelled && Read(faulted).unknown == 1, "faulted dormant responsibility can still close exactly");
    auto& unknown = Configure(8); Check(api.fault(unknown.slot) == 0, "baseline marked faulted");
    void* receipt = nullptr;
    Check(api.prepare(unknown.slot, 0, &unknown.policy, &receipt) == 3 && receipt == nullptr, "Unknown baseline rejected");
    auto& closed = Configure(9); Check(api.withdraw(closed.slot, 1) == 0, "baseline logically closed");
    Check(api.prepare(closed.slot, 0, &closed.policy, &receipt) == 4 && receipt == nullptr, "closed baseline rejected");
    auto& observers = Configure(10); Check(api.withdraw(observers.slot, 2) == 0, "baseline observer duty closed");
    Check(api.prepare(observers.slot, 0, &observers.policy, &receipt) == 4 && receipt == nullptr, "closed observer baseline rejected");
    auto& claimed = Configure(11); Check(api.claim(claimed.slot, &claimed.policy) == 0, "ordinary claimed baseline remains supported");
    Check(api.prepare(claimed.slot, 0, &claimed.policy, &receipt) == 2 && receipt == nullptr, "previously claimed/published baseline rejected");
}
void ArmedSuccessor()
{
    Begin("armed ordinary successor, stale cancellation and exact cleanup");
    auto& owner = Configure(12); Prepare(owner); Check(api.arm(owner.receipt) == 0, "initial receipt armed");
    const auto initial = Read(owner); auto successor = owner.policy; successor.policyGeneration = 2; successor.rate = 0.75;
    Check(api.publish(owner.slot, 1, &successor) == 0, "ordinary increasing same-identity successor publishes after arm");
    owner.policy = successor;
    const auto current = Read(owner);
    Check(current.phase == RizMusicClockArmed && current.initialGeneration == 1 && current.currentGeneration == 2
        && current.receipt == initial.receipt && current.epoch == initial.epoch, "readback distinguishes immutable initial from current generation");
    Check(std::memcmp(&current.policy, &successor, sizeof(successor)) == 0, "readback returns full exact successor policy");
    Check(api.cancel(owner.receipt) == 2 && api.arm(owner.receipt) == 2 && Stats(owner).authorityActive == 1, "old bootstrap operations cannot cancel or rearm successor");
    Check(api.withdrawClock(owner.slot, 1) == 5 && Invoke(owner).bits == Bits(21.5f), "stale exact withdrawal cannot revoke current selected-rate result");
    Check(api.withdrawClock(owner.slot, 2) == 0 && Read(owner).phase == RizMusicClockCancelled, "current exact withdrawal performs armed cleanup");
    const auto reads = owner.readerCalls;
    Check(Invoke(owner).bits == owner.originalBits && owner.readerCalls == reads && Stats(owner).authorityActive == 0, "closed successor has no further ClockRead");
}
void OriginalException()
{
    Begin("real original SEH during dormancy unwinds, drains and can arm later");
    auto& owner = Configure(13); Prepare(owner); InterlockedExchange(&owner.mode, 2);
    const auto direct = InvokeAt(owner, &Original);
    const auto result = Invoke(owner);
    std::printf("SEH direct flags=%u dormant flags=%u\n", direct.exceptionFlags, result.exceptionFlags);
    Check(!result.returned && !direct.returned && result.exceptionCode == 0xe0525301u
        && result.exceptionCode == direct.exceptionCode && result.exceptionFlags == direct.exceptionFlags
        && result.exceptionCount == 3 && result.exceptionCount == direct.exceptionCount, "actual original exception reaches caller unchanged from direct native baseline");
    Check(result.info[0] == reinterpret_cast<std::uintptr_t>(&owner) && result.info[1] == reinterpret_cast<std::uintptr_t>(owner.policy.method)
        && result.info[2] == 0xfedcba9876543210ull, "actual original exception arguments preserved");
    const auto stats = Stats(owner);
    Check(stats.originalCalls == 1 && stats.entered == 1 && stats.exited == 1 && stats.inFlight == 0 && stats.unknown == 0, "original SEH executes native finally and real drain");
    Check(owner.enters == 1 && owner.exits == 1 && owner.readerCalls == 0 && (owner.completion.flags & RizSingleAbnormal)
        && !(owner.completion.flags & RizSingleAuthorityCaptured), "dormant original SEH preserves captured observer completion without authority");
    InterlockedExchange(&owner.mode, 0);
    Check(api.arm(owner.receipt) == 0 && Invoke(owner).bits == Bits(23.0f), "drained original exception does not fabricate Unknown and later arm works");
    InterlockedExchange(&owner.mode, 2); const auto armed = Invoke(owner);
    Check(!armed.returned && armed.exceptionCode == 0xe0525301u && owner.readerCalls == 1 && Stats(owner).inFlight == 0, "armed original SEH is still outside authority catch and invokes no reader");
}
void CallbackFaults()
{
    Begin("real dormant observer fault and armed clock fault reject authority");
    auto& observer = Configure(14); Prepare(observer); observer.observerFault = 1;
    Check(Invoke(observer).bits == observer.originalBits && observer.originalCalls == 1 && observer.readerCalls == 0, "dormant enter SEH still calls original exactly once");
    Check(Read(observer).unknown == 1 && api.arm(observer.receipt) == 3 && Stats(observer).enterFaults == 1, "actual dormant observer fault prevents arm");
    Check(api.cancel(observer.receipt) == 0, "faulted dormant observer gate can close");
    auto& reader = Configure(15); Prepare(reader); Check(api.arm(reader.receipt) == 0, "reader-fault gate armed"); reader.readerFault = 1;
    Check(Invoke(reader).bits == reader.originalBits && reader.originalCalls == 1 && reader.readerCalls == 1, "actual armed clock SEH returns exact original");
    Check(Read(reader).unknown == 1 && api.arm(reader.receipt) == 3 && api.cancel(reader.receipt) == 2, "clock fault reflected without giving bootstrap cancellation armed authority");
    Check(api.withdrawClock(reader.slot, 1) == 0 && Read(reader).phase == RizMusicClockCancelled && Stats(reader).sourceFaults == 1, "existing exact withdrawal cleans armed faulted policy");
}
template<class Function> bool Load(HMODULE library, Function& destination, const char* name)
{
    destination = reinterpret_cast<Function>(GetProcAddress(library, name));
    if (!destination) std::fprintf(stderr, "missing export %s\n", name);
    return destination != nullptr;
}
int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) { std::fprintf(stderr, "usage: OwnedDormantBootstrap.Fixture.exe isolated-helper.dll\n"); return 2; }
    const auto library = LoadLibraryW(argv[1]);
    if (!library) { std::fprintf(stderr, "LoadLibraryW failed %lu\n", GetLastError()); return 3; }
    bool loaded = true;
#define LOAD(member, name) loaded = Load(library, api.member, "RizSingleReturnPrototype_" name) && loaded
    LOAD(limit, "GetLimit"); LOAD(entry, "GetEntry"); LOAD(configure, "Configure"); LOAD(claim, "ClaimMusicClock");
    LOAD(publish, "PublishMusicClock"); LOAD(readPolicy, "ReadClockPolicy"); LOAD(withdraw, "Withdraw");
    LOAD(withdrawClock, "WithdrawMusicClock"); LOAD(fault, "MarkFault"); LOAD(stats, "GetStats");
    LOAD(prepare, "PrepareDormantMusicClock"); LOAD(arm, "ArmDormantMusicClock");
    LOAD(cancel, "CancelDormantMusicClock"); LOAD(read, "ReadDormantMusicClock");
#undef LOAD
    if (!loaded) return 4;
    AbiAndBadBaseline(); DormantAndArm(); BusyArm(); BusyPrepare(); InvalidReceipts(); Cancel();
    WithdrawAndFault(); ArmedSuccessor(); OriginalException(); CallbackFaults();
    Begin("logical withdrawal inside captured native reader preserves healthy physical owner");
    auto& withdrawnReader = Configure(16); Prepare(withdrawnReader); Check(api.arm(withdrawnReader.receipt) == 0, "armed reader-withdrawal baseline");
    withdrawnReader.readerFault = 2;
    const auto withdrawnResult = Invoke(withdrawnReader); const auto withdrawnStats = Stats(withdrawnReader);
    Check(withdrawnResult.returned && withdrawnResult.bits == withdrawnReader.originalBits && withdrawnReader.readerCalls == 1,
        "captured reader withdrawal forwards exact original bits");
    Check(withdrawnStats.unknown == 0 && withdrawnStats.sourceFaults == 0 && withdrawnStats.inFlight == 0
        && withdrawnStats.authorityActive == 0 && withdrawnReader.completion.disposition == RizSingleWithdrawn,
        "logical reader withdrawal does not poison physical transport or erase its actual drain");
    for (auto& owner : owners) { if (owner.entered) CloseHandle(owner.entered); if (owner.release) CloseHandle(owner.release); }
    std::printf("{\"schemaVersion\":1,\"kind\":\"owned-native-dormant-bootstrap-gate\",\"passed\":%s,\"cases\":%u,\"checks\":%u,\"failures\":%u,\"nativeReceipts\":%u,\"actualNativeCalls\":true,\"oldFloatMatrixRepeated\":false,\"oldMachineMatrixRepeated\":false,\"productionAdmission\":false,\"gameExecuted\":false,\"installed\":false}\n",
        failures == 0 ? "true" : "false", cases, checks, failures, receiptCount);
    return failures == 0 ? 0 : 1;
}
