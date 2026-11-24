#pragma once
#include "MidContext.h"
#include "OperationContext.h"

#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZTSE_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZTSE_EXPORT extern "C"
#endif

enum RizTweenSingleEventStageV1 : std::uint32_t {
    RizTweenSingleEventMid, RizTweenSingleEventBefore,
    RizTweenSingleEventReturned, RizTweenSingleEventAbnormal
};
enum RizTweenSingleEventFlagsV1 : std::uint32_t {
    RizTweenSingleEventObserverFaulted=1, RizTweenSingleEventMissingPost=2,
    RizTweenSingleEventParentAbnormal=4, RizTweenSingleEventTargetReadFaulted=8
};
struct RizTweenSingleEventFrameV1 {
    std::uint32_t abiVersion,byteSize,stage,site;
    std::uint32_t threadId,flags,reserved0,reserved1;
    std::uint64_t epoch,parentEpoch,parentSequence,childSequence,parentManagedToken;
    const void* event;
    const void* list;
    const void* invokable;
    const void* actualTarget;
    const void* argRcx;
    const void* argRdx;
    const void* argR8;
    const void* argR9;
    const void* parentMethod;
    const void* parentCaller;
    const void* sourceReturnPc;
    const void* transportReturnPc;
    std::uint64_t sourceRsp,index;
    std::uint32_t valueBits,reserved2;
    const RizEventMidFrameV1* machineFrame;
};
using RizTweenSingleEventNotifyV1=std::uint32_t(__cdecl*)(const RizTweenSingleEventFrameV1*,void*);
struct RizTweenSingleEventObserverV1 {
    std::uint32_t abiVersion,byteSize;
    RizTweenSingleEventNotifyV1 notify;
    void* user;
};
struct RizTweenSingleEventInfoV1 {
    std::uint32_t abiVersion,byteSize,configured,attached,unknown,parentCapacity;
    std::uint64_t epoch,parentEpoch,parentsEntered,parentsExited,parentsInFlight,mids;
    std::uint64_t childrenEntered,childrenExited,childrenInFlight,callbackFaults,rejectedReads,missingPosts;
    std::uint64_t retainedParents,retainedChildren,overflow;
};
RIZTSE_EXPORT void* __cdecl RizTweenSingleEventContext_GetMidEntry();
RIZTSE_EXPORT void* __cdecl RizTweenSingleEventContext_GetPreEntry();
RIZTSE_EXPORT void* __cdecl RizTweenSingleEventContext_GetPostEntry();
RIZTSE_EXPORT std::uint32_t __cdecl RizTweenSingleEventContext_Attach(std::uint64_t epoch,
    const RizTweenSingleEventObserverV1* observer);
RIZTSE_EXPORT std::uint32_t __cdecl RizTweenSingleEventContext_GetInfo(RizTweenSingleEventInfoV1* info);
// Only the actual current TLS delivery address is read. Copied/stale/worker
// frames do not grant source authority. No root or game call is performed.
RIZTSE_EXPORT std::uint32_t __cdecl RizTweenSingleEventContext_ReadCurrent(
    const RizTweenSingleEventFrameV1* actual,RizTweenSingleEventFrameV1* copy,RizEventMidFrameV1* machineCopy);

// Private same-helper publication input. This is not exported and supplies no
// late Ready bypass. Future FirstInstall must verify/publish all seven spans.
struct RizTweenSingleEventConfigV1 {
    std::uint32_t abiVersion,byteSize;
    std::uint64_t parentEpoch;
    const void* midReturnPc;
    const void* sourceReturnPc[3];
    const void* postTransportReturnPc[3];
    const void* directTarget;
};
std::uint32_t RizTweenSingleEventConfigure(const RizTweenSingleEventConfigV1* config);
// Only actual OperationContext creates/consumes these private native loans.
// CaptureEntry is called before Operation's observer snapshot. Begin never
// trusts a caller-created public Single frame as a physical parent issuer.
struct RizTweenSingleEventEntryCapture {const void* observer{};std::uint64_t epoch{};};
struct RizTweenSingleEventParent {void* record{};std::uint64_t sequence{};};
RizTweenSingleEventEntryCapture RizTweenSingleEventCaptureEntry();
RizTweenSingleEventParent RizTweenSingleEventBegin(const RizTweenSingleEventEntryCapture& capture,
    const RizOperationFrameV1& frame,std::uint64_t parentEpoch);
void RizTweenSingleEventOriginal(RizTweenSingleEventParent parent,std::uintptr_t managedToken);
void RizTweenSingleEventClose(RizTweenSingleEventParent parent,bool returned);

static_assert(sizeof(RizTweenSingleEventConfigV1)==80);
static_assert(sizeof(RizTweenSingleEventObserverV1)==24);
static_assert(sizeof(RizTweenSingleEventFrameV1)==200);
static_assert(offsetof(RizTweenSingleEventFrameV1,parentSequence)==48);
static_assert(offsetof(RizTweenSingleEventFrameV1,machineFrame)==192);
static_assert(sizeof(RizTweenSingleEventInfoV1)==144);
