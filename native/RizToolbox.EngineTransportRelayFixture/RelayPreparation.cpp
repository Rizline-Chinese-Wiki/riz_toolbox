#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <string>
#include <vector>
#include <sstream>
#include <iomanip>
#include <stdexcept>
#include "../RizToolbox.EngineBootstrap/EngineTransportRelays.h"
#include "../RizToolbox.EngineBootstrap/EngineTransportGameProfile.h"
#pragma comment(lib,"bcrypt.lib")

namespace {
namespace R=RizEngineTransportRelays;namespace F=RizEngineTransportGameProfile;
using U8=std::uint8_t;using U32=std::uint32_t;using U64=std::uint64_t;
U32 checks{},failures{},ownedMutations{};bool mappedSourceUnchanged=true,mappedImageNeverExecutable=true;
R::Plan plan{};
struct Case {std::string name;U32 result,expected,flags,written,attempted,checks;bool passed,sourceUnchanged,nearUntouched;};
std::vector<Case> cases;
struct Normal {bool passed{};U32 sites{},windows{},tables{},used{},written{};} normal;
void Check(bool value,const char* reason){++checks;if(!value){++failures;std::fprintf(stderr,"RelayPreparation failed: %s\n",reason);}}
void Must(bool value,const char* reason){Check(value,reason);if(!value)throw std::runtime_error(reason);}
U32 Dword(const U8* at){U32 value{};for(U32 i=0;i<4;++i)value|=static_cast<U32>(at[i])<<(8*i);return value;}
U64 Qword(const U8* at){U64 value{};for(U32 i=0;i<8;++i)value|=static_cast<U64>(at[i])<<(8*i);return value;}
U64 Target(U64 after,U32 encoded){const auto delta=static_cast<std::int32_t>(encoded);
    return delta<0?after-static_cast<U64>(-static_cast<std::int64_t>(delta)):after+static_cast<U32>(delta);}
bool Filled(const U8* bytes,std::size_t count,U8 value){for(std::size_t i=0;i<count;++i)if(bytes[i]!=value)return false;return true;}
std::string Hash(const std::vector<U8>& input){
    BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE hash{};DWORD objectBytes{},copied{};std::vector<U8> object;U8 output[32]{};
    bool okay=BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0;
    if(okay)okay=BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&objectBytes),sizeof(objectBytes),&copied,0)>=0&&copied==sizeof(objectBytes);
    if(okay){object.resize(objectBytes);okay=BCryptCreateHash(algorithm,&hash,object.data(),objectBytes,nullptr,0,0)>=0;}
    if(okay)okay=BCryptHashData(hash,const_cast<PUCHAR>(input.data()),static_cast<ULONG>(input.size()),0)>=0;
    if(okay)okay=BCryptFinishHash(hash,output,sizeof(output),0)>=0;
    if(hash)okay=(BCryptDestroyHash(hash)>=0)&&okay;if(algorithm)okay=(BCryptCloseAlgorithmProvider(algorithm,0)>=0)&&okay;
    Must(okay,"Actual SHA256 of the locked fixed disk input");std::ostringstream value;
    for(const auto byte:output)value<<std::hex<<std::setw(2)<<std::setfill('0')<<static_cast<unsigned>(byte);return value.str();
}
std::vector<U8> Read(const wchar_t* path){
    const HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    Must(file!=INVALID_HANDLE_VALUE,"Open fixed disk input for read only");LARGE_INTEGER size{};
    const bool sized=GetFileSizeEx(file,&size)!=0&&size.QuadPart==static_cast<LONGLONG>(F::UnityFileBytes);
    if(!sized){CloseHandle(file);Must(false,"Exact pinned disk byte size");}
    std::vector<U8> bytes(static_cast<std::size_t>(size.QuadPart));std::size_t total{};bool okay=true;
    while(total<bytes.size()){const auto remaining=bytes.size()-total;const DWORD wanted=static_cast<DWORD>(remaining>0x100000?0x100000:remaining);DWORD got{};
        if(!ReadFile(file,bytes.data()+total,wanted,&got,nullptr)||got!=wanted){okay=false;break;}total+=got;}
    const bool closed=CloseHandle(file)!=0;Must(okay&&closed&&total==bytes.size(),"Read and close exact fixed disk bytes");return bytes;
}
template<unsigned Index> __declspec(noinline) U64 __fastcall OwnedEntry(U64 a,U64 b,U64 c,U64 d){return a^b^c^d^(0x72340000u+Index);}
struct Mapping {
    U8* image{};U8* nearBytes{};std::vector<U8> original;
    ~Mapping(){if(image)VirtualFree(image,0,MEM_RELEASE);}
    void Protection(U8* at,std::size_t size,DWORD value){DWORD prior{};Must(VirtualProtect(at,size,value,&prior)!=0,"Owned mapping protection change");}
    void Create(const std::vector<U8>& disk){
        image=static_cast<U8*>(VirtualAlloc(nullptr,static_cast<SIZE_T>(F::ImageSize)+0x10000,MEM_RESERVE,PAGE_NOACCESS));Must(image!=nullptr,"Reserve private non-executable image and near space");
        Must(VirtualAlloc(image,F::ImageSize,MEM_COMMIT,PAGE_READWRITE)==image,"Commit private image as non-executable RW");
        const U64 next=(reinterpret_cast<U64>(image)+F::ImageSize+0xffff)&~U64{0xffff};nearBytes=reinterpret_cast<U8*>(next);
        Must(VirtualAlloc(nearBytes,R::ArenaMinimumBytes,MEM_COMMIT,PAGE_READWRITE)==nearBytes,"Commit near block above image as RW");
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(disk.data());const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(disk.data()+dos->e_lfanew);
        std::memcpy(image,disk.data(),nt->OptionalHeader.SizeOfHeaders);const auto* sections=IMAGE_FIRST_SECTION(nt);
        for(WORD i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& section=sections[i];
            Must(static_cast<U64>(section.PointerToRawData)+section.SizeOfRawData<=disk.size()
                &&static_cast<U64>(section.VirtualAddress)+section.SizeOfRawData<=F::ImageSize,"Owned section copy stays within disk and image");
            std::memcpy(image+section.VirtualAddress,disk.data()+section.PointerToRawData,section.SizeOfRawData);}
        original.assign(image,image+F::ImageSize);Protection(image,F::ImageSize,PAGE_READONLY);
    }
    bool NoExecute(){
        U64 address=reinterpret_cast<U64>(image),end=address+F::ImageSize;bool okay=true;
        while(address<end){MEMORY_BASIC_INFORMATION info{};if(VirtualQuery(reinterpret_cast<void*>(address),&info,sizeof(info))!=sizeof(info)){okay=false;break;}
            const DWORD protect=info.Protect&0xff;okay=okay&&info.Type==MEM_PRIVATE&&info.AllocationBase==image
                &&protect!=PAGE_EXECUTE&&protect!=PAGE_EXECUTE_READ&&protect!=PAGE_EXECUTE_READWRITE&&protect!=PAGE_EXECUTE_WRITECOPY;
            const U64 next=reinterpret_cast<U64>(info.BaseAddress)+info.RegionSize;if(next<=address){okay=false;break;}address=next;}
        MEMORY_BASIC_INFORMATION nearInfo{};okay=VirtualQuery(nearBytes,&nearInfo,sizeof(nearInfo))==sizeof(nearInfo)&&okay;
        const DWORD protect=nearInfo.Protect&0xff;okay=okay&&nearInfo.Type==MEM_PRIVATE&&nearInfo.AllocationBase==image
            &&protect!=PAGE_EXECUTE&&protect!=PAGE_EXECUTE_READ&&protect!=PAGE_EXECUTE_READWRITE&&protect!=PAGE_EXECUTE_WRITECOPY;
        mappedImageNeverExecutable=mappedImageNeverExecutable&&okay;return okay;
    }
    bool Unchanged(){const bool okay=std::memcmp(image,original.data(),F::ImageSize)==0;mappedSourceUnchanged=mappedSourceUnchanged&&okay;return okay;}
};
R::Request Request(const Mapping& mapping){R::Request value{};value.version=R::Version;value.byteSize=static_cast<U32>(sizeof(value));
    value.fixedImageBase=reinterpret_cast<U64>(mapping.image);value.fixedImageBytes=F::ImageSize;value.nearRw=reinterpret_cast<U64>(mapping.nearBytes);value.nearCapacity=R::ArenaMinimumBytes;
    const void* entries[]{reinterpret_cast<void*>(&OwnedEntry<0>),reinterpret_cast<void*>(&OwnedEntry<1>),reinterpret_cast<void*>(&OwnedEntry<2>),reinterpret_cast<void*>(&OwnedEntry<3>),
        reinterpret_cast<void*>(&OwnedEntry<4>),reinterpret_cast<void*>(&OwnedEntry<5>),reinterpret_cast<void*>(&OwnedEntry<6>),reinterpret_cast<void*>(&OwnedEntry<7>),
        reinterpret_cast<void*>(&OwnedEntry<8>),reinterpret_cast<void*>(&OwnedEntry<9>),reinterpret_cast<void*>(&OwnedEntry<10>),reinterpret_cast<void*>(&OwnedEntry<11>),reinterpret_cast<void*>(&OwnedEntry<12>)};
    for(U32 i=0;i<R::TypedCount;++i)value.typedEntries[i]=reinterpret_cast<U64>(entries[i]);
    value.inlineEntry=reinterpret_cast<U64>(&OwnedEntry<13>);value.rowEntry=reinterpret_cast<U64>(&OwnedEntry<14>);value.waitPhaseEntry=reinterpret_cast<U64>(&OwnedEntry<15>);return value;}
