// Private implementation included by FirstInstall.cpp in its anonymous namespace.
// It shares the enclosing write transaction, seals and snapshot lock. No export,
// caller address, or Ready bit can configure or publish this lane.
constexpr std::uint32_t singleSpanRva[7]{0xbc1087,0xbc1107,0xbc110d,0xbc115d,0xbc1162,0xbc121b,0xbc1221};
constexpr std::uint32_t singleSpanWidth[7]{9,6,9,5,9,6,9};
constexpr std::uint32_t singleReturnRva[3]{0xbc110d,0xbc1162,0xbc1221};
constexpr std::size_t singleAllocationSize=4096;
struct SinglePublicationSeal {
    unsigned char* block{};
    unsigned char expected[singleAllocationSize]{};
    unsigned char patch[7][9]{},original[7][9]{};
    bool sealed{},tableRegistered{},published{};
} singleSeal;

bool SingleConditional(unsigned char* at,unsigned char condition,const void* target)
{
    const auto delta=reinterpret_cast<std::intptr_t>(target)-reinterpret_cast<std::intptr_t>(at+6);
    if(delta<(std::numeric_limits<std::int32_t>::min)()||delta>(std::numeric_limits<std::int32_t>::max)())return false;
    const auto value=static_cast<std::int32_t>(delta);at[0]=0x0f;at[1]=condition;std::memcpy(at+2,&value,4);return true;
}
// These detail values describe checks already performed by the published
// SingleEvent verifier. A native fault mask comes from its one GetInfo read.
enum SingleFailureDetail : std::uint32_t {
    SingleReceipt = 1, SingleBlock = 2, SingleParent = 3, SingleNative = 4,
    SingleSpan = 5, SingleUnwind = 6, SingleNativeFaultMask = 0x100
};
std::uint32_t SingleNativeFailure(const RizFirstInstallInfoV7& packet,bool unattached)
{
    const auto& info=packet.singleEvent;
    RizTweenSingleEventInfoV1 native{1,sizeof(native)};
    if(info.parentEpoch!=packet.operations[25].epoch||!OperationNativeIdentity(packet.operations[25],unattached))return SingleParent;
    if(RizTweenSingleEventContext_GetInfo(&native)!=0||native.configured!=1)return SingleNative;
    const auto faults=(native.unknown?0x01u:0u)|(native.callbackFaults?0x02u:0u)|(native.rejectedReads?0x04u:0u)
        |(native.missingPosts?0x08u:0u)|(native.retainedParents?0x10u:0u)|(native.retainedChildren?0x20u:0u)|(native.overflow?0x40u:0u);
    if(native.unknown)return SingleNativeFaultMask|faults;
    if(native.epoch!=info.epoch||native.parentEpoch!=info.parentEpoch||!native.epoch
        ||(unattached&&(native.attached||native.parentsInFlight||native.childrenInFlight)))return SingleNative;
    return faults?SingleNativeFaultMask|faults:0u;
}
bool SingleNativeIdentity(const RizFirstInstallInfoV7& packet,bool unattached)
{return SingleNativeFailure(packet,unattached)==0;}
std::uint32_t SingleBlockFailure(const RizFirstInstallInfoV7& packet)
{
    const auto& info=packet.singleEvent;const auto* base=reinterpret_cast<const unsigned char*>(packet.publication.common.moduleBase);
    const auto* block=singleSeal.block;MEMORY_BASIC_INFORMATION memory{};
    if(!base||!singleSeal.sealed||!singleSeal.tableRegistered||!block||info.capabilityVersion!=1||info.byteSize!=sizeof(info)
        ||info.operationId!=25||info.kind!=3||info.slot!=31||info.expectedWrites!=7||info.reserved||info.configuredMask!=0x7f
        ||info.allocation!=block||info.allocationSize!=singleAllocationSize||info.midEntry!=RizTweenSingleEventContext_GetMidEntry()
        ||info.preEntry!=RizTweenSingleEventContext_GetPreEntry()||info.postEntry!=RizTweenSingleEventContext_GetPostEntry()
        ||info.midRelay!=block+0x500||info.preRelay!=block+0x520||info.postRelay!=block+0x540
        ||info.midReturnPc!=base+0xbc108c||info.directTarget!=base+0x154e5b0
        ||info.functionTable!=block+0x380||info.functionTableBase!=reinterpret_cast<std::uint64_t>(base)||info.registeredFunctionCount!=3
        ||!ReadableRange(block,singleAllocationSize,nullptr,true)||VirtualQuery(block,&memory,sizeof(memory))!=sizeof(memory)
        ||memory.AllocationBase!=block||memory.Type!=MEM_PRIVATE||memory.Protect!=PAGE_EXECUTE_READ
        ||memory.RegionSize<singleAllocationSize||std::memcmp(block,singleSeal.expected,singleAllocationSize)!=0)return SingleBlock;
    for(unsigned span=0;span!=7;++span){DWORD protection{};
        if(info.spanPc[span]!=base+singleSpanRva[span]||info.spanWidth[span]!=singleSpanWidth[span]
            ||!Protection(info.spanPc[span],info.spanWidth[span],base,&protection)||protection!=info.protection[span])return SingleBlock;
    }
    for(unsigned site=0;site!=3;++site)
        if(info.sourceReturnPc[site]!=base+singleReturnRva[site]||info.postStub[site]!=block+site*0x100
            ||info.postTransportReturnPc[site]!=block+site*0x100+5||info.unwindInfo[site]!=block+site*0x100+0x40
            ||info.stubSize[site]!=21||info.unwindSize[site]!=36)return SingleBlock;
    // Raw sealed block/xdata comparison above precedes every OS unwinder use.
    if(!OperationContinuationIdentity(base))return SingleBlock;
    return SingleNativeFailure(packet,false);
}
bool SingleBlockIdentity(const RizFirstInstallInfoV7& packet)
{return SingleBlockFailure(packet)==0;}
bool SingleBoundary(const RizTweenSingleEventPublicationV1& info,unsigned site,unsigned offset)
{
    DWORD64 actualBase{};const auto pc=reinterpret_cast<DWORD64>(info.postStub[site])+offset;
    auto* function=RtlLookupFunctionEntry(pc,&actualBase,nullptr);
    const auto* expected=static_cast<const RUNTIME_FUNCTION*>(info.functionTable)+site;
    if(!function||actualBase!=info.functionTableBase||std::memcmp(function,expected,sizeof(*function)))return false;
    alignas(16) DWORD64 stack[32]{};
    stack[0x58/8]=reinterpret_cast<DWORD64>(&RizFirstInstall_GetInfoV7);
    stack[0x50/8]=0x5500550055005500ull;stack[0x48/8]=0x1300130013001300ull;
    stack[0x30/8]=0x1500150015001500ull;stack[0x38/8]=0x1400140014001400ull;stack[0x40/8]=0x1200120012001200ull;
    stack[0x68/8]=0x3300330033003300ull;stack[0x70/8]=0x6600660066006600ull;stack[0x78/8]=0x7700770077007700ull;
    std::memset(reinterpret_cast<unsigned char*>(stack)+0x20,0x6a,16);
    CONTEXT context{};context.ContextFlags=CONTEXT_ALL;context.Rip=pc;context.Rsp=reinterpret_cast<DWORD64>(stack);
    std::memset(&context.Xmm7,0x5a,9*sizeof(M128A));const auto before=context;
    void* handler{};DWORD64 establisher{};
    RtlVirtualUnwind(UNW_FLAG_NHANDLER,actualBase,context.Rip,function,&context,&handler,&establisher,nullptr);
    unsigned char xmm[16];std::memset(xmm,0x6a,sizeof(xmm));
    return context.Rip==stack[0x58/8]&&context.Rsp==reinterpret_cast<DWORD64>(stack)+0x60
        &&context.Rbp==stack[0x50/8]&&context.R13==stack[0x48/8]&&context.R15==stack[0x30/8]
        &&context.R14==stack[0x38/8]&&context.R12==stack[0x40/8]&&context.Rbx==stack[0x68/8]
        &&context.Rsi==stack[0x70/8]&&context.Rdi==stack[0x78/8]&&std::memcmp(&context.Xmm6,xmm,16)==0
        &&std::memcmp(&context.Xmm7,&before.Xmm7,9*sizeof(M128A))==0;
}
bool PrepareSingleEvent(RizFirstInstallInfoV7& packet)
{
    auto& info=packet.singleEvent;auto* base=reinterpret_cast<unsigned char*>(packet.publication.common.moduleBase);
    if(singleSeal.block||!base||!StillPreparing()||!OperationOriginalIdentity(base)||!OperationNativeIdentity(packet.operations[25],true))return false;
    info={};info.capabilityVersion=1;info.byteSize=sizeof(info);info.phase=RizFirstInstallPreparing;
    info.operationId=25;info.kind=3;info.slot=31;info.expectedWrites=7;info.parentEpoch=packet.operations[25].epoch;
    info.midEntry=RizTweenSingleEventContext_GetMidEntry();info.preEntry=RizTweenSingleEventContext_GetPreEntry();info.postEntry=RizTweenSingleEventContext_GetPostEntry();
    info.directTarget=base+0x154e5b0;info.midReturnPc=base+0xbc108c;
    SYSTEM_INFO system{};GetSystemInfo(&system);const auto step=static_cast<std::uintptr_t>(system.dwAllocationGranularity);
    if(!step||(step&(step-1)))return false;
    for(std::uintptr_t distance=(Profile::ImageSize+step-1)&~(step-1);distance<0x70000000ull;distance+=step){
        const auto address=reinterpret_cast<std::uintptr_t>(base);
        if(address>(std::numeric_limits<std::uintptr_t>::max)()-distance)return false;
        singleSeal.block=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(address+distance),singleAllocationSize,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        if(singleSeal.block)break;
    }
    if(!singleSeal.block)return false;
    auto* block=singleSeal.block;info.allocation=block;info.allocationSize=singleAllocationSize;StorePreparedIdentityV7(packet);
    std::memset(block,0xcc,singleAllocationSize);info.midRelay=block+0x500;info.preRelay=block+0x520;info.postRelay=block+0x540;
    Tail(block+0x500,info.midEntry);Tail(block+0x520,info.preEntry);Tail(block+0x540,info.postEntry);
    auto* functions=reinterpret_cast<RUNTIME_FUNCTION*>(block+0x380);info.functionTable=functions;info.functionTableBase=reinterpret_cast<std::uint64_t>(base);
    for(unsigned site=0;site!=3;++site){
        auto* stub=block+site*0x100;auto* unwind=stub+0x40;
        if(!Relative(stub,0xe8,info.postRelay)||!SingleConditional(stub+9,0x84,base+0xbc1090)
            ||!SingleConditional(stub+15,0x85,base+0xbc1090))return false;
        const unsigned char replay[]{0xff,0xc6,0x8b,0xce};std::memcpy(stub+5,replay,4);
        std::memcpy(unwind,Profile::Operation25Continuation1Unwind,36);
        const auto rva=reinterpret_cast<std::uintptr_t>(stub)-reinterpret_cast<std::uintptr_t>(base);
        const auto unwindRva=reinterpret_cast<std::uintptr_t>(unwind)-reinterpret_cast<std::uintptr_t>(base);
        if(rva>MAXDWORD-21||unwindRva>MAXDWORD)return false;
        functions[site]={static_cast<DWORD>(rva),static_cast<DWORD>(rva+21),static_cast<DWORD>(unwindRva)};
        info.sourceReturnPc[site]=base+singleReturnRva[site];info.postStub[site]=stub;info.postTransportReturnPc[site]=stub+5;
        info.unwindInfo[site]=unwind;info.stubSize[site]=21;info.unwindSize[site]=36;
    }
    for(unsigned span=0;span!=7;++span){
        auto* source=base+singleSpanRva[span];info.spanPc[span]=source;info.spanWidth[span]=singleSpanWidth[span];
        DWORD protection{};if(!Protection(source,info.spanWidth[span],base,&protection))return false;info.protection[span]=protection;
        std::memcpy(singleSeal.original[span],source,info.spanWidth[span]);
        auto* patch=singleSeal.patch[span];std::memset(patch,0x90,9);
        const auto call=span==0||span%2==1;const auto start=span==1||span==5?1u:0u;
        const auto* target=span==0?info.midRelay:call?info.preRelay:info.postStub[(span-2)/2];
        const auto delta=reinterpret_cast<std::intptr_t>(target)-reinterpret_cast<std::intptr_t>(source+start+5);
        if(delta<(std::numeric_limits<std::int32_t>::min)()||delta>(std::numeric_limits<std::int32_t>::max)())return false;
        const auto value=static_cast<std::int32_t>(delta);patch[start]=call?0xe8:0xe9;std::memcpy(patch+start+1,&value,4);
    }
    std::memcpy(singleSeal.expected,block,singleAllocationSize);DWORD previous{};
    if(!VirtualProtect(block,singleAllocationSize,PAGE_EXECUTE_READ,&previous)
        ||!FlushInstructionCache(GetCurrentProcess(),block,singleAllocationSize)||!RtlAddFunctionTable(functions,3,info.functionTableBase))return false;
    singleSeal.tableRegistered=true;info.registeredFunctionCount=3;singleSeal.sealed=true;StorePreparedIdentityV7(packet);
    const RizTweenSingleEventConfigV1 config{1,sizeof(config),info.parentEpoch,info.midReturnPc,
        {info.sourceReturnPc[0],info.sourceReturnPc[1],info.sourceReturnPc[2]},
        {info.postTransportReturnPc[0],info.postTransportReturnPc[1],info.postTransportReturnPc[2]},info.directTarget};
    if(RizTweenSingleEventConfigure(&config))return false;
    RizTweenSingleEventInfoV1 native{1,sizeof(native)};
    if(RizTweenSingleEventContext_GetInfo(&native)||native.unknown||native.attached||!native.epoch)return false;
    info.epoch=native.epoch;StorePreparedIdentityV7(packet);
    for(unsigned span=0;span!=7;++span){info.configuredMask|=1u<<span;StorePreparedIdentityV7(packet);}
    if(!SingleBlockIdentity(packet)||!SingleNativeIdentity(packet,true))return false;
    for(unsigned site=0;site!=3;++site)for(const unsigned offset:{0u,5u,7u,9u,15u}){
        if(!SingleBoundary(info,site,offset))return false;++info.lookupChecks;++info.boundaryChecks;
    }
    StorePreparedIdentityV7(packet);return true;
}
bool CommitSingleEvent(RizFirstInstallInfoV7& packet)
{
    auto& info=packet.singleEvent;
    if(!StillPreparing()||!SingleBlockIdentity(packet)||!SingleNativeIdentity(packet,true)||info.lookupChecks!=15||info.boundaryChecks!=15)return false;
    for(unsigned span=0;span!=7;++span){
        if(!StillPreparing()||!SingleBlockIdentity(packet))return false;
        ++info.attemptedWrites;StorePreparedIdentityV7(packet);
        if(!WriteSource(static_cast<unsigned char*>(const_cast<void*>(info.spanPc[span])),singleSeal.original[span],singleSeal.patch[span],info.spanWidth[span],info.protection[span]))return false;
        ++info.completedWrites;info.publishedMask|=1u<<span;StorePreparedIdentityV7(packet);
#if defined(RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE)
        RizFirstInstallFixture_AfterSingleWrite(span);
#endif
    }
    singleSeal.published=true;return true;
}
std::uint32_t SinglePublishedIdentityFailure(const RizFirstInstallInfoV7& packet)
{
    const auto& info=packet.singleEvent;
    if(!singleSeal.published||info.publishedMask!=0x7f||info.attemptedWrites!=7||info.completedWrites!=7
        ||info.lookupChecks!=15||info.boundaryChecks!=15)return SingleReceipt;
    if(const auto failure=SingleBlockFailure(packet))return failure;
    for(unsigned span=0;span!=7;++span)
        if(std::memcmp(info.spanPc[span],singleSeal.patch[span],info.spanWidth[span]))return SingleSpan;
    for(unsigned site=0;site!=3;++site)for(const unsigned offset:{0u,5u,7u,9u,15u}){
        DWORD64 imageBase{};const auto pc=reinterpret_cast<DWORD64>(info.postStub[site])+offset;
        const auto* actual=RtlLookupFunctionEntry(pc,&imageBase,nullptr);
        if(!actual||imageBase!=info.functionTableBase||std::memcmp(actual,static_cast<const RUNTIME_FUNCTION*>(info.functionTable)+site,sizeof(*actual)))return SingleUnwind;
    }
    return 0;
}
