#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "../FirstInstallPlaybackAttestation.h"
#include "../FirstInstallProfile.h"
namespace {
unsigned checks{}, failures{};
void Check(bool value, const char* text, int line) { ++checks; if (!value) { ++failures; std::fprintf(stderr,"FAILED attestation %d: %s\n",line,text); } }
#define CHECK(x) Check(!!(x),#x,__LINE__)
template<class T> T Symbol(HMODULE module, const char* name) { auto* p = GetProcAddress(module,name); CHECK(p); return reinterpret_cast<T>(p); }
void Change(unsigned char* address, unsigned char byte) { DWORD old{},ignored{}; CHECK(VirtualProtect(address,1,PAGE_EXECUTE_READWRITE,&old)); *address=byte; CHECK(VirtualProtect(address,1,old,&ignored)); CHECK(FlushInstructionCache(GetCurrentProcess(),address,1)); }
}
int wmain(int argc,wchar_t** argv)
{
    if(argc!=5)return 2;
    const auto* mode=argv[4];
    auto helper=LoadLibraryW(argv[1]); CHECK(helper);
    auto game=LoadLibraryExW(argv[3],nullptr,DONT_RESOLVE_DLL_REFERENCES); CHECK(game);
    if(!helper||!game)return 3;
    auto before=Symbol<decltype(&RizFirstInstall_BeforeIl2CppInit)>(helper,"RizFirstInstall_BeforeIl2CppInit");
    auto get=Symbol<decltype(&RizFirstInstall_GetPlaybackAttestationV1)>(helper,"RizFirstInstall_GetPlaybackAttestationV1");
    auto read=Symbol<decltype(&RizFirstInstall_ReadPlaybackAttestedRangeV1)>(helper,"RizFirstInstall_ReadPlaybackAttestedRangeV1");
    auto batch=Symbol<decltype(&RizFirstInstall_ReadPlaybackAttestedRangesV1)>(helper,"RizFirstInstall_ReadPlaybackAttestedRangesV1");
    auto first=Symbol<decltype(&RizFirstInstall_GetInfoV7)>(helper,"RizFirstInstall_GetInfoV7");
    RizFirstInstallPlaybackAttestationV1 info{1,sizeof(info)};
    CHECK(get(&info)==2 && info.token==nullptr && info.state==0);
    auto* base=reinterpret_cast<unsigned char*>(game);
    if(!std::wcscmp(mode,L"prewrite-foreign"))Change(base+0x401218,base[0x401218]^1);
    const auto result=before(GetProcAddress(game,"il2cpp_init"),argv[2]);
    info={1,sizeof(info)}; const auto got=get(&info);
    const auto actualPrepareCount=info.originalCount,actualWriteCount=info.completedWrites;
    std::fprintf(stderr,"producer result=%u get=%u state=%u prepared=%u supported=%u writes=%u lookup=%u boundary=%u\n",result,got,info.state,info.originalCount,info.supportedOriginals,info.rangeCount,info.lookupChecks,info.boundaryChecks);
    if(!std::wcscmp(mode,L"prewrite-foreign")) { CHECK(result==0); CHECK(got==2 && info.token==nullptr); }
    else {
        CHECK(result==1); CHECK(got==0 && info.state==3 && info.unknown==0 && info.token && info.epoch==1);
        CHECK(info.module==game && info.helper==helper && info.imageSize==RizFirstInstallProfile::ImageSize);
        CHECK(info.originalCount==28 && info.supportedOriginals==28 && info.rangeCount==64 && info.completedWrites==64);
        CHECK(std::memcmp(info.fileSha256,RizFirstInstallProfile::GameSha256,32)==0);
        if(got==0) {
            unsigned detours=0,spans=0; RizFirstInstallPlaybackRangeV1 rows[64]{};
            for(auto& row:rows)row={1,sizeof(row)};
            CHECK(batch(&info,rows,64)==1); // A copied header is not the private owner token.
            CHECK(batch(info.token,rows,63)==1);
            rows[17].reserved0=1; CHECK(batch(info.token,rows,64)==1); rows[17].reserved0=0;
            CHECK(batch(info.token,rows,64)==0);
            RizFirstInstallPlaybackRangeV1 firstRow{1,sizeof(firstRow)};
            CHECK(read(info.token,0,&firstRow)==0 && std::memcmp(&firstRow,&rows[0],sizeof(firstRow))==0);
            for(unsigned i=0;i<64;++i) {
                auto& row=rows[i];
                CHECK(row.id==i && row.address && row.length && row.length<=32 && row.protection==PAGE_EXECUTE_READ);
                CHECK(std::memcmp(row.address,row.afterBytes,row.length)==0);
                if(row.kind==1) {
                    ++detours; CHECK(row.original && row.entry && row.epoch && row.functionTable && row.functionCount>=3);
                    CHECK(row.originalByteCount==row.movedByteCount+14 && row.continuation==static_cast<const unsigned char*>(row.address)+row.movedByteCount);
                    CHECK(row.lookupChecks==row.boundaryChecks && row.lookupChecks==row.functionCount+2);
                    const auto* code=static_cast<const unsigned char*>(row.original)+row.movedByteCount;
                    CHECK(code[0]==0x49 && code[1]==0xbb && code[10]==0x41 && code[11]==0xff && code[12]==0xe3 && code[13]==0x90);
                    CHECK(std::memcmp(code+2,&row.continuation,8)==0);
                    std::printf("original id=%u source=%p entry=%p original=%p moved=%u bytes=",i,row.address,row.entry,row.original,row.movedByteCount);
                    for(unsigned b=0;b<row.originalByteCount;++b)std::printf("%02x",static_cast<const unsigned char*>(row.original)[b]);
                    std::printf(" functions=%u unwindBytes=%u\n",row.functionCount,row.unwindByteCount);
                } else { ++spans; CHECK(row.kind==2 && !row.original && !row.entry && !row.functionTable); }
            }
            CHECK(detours==28 && spans==36);
            auto copied=info; RizFirstInstallPlaybackRangeV1 bad{1,sizeof(bad)};
            CHECK(read(&copied,0,&bad)==1); CHECK(read(info.token,64,&bad)==1);
            if(std::wcscmp(mode,L"normal")) {
                unsigned char* target{};
                if(!std::wcscmp(mode,L"original"))target=const_cast<unsigned char*>(static_cast<const unsigned char*>(rows[7].original));
                else if(!std::wcscmp(mode,L"unwind"))target=const_cast<unsigned char*>(static_cast<const unsigned char*>(rows[7].functionTable))+128;
                else if(!std::wcscmp(mode,L"patch"))target=const_cast<unsigned char*>(static_cast<const unsigned char*>(rows[7].address));
                else if(!std::wcscmp(mode,L"outside"))target=base+0x401218;
                else if(!std::wcscmp(mode,L"continuation"))target=const_cast<unsigned char*>(static_cast<const unsigned char*>(rows[7].continuation))+50;
                CHECK(target); if(target) {
                    const auto value=*target; Change(target,value^1);
                    const bool outside=std::wcscmp(mode,L"outside")==0;
                    info={1,sizeof(info)}; CHECK(get(&info)==(outside?0u:2u) && info.unknown==(outside?0u:1u));
                    for(auto& row:rows)row={1,sizeof(row)};
                    CHECK(batch(copied.token,rows,64)==(outside?0u:2u));
                    Change(target,value); info={1,sizeof(info)}; CHECK(get(&info)==(outside?0u:2u) && info.unknown==(outside?0u:1u));
                    CHECK(read(copied.token,0,&bad)==(outside?0u:2u));
                    RizFirstInstallInfoV7 parent{};parent.publication.common.abiVersion=7;parent.publication.common.byteSize=sizeof(parent);
                    CHECK(first(&parent)==0 && parent.publication.common.phase==(outside?RizFirstInstallReady:RizFirstInstallUnknown)
                        && parent.publication.common.unknown==(outside?0u:1u));
                }
            }
        }
    }
    std::printf("{\"kind\":\"first-install-private-preprepare-attestation\",\"checks\":%u,\"failures\":%u,\"passed\":%s,\"actualPrepareCount\":%u,\"actualWriteCount\":%u,\"gameCodeExecuted\":false,\"originalInitInvoked\":false,\"dllMainInvoked\":false}\n",checks,failures,failures?"false":"true",actualPrepareCount,actualWriteCount);
    return failures?1:0;
}