struct Stream {
    const R::Site& site;const U8* bytes;U32 position{};
    void Bytes(const U8* expected,U32 count){Check(position+count<=site.codeBytes&&std::memcmp(bytes+position,expected,count)==0,"Exact replay instruction bytes");position+=count;}
    void Jump(U64 target){Check(position+5<=site.codeBytes&&bytes[position]==0xe9&&Target(site.relay+position+5,Dword(bytes+position+1))==target,"Decoded relay continuation JMP");position+=5;}
    void Zero(U64 target){Check(position+6<=site.codeBytes&&bytes[position]==0x0f&&bytes[position+1]==0x84&&Target(site.relay+position+6,Dword(bytes+position+2))==target,"Decoded replay conditional branch");position+=6;}
    void Probe(){Check(position+6<=site.codeBytes&&bytes[position]==0xff&&bytes[position+1]==0x15
            &&Target(site.relay+position+6,Dword(bytes+position+2))==site.literal&&site.relayReturnPc==site.relay+position+6,"Decoded exact indirect probe CALL and return PC");position+=6;}
    void Rip(const U8* original,U64 pc){Check(position+7<=site.codeBytes&&std::memcmp(bytes+position,original,3)==0
            &&Target(site.relay+position+7,Dword(bytes+position+3))==Target(pc+7,Dword(original+3)),"RIP-relative replay retains its original absolute target");position+=7;}
    void ZeroPart(){Check(position<=112&&Filled(bytes+position,112-position,0x90),"Unexecuted fixed gap contains only NOP bytes");position=112;}
};
void VerifySite(const Mapping& mapping,const R::Request& request,U32 index){
    const auto& site=plan.sites[index];const auto& fact=F::Sites[index];const auto& physical=F::PhysicalWindows[fact.physicalWindowIndex];const U8* source=physical.bytes;
    Check(site.index==index&&site.family==fact.family&&site.phase==fact.familyIndex&&site.windowIndex==fact.physicalWindowIndex
        &&site.functionIndex==physical.functionIndex&&site.relativeOffset==R::SlotOffset+index*R::SlotBytes,"Exact site and physical window mapping");
    Check(site.logicalSitePc==request.fixedImageBase+fact.observationRva&&site.physicalPatchPc==request.fixedImageBase+fact.patchRva
        &&site.relay==request.nearRw+site.relativeOffset&&site.toImageStart==site.relay-request.fixedImageBase
        &&site.literal==site.relay+R::LiteralOffset&&site.unwind==site.relay+R::UnwindOffset,"Logical/physical/relay/RVA identities are separate and exact");
    const auto entry=fact.family==0?request.inlineEntry:fact.family==1?request.rowEntry:request.waitPhaseEntry;
    Check(site.probeEntry==entry&&Qword(mapping.nearBytes+site.relativeOffset+R::LiteralOffset)==entry,"Actual owned entry literal bytes");
    const auto* table=mapping.nearBytes+index*sizeof(R::RuntimeFunction);const auto* unwind=mapping.nearBytes+site.relativeOffset+R::UnwindOffset;
    Check(Dword(table)==site.toImageStart&&Dword(table+4)==site.toImageStart+site.codeBytes&&Dword(table+8)==site.toImageStart+R::UnwindOffset
        &&site.runtimeFunction.beginRva==Dword(table)&&site.runtimeFunction.endRva==Dword(table+4)&&site.runtimeFunction.unwindRva==Dword(table+8),"Exact emitted RF table and POD identities");
    if(index>=17&&index<=20){
        // These Dispatch PCs occur before the source's late RBX save. A chain
        // to the complete source prolog would restore an uninitialized slot.
        // PUSH RDI and ALLOC150 are already active at every relay instruction,
        // hence CodeOffset0/Prolog0 rather than copied source offsets2/9.
        constexpr U8 partial[]{0x01,0x00,0x03,0x00,0x00,0x01,0x2a,0x00,0x00,0x70,0x00,0x00};
        Check(site.unwindBytes==R::UnwindBytes&&unwind[0]==0x21&&unwind[1]==0&&unwind[2]==0&&unwind[3]==0
            &&Dword(unwind+4)==physical.runtimeFunction.beginRva&&Dword(unwind+8)==physical.runtimeFunction.endRva
            &&Dword(unwind+12)==site.toImageStart+R::DispatchPrimaryUnwindOffset
            &&std::memcmp(mapping.nearBytes+site.relativeOffset+R::DispatchPrimaryUnwindOffset,partial,sizeof(partial))==0,
            "Explicit synthetic Dispatch primary retains source function identity and excludes its future RBX save");
        const auto& dispatch=F::Functions[11];const F::UnwindRecord* sourceUnwind{};
        for(const auto& candidate:F::Unwinds)if(candidate.rva==physical.runtimeFunction.unwindRva)sourceUnwind=&candidate;
        constexpr U8 sourceProlog[]{0x01,0x4e,0x05,0x00,0x4e,0x34,0x2d,0x00,0x09,0x01,0x2a,0x00,0x02,0x70,0x00,0x00};
        Check(sourceUnwind&&sourceUnwind->byteCount==sizeof(sourceProlog)
            &&std::memcmp(sourceUnwind->raw,sourceProlog,sizeof(sourceProlog))==0
            &&std::memcmp(mapping.image+sourceUnwind->rva,sourceProlog,sizeof(sourceProlog))==0
            &&physical.runtimeFunction.beginRva==dispatch.beginRva&&fact.patchRva-dispatch.beginRva>=9
            &&physical.endRva-dispatch.beginRva<0x4e,"Actual pinned Dispatch site precedes late RBX save while PUSH and ALLOC are already active");
    }else Check(unwind[0]==0x21&&unwind[1]==0&&unwind[2]==0&&unwind[3]==0&&site.unwindBytes==16
        &&Dword(unwind+4)==physical.runtimeFunction.beginRva&&Dword(unwind+8)==physical.runtimeFunction.endRva&&Dword(unwind+12)==physical.runtimeFunction.unwindRva,"Exact no-local-frame CHAININFO parent");
    U32 flattened{},matches{};for(U32 f=0;f<F::TypedCount;++f)for(U32 n=0;n<F::Functions[f].runtimeFunctionCount;++n,++flattened){const auto& rf=F::Functions[f].runtimeFunctions[n];
        if(rf.beginRva==physical.runtimeFunction.beginRva&&rf.endRva==physical.runtimeFunction.endRva&&rf.unwindRva==physical.runtimeFunction.unwindRva){++matches;Check(site.sourceRfIndex==flattened,"Exact retained source RF index");}}
    Check(matches==1&&site.codeBytes<R::LiteralOffset,"Unique source RF and no executable literal");
    Stream stream{site,mapping.nearBytes+site.relativeOffset};const U64 branch=request.fixedImageBase+fact.branchTargetRva;
    if(fact.expandedPhysicalBlock){const auto& bypass=F::ConditionalBypasses[index==9?0:index==11?1:2];
        Check(site.alternateContinuation==request.fixedImageBase+bypass.zeroContinuationRva,"Covered-tail bypass continuation");
        if(index==9){stream.Bytes(source,7);stream.Zero(site.relay+112);stream.Probe();stream.Bytes(source+9,3);stream.Rip(source+12,request.fixedImageBase+fact.patchRva+12);}
        else{stream.Bytes(source,3);stream.Zero(site.relay+112);stream.Probe();stream.Bytes(source+5,7);}
        stream.Jump(request.fixedImageBase+bypass.nonzeroContinuationRva);stream.ZeroPart();
        if(bypass.commonRipMov)stream.Rip(bypass.zeroReplay,request.fixedImageBase+bypass.zeroReplayRva);else stream.Bytes(bypass.zeroReplay,bypass.zeroReplayBytes);
        stream.Jump(request.fixedImageBase+bypass.zeroContinuationRva);
    }else{stream.Probe();switch(index){
        case 1:case 2:case 8:stream.Jump(branch);break;
        case 3:stream.Bytes(source,4);stream.Zero(branch);stream.Jump(site.continuation);break;
        case 5:case 10:stream.Bytes(source,3);stream.Rip(source+3,request.fixedImageBase+fact.patchRva+3);stream.Jump(site.continuation);break;
        case 6:stream.Zero(branch);stream.Bytes(source+2,4);stream.Jump(site.continuation);break;
        case 17:case 19:stream.Bytes(source,3);stream.Zero(branch);stream.Jump(site.continuation);break;
        case 21:stream.Bytes(source,4);stream.Zero(branch);stream.Jump(site.continuation);break;
        default:stream.Bytes(source,physical.byteCount);stream.Jump(site.continuation);break;}}
    Check(stream.position==site.codeBytes,"Every emitted executable relay byte was decoded");
}
void VerifyPlan(const Mapping& mapping,const R::Request& request){
    Check(plan.version==R::Version&&plan.byteSize==sizeof(plan)&&plan.flags==(R::FactsVerified|R::NearPrepared),"Successful preparation flags only describe bytes");
    Check(plan.siteCount==23&&plan.windowCount==36&&plan.tableCount==23&&plan.usedBytes==6400&&plan.nearWriteAttempted==1&&plan.nearBytesWritten==6400,"Exact complete preparation counts");
    Check(plan.tableBase==request.fixedImageBase&&plan.tableAddress==request.nearRw&&plan.arenaToImageStart==request.nearRw-request.fixedImageBase,"One consistent positive table image base");
    Check(std::memcmp(mapping.nearBytes,plan.preparedBytes,6400)==0&&Filled(mapping.nearBytes+6400,R::ArenaMinimumBytes-6400,0xa5),"Exact 6400-byte near readback and untouched capacity tail");
    for(U32 i=0;i<36;++i){const auto& window=plan.windows[i];const auto& fact=F::PhysicalWindows[i];
        Check(window.index==i&&window.role==fact.role&&window.roleIndex==fact.roleIndex&&window.byteCount==fact.byteCount
            &&window.source==request.fixedImageBase+fact.beginRva&&std::memcmp(window.expected,fact.bytes,fact.byteCount)==0
            &&std::memcmp(reinterpret_cast<const void*>(window.source),window.expected,window.byteCount)==0,"Exact original source window remains unchanged");
        const U32 instruction=i<13?14u:5u;Check(Filled(window.patch+instruction,window.byteCount-instruction,0x90),"Exact patch NOP padding");
        if(i<13)Check(window.patch[0]==0xff&&window.patch[1]==0x25&&Dword(window.patch+2)==0&&Qword(window.patch+6)==request.typedEntries[i],"Exact typed FF25 patch and owned literal");
        else Check(window.patch[0]==0xe9&&Target(window.source+5,Dword(window.patch+1))==plan.sites[i-13].relay,"Exact near E9 side patch target");
        Check(Filled(window.expected+window.byteCount,32-window.byteCount,0)&&Filled(window.patch+window.byteCount,32-window.byteCount,0),"Unused source-window bytes stay zero");}
    U32 chained{},partial{},outerChains{};
    for(U32 i=0;i<23;++i){VerifySite(mapping,request,i);const auto flags=mapping.nearBytes[plan.sites[i].relativeOffset+R::UnwindOffset]>>3;
        if(flags==4){++outerChains;if(i>=17&&i<=20)++partial;else ++chained;}}
    Check(outerChains==23&&chained==19&&partial==4,"Twenty-three outer chains identify nineteen source primaries and four explicit synthetic partial primaries");
    for(const auto& call:F::PreservedCalls)Check(std::memcmp(mapping.image+call.callRva,call.bytes,call.byteCount)==0,"Original CALL opcode and return PC remain unchanged");
}
U8* FindRfField(Mapping& mapping){const auto& target=F::Functions[0].runtimeFunctions[0];
    for(U32 offset=0;offset<F::ExceptionBytes;offset+=12){auto* entry=mapping.image+F::ExceptionRva+offset;if(Dword(entry)==target.beginRva)return entry+8;}throw std::runtime_error("Pinned source RF missing from actual mapped table");}
