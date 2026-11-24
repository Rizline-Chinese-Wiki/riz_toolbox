#pragma once
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstddef>
#include <cstdint>
#include "../RizToolbox.EngineBootstrap/EngineTransportOriginals.h"

// These records are populated by actual owned machine calls and the Windows
// single-step exception CONTEXT, never by caller-submitted observation frames.
struct TransportCallRecord {
    std::uint32_t kind,variant,branch,exceptionKind,caught,passed,reserved0,reserved1;
    std::uint64_t args[4];
    // RAX,R11,RSP,RFLAGS,RCX,RDX,R8,R9 at the exact continuation entry.
    std::uint64_t tail[8];
    std::uint64_t resultRax,callerRspBefore,callerRspAfter,canaryBefore,canaryAfter,r11Write;
    std::uint64_t afterNv[8];
    std::uint32_t lastErrorBefore,lastErrorAfter,tailCount,exceptionCode;
};
struct TransportUnwindRecord {
    std::uint32_t kind,variant,branch,offset,passed,lookup,functionBegin,functionEnd,unwindRva,prologSize;
    std::uint64_t pc,imageBase,expectedRip,expectedRsp,beforeRsp,afterRip,afterRsp;
    std::uint64_t beforeNv[8],afterNv[8];
    std::uint64_t tailR11,rflags;
    std::uint32_t actualContext,reserved0,reserved1,reserved2;
};
struct TransportRawHeader {
    std::uint64_t magic;
    std::uint32_t version,headerBytes,callRecordBytes,unwindRecordBytes;
    std::uint32_t checks,failures,callCount,unwindCount;
    std::uint32_t baselineCalls,originalCalls,sehCaught,cppCaught;
    std::uint32_t actualContext,selfOwnedOnly,sourcePatched,gameExecuted,persistenceChecks,reserved0;
    std::uint64_t rawBytes,reserved[5];
};
static_assert(sizeof(TransportCallRecord)==256&&offsetof(TransportCallRecord,passed)==20);
static_assert(offsetof(TransportCallRecord,tail)==64&&offsetof(TransportCallRecord,resultRax)==128);
static_assert(offsetof(TransportCallRecord,afterNv)==176&&offsetof(TransportCallRecord,lastErrorBefore)==240);
static_assert(sizeof(TransportUnwindRecord)==256&&offsetof(TransportUnwindRecord,actualContext)==240);
static_assert(sizeof(TransportRawHeader)==128&&offsetof(TransportRawHeader,checks)==24&&offsetof(TransportRawHeader,rawBytes)==80);

extern "C" {
extern TransportCallRecord* RtTransportActiveCall;
extern std::uint32_t RtTransportTraceRequested;
std::uint64_t RtTransportInvoke(void* target,const std::uint64_t* args,TransportCallRecord* record);
extern const unsigned char RtTransportInvokeReturn;
std::uint64_t RtTransportOwnedBody(TransportCallRecord*);
#define TRANSPORT_TARGET(name) \
    std::uint64_t RtTransportBaseline##name(void*,void*,void*,void*); \
    extern const unsigned char RtTransportBaseline##name##PrefixEnd; \
    extern const unsigned char RtTransportBaseline##name##End;
TRANSPORT_TARGET(Register) TRANSPORT_TARGET(Remove) TRANSPORT_TARGET(Consume)
TRANSPORT_TARGET(Pump) TRANSPORT_TARGET(Drain) TRANSPORT_TARGET(Callback)
TRANSPORT_TARGET(Yield) TRANSPORT_TARGET(Dispatch) TRANSPORT_TARGET(Batch)
#undef TRANSPORT_TARGET
}
