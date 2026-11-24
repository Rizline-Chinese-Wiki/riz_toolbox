#pragma once
#include "FixedPublisher.h"
#include <cstdio>
#include <cstring>

// Controlled stack/context evidence is separate from actual SEH/C++ tests.
// Reconstructs only the four explicitly fixed instruction sequences; never
// treats this reconstruction as execution at every PC or a generic emulator.
inline unsigned RizFixedProbeUnwind(const RizFixedInfoV1& info, FILE* log, unsigned* failures)
{
    constexpr unsigned lengths[]{15,18,16,15};
    constexpr unsigned boundaries[4][9]{{0,5,10,15,22,25,0,0,0},{0,2,3,4,6,11,18,25,28},
        {0,2,6,9,12,16,23,26,0},{0,5,6,10,13,15,22,25,0}};
    constexpr unsigned counts[]{6,9,8,8};
    constexpr DWORD64 sentinels[]{0x1122334455667788ull,0x2233445566778899ull,
        0x33445566778899aaull,0x445566778899aabbull,0x5566778899aabbccull,
        0x66778899aabbccddull,0x778899aabbccddeeull,0x8899aabbccddeeffull};
    unsigned tested{};
    for (unsigned kind=0; kind!=4; ++kind) {
        for (unsigned original=0; original!=2; ++original) {
            for (unsigned index=0; index<counts[kind]; ++index) {
                const auto offset=boundaries[kind][index];
                if ((!original && offset>lengths[kind]) || (original && offset==lengths[kind]+10)) continue;
                alignas(16) std::uint64_t stack[256]{};
                auto* entry = stack + 129; // x64 callee entry RSP is 8 mod 16.
                const auto expectedRip = reinterpret_cast<DWORD64>(&RizFixedProbeUnwind);
                *entry=expectedRip;
                CONTEXT context{}; context.ContextFlags=CONTEXT_FULL;
                context.Rcx=0x9135792468abcdefull; // Source for fixed MOV RBX/ RDI,RCX.
                context.Rbx=sentinels[0];context.Rbp=sentinels[1];context.Rsi=sentinels[2];context.Rdi=sentinels[3];
                context.R12=sentinels[4];context.R13=sentinels[5];context.R14=sentinels[6];context.R15=sentinels[7];
                context.Rsp=reinterpret_cast<DWORD64>(entry); context.EFlags=0x246;
                const auto save=[&](unsigned byteOffset, DWORD64 value){*reinterpret_cast<DWORD64*>(reinterpret_cast<unsigned char*>(entry)+byteOffset)=value;};
                const auto push=[&](DWORD64 value){context.Rsp-=8;*reinterpret_cast<DWORD64*>(context.Rsp)=value;};
                if(kind==0){if(offset>=5)save(8,context.Rbx);if(offset>=10)save(0x18,context.Rsi);if(offset>=15)save(0x20,context.Rdi);}
                if(kind==1){if(offset>=2)push(context.Rbp);if(offset>=3)push(context.Rbx);if(offset>=4)push(context.Rdi);if(offset>=6)push(context.R14);
                    if(offset>=11)context.Rbp=context.Rsp-0x78;if(offset>=18)context.Rsp-=0x178;}
                if(kind==2){if(offset>=2)push(context.Rbx);if(offset>=6)context.Rsp-=0x20;if(offset>=12)context.Rbx=0x9135792468abcdefull;}
                if(kind==3){if(offset>=5)save(0x20,context.Rbp);if(offset>=6)push(context.Rdi);if(offset>=10)context.Rsp-=0x20;
                    if(offset>=13)context.Rdi=0x9135792468abcdefull;if(offset>=15)context.Rbp=0;}
                const auto& receipt=info.receipts[kind];
                if(original&&offset>=lengths[kind]+7)context.R11=receipt.continuation;
                context.Rip=(original?receipt.original:receipt.target)+offset;
                const auto before=context;
                DWORD64 imageBase{}, establisher{};PVOID handlerData{};
                const auto function=RtlLookupFunctionEntry(context.Rip,&imageBase,nullptr);
                bool passed=function!=nullptr;
                RUNTIME_FUNCTION raw{};
                unsigned char unwindHeader[4]{};
                if(function){
                    raw=*function;std::memcpy(unwindHeader,reinterpret_cast<void*>(imageBase+raw.UnwindData),4);
                    RtlVirtualUnwind(UNW_FLAG_NHANDLER,imageBase,context.Rip,function,&context,&handlerData,&establisher,nullptr);
                    passed=context.Rip==expectedRip&&context.Rsp==reinterpret_cast<DWORD64>(entry+1)
                        &&context.Rbx==sentinels[0]&&context.Rbp==sentinels[1]&&context.Rsi==sentinels[2]&&context.Rdi==sentinels[3]
                        &&context.R12==sentinels[4]&&context.R13==sentinels[5]&&context.R14==sentinels[6]&&context.R15==sentinels[7];
                }
                if(!passed)++*failures;
                ++tested;
                std::fprintf(log,"{\"kind\":%u,\"originalStub\":%s,\"offset\":%u,\"passed\":%s,\"controlPc\":%llu,\"imageBase\":%llu,\"functionBegin\":%u,\"functionEnd\":%u,\"unwindRva\":%u,\"prologSize\":%u,\"codeCount\":%u,\"expectedRip\":%llu,\"expectedRsp\":%llu,\"beforeRsp\":%llu,\"afterRip\":%llu,\"afterRsp\":%llu,\"beforeNonvol\":[%llu,%llu,%llu,%llu,%llu,%llu,%llu,%llu],\"afterNonvol\":[%llu,%llu,%llu,%llu,%llu,%llu,%llu,%llu],\"expectedNonvol\":[",
                    kind,original?"true":"false",offset,passed?"true":"false",before.Rip,imageBase,raw.BeginAddress,raw.EndAddress,raw.UnwindData,
                    static_cast<unsigned>(unwindHeader[1]),static_cast<unsigned>(unwindHeader[2]),expectedRip,reinterpret_cast<DWORD64>(entry+1),before.Rsp,context.Rip,context.Rsp,
                    before.Rbx,before.Rbp,before.Rsi,before.Rdi,before.R12,before.R13,before.R14,before.R15,
                    context.Rbx,context.Rbp,context.Rsi,context.Rdi,context.R12,context.R13,context.R14,context.R15);
                for(unsigned i=0;i!=8;++i)std::fprintf(log,"%s%llu",i?",":"",sentinels[i]);
                std::fprintf(log,"],\"stackBase\":%llu,\"stackHex\":\"",reinterpret_cast<DWORD64>(stack));
                for(auto byte:reinterpret_cast<const unsigned char(&)[sizeof(stack)]>(stack))std::fprintf(log,"%02x",byte);
                std::fprintf(log,"\",\"contextBeforeHex\":\"");
                for(auto byte:reinterpret_cast<const unsigned char(&)[sizeof(CONTEXT)]>(before))std::fprintf(log,"%02x",byte);
                std::fprintf(log,"\",\"contextAfterHex\":\"");
                for(auto byte:reinterpret_cast<const unsigned char(&)[sizeof(CONTEXT)]>(context))std::fprintf(log,"%02x",byte);
                std::fprintf(log,"\",\"controlledContext\":true,\"actualExceptionAtEachPc\":false}\n");
            }
        }
    }
    return tested;
}
