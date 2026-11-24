#pragma once
#include "../TweenSourcePublication.h"
#include "../TweenSourceProfile.h"
#include "../TweenSourceContext.h"
#include "../TweenSourceReferences.h"

namespace RizTweenSourceChecks {
using Reader=std::uint32_t(__cdecl*)(RizTweenSourcePublicationV1*);
using CheckFn=void(*)(bool,const char*,int);
inline unsigned referenceCallbacks=0;
inline std::uint32_t __cdecl UnattachedReferenceCallback(const RizTweenSourceReferenceNoticeV1*,void*)
{ ++referenceCallbacks;return 0; }
inline RizTweenSourcePublicationV1 Header(){return {1,sizeof(RizTweenSourcePublicationV1)};}
inline int ModeId(const wchar_t* mode)
{
    constexpr wchar_t prefix[]=L"tamper-tween-source-";
    if(std::wcsncmp(mode,prefix,(sizeof(prefix)/sizeof(wchar_t))-1)!=0)return -1;
    const auto* cursor=mode+(sizeof(prefix)/sizeof(wchar_t))-1;
    if(!*cursor)return -1;unsigned value=0;
    for(;*cursor;++cursor){if(*cursor<L'0'||*cursor>L'9')return -1;value=value*10+static_cast<unsigned>(*cursor-L'0');if(value>=22)return -1;}
    return static_cast<int>(value);
}
inline unsigned Validate(const unsigned char* module,HMODULE helper,Reader read,CheckFn check,RizTweenSourcePublicationV1& value)
{
#define RIZ_TS_CHECK(expression) check(!!(expression),#expression,__LINE__)
    value=Header();RIZ_TS_CHECK(read && read(&value)==0);
    RIZ_TS_CHECK(value.abiVersion==1 && value.byteSize==sizeof(value) && value.prepared==1 && value.published==1 && value.unknown==0
        && value.siteCount==22 && value.bindingCount==43 && value.functionCount==56 && value.epoch!=0
        && value.publishedMask==0x3fffff && value.moduleBase==module && value.allocation && value.functionTable && value.entry && !value.reserved);
    MEMORY_BASIC_INFORMATION memory{};
    RIZ_TS_CHECK(VirtualQuery(value.allocation,&memory,sizeof(memory))==sizeof(memory) && memory.Type==MEM_PRIVATE
        && memory.AllocationBase==value.allocation && memory.Protect==PAGE_EXECUTE_READ && memory.RegionSize>=value.allocationSize);
    RIZ_TS_CHECK(VirtualQuery(value.entry,&memory,sizeof(memory))==sizeof(memory) && memory.AllocationBase==helper && memory.Type==MEM_IMAGE);
    using InfoRead=std::uint32_t(__cdecl*)(RizTweenSourceInfoV1*);
    const auto address=GetProcAddress(helper,"RizTweenSourceContext_GetInfo");InfoRead readInfo{};
    static_assert(sizeof(address)==sizeof(readInfo));std::memcpy(&readInfo,&address,sizeof(readInfo));
    RizTweenSourceInfoV1 state{1,sizeof(state)};
    RIZ_TS_CHECK(readInfo && readInfo(&state)==0 && state.configured==1 && state.unknown==0 && state.metadataBound==0
        && state.epoch==value.epoch && state.bindingCount==43 && state.activeUpdates==0 && state.events==0 && state.transactions==0);
    // A published source image alone is insufficient to attach an observer.
    // Actual post-init metadata binding is required by the helper's real gate.
    using ReferenceRead=std::uint32_t(__cdecl*)(RizTweenSourceReferencesInfoV1*);
    using ReferenceAttach=std::uint32_t(__cdecl*)(const RizTweenSourceReferencesObserverV1*);
    const auto referenceReadAddress=GetProcAddress(helper,"RizTweenSourceReferences_GetInfo");
    const auto referenceAttachAddress=GetProcAddress(helper,"RizFirstInstall_AttachTweenSourceReferencesV1");
    ReferenceRead readReferences{};ReferenceAttach attachReferences{};
    static_assert(sizeof(referenceReadAddress)==sizeof(readReferences));
    static_assert(sizeof(referenceAttachAddress)==sizeof(attachReferences));
    std::memcpy(&readReferences,&referenceReadAddress,sizeof(readReferences));
    std::memcpy(&attachReferences,&referenceAttachAddress,sizeof(attachReferences));
    RizTweenSourceReferencesInfoV1 referencesBefore{1,sizeof(referencesBefore)};
    RIZ_TS_CHECK(readReferences && attachReferences && readReferences(&referencesBefore)==0 && referencesBefore.attached==0
        && referencesBefore.unknown==0 && referencesBefore.scopeCapacity==128 && referencesBefore.sourceEpoch==0 && referencesBefore.observerEpoch==0);
    const RizTweenSourceReferencesObserverV1 observer{1,sizeof(observer),UnattachedReferenceCallback,nullptr};
    RIZ_TS_CHECK(attachReferences && attachReferences(&observer)==2 && referenceCallbacks==0);
    RizTweenSourceReferencesInfoV1 referencesAfter{1,sizeof(referencesAfter)};
    RIZ_TS_CHECK(readReferences && readReferences(&referencesAfter)==0 && std::memcmp(&referencesBefore,&referencesAfter,sizeof(referencesAfter))==0);
    unsigned verified=0;
    for(unsigned i=0;i<22;++i){
        const auto& row=value.sites[i];const auto& expected=RizTweenSourceProfile::Sites[i];
        const auto* source=module+expected.beginRva;
        RIZ_TS_CHECK(row.site==i && row.beginRva==expected.beginRva && row.endRva==expected.endRva && row.width==expected.size
            && row.sourcePc==source && row.continuation==module+expected.endRva && row.preRelay && row.preReturnPc
            && row.deadContinuation==(expected.deadContinuationRva?module+expected.deadContinuationRva:nullptr)
            && (i==13? !row.postRelay&&!row.postReturnPc:row.postRelay&&row.postReturnPc));
        std::int32_t displacement{};std::memcpy(&displacement,source+1,4);
        RIZ_TS_CHECK(source[0]==0xe9 && source+5+displacement==row.preRelay);
        for(unsigned offset=5;offset<row.width;++offset)RIZ_TS_CHECK(source[offset]==0x90);
        RIZ_TS_CHECK(VirtualQuery(source,&memory,sizeof(memory))==sizeof(memory) && memory.Protect==row.protection);
        DWORD64 image{};const auto* function=RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(row.preRelay),&image,nullptr);
        RIZ_TS_CHECK(function && image==reinterpret_cast<DWORD64>(module)
            && image+function->BeginAddress==reinterpret_cast<DWORD64>(row.preRelay)
            && image+function->UnwindData==reinterpret_cast<DWORD64>(row.unwindInfo));
        ++verified;
    }
    return verified;
#undef RIZ_TS_CHECK
}
}