U8* TextCharacteristics(Mapping& mapping){const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(mapping.image);auto* nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(mapping.image+dos->e_lfanew);auto* sections=IMAGE_FIRST_SECTION(nt);
    for(WORD i=0;i<nt->FileHeader.NumberOfSections;++i)if(F::Functions[0].beginRva>=sections[i].VirtualAddress&&F::Functions[0].beginRva<sections[i].VirtualAddress+sections[i].Misc.VirtualSize)return reinterpret_cast<U8*>(&sections[i].Characteristics);
    throw std::runtime_error("Pinned code section missing");}
void Store(Mapping& mapping,U8* pointer,U32 bytes,U32 value){auto* page=reinterpret_cast<U8*>(reinterpret_cast<U64>(pointer)&~U64{0xfff});mapping.Protection(page,4096,PAGE_READWRITE);
    for(U32 i=0;i<bytes;++i)pointer[i]=static_cast<U8>(value>>(8*i));mapping.Protection(page,4096,PAGE_READONLY);}
void Scenario(Mapping& mapping,U32 mode,const char* name,R::Result expected){
    const U32 beforeChecks=checks,beforeFailures=failures;std::memset(mapping.nearBytes,0xa5,R::ArenaMinimumBytes);auto request=Request(mapping);
    U8* mutation{};U32 oldValue{},mutationBytes{};
    if(mode==1)request.waitPhaseEntry=0;
    else if(mode==2)request.typedEntries[1]=request.typedEntries[0];
    else if(mode==3)request.nearCapacity=8191;
    else if(mode==4)request.nearRw=request.fixedImageBase-0x10000;
    else if(mode==5)request.nearRw=request.fixedImageBase+0xfffff000ull;
    else if(mode==6)request.nearRw=request.fixedImageBase+0x90000000ull;
    else if(mode==7){mutation=mapping.image+F::Functions[0].beginRva+0x20;mutationBytes=1;oldValue=*mutation;Store(mapping,mutation,1,oldValue^1);}
    else if(mode==8){mutation=FindRfField(mapping);mutationBytes=4;oldValue=Dword(mutation);Store(mapping,mutation,4,oldValue^4);}
    else if(mode==9){mutation=mapping.image+F::Unwinds[0].rva+4;mutationBytes=1;oldValue=*mutation;Store(mapping,mutation,1,oldValue^1);}
    else if(mode==10){mutation=mapping.image;mutationBytes=1;oldValue=*mutation;Store(mapping,mutation,1,oldValue^1);}
    else if(mode==11){mutation=TextCharacteristics(mapping);mutationBytes=4;oldValue=Dword(mutation);Store(mapping,mutation,4,oldValue|IMAGE_SCN_MEM_WRITE);}
    if(mutation)++ownedMutations;
    const std::vector<U8> beforeImage(mapping.image,mapping.image+F::ImageSize);
    if(mode==12)mapping.Protection(mapping.image,4096,PAGE_NOACCESS);
    if(mode==13)mapping.Protection(mapping.nearBytes+4096,4096,PAGE_NOACCESS);
    const auto actual=R::Prepare(&request,&plan);
    if(mode==12)mapping.Protection(mapping.image,4096,PAGE_READONLY);
    if(mode==13)mapping.Protection(mapping.nearBytes+4096,4096,PAGE_READWRITE);
    const bool unchanged=std::memcmp(mapping.image,beforeImage.data(),F::ImageSize)==0;
    mappedSourceUnchanged=mappedSourceUnchanged&&unchanged;Check(unchanged,"Prepare never writes any private source-image byte");
    Check(actual==expected&&plan.result==expected,"Actual preparation result matches this specific controlled scenario");
    bool untouched=false;
    if(mode==0){if(actual==R::Success)VerifyPlan(mapping,request);normal={failures==beforeFailures,plan.siteCount,plan.windowCount,plan.tableCount,plan.usedBytes,plan.nearBytesWritten};}
    else if(mode==13){Check((plan.flags&R::NearPrepared)==0&&(plan.flags&R::FactsVerified)!=0&&plan.nearWriteAttempted==1&&plan.nearBytesWritten==4096,"Partial near fault retains exact successful 4096-byte write responsibility");
        Check(std::memcmp(mapping.nearBytes,plan.preparedBytes,4096)==0&&Filled(mapping.nearBytes+4096,4096,0xa5),"Partial near output has exact prefix and untouched inaccessible suffix");}
    else{untouched=Filled(mapping.nearBytes,R::ArenaMinimumBytes,0xa5);Check((plan.flags&R::NearPrepared)==0&&plan.nearWriteAttempted==0&&plan.nearBytesWritten==0&&untouched,"Failed preflight leaves entire real near block untouched");}
    if(mutation)Store(mapping,mutation,mutationBytes,oldValue);
    Check(mapping.Unchanged(),"Owned negative mutation restored; original mapped input is intact");Check(mapping.NoExecute(),"All owned image/near pages remain non-executable");
    cases.push_back({name,static_cast<U32>(actual),static_cast<U32>(expected),plan.flags,plan.nearBytesWritten,plan.nearWriteAttempted,checks-beforeChecks,failures==beforeFailures,unchanged,untouched});
}
std::string Escape(const std::string& value){std::string result;for(const unsigned char c:value){if(c=='"'||c=='\\'){result+='\\';result+=static_cast<char>(c);}else if(c<' '){result+='?';}else result+=static_cast<char>(c);}return result;}
std::string Json(const std::string& diskHash,const std::string& fatal){const bool passed=failures==0&&cases.size()==14&&normal.passed;
    std::ostringstream out;out<<"{\"schemaVersion\":1,\"kind\":\"engine-transport-relay-preparation\",\"pid\":"<<GetCurrentProcessId()<<",\"passed\":"<<(passed?"true":"false")
        <<",\"checks\":"<<checks<<",\"failures\":"<<failures<<",\"fixtureExecuted\":true,\"relayCodeExecuted\":false,\"gameExecuted\":false,\"installerExecuted\":false,\"productionAdmissionVerified\":false,\"sourcePatched\":false"
        <<",\"mappedSourceUnchanged\":"<<(mappedSourceUnchanged?"true":"false")<<",\"mappedImageNeverExecutable\":"<<(mappedImageNeverExecutable?"true":"false")
        <<",\"actualKernelEntryVerified\":false,\"ownedEntryLiteralsOnly\":true,\"functionTableRegistered\":false,\"ownedNegativeImageMutations\":"<<ownedMutations
        <<",\"fixedDiskSha256\":\""<<diskHash<<"\",\"normal\":{\"passed\":"<<(normal.passed?"true":"false")<<",\"siteCount\":"<<normal.sites<<",\"windowCount\":"<<normal.windows
        <<",\"tableCount\":"<<normal.tables<<",\"usedBytes\":"<<normal.used<<",\"nearBytesWritten\":"<<normal.written<<"},\"fatal\":\""<<Escape(fatal)<<"\",\"cases\":[";
    for(std::size_t i=0;i<cases.size();++i){const auto& value=cases[i];if(i)out<<',';out<<"{\"name\":\""<<value.name<<"\",\"passed\":"<<(value.passed?"true":"false")<<",\"checks\":"<<value.checks
        <<",\"result\":"<<value.result<<",\"expected\":"<<value.expected<<",\"flags\":"<<value.flags<<",\"nearWriteAttempted\":"<<value.attempted<<",\"nearBytesWritten\":"<<value.written
        <<",\"sourceUnchanged\":"<<(value.sourceUnchanged?"true":"false")<<",\"nearUntouched\":"<<(value.nearUntouched?"true":"false")<<'}';}out<<"]}\n";return out.str();}
