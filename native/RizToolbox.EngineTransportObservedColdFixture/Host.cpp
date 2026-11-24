#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "../RizToolbox.EngineBootstrap/EngineTransportPublication.h"
#include "EngineTransportObservation.Generated.h"
extern "C" __declspec(dllimport) int __cdecl UnityMain2();
namespace {
namespace O=RizEngineTransportOwnedObservation;
RizEngineTransportPublicationV5 first{},second{},finalPublication{};
unsigned char initial[O::StorageBytes]{},finalObservation[O::StorageBytes]{};
std::uint32_t checks=0,failures=0;
struct ObservedRegion {
    std::uint64_t queriedAddress,queryBytes,base,allocationBase,regionBytes;
    std::uint32_t allocationProtect,state,protect,type,lastError;
};
struct ObservationReadDiagnostic {
    std::uint64_t requestedAddress,expectedModule,requestedBytes,copiedBytes;
    std::uint32_t regionCount,strictMemoryAccepted,copySucceeded,copyError;
    std::uint32_t sectionRwBackedAccepted,readwriteOnlyAccepted,copyOnWriteRegions;
    ObservedRegion regions[16];
};
ObservationReadDiagnostic firstDiagnostic{},finalDiagnostic{};
bool Check(bool value,const char* text,int line){++checks;if(!value){++failures;std::fprintf(stderr,"ObservedCold line %d: %s\n",line,text);std::fflush(stderr);}return value;}
#define CHECK(value) Check(!!(value),#value,__LINE__)
template<class T>T Read(const unsigned char* value,std::size_t offset){T result{};std::memcpy(&result,value+offset,sizeof(result));return result;}
bool Zero(const unsigned char* value,std::size_t bytes){for(std::size_t i=0;i<bytes;++i)if(value[i])return false;return true;}
bool Persist(const wchar_t* path,const void* value,std::size_t bytes){FILE* file{};if(_wfopen_s(&file,path,L"wb")||!file)return false;
    const bool wrote=std::fwrite(value,1,bytes,file)==bytes,flushed=std::fflush(file)==0,closed=std::fclose(file)==0;return wrote&&flushed&&closed;}
template<class T>T Export(HMODULE module,const char* name){return reinterpret_cast<T>(GetProcAddress(module,name));}
bool Memory(const void* address,std::size_t bytes,const void* module,DWORD protection){
    auto current=reinterpret_cast<std::uintptr_t>(address);const auto end=current+bytes;
    if(end<current)return false;
    while(current<end){MEMORY_BASIC_INFORMATION memory{};
        if(VirtualQuery(reinterpret_cast<void*>(current),&memory,sizeof(memory))!=sizeof(memory)||memory.AllocationBase!=module
            ||memory.State!=MEM_COMMIT||memory.Type!=MEM_IMAGE||memory.Protect!=protection)return false;
        const auto next=reinterpret_cast<std::uintptr_t>(memory.BaseAddress)+memory.RegionSize;if(next<=current)return false;
        current=next<end?next:end;}
    return true;
}
bool ObservationStorage(HMODULE target){
    // This exception is specific to backed non-executable writable image data.
    // The generic code/unwind Memory checks retain their exact RX/R protections.
    if(O::StorageBytes!=9792||!Memory(target,sizeof(IMAGE_DOS_HEADER),target,PAGE_READONLY))return false;
    const auto base=reinterpret_cast<std::uintptr_t>(target);const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(target);
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<64||dos->e_lfanew>4096-static_cast<LONG>(sizeof(IMAGE_NT_HEADERS64)))return false;
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(base+static_cast<std::uintptr_t>(dos->e_lfanew));
    if(!Memory(nt,sizeof(*nt),target,PAGE_READONLY)||nt->Signature!=IMAGE_NT_SIGNATURE
        ||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||nt->FileHeader.NumberOfSections!=8
        ||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||nt->FileHeader.SizeOfOptionalHeader!=sizeof(IMAGE_OPTIONAL_HEADER64))return false;
    const auto* sections=IMAGE_FIRST_SECTION(nt);const auto sectionBytes=8*sizeof(IMAGE_SECTION_HEADER);
    if(reinterpret_cast<std::uintptr_t>(sections)<base||reinterpret_cast<std::uintptr_t>(sections)-base+sectionBytes>nt->OptionalHeader.SizeOfHeaders
        ||!Memory(sections,sectionBytes,target,PAGE_READONLY))return false;
    unsigned matching=0;
    for(unsigned index=0;index<8;++index){const auto& section=sections[index];
        if(std::memcmp(section.Name,".data\0\0\0",8)!=0)continue;
        if((section.Characteristics&(IMAGE_SCN_MEM_READ|IMAGE_SCN_MEM_WRITE|IMAGE_SCN_MEM_EXECUTE))!=(IMAGE_SCN_MEM_READ|IMAGE_SCN_MEM_WRITE)
            ||O::StorageRva!=section.VirtualAddress+0x1000||O::StorageRva<section.VirtualAddress)return false;
        const auto relative=static_cast<std::uint64_t>(O::StorageRva)-section.VirtualAddress;
        if(relative+O::StorageBytes>section.Misc.VirtualSize||relative+O::StorageBytes>section.SizeOfRawData)return false;
        ++matching;
    }
    return matching==1;
}
bool CopyObservation(HMODULE target,unsigned char* destination,ObservationReadDiagnostic& diagnostic){
    const auto* address=reinterpret_cast<const unsigned char*>(target)+O::StorageRva;
    diagnostic.requestedAddress=reinterpret_cast<std::uint64_t>(address);diagnostic.expectedModule=reinterpret_cast<std::uint64_t>(target);
    diagnostic.requestedBytes=O::StorageBytes;
    auto current=diagnostic.requestedAddress;const auto end=current+O::StorageBytes;
    while(current<end&&diagnostic.regionCount<16){MEMORY_BASIC_INFORMATION memory{};
        auto& row=diagnostic.regions[diagnostic.regionCount++];row.queriedAddress=current;SetLastError(ERROR_SUCCESS);
        row.queryBytes=VirtualQuery(reinterpret_cast<void*>(current),&memory,sizeof(memory));row.lastError=GetLastError();
        row.base=reinterpret_cast<std::uint64_t>(memory.BaseAddress);row.allocationBase=reinterpret_cast<std::uint64_t>(memory.AllocationBase);
        row.regionBytes=memory.RegionSize;row.allocationProtect=memory.AllocationProtect;row.state=memory.State;row.protect=memory.Protect;row.type=memory.Type;
        std::fprintf(stderr,"ObservedCold storage query address=0x%llx returned=%llu base=0x%llx allocation=0x%llx bytes=%llu allocProtect=0x%lx state=0x%lx protect=0x%lx type=0x%lx error=%lu\n",
            static_cast<unsigned long long>(current),static_cast<unsigned long long>(row.queryBytes),static_cast<unsigned long long>(row.base),
            static_cast<unsigned long long>(row.allocationBase),static_cast<unsigned long long>(row.regionBytes),
            static_cast<unsigned long>(row.allocationProtect),static_cast<unsigned long>(row.state),static_cast<unsigned long>(row.protect),
            static_cast<unsigned long>(row.type),static_cast<unsigned long>(row.lastError));std::fflush(stderr);
        if(row.queryBytes!=sizeof(memory)||row.base+row.regionBytes<=current)break;
        current=row.base+row.regionBytes<end?row.base+row.regionBytes:end;
    }
    diagnostic.sectionRwBackedAccepted=ObservationStorage(target)?1u:0u;
    diagnostic.readwriteOnlyAccepted=Memory(address,O::StorageBytes,target,PAGE_READWRITE)?1u:0u;
    bool storageAccepted=diagnostic.sectionRwBackedAccepted&&current==end&&diagnostic.regionCount>0;
    for(unsigned index=0;index<diagnostic.regionCount;++index){const auto& row=diagnostic.regions[index];
        if(row.queryBytes!=sizeof(MEMORY_BASIC_INFORMATION)||row.allocationBase!=diagnostic.expectedModule
            ||row.state!=MEM_COMMIT||row.type!=MEM_IMAGE||(row.protect!=PAGE_READWRITE&&row.protect!=PAGE_WRITECOPY)
            ||row.queriedAddress<row.base||row.queriedAddress-row.base>=row.regionBytes)storageAccepted=false;
        if(row.protect==PAGE_WRITECOPY)++diagnostic.copyOnWriteRegions;
    }
    diagnostic.strictMemoryAccepted=storageAccepted?1u:0u;
    // Evidence-only copy: successful ReadProcessMemory must never promote a
    // rejected memory identity/protection into a passing storage proof.
    SIZE_T copied{};SetLastError(ERROR_SUCCESS);
    diagnostic.copySucceeded=ReadProcessMemory(GetCurrentProcess(),address,destination,O::StorageBytes,&copied)?1u:0u;
    diagnostic.copyError=GetLastError();diagnostic.copiedBytes=copied;
    return diagnostic.strictMemoryAccepted&&diagnostic.copySucceeded&&copied==O::StorageBytes;
}
bool PersistFirstReadFailure(wchar_t** argv,HMODULE target){
    const bool initialRaw=Persist(argv[3],initial,sizeof(initial));
    const bool publicationRaw=Persist(argv[2],&first,sizeof(first));
    const bool finalRaw=Persist(argv[4],finalObservation,sizeof(finalObservation));
    char json[16000]{};std::size_t used=0;
    const int header=std::snprintf(json,sizeof(json),
        "{\"schemaVersion\":1,\"kind\":\"engine-transport-observed-owned-cold\",\"passed\":false,\"pid\":%lu,\"stage\":\"initial-observation-read\",\"exitCode\":4,\"storageRva\":%u,\"storageBytes\":%u,\"actualTargetBase\":%llu,\"requestedAddress\":%llu,\"storagePolicyVersion\":2,\"allowedStorageProtections\":[4,8],\"strictMemoryAccepted\":%s,\"diagnosticCopySucceeded\":%s,\"diagnosticCopiedBytes\":%llu,\"diagnosticCopyError\":%u,\"initialRawPersisted\":%s,\"publicationRawPersisted\":%s,\"publicationRawValid\":false,\"finalRawPersisted\":%s,\"finalObservationSampled\":false,\"regions\":[",
        GetCurrentProcessId(),O::StorageRva,O::StorageBytes,static_cast<unsigned long long>(reinterpret_cast<std::uint64_t>(target)),
        static_cast<unsigned long long>(firstDiagnostic.requestedAddress),firstDiagnostic.strictMemoryAccepted?"true":"false",
        firstDiagnostic.copySucceeded?"true":"false",static_cast<unsigned long long>(firstDiagnostic.copiedBytes),firstDiagnostic.copyError,
        initialRaw?"true":"false",publicationRaw?"true":"false",finalRaw?"true":"false");
    if(header<=0||static_cast<std::size_t>(header)>=sizeof(json))return false;used=static_cast<std::size_t>(header);
    for(unsigned index=0;index<firstDiagnostic.regionCount;++index){const auto& row=firstDiagnostic.regions[index];
        const int added=std::snprintf(json+used,sizeof(json)-used,
            "%s{\"queriedAddress\":%llu,\"queryBytes\":%llu,\"base\":%llu,\"allocationBase\":%llu,\"regionBytes\":%llu,\"allocationProtect\":%u,\"state\":%u,\"protect\":%u,\"type\":%u,\"lastError\":%u}",index?",":"",
            static_cast<unsigned long long>(row.queriedAddress),static_cast<unsigned long long>(row.queryBytes),static_cast<unsigned long long>(row.base),
            static_cast<unsigned long long>(row.allocationBase),static_cast<unsigned long long>(row.regionBytes),row.allocationProtect,row.state,row.protect,row.type,row.lastError);
        if(added<=0||static_cast<std::size_t>(added)>=sizeof(json)-used)return false;used+=static_cast<std::size_t>(added);
    }
    const int tail=std::snprintf(json+used,sizeof(json)-used,
        "],\"productionAdmissionVerified\":false,\"gameExecuted\":false,\"originalGameCodeExecuted\":false,\"fullFamilyAdoptionVerified\":false}\n");
    if(tail<=0||static_cast<std::size_t>(tail)>=sizeof(json)-used)return false;used+=static_cast<std::size_t>(tail);
    const bool saved=Persist(argv[1],json,used);std::fwrite(json,1,used,stdout);std::fflush(stdout);return saved;
}
bool Code(HMODULE target){
    const auto base=reinterpret_cast<std::uint64_t>(target);
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(target);const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(base+dos->e_lfanew);
    const auto table=base+nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION].VirtualAddress;
    bool result=true;const unsigned char leaf[4]{1,0,0,0};
    for(unsigned i=0;i<5;++i){const auto& range=O::Code[i];DWORD64 foundBase{};
        const auto* function=RtlLookupFunctionEntry(base+range.beginRva,&foundBase,nullptr);
        result=CHECK(function&&foundBase==base&&reinterpret_cast<std::uint64_t>(function)==table+static_cast<std::uint64_t>(range.ordinal)*12
            &&function->BeginAddress==range.beginRva&&function->EndAddress==range.endRva&&function->UnwindData==range.unwindRva)&&result;
        result=CHECK(Memory(reinterpret_cast<void*>(base+range.beginRva),range.endRva-range.beginRva,target,PAGE_EXECUTE_READ)
            &&Memory(reinterpret_cast<void*>(base+range.unwindRva),4,target,PAGE_READONLY)
            &&std::memcmp(reinterpret_cast<void*>(base+range.unwindRva),leaf,4)==0)&&result;
    }
    for(unsigned i=0;i<4;++i)result=CHECK(std::memcmp(reinterpret_cast<void*>(base+O::CallbackRvas[i]),O::Wrappers[i],11)==0)&&result;
    return CHECK(sizeof(O::SamplerCode)==O::SamplerBytes&&std::memcmp(reinterpret_cast<void*>(base+O::SamplerRva),O::SamplerCode,O::SamplerBytes)==0)&&result;
}
bool Observations(const unsigned char* data,unsigned count,HMODULE target,const RizEngineTransportPublicationV5& publication){
    const auto base=reinterpret_cast<std::uint64_t>(target);bool result=true;
    result=CHECK(std::memcmp(data,O::Magic,16)==0&&Read<std::uint32_t>(data,16)==1&&Read<std::uint32_t>(data,20)==64
        &&Read<std::uint32_t>(data,24)==O::StorageBytes&&Read<std::uint32_t>(data,28)==8
        &&Read<std::uint32_t>(data,32)==1216&&Read<std::uint32_t>(data,36)==36
        &&Read<std::uint64_t>(data,40)==count&&Read<std::uint64_t>(data,48)==0&&Read<std::uint64_t>(data,56)==count)&&result;
    std::uint32_t payloadBytes=0;for(const auto& window:O::Windows)payloadBytes+=window.byteCount;
    for(unsigned record=0;record<count;++record){
        const auto* row=data+O::HeaderBytes+static_cast<std::size_t>(record)*O::RecordBytes;
        const auto sequence=Read<std::uint64_t>(row,0),commit=Read<std::uint64_t>(row,8);
        const auto id=Read<std::uint32_t>(row,16),thread=Read<std::uint32_t>(row,20),reason=Read<std::uint32_t>(row,24);
        result=CHECK(sequence==record+1&&commit==sequence&&id==record&&thread==GetCurrentThreadId()
            &&thread==publication.mainThread&&thread==publication.producerThread&&reason==(record<3?DLL_PROCESS_ATTACH:0xffffffffu)
            &&Read<std::uint32_t>(row,28)==36&&Read<std::uint64_t>(row,32)==base
            &&Read<std::uint64_t>(row,40)!=0&&Read<std::uint32_t>(row,48)==payloadBytes&&Zero(row+52,12))&&result;
        MEMORY_BASIC_INFORMATION caller{};const auto pc=Read<std::uint64_t>(row,40);
        result=CHECK(VirtualQuery(reinterpret_cast<void*>(pc),&caller,sizeof(caller))==sizeof(caller)&&caller.State==MEM_COMMIT
            &&caller.Type==MEM_IMAGE&&(caller.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE))!=0)&&result;
        for(unsigned index=0;index<36;++index){const auto& expected=O::Windows[index];const auto& actual=publication.windows[index];
            const auto* copied=row+O::PayloadOffset+index*O::SlotBytes;
            result=CHECK(actual.index==index&&actual.physicalRva==expected.sourceRva&&actual.physicalBytes==expected.byteCount
                &&actual.source==base+expected.sourceRva&&actual.readbackVerified==1&&actual.sourceWriteAttempts==1
                &&std::memcmp(copied,actual.observedBytes,expected.byteCount)==0
                &&std::memcmp(copied,actual.patchBytes,expected.byteCount)==0&&Zero(copied+expected.byteCount,32-expected.byteCount))&&result;
        }
    }
    return CHECK(Zero(data+O::HeaderBytes+static_cast<std::size_t>(count)*O::RecordBytes,
        (O::Capacity-count)*O::RecordBytes))&&result;
}
}
int wmain(int argc,wchar_t** argv){
    // No target or publication function is called before copying the first
    // three observations that the loader itself already caused.
    if(argc!=5)return 2;
    const auto target=GetModuleHandleW(O::ModuleName);if(!target)return 3;
    if(!CopyObservation(target,initial,firstDiagnostic)){PersistFirstReadFailure(argv,target);return 4;}
    // Persist the actual earliest bytes immediately, even if a later proof or
    // callback fails. This file is evidence, never a synthesized Ready receipt.
    const bool initialEarlyPersisted=Persist(argv[3],initial,sizeof(initial));
    const auto bootstrap=GetModuleHandleW(L"RizToolbox.EngineBootstrap.dll");
    if(!bootstrap||GetModuleHandleW(L"UnityPlayer.dll")||GetModuleHandleW(L"GameAssembly.dll"))return 5;
    using Scalar=std::uint32_t(__cdecl*)();using GetInfo=std::uint32_t(__cdecl*)(RizEngineTransportPublicationV5*);
    const auto read=Export<GetInfo>(bootstrap,"RizEngineTransport_GetInfoV5");const auto verify=Export<Scalar>(bootstrap,"RizEngineTransport_VerifyV5");
    if(!read||!verify)return 6;
    first.version=second.version=finalPublication.version=5;first.byteSize=second.byteSize=finalPublication.byteSize=sizeof(first);
    const auto firstRead=read(&first),verified=verify(),secondRead=read(&second);
    const bool publication=CHECK(!firstRead&&!verified&&!secondRead&&first.state==RizEngineTransportReadyV5&&first.profile==1
        &&first.byteSize==91376&&first.schemaMagic==RizEngineTransportPublicationLayout::Magic&&first.failure==0
        &&first.processId==GetCurrentProcessId()&&first.runtimeFunctionCount==67&&first.sourceWriteCompletions==36
        &&first.committedWindows==((1ull<<36)-1)&&first.mainThreadProof==RizEngineTransportMainThreadOwnedContractV5
        &&std::memcmp(&first,&second,sizeof(first))==0);
    const bool code=Code(target);const bool firstObserved=Observations(initial,3,target,first);
    const int unity=UnityMain2();
    const bool copied=CopyObservation(target,finalObservation,finalDiagnostic);const auto finalRead=read(&finalPublication),finalVerified=verify();
    const bool finalObserved=copied&&Observations(finalObservation,4,target,first);
    const bool unchanged=CHECK(!finalRead&&!finalVerified&&std::memcmp(&first,&finalPublication,sizeof(first))==0
        &&std::memcmp(initial+64,finalObservation+64,3*O::RecordBytes)==0);
    const bool raw=Persist(argv[2],&first,sizeof(first))&&initialEarlyPersisted&&Persist(argv[4],finalObservation,sizeof(finalObservation));
    const bool passed=publication&&code&&firstObserved&&finalObserved&&unchanged&&raw&&unity==0&&failures==0;
    char json[1800]{};const int bytes=std::snprintf(json,sizeof(json),
        "{\"schemaVersion\":1,\"kind\":\"engine-transport-observed-owned-cold\",\"passed\":%s,\"pid\":%lu,\"checks\":%u,\"failures\":%u,\"initialRecords\":3,\"finalRecords\":4,\"windowsPerRecord\":36,\"actualLoaderTlsEntryOrder\":%s,\"actualSourcePayloadMatches\":%s,\"staticLeafCodeVerified\":%s,\"immutablePublication\":%s,\"rawPersisted\":%s,\"ownedUnityResult\":%d,\"threadId\":%lu,\"storagePolicyVersion\":2,\"allowedStorageProtections\":[4,8],\"initialStorageSectionRwBacked\":%s,\"finalStorageSectionRwBacked\":%s,\"initialStorageStrictAccepted\":%s,\"finalStorageStrictAccepted\":%s,\"initialStorageReadwriteOnly\":%s,\"finalStorageReadwriteOnly\":%s,\"initialStorageCowRegions\":%u,\"finalStorageCowRegions\":%u,\"productionAdmissionVerified\":false,\"gameExecuted\":false,\"originalGameCodeExecuted\":false,\"fullFamilyAdoptionVerified\":false}\n",
        passed?"true":"false",GetCurrentProcessId(),checks,failures,firstObserved?"true":"false",finalObserved?"true":"false",code?"true":"false",
        unchanged?"true":"false",raw?"true":"false",unity,GetCurrentThreadId(),
        firstDiagnostic.sectionRwBackedAccepted?"true":"false",finalDiagnostic.sectionRwBackedAccepted?"true":"false",
        firstDiagnostic.strictMemoryAccepted?"true":"false",finalDiagnostic.strictMemoryAccepted?"true":"false",
        firstDiagnostic.readwriteOnlyAccepted?"true":"false",finalDiagnostic.readwriteOnlyAccepted?"true":"false",
        firstDiagnostic.copyOnWriteRegions,finalDiagnostic.copyOnWriteRegions);
    if(bytes<=0||static_cast<std::size_t>(bytes)>=sizeof(json)||!Persist(argv[1],json,static_cast<std::size_t>(bytes)))return 7;
    std::fwrite(json,1,static_cast<std::size_t>(bytes),stdout);return passed?0:1;
}
