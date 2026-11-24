#pragma once
#include <cstdint>
#include <cstddef>

// Private preparation API. This is neither a publication record nor a startup
// permission. The caller owns the validated image and writable near allocation.
// No source code, page protection, Configure claim or function table is changed.
namespace RizEngineTransportRelays {
inline constexpr std::uint32_t Version=1,SiteCount=23,WindowCount=36,TypedCount=13;
inline constexpr std::uint32_t ArenaMinimumBytes=8192,ArenaUsedBytes=6400;
inline constexpr std::uint32_t TableOffset=0,SlotOffset=512,SlotBytes=256;
// All outer records are sixteen-byte CHAININFO. Dispatch sites 17..20 refer
// to an explicit synthetic partial-primary at slot+192: it retains the actual
// source Begin/End identity while describing only the already-executed stack.
inline constexpr std::uint32_t LiteralOffset=160,UnwindOffset=176,UnwindBytes=16,DispatchUnwindBytes=12;
inline constexpr std::uint32_t DispatchPrimaryUnwindOffset=UnwindOffset+UnwindBytes;
enum Result:std::uint32_t { Success=0,InvalidInput=1,BadImage=2,BadBody=3,
    BadRuntimeFunction=4,BadSource=5,BadEntry=6,AddressRange=7,Overlap=8,
    InvalidMemory=9,EmissionMismatch=10,Busy=11 };
// Byte preparation facts only. Neither flag proves loaded module/file identity,
// actual mapping protections, main thread, startup safety or production Ready.
enum Flags:std::uint32_t { FactsVerified=1,NearPrepared=2 };
enum Object:std::uint32_t { NoObject=0,ImageObject=1,FunctionObject=2,
    UnwindObject=3,WindowObject=4,SiteObject=5,CallObject=6 };
struct Request {
    std::uint32_t version,byteSize;
    std::uint64_t fixedImageBase,fixedImageBytes,nearRw,nearCapacity;
    std::uint64_t typedEntries[TypedCount];
    std::uint64_t inlineEntry,rowEntry,waitPhaseEntry;
};
struct RuntimeFunction { std::uint32_t beginRva,endRva,unwindRva; };
struct Window {
    std::uint32_t index,role,roleIndex,byteCount;
    std::uint64_t source,relay,entry;
    std::uint8_t expected[32],patch[32];
};
struct Site {
    // family: fixed facts 0=Inline,1=DrainRow,2=WaitPhase; not V5 family index.
    std::uint32_t index,family,phase,windowIndex,functionIndex,sourceRfIndex;
    // relativeOffset is within the supplied near block; toImageStart is the
    // positive DWORD RVA relative to fixedImageBase used by RUNTIME_FUNCTION.
    // unwindBytes is the actual encoded size (12 or 16), not slot capacity.
    std::uint32_t relativeOffset,toImageStart,codeBytes,unwindBytes;
    std::uint64_t logicalSitePc,physicalPatchPc,relay,relayReturnPc,probeEntry;
    std::uint64_t continuation,alternateContinuation,literal,unwind;
    // sourceRfIndex is an immutable source fact. It is not necessarily a
    // CHAININFO target: Dispatch's four early sites must omit its later RBX save.
    RuntimeFunction runtimeFunction;
    std::uint32_t expandedPhysicalBlock;
};
struct Plan {
    std::uint32_t version,byteSize,result,flags,failureObject,failureIndex;
    std::uint32_t nearWriteAttempted,nearBytesWritten,siteCount,windowCount;
    std::uint32_t tableCount,usedBytes;
    std::uint64_t fixedImageBase,nearRw,nearCapacity,tableBase,tableAddress;
    std::uint32_t arenaToImageStart,reserved;
    Site sites[SiteCount];
    Window windows[WindowCount];
    // Unpublished staging bytes. The entire plan is checked before these bytes
    // are copied once to nearRw; partial/failed plans are not executable plans.
    std::uint8_t preparedBytes[ArenaUsedBytes];
};
// All validation and address planning precede the first near write. On failure
// NearPrepared is absent. InvalidMemory during emission retains exact attempted/
// completed byte progress; the caller must retain its allocation accordingly.
// Invalid/overlapping Request or Plan storage is rejected without touching Plan.
// Caller proves actual module/file ownership and RW allocation/protection; this
// routine independently checks the supplied mapped bytes against pinned facts.
// SEH catches inaccessible image/near/storage pointers as InvalidMemory; rejected
// invalid/overlapping storage cannot safely be modified, and remains untouched.
Result Prepare(const Request* request,Plan* output);
// Read-only canonical re-emission. Requires original fixed source bodies before
// any source write. Independently reconstructs every site/window/unwind byte
// from fixed facts; matching caller Plan and near bytes alone is insufficient.
// Uses private BSS staging under a nonwaiting gate; never writes near/source.
Result VerifyPrepared(const Plan* prepared);
static_assert(sizeof(RuntimeFunction)==12&&alignof(Request)==8);
static_assert(sizeof(Request)==168&&sizeof(Site)==128&&sizeof(Window)==104);
static_assert(sizeof(Plan)==13184&&offsetof(Plan,preparedBytes)==6784);
static_assert(ArenaUsedBytes==SlotOffset+SiteCount*SlotBytes);
}
