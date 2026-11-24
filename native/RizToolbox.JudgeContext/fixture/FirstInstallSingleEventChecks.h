#pragma once
#include <Windows.h>
#include <array>
#include <cstdio>
#include <cstring>
#include "../FirstInstall.h"
#include "../TweenSingleEventContext.h"
#include "../FirstInstallProfile.h"

// Independent mapped-only checks. No entry, target, stub or game API is called.
namespace RizSingleEventChecks {
using CheckFn=void(*)(bool,const char*,int);
#define RIZ_SINGLE_CHECK(value) check(!!(value),#value,__LINE__)
constexpr unsigned Rvas[7]{0xbc1087,0xbc1107,0xbc110d,0xbc115d,0xbc1162,0xbc121b,0xbc1221};
constexpr unsigned Widths[7]{9,6,9,5,9,6,9};
constexpr unsigned Returns[3]{0xbc110d,0xbc1162,0xbc1221};
struct Snapshot{unsigned char bytes[7][9]{};DWORD protection[7]{};};
struct Evidence{unsigned sources{},blockBytes{},functions{},boundaries{};bool countersZero{},unwind{};};
inline Snapshot Capture(const unsigned char* base,CheckFn check)
{
    Snapshot snapshot{};
    for(unsigned span=0;span!=7;++span){MEMORY_BASIC_INFORMATION memory{};
        RIZ_SINGLE_CHECK(VirtualQuery(base+Rvas[span],&memory,sizeof(memory))==sizeof(memory)&&memory.Type==MEM_IMAGE&&memory.AllocationBase==base);
        snapshot.protection[span]=memory.Protect;std::memcpy(snapshot.bytes[span],base+Rvas[span],Widths[span]);
    }
    return snapshot;
}
inline void Relative(unsigned char* local,const unsigned char* physical,const void* target,unsigned length,unsigned opcode)
{
    const auto delta=static_cast<std::int32_t>(reinterpret_cast<std::intptr_t>(target)-reinterpret_cast<std::intptr_t>(physical+length));
    if(length==5)local[0]=static_cast<unsigned char>(opcode);else{local[0]=0x0f;local[1]=static_cast<unsigned char>(opcode);}
    std::memcpy(local+length-4,&delta,4);
}
inline Evidence Validate(const unsigned char* base,HMODULE helper,const RizFirstInstallInfoV7& packet,const Snapshot& before,CheckFn check)
{
    Evidence evidence{};const auto& info=packet.singleEvent;const auto* block=static_cast<const unsigned char*>(info.allocation);
    RIZ_SINGLE_CHECK(sizeof(packet)==2528&&packet.publication.common.abiVersion==7&&packet.expectedWrites==64&&packet.publication.common.writesStarted==64);
    RIZ_SINGLE_CHECK(info.capabilityVersion==1&&info.byteSize==416&&info.phase==RizFirstInstallReady&&!info.unknown
        &&info.operationId==25&&info.kind==3&&info.slot==31&&info.expectedWrites==7&&info.epoch&&info.parentEpoch==packet.operations[25].epoch
        &&info.configuredMask==0x7f&&info.publishedMask==0x7f&&info.attemptedWrites==7&&info.completedWrites==7&&!info.reserved);
    RIZ_SINGLE_CHECK(block&&info.allocationSize==4096&&(reinterpret_cast<std::uintptr_t>(block)&0xfffu)==0
        &&info.functionTableBase==reinterpret_cast<std::uint64_t>(base)&&info.registeredFunctionCount==3&&info.lookupChecks==15&&info.boundaryChecks==15);
    if(!block)return evidence;
    MEMORY_BASIC_INFORMATION allocation{};
    RIZ_SINGLE_CHECK(VirtualQuery(block,&allocation,sizeof(allocation))==sizeof(allocation)&&allocation.AllocationBase==block
        &&allocation.Type==MEM_PRIVATE&&allocation.Protect==PAGE_EXECUTE_READ&&allocation.RegionSize>=4096);
    const char* names[]{"RizTweenSingleEventContext_GetMidEntry","RizTweenSingleEventContext_GetPreEntry","RizTweenSingleEventContext_GetPostEntry"};
    const void* entries[]{info.midEntry,info.preEntry,info.postEntry};
    const void* relays[]{info.midRelay,info.preRelay,info.postRelay};
    std::array<unsigned char,4096> expected{};expected.fill(0xcc);
    for(unsigned entry=0;entry!=3;++entry){
        using Getter=void*(__cdecl*)();Getter getter{};const auto symbol=GetProcAddress(helper,names[entry]);std::memcpy(&getter,&symbol,sizeof(getter));
        RIZ_SINGLE_CHECK(getter&&getter()==entries[entry]&&relays[entry]==block+0x500+entry*0x20);
        auto* tail=expected.data()+0x500+entry*0x20;const unsigned char head[]{0xff,0x25,0,0,0,0};
        std::memcpy(tail,head,6);std::memcpy(tail+6,entries+entry,8);
    }
    RIZ_SINGLE_CHECK(info.midReturnPc==base+0xbc108c&&info.directTarget==base+0x154e5b0&&info.functionTable==block+0x380);
    for(unsigned span=0;span!=7;++span){
        RIZ_SINGLE_CHECK(info.spanPc[span]==base+Rvas[span]&&info.spanWidth[span]==Widths[span]&&info.protection[span]==before.protection[span]);
        MEMORY_BASIC_INFORMATION source{};RIZ_SINGLE_CHECK(VirtualQuery(info.spanPc[span],&source,sizeof(source))==sizeof(source)
            &&source.Type==MEM_IMAGE&&source.AllocationBase==base&&source.Protect==before.protection[span]);
        unsigned char patch[9];std::memset(patch,0x90,sizeof(patch));const auto start=span==1||span==5?1u:0u;
        const bool call=span==0||span%2==1;const auto* target=span==0?info.midRelay:call?info.preRelay:info.postStub[(span-2)/2];
        Relative(patch+start,base+Rvas[span]+start,target,5,call?0xe8u:0xe9u);
        RIZ_SINGLE_CHECK(std::memcmp(info.spanPc[span],patch,Widths[span])==0&&std::memcmp(before.bytes[span],patch,Widths[span])!=0);++evidence.sources;
    }
    constexpr unsigned char unwind[]{0x21,0,10,0,0,0xe4,7,0,0,0xc4,8,0,0,0x74,15,0,0,0x64,14,0,0,0x34,13,0,
        0xf0,0x0f,0xbc,0,0x6a,0x10,0xbc,0,0xa0,0xe3,0xd4,1};
    auto* table=reinterpret_cast<RUNTIME_FUNCTION*>(expected.data()+0x380);
    for(unsigned site=0;site!=3;++site){
        auto* local=expected.data()+site*0x100;const auto* stub=block+site*0x100;
        Relative(local,stub,info.postRelay,5,0xe8);const unsigned char replay[]{0xff,0xc6,0x8b,0xce};std::memcpy(local+5,replay,4);
        Relative(local+9,stub+9,base+0xbc1090,6,0x84);Relative(local+15,stub+15,base+0xbc1090,6,0x85);
        std::memcpy(local+0x40,unwind,sizeof(unwind));
        table[site]={static_cast<DWORD>(stub-base),static_cast<DWORD>(stub-base+21),static_cast<DWORD>(stub-base+0x40)};
        RIZ_SINGLE_CHECK(info.sourceReturnPc[site]==base+Returns[site]&&info.postStub[site]==stub&&info.postTransportReturnPc[site]==stub+5
            &&info.unwindInfo[site]==stub+0x40&&info.stubSize[site]==21&&info.unwindSize[site]==36);
        for(const auto offset:{0u,5u,7u,9u,15u}){
            const auto pc=reinterpret_cast<DWORD64>(stub+offset);DWORD64 module{};auto* rf=RtlLookupFunctionEntry(pc,&module,nullptr);
            RIZ_SINGLE_CHECK(rf&&module==reinterpret_cast<DWORD64>(base)&&std::memcmp(rf,table+site,sizeof(*rf))==0);if(!rf)continue;
            alignas(16) std::array<DWORD64,32> stack{};stack[11]=reinterpret_cast<DWORD64>(&Capture);
            stack[10]=0x55;stack[9]=0x13;stack[6]=0x15;stack[7]=0x14;stack[8]=0x12;stack[13]=0x33;stack[14]=0x66;stack[15]=0x77;
            std::memset(reinterpret_cast<unsigned char*>(stack.data())+0x20,0x6a,16);
            CONTEXT context{};context.ContextFlags=CONTEXT_ALL;context.Rip=pc;context.Rsp=reinterpret_cast<DWORD64>(stack.data());void* handler{};DWORD64 establisher{};
            RtlVirtualUnwind(UNW_FLAG_NHANDLER,module,pc,rf,&context,&handler,&establisher,nullptr);
            RIZ_SINGLE_CHECK(context.Rip==stack[11]&&context.Rsp==reinterpret_cast<DWORD64>(stack.data())+0x60&&context.Rbp==stack[10]
                &&context.R13==stack[9]&&context.R15==stack[6]&&context.R14==stack[7]&&context.R12==stack[8]&&context.Rbx==stack[13]
                &&context.Rsi==stack[14]&&context.Rdi==stack[15]);++evidence.boundaries;
        }
        ++evidence.functions;
    }
    RIZ_SINGLE_CHECK(std::memcmp(block,expected.data(),expected.size())==0);evidence.blockBytes=4096;evidence.unwind=evidence.boundaries==15;
    // Every byte outside the seven patches is independently compared as well.
    for(const auto& row:RizFirstInstallProfile::OperationContinuations){
        std::array<unsigned char,491> body{};RIZ_SINGLE_CHECK(row.originalSize<=body.size());if(row.originalSize>body.size())continue;
        std::memcpy(body.data(),row.original,row.originalSize);
        for(unsigned span=0;span!=7;++span)if(Rvas[span]>=row.rva&&Rvas[span]+Widths[span]<=row.endRva)
            std::memcpy(body.data()+Rvas[span]-row.rva,info.spanPc[span],Widths[span]);
        RIZ_SINGLE_CHECK(std::memcmp(base+row.rva,body.data(),row.originalSize)==0
            &&std::memcmp(base+row.unwindRva,row.unwind,row.unwindSize)==0);
    }
    using Reader=std::uint32_t(__cdecl*)(RizTweenSingleEventInfoV1*);Reader reader{};
    const auto symbol=GetProcAddress(helper,"RizTweenSingleEventContext_GetInfo");std::memcpy(&reader,&symbol,sizeof(reader));RizTweenSingleEventInfoV1 native{1,sizeof(native)};
    evidence.countersZero=reader&&reader(&native)==0&&native.configured==1&&!native.attached&&!native.unknown&&native.epoch==info.epoch&&native.parentEpoch==info.parentEpoch
        &&!native.parentsEntered&&!native.parentsExited&&!native.parentsInFlight&&!native.mids&&!native.childrenEntered&&!native.childrenExited&&!native.childrenInFlight
        &&!native.callbackFaults&&!native.rejectedReads&&!native.missingPosts&&!native.retainedParents&&!native.retainedChildren&&!native.overflow;
    RIZ_SINGLE_CHECK(evidence.countersZero);return evidence;
}
inline void Print(const RizFirstInstallInfoV7& packet,const Evidence& evidence,int span,int offset,int blockOffset,int protection)
{
    const auto& info=packet.singleEvent;
    std::printf("\"singleEventPublication\":{\"capabilityVersion\":%u,\"byteSize\":%u,\"phase\":%u,\"unknown\":%u,\"reserved\":%u,\"operationId\":%u,\"kind\":%u,\"slot\":%u,\"expectedWrites\":%u,\"epoch\":%llu,\"parentEpoch\":%llu,\"parentRowEpoch\":%llu,\"parentEpochMatchesOperation25\":%s,\"spanRvas\":[",
        info.capabilityVersion,info.byteSize,info.phase,info.unknown,info.reserved,info.operationId,info.kind,info.slot,info.expectedWrites,info.epoch,info.parentEpoch,packet.operations[25].epoch,info.parentEpoch==packet.operations[25].epoch?"true":"false");
    for(unsigned i=0;i!=7;++i)std::printf("%s%llu",i?",":"",reinterpret_cast<std::uintptr_t>(info.spanPc[i])-packet.publication.common.moduleBase);
    std::printf("],\"spanWidths\":[");for(unsigned i=0;i!=7;++i)std::printf("%s%u",i?",":"",info.spanWidth[i]);
    std::printf("],\"spanProtections\":[");for(unsigned i=0;i!=7;++i)std::printf("%s%u",i?",":"",info.protection[i]);
    std::printf("],\"stubSizes\":[%u,%u,%u],\"unwindSizes\":[%u,%u,%u],\"configuredMask\":%u,\"publishedMask\":%u,\"attemptedWrites\":%llu,\"completedWrites\":%llu,\"allocationSize\":%llu,\"registeredFunctionCount\":%u,\"lookupChecks\":%u,\"boundaryChecks\":%u},",
        info.stubSize[0],info.stubSize[1],info.stubSize[2],info.unwindSize[0],info.unwindSize[1],info.unwindSize[2],info.configuredMask,info.publishedMask,info.attemptedWrites,info.completedWrites,info.allocationSize,info.registeredFunctionCount,info.lookupChecks,info.boundaryChecks);
    std::printf("\"singleSourceBytesVerified\":%s,\"singleBlockBytesVerified\":%u,\"singleUnwindVerified\":%s,\"singleCountersZero\":%s,\"singleSyntheticBoundaryChecks\":%u,\"tamperedSingleSpan\":%d,\"tamperedSingleOffset\":%d,\"tamperedSingleBlockOffset\":%d,\"tamperedSingleProtection\":%d,",
        evidence.sources==7?"true":"false",evidence.blockBytes,evidence.unwind?"true":"false",evidence.countersZero?"true":"false",evidence.boundaries,span,offset,blockOffset,protection);
}
#undef RIZ_SINGLE_CHECK
}
