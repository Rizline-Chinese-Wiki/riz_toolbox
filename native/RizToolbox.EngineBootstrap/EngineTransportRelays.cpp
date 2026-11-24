#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include "EngineTransportRelays.h"
#include "EngineTransportGameProfile.h"

namespace RizEngineTransportRelays {
namespace {
namespace Facts=RizEngineTransportGameProfile;
using U8=std::uint8_t;using U32=std::uint32_t;using U64=std::uint64_t;
constexpr U32 NoIndex=0xffffffffu,ZeroCodeOffset=112;
bool Add(U64 a,U64 b,U64* result){if(a>~U64{0}-b)return false;*result=a+b;return true;}
bool Fits(U64 offset,U64 bytes,U64 total){return offset<=total&&bytes<=total-offset;}
bool Intersects(U64 a,U64 countA,U64 b,U64 countB){U64 endA{},endB{};
    return !Add(a,countA,&endA)||!Add(b,countB,&endB)||(a<endB&&b<endA);}
bool Equal(const U8* a,const U8* b,U32 count){for(U32 n=0;n<count;++n)if(a[n]!=b[n])return false;return true;}
void Copy(U8* to,const U8* from,U32 count){__movsb(to,from,count);}
void Put32(U8* to,U32 value){for(U32 n=0;n<4;++n)to[n]=static_cast<U8>(value>>(n*8));}
void Put64(U8* to,U64 value){for(U32 n=0;n<8;++n)to[n]=static_cast<U8>(value>>(n*8));}
U32 Read32(const U8* from){U32 value{};for(U32 n=0;n<4;++n)value|=static_cast<U32>(from[n])<<(n*8);return value;}
bool Relative(U64 target,U64 after,U32* encoded){
    if(target>=after){const U64 delta=target-after;if(delta>0x7fffffffull)return false;*encoded=static_cast<U32>(delta);}
    else{const U64 delta=after-target;if(delta>0x80000000ull)return false;*encoded=0u-static_cast<U32>(delta);}
    return true;
}
Result Fail(Plan* plan,Result result,Object object,U32 index){plan->result=result;plan->failureObject=object;plan->failureIndex=index;
    plan->flags&=~NearPrepared;return result;}
struct Image {
    const U8* base;const IMAGE_SECTION_HEADER* sections;const IMAGE_RUNTIME_FUNCTION_ENTRY* functions;
    U32 sectionCount,functionCount,headersBytes,sectionAlignment,fileAlignment;
};
bool PowerOfTwo(U32 value){return value&&(value&(value-1))==0;}
bool SectionContains(const Image& image,U32 rva,U32 count,U32 required,U32 forbidden){
    if(!count||!Fits(rva,count,Facts::ImageSize))return false;U32 matches{};
    for(U32 index=0;index<image.sectionCount;++index){const auto& section=image.sections[index];
        if(rva>=section.VirtualAddress&&Fits(static_cast<U64>(rva)-section.VirtualAddress,count,section.Misc.VirtualSize)){
            if((section.Characteristics&required)!=required||(section.Characteristics&forbidden)!=0)return false;++matches;}}
    return matches==1;
}
bool SameRf(const IMAGE_RUNTIME_FUNCTION_ENTRY& actual,const Facts::RuntimeFunction& expected){
    return actual.BeginAddress==expected.beginRva&&actual.EndAddress==expected.endRva&&actual.UnwindData==expected.unwindRva;
}
bool FindRf(const Image& image,const Facts::RuntimeFunction& expected){
    U32 low{},high=image.functionCount;
    while(low<high){const U32 middle=low+(high-low)/2;const auto& entry=image.functions[middle];
        if(entry.BeginAddress<expected.beginRva)low=middle+1;else high=middle;}
    return low<image.functionCount&&SameRf(image.functions[low],expected);
}
bool ParseImage(const Request& request,Image* image){
    if(request.fixedImageBytes!=Facts::ImageSize)return false;
    image->base=reinterpret_cast<const U8*>(request.fixedImageBase);
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(image->base);
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<static_cast<LONG>(sizeof(IMAGE_DOS_HEADER)))return false;
    const U32 ntOffset=static_cast<U32>(dos->e_lfanew);
    if(!Fits(ntOffset,sizeof(IMAGE_NT_HEADERS64),request.fixedImageBytes))return false;
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(image->base+ntOffset);
    if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=Facts::Machine
        ||nt->FileHeader.TimeDateStamp!=Facts::TimeDateStamp||nt->FileHeader.SizeOfOptionalHeader!=sizeof(IMAGE_OPTIONAL_HEADER64)
        ||!nt->FileHeader.NumberOfSections||nt->FileHeader.NumberOfSections>96
        ||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||nt->OptionalHeader.SizeOfImage!=Facts::ImageSize
        ||(nt->OptionalHeader.ImageBase!=Facts::PreferredImageBase&&nt->OptionalHeader.ImageBase!=request.fixedImageBase)
        ||nt->OptionalHeader.AddressOfEntryPoint!=Facts::EntryRva
        ||nt->OptionalHeader.NumberOfRvaAndSizes!=IMAGE_NUMBEROF_DIRECTORY_ENTRIES)return false;
    const auto& exception=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    if(exception.VirtualAddress!=Facts::ExceptionRva||exception.Size!=Facts::ExceptionBytes
        ||exception.Size%sizeof(IMAGE_RUNTIME_FUNCTION_ENTRY)!=0)return false;
    image->sectionAlignment=nt->OptionalHeader.SectionAlignment;image->fileAlignment=nt->OptionalHeader.FileAlignment;
    image->headersBytes=nt->OptionalHeader.SizeOfHeaders;image->sectionCount=nt->FileHeader.NumberOfSections;
    if(!PowerOfTwo(image->sectionAlignment)||!PowerOfTwo(image->fileAlignment)||image->sectionAlignment<0x1000
        ||image->fileAlignment<0x200||image->fileAlignment>0x10000||image->sectionAlignment<image->fileAlignment
        ||image->headersBytes%image->fileAlignment||!image->headersBytes||image->headersBytes>Facts::ImageSize
        ||Facts::ImageSize%image->sectionAlignment)return false;
    const U64 sectionOffset=static_cast<U64>(ntOffset)+sizeof(IMAGE_NT_HEADERS64);
    if(!Fits(sectionOffset,static_cast<U64>(image->sectionCount)*sizeof(IMAGE_SECTION_HEADER),image->headersBytes))return false;
    image->sections=reinterpret_cast<const IMAGE_SECTION_HEADER*>(image->base+sectionOffset);
    for(U32 index=0;index<image->sectionCount;++index){const auto& section=image->sections[index];
        if(section.VirtualAddress%image->sectionAlignment||section.VirtualAddress<image->headersBytes
            ||!section.Misc.VirtualSize||!Fits(section.VirtualAddress,section.Misc.VirtualSize,Facts::ImageSize)
            ||section.SizeOfRawData%image->fileAlignment||section.PointerToRawData%image->fileAlignment
            ||(section.SizeOfRawData&&(section.PointerToRawData<image->headersBytes||!Fits(section.VirtualAddress,section.SizeOfRawData,Facts::ImageSize)))
            ||!Fits(section.PointerToRawData,section.SizeOfRawData,Facts::UnityFileBytes))return false;
        for(U32 other=0;other<index;++other){const auto& prior=image->sections[other];
            if(Intersects(section.VirtualAddress,section.Misc.VirtualSize,prior.VirtualAddress,prior.Misc.VirtualSize)
                ||(section.SizeOfRawData&&prior.SizeOfRawData&&Intersects(section.PointerToRawData,section.SizeOfRawData,prior.PointerToRawData,prior.SizeOfRawData)))return false;}}
    if(!SectionContains(*image,Facts::ExceptionRva,Facts::ExceptionBytes,IMAGE_SCN_MEM_READ,IMAGE_SCN_MEM_WRITE|IMAGE_SCN_MEM_EXECUTE))return false;
    image->functions=reinterpret_cast<const IMAGE_RUNTIME_FUNCTION_ENTRY*>(image->base+Facts::ExceptionRva);
    image->functionCount=Facts::ExceptionBytes/static_cast<U32>(sizeof(IMAGE_RUNTIME_FUNCTION_ENTRY));
    for(U32 index=0;index<image->functionCount;++index){const auto& entry=image->functions[index];
        if(entry.BeginAddress>=entry.EndAddress||entry.EndAddress>Facts::ImageSize||!entry.UnwindData||entry.UnwindData>=Facts::ImageSize
            ||(index&&image->functions[index-1].BeginAddress>=entry.BeginAddress))return false;}
    return true;
}
Result ValidateFacts(const Request& request,Plan* plan){
    Image image;__stosb(reinterpret_cast<U8*>(&image),0,sizeof(image));if(!ParseImage(request,&image))return Fail(plan,BadImage,ImageObject,0);
    U32 sourceCount{};
    for(U32 index=0;index<Facts::TypedCount;++index){const auto& function=Facts::Functions[index];
        if(function.index!=index||function.bodyBytes!=function.endRva-function.beginRva
            ||function.continuationRva!=function.beginRva+function.prefixBytes
            ||!SectionContains(image,function.beginRva,function.bodyBytes,IMAGE_SCN_MEM_READ|IMAGE_SCN_MEM_EXECUTE,IMAGE_SCN_MEM_WRITE)
            ||!Equal(image.base+function.beginRva,function.body,function.bodyBytes))return Fail(plan,BadBody,FunctionObject,index);
        for(U32 rfIndex=0;rfIndex<function.runtimeFunctionCount;++rfIndex){const auto& rf=function.runtimeFunctions[rfIndex];
            if(!FindRf(image,rf)||rf.beginRva<function.beginRva||rf.endRva>function.endRva)return Fail(plan,BadRuntimeFunction,FunctionObject,index);++sourceCount;}}
    if(sourceCount!=27)return Fail(plan,BadRuntimeFunction,ImageObject,0);
    for(U32 index=0;index<sizeof(Facts::Unwinds)/sizeof(Facts::Unwinds[0]);++index){const auto& unwind=Facts::Unwinds[index];
        if(unwind.version!=1||(unwind.flags!=0&&unwind.flags!=4)||unwind.frameRegister||unwind.frameOffsetBytes
            ||!SectionContains(image,unwind.rva,unwind.byteCount,IMAGE_SCN_MEM_READ,IMAGE_SCN_MEM_WRITE|IMAGE_SCN_MEM_EXECUTE)
            ||!Equal(image.base+unwind.rva,unwind.raw,unwind.byteCount))return Fail(plan,BadRuntimeFunction,UnwindObject,index);
        if(unwind.flags==4&&!FindRf(image,unwind.chain))return Fail(plan,BadRuntimeFunction,UnwindObject,index);}
    for(U32 index=0;index<WindowCount;++index){const auto& window=Facts::PhysicalWindows[index];auto& output=plan->windows[index];
        if(window.index!=index||window.byteCount<5||window.byteCount>32||window.endRva-window.beginRva!=window.byteCount
            ||!SectionContains(image,window.beginRva,window.byteCount,IMAGE_SCN_MEM_READ|IMAGE_SCN_MEM_EXECUTE,IMAGE_SCN_MEM_WRITE)
            ||!Equal(image.base+window.beginRva,window.bytes,window.byteCount)||!FindRf(image,window.runtimeFunction))return Fail(plan,BadSource,WindowObject,index);
        for(U32 prior=0;prior<index;++prior){const auto& other=Facts::PhysicalWindows[prior];
            if(Intersects(window.beginRva,window.byteCount,other.beginRva,other.byteCount))return Fail(plan,Overlap,WindowObject,index);}
        output.index=index;output.role=window.role;output.roleIndex=window.roleIndex;output.byteCount=window.byteCount;
        output.source=request.fixedImageBase+window.beginRva;Copy(output.expected,window.bytes,window.byteCount);}
    for(U32 index=0;index<sizeof(Facts::PreservedCalls)/sizeof(Facts::PreservedCalls[0]);++index){const auto& call=Facts::PreservedCalls[index];
        if(call.callRva+call.byteCount!=call.returnPcRva||!SectionContains(image,call.callRva,call.byteCount,IMAGE_SCN_MEM_READ|IMAGE_SCN_MEM_EXECUTE,IMAGE_SCN_MEM_WRITE)
            ||!Equal(image.base+call.callRva,call.bytes,call.byteCount))return Fail(plan,BadSource,CallObject,index);
        for(U32 windowIndex=0;windowIndex<WindowCount;++windowIndex){const auto& window=Facts::PhysicalWindows[windowIndex];
            if(Intersects(call.callRva,call.byteCount,window.beginRva,window.byteCount))return Fail(plan,Overlap,CallObject,index);}}
    plan->flags=FactsVerified;return Success;
}
struct Emitter {
    Plan* plan;Site* site;U8* bytes;U32 cursor;bool okay;
    void Byte(U8 value){if(cursor>=LiteralOffset){okay=false;return;}bytes[cursor++]=value;}
    void Bytes(const U8* values,U32 count){for(U32 n=0;n<count;++n)Byte(values[n]);}
    void Dword(U32 value){for(U32 n=0;n<4;++n)Byte(static_cast<U8>(value>>(n*8)));}
    void Jump(U64 target){U32 displacement{};if(!Relative(target,site->relay+cursor+5,&displacement)){okay=false;return;}Byte(0xe9);Dword(displacement);}
    void Zero(U64 target){U32 displacement{};if(!Relative(target,site->relay+cursor+6,&displacement)){okay=false;return;}Byte(0x0f);Byte(0x84);Dword(displacement);}
    void Probe(){U32 displacement{};if(!Relative(site->literal,site->relay+cursor+6,&displacement)){okay=false;return;}
        Byte(0xff);Byte(0x15);Dword(displacement);site->relayReturnPc=site->relay+cursor;}
    void Rip(const U8* instruction,U64 originalPc){
        const U32 encoded=Read32(instruction+3);const auto signedOffset=static_cast<std::int32_t>(encoded);
        U64 target{};const U64 after=originalPc+7;
        if(signedOffset<0){const U64 distance=static_cast<U64>(-static_cast<std::int64_t>(signedOffset));if(after<distance){okay=false;return;}target=after-distance;}
        else if(!Add(after,static_cast<U32>(signedOffset),&target)){okay=false;return;}
        U32 replacement{};if(!Relative(target,site->relay+cursor+7,&replacement)){okay=false;return;}
        Bytes(instruction,3);Dword(replacement);
    }
    void ToZero(){if(cursor>ZeroCodeOffset){okay=false;return;}while(cursor<ZeroCodeOffset)Byte(0x90);}
};
U32 SourceRfIndex(const Facts::PhysicalWindow& window){U32 index{};
    for(U32 function=0;function<Facts::TypedCount;++function)for(U32 n=0;n<Facts::Functions[function].runtimeFunctionCount;++n,++index){
        const auto& actual=Facts::Functions[function].runtimeFunctions[n];
        if(actual.beginRva==window.runtimeFunction.beginRva&&actual.endRva==window.runtimeFunction.endRva&&actual.unwindRva==window.runtimeFunction.unwindRva)return index;}
    return NoIndex;
}
bool EmitSite(const Request& request,Plan* plan,U32 index){
    const auto& fact=Facts::Sites[index];const auto& physical=Facts::PhysicalWindows[fact.physicalWindowIndex];auto& site=plan->sites[index];
    site.index=index;site.family=fact.family;site.phase=fact.familyIndex;site.windowIndex=fact.physicalWindowIndex;site.functionIndex=physical.functionIndex;
    site.sourceRfIndex=SourceRfIndex(physical);site.relativeOffset=SlotOffset+index*SlotBytes;site.toImageStart=plan->arenaToImageStart+site.relativeOffset;
    site.logicalSitePc=request.fixedImageBase+fact.observationRva;site.physicalPatchPc=request.fixedImageBase+fact.patchRva;
    const bool earlyDispatch=index>=17&&index<=20;
    site.relay=request.nearRw+site.relativeOffset;site.literal=site.relay+LiteralOffset;site.unwind=site.relay+UnwindOffset;
    site.unwindBytes=UnwindBytes;
    site.probeEntry=fact.family==0?request.inlineEntry:fact.family==1?request.rowEntry:request.waitPhaseEntry;
    site.continuation=request.fixedImageBase+fact.physicalContinuationRva;site.expandedPhysicalBlock=fact.expandedPhysicalBlock?1u:0u;
    Emitter out{plan,&site,plan->preparedBytes+site.relativeOffset,0,true};const U8* source=physical.bytes;
    const U64 branch=request.fixedImageBase+fact.branchTargetRva;
    if(fact.expandedPhysicalBlock){
        const U32 bypassIndex=index==9?0u:index==11?1u:2u;const auto& bypass=Facts::ConditionalBypasses[bypassIndex];
        if(bypass.observationRva!=fact.observationRva||bypass.patchRva!=fact.patchRva||bypass.patchBytes!=physical.byteCount)return false;
        site.alternateContinuation=request.fixedImageBase+bypass.zeroContinuationRva;
        if(index==9){out.Bytes(source,7);out.Zero(site.relay+ZeroCodeOffset);out.Probe();out.Bytes(source+9,3);out.Rip(source+12,request.fixedImageBase+fact.patchRva+12);}
        else{out.Bytes(source,3);out.Zero(site.relay+ZeroCodeOffset);out.Probe();out.Bytes(source+5,7);}
        out.Jump(site.continuation);out.ToZero();
        if(bypass.commonRipMov)out.Rip(bypass.zeroReplay,request.fixedImageBase+bypass.zeroReplayRva);
        else out.Bytes(bypass.zeroReplay,bypass.zeroReplayBytes);
        out.Jump(site.alternateContinuation);
    }else{
        out.Probe();
        switch(index){
        case 1:case 2:case 8:out.Jump(branch);break;
        case 3:out.Bytes(source,4);out.Zero(branch);out.Jump(site.continuation);break;
        case 5:out.Bytes(source,3);out.Rip(source+3,request.fixedImageBase+fact.patchRva+3);out.Jump(site.continuation);break;
        case 6:out.Zero(branch);out.Bytes(source+2,4);out.Jump(site.continuation);break;
        case 10:out.Bytes(source,3);out.Rip(source+3,request.fixedImageBase+fact.patchRva+3);out.Jump(site.continuation);break;
        case 17:case 19:out.Bytes(source,3);out.Zero(branch);out.Jump(site.continuation);break;
        case 21:out.Bytes(source,4);out.Zero(branch);out.Jump(site.continuation);break;
        default:out.Bytes(source,physical.byteCount);out.Jump(site.continuation);break;
        }
    }
    site.codeBytes=out.cursor;if(!out.okay||site.sourceRfIndex==NoIndex||!site.relayReturnPc||site.codeBytes>LiteralOffset)return false;
    Put64(out.bytes+LiteralOffset,site.probeEntry);
    auto* unwind=out.bytes+UnwindOffset;
    unwind[0]=0x21; // Version1|UNW_FLAG_CHAININFO; no local stack change.
    Put32(unwind+4,physical.runtimeFunction.beginRva);Put32(unwind+8,physical.runtimeFunction.endRva);
    if(earlyDispatch){
        // The source primary has a late RBX save which has not happened at
        // these four sites. Use a distinct, explicit partial primary rather
        // than applying that future save. Its Begin/End retain the actual
        // Dispatch function identity, so source/relay E9s remain intra-function
        // branches to the Windows epilog classifier. This parent is metadata,
        // not another registered executable range or an actual source RF row.
        Put32(unwind+12,site.toImageStart+DispatchPrimaryUnwindOffset);
        const U8 established[]{0x01,0x00,0x03,0x00,0x00,0x01,0x2a,0x00,0x00,0x70,0x00,0x00};
        Copy(out.bytes+DispatchPrimaryUnwindOffset,established,DispatchUnwindBytes);
    }else Put32(unwind+12,physical.runtimeFunction.unwindRva);
    site.runtimeFunction={site.toImageStart,site.toImageStart+site.codeBytes,site.toImageStart+UnwindOffset};
    auto* table=plan->preparedBytes+index*sizeof(RuntimeFunction);Put32(table,site.runtimeFunction.beginRva);Put32(table+4,site.runtimeFunction.endRva);Put32(table+8,site.runtimeFunction.unwindRva);
    auto& window=plan->windows[site.windowIndex];window.relay=site.relay;window.entry=site.probeEntry;
    U32 displacement{};if(!Relative(site.relay,window.source+5,&displacement))return false;window.patch[0]=0xe9;Put32(window.patch+1,displacement);
    for(U32 n=5;n<window.byteCount;++n)window.patch[n]=0x90;
    return true;
}
Result PrepareCore(const Request& request,Plan* plan,bool writeNear){
    plan->version=Version;plan->byteSize=static_cast<U32>(sizeof(Plan));plan->failureIndex=NoIndex;
    if(request.version!=Version||request.byteSize!=sizeof(Request)||!request.fixedImageBase||!request.nearRw
        ||request.fixedImageBase%0x1000||request.nearCapacity<ArenaMinimumBytes)return Fail(plan,InvalidInput,NoObject,NoIndex);
    U64 imageEnd{},nearEnd{};
    if(!Add(request.fixedImageBase,request.fixedImageBytes,&imageEnd)||!Add(request.nearRw,request.nearCapacity,&nearEnd)
        ||request.nearRw<imageEnd||request.nearRw-request.fixedImageBase>0xffffffffull-ArenaUsedBytes
        ||request.nearRw%16)return Fail(plan,AddressRange,NoObject,NoIndex);
    if(!request.inlineEntry||!request.rowEntry||!request.waitPhaseEntry)return Fail(plan,BadEntry,NoObject,NoIndex);
    for(U32 index=0;index<TypedCount;++index){if(!request.typedEntries[index])return Fail(plan,BadEntry,FunctionObject,index);
        if(Intersects(request.typedEntries[index],1,request.fixedImageBase,request.fixedImageBytes)
            ||Intersects(request.typedEntries[index],1,request.nearRw,request.nearCapacity))return Fail(plan,BadEntry,FunctionObject,index);
        for(U32 prior=0;prior<index;++prior)if(request.typedEntries[index]==request.typedEntries[prior])return Fail(plan,BadEntry,FunctionObject,index);}
    if(request.inlineEntry==request.rowEntry||request.inlineEntry==request.waitPhaseEntry||request.rowEntry==request.waitPhaseEntry)return Fail(plan,BadEntry,SiteObject,NoIndex);
    const U64 probes[]{request.inlineEntry,request.rowEntry,request.waitPhaseEntry};
    for(U32 index=0;index<3;++index){if(Intersects(probes[index],1,request.fixedImageBase,request.fixedImageBytes)
        ||Intersects(probes[index],1,request.nearRw,request.nearCapacity))return Fail(plan,BadEntry,SiteObject,index);
        for(U32 typed=0;typed<TypedCount;++typed)if(probes[index]==request.typedEntries[typed])return Fail(plan,BadEntry,SiteObject,index);}
    plan->fixedImageBase=request.fixedImageBase;plan->nearRw=request.nearRw;plan->nearCapacity=request.nearCapacity;
    plan->tableBase=request.fixedImageBase;plan->tableAddress=request.nearRw+TableOffset;plan->arenaToImageStart=static_cast<U32>(request.nearRw-request.fixedImageBase);
    const auto validated=ValidateFacts(request,plan);if(validated!=Success)return validated;
    for(U32 index=0;index<TypedCount;++index){auto& window=plan->windows[index];
        if(window.byteCount<14)return Fail(plan,BadSource,WindowObject,index);window.entry=request.typedEntries[index];
        window.patch[0]=0xff;window.patch[1]=0x25;Put64(window.patch+6,window.entry);
        for(U32 n=14;n<window.byteCount;++n)window.patch[n]=0x90;}
    for(U32 index=0;index<SiteCount;++index)if(!EmitSite(request,plan,index))return Fail(plan,AddressRange,SiteObject,index);
    plan->siteCount=SiteCount;plan->windowCount=WindowCount;plan->tableCount=SiteCount;plan->usedBytes=ArenaUsedBytes;
    if(!writeNear){plan->result=Success;return Success;}
    // Only now touch caller-owned near memory. No source address is ever a
    // destination. Increment after each successful byte for fault retention.
    auto* nearOutput=reinterpret_cast<volatile U8*>(request.nearRw);plan->nearWriteAttempted=1;
    for(U32 index=0;index<ArenaUsedBytes;++index){nearOutput[index]=plan->preparedBytes[index];++plan->nearBytesWritten;}
    for(U32 index=0;index<ArenaUsedBytes;++index)if(nearOutput[index]!=plan->preparedBytes[index])return Fail(plan,EmissionMismatch,NoObject,index);
    plan->flags|=NearPrepared;plan->result=Success;return Success;
}
}
Result Prepare(const Request* request,Plan* output){
    if(!request||!output)return InvalidInput;bool writable=false;
    __try{
        Request copied;__movsb(reinterpret_cast<U8*>(&copied),reinterpret_cast<const U8*>(request),sizeof(copied));
        const U64 requestAddress=reinterpret_cast<U64>(request),outputAddress=reinterpret_cast<U64>(output);
        if(Intersects(requestAddress,sizeof(Request),outputAddress,sizeof(Plan))
            ||Intersects(outputAddress,sizeof(Plan),copied.fixedImageBase,copied.fixedImageBytes)
            ||Intersects(requestAddress,sizeof(Request),copied.fixedImageBase,copied.fixedImageBytes)
            ||Intersects(outputAddress,sizeof(Plan),copied.nearRw,copied.nearCapacity)
            ||Intersects(requestAddress,sizeof(Request),copied.nearRw,copied.nearCapacity))return Overlap;
        __stosb(reinterpret_cast<U8*>(output),0,sizeof(Plan));writable=true;return PrepareCore(copied,output,true);
    }__except(EXCEPTION_EXECUTE_HANDLER){
        if(writable){__try{output->result=InvalidMemory;output->flags&=~NearPrepared;}__except(EXCEPTION_EXECUTE_HANDLER){}}
        return InvalidMemory;
    }
}
namespace {
Plan canonicalPlan{};
volatile LONG canonicalGate{};
}
Result VerifyPrepared(const Plan* prepared){
    if(!prepared)return InvalidInput;
    if(InterlockedCompareExchange(&canonicalGate,1,0))return Busy;
    Result result=InvalidInput;
    __try{
        const U64 privateAddress=reinterpret_cast<U64>(&canonicalPlan);
        if(Intersects(reinterpret_cast<U64>(prepared),sizeof(Plan),privateAddress,sizeof(Plan)))__leave;
        if(prepared->version!=Version||prepared->byteSize!=sizeof(Plan)||prepared->result!=Success
            ||prepared->flags!=(FactsVerified|NearPrepared)||prepared->nearWriteAttempted!=1
            ||prepared->nearBytesWritten!=ArenaUsedBytes)__leave;
        Request request;__stosb(reinterpret_cast<U8*>(&request),0,sizeof(request));
        request.version=Version;request.byteSize=sizeof(request);
        request.fixedImageBase=prepared->fixedImageBase;request.fixedImageBytes=Facts::ImageSize;
        request.nearRw=prepared->nearRw;request.nearCapacity=prepared->nearCapacity;
        for(U32 i=0;i<TypedCount;++i)request.typedEntries[i]=prepared->windows[i].entry;
        request.inlineEntry=prepared->sites[0].probeEntry;request.rowEntry=prepared->sites[3].probeEntry;
        request.waitPhaseEntry=prepared->sites[5].probeEntry;
        if(Intersects(privateAddress,sizeof(Plan),request.fixedImageBase,request.fixedImageBytes)
            ||Intersects(privateAddress,sizeof(Plan),request.nearRw,request.nearCapacity))__leave;
        __stosb(reinterpret_cast<U8*>(&canonicalPlan),0,sizeof(canonicalPlan));
        result=PrepareCore(request,&canonicalPlan,false);if(result!=Success)__leave;
        // Normalize only the three successful actual-copy fields. Every other
        // metadata byte and all 6400 emitted bytes remain independently built.
        canonicalPlan.flags|=NearPrepared;canonicalPlan.nearWriteAttempted=1;
        canonicalPlan.nearBytesWritten=ArenaUsedBytes;
        if(!Equal(reinterpret_cast<const U8*>(prepared),reinterpret_cast<const U8*>(&canonicalPlan),sizeof(Plan))
            ||!Equal(reinterpret_cast<const U8*>(request.nearRw),canonicalPlan.preparedBytes,ArenaUsedBytes))result=EmissionMismatch;
    }__except(EXCEPTION_EXECUTE_HANDLER){result=InvalidMemory;}
    InterlockedExchange(&canonicalGate,0);return result;
}
}