bool Absolute(const wchar_t* path){return path&&((std::wcslen(path)>3&&path[1]==L':'&&(path[2]==L'/'||path[2]==L'\\'))||(path[0]==L'\\'&&path[1]==L'\\'));}
}
int wmain(int argc,wchar_t** argv){
    if(argc!=4||std::wcscmp(argv[1],L"--all")||!Absolute(argv[2])||!Absolute(argv[3])||_wcsicmp(argv[2],argv[3])==0){std::fputs("Usage: --all <absolute-fixed-Unity-disk-path> <absolute-child-json-path>\n",stderr);return 2;}
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);std::string diskHash,fatal;
    try{Must(GetModuleHandleW(L"UnityPlayer.dll")==nullptr&&GetModuleHandleW(L"GameAssembly.dll")==nullptr,"Game modules are not loaded in the fixture process");
        const auto disk=Read(argv[2]);diskHash=Hash(disk);Must(diskHash==F::UnitySha256,"Actual fixed disk SHA matches generated facts");Mapping mapping;mapping.Create(disk);
        Must(mapping.NoExecute()&&mapping.Unchanged(),"Initial private image is unchanged and non-executable");
        const char* names[]{"normal","missing-entry","duplicate-entry","small-arena","near-below-image","rva-overflow","far-rel32","bad-fullbody","bad-runtimefunction","bad-unwind","bad-pe","writable-code-section","unreadable-image","partial-near"};
        const R::Result expected[]{R::Success,R::BadEntry,R::BadEntry,R::InvalidInput,R::AddressRange,R::AddressRange,R::AddressRange,R::BadBody,R::BadRuntimeFunction,R::BadRuntimeFunction,R::BadImage,R::BadBody,R::InvalidMemory,R::InvalidMemory};
        for(U32 i=0;i<14;++i)Scenario(mapping,i,names[i],expected[i]);Check(cases.size()==14,"Every actual prepare-only scenario completed");
        Must(Hash(Read(argv[2]))==diskHash,"Actual read-only game disk file remains unchanged after fixture work");
    }catch(const std::exception& error){fatal=error.what();Check(false,"Unexpected fixture setup/runtime exception");}
    catch(...){fatal="Unexpected native exception in owned fixture";Check(false,"Unexpected native fixture exception");}
    const auto json=Json(diskHash,fatal);FILE* file{};if(_wfopen_s(&file,argv[3],L"wb")!=0||!file){std::fputs("Cannot persist child JSON.\n",stderr);return 1;}
    const bool wrote=std::fwrite(json.data(),1,json.size(),file)==json.size();const bool flushed=std::fflush(file)==0;const bool closed=std::fclose(file)==0;
    if(!wrote||!flushed||!closed){std::fputs("Child JSON persistence failed.\n",stderr);return 1;}
    if(std::fwrite(json.data(),1,json.size(),stdout)!=json.size()||std::fflush(stdout)!=0)return 1;return failures||cases.size()!=14||!normal.passed?1:0;
}
