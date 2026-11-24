#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <cstdint>
#include <cwchar>
#include <initializer_list>
#include "PlaybackSourceContext.h"
#include "../RizToolbox.PlaybackStockFixture/PlaybackStockFixture.h"

namespace {
using Prepare = int(__cdecl*)(void*,void*,void**);
using Commit = int(__cdecl*)(void*);
decltype(&RtPlaybackSourceCreateOwner) createOwner;
decltype(&RtPlaybackSourceConfigure) configure;
decltype(&RtPlaybackSourceGetEntry) getEntry;
decltype(&RtPlaybackSourceSetArmed) setArmed;
decltype(&RtPlaybackSourceGetInfo) getInfo;
decltype(&RtPlaybackSourceReadOwner) readOwner;
decltype(&RtPlaybackSourceReadCurrent) readCurrent;
decltype(&RtPlaybackSourceVerifyCurrent) verifyCurrent;
decltype(&RtPlaybackSourceReadReceipt) readReceipt;
decltype(&RtPlaybackSourceReleaseReceipt) releaseReceipt;
decltype(&StockFixtureCreateSource) createSource;
decltype(&StockFixtureCreatePlayer) createPlayer;
decltype(&StockFixtureCreateProgress) createProgress;
decltype(&StockFixtureConfigure) setSource;
decltype(&StockFixtureInvoke) invoke;
decltype(&StockMethod) method;
decltype(&StockFixtureWaitPitch) waitPitch;
decltype(&StockFixtureReleasePitch) releasePitch;
decltype(&StockFixtureMachineInvoke) machineInvoke;
HMODULE helper, fixture;
void* owner;
RtPlaybackSourceInfo initial{};
void* targets[9]{};
std::uint32_t kinds[9]{0,2,1,2,2,3,4,5,1};
std::uint64_t checks{}, failures{}, prepared{}, committed{}, callbacks{}, beforeCount{}, returnedCount{}, abnormalCount{};
std::uint64_t fixtureLoadedBase{}, fixturePreferredBase{}, fixtureDir64Rva{}, fixtureDir64Value{};
RtPlaybackSourceFrame observed[2048]{};
std::size_t observedCount{};
bool restorePitch{}, observerFault{}, inspectAfter{};
volatile LONG observerEntered;
HANDLE observerContinue;

void Check(bool value, const char* expression, int line) {
    InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(&checks));
    if (!value) {
        InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(&failures));
        std::fprintf(stderr,"FAILED source line %d: %s\n",line,expression);
    }
}
#define CHECK(x) Check(!!(x),#x,__LINE__)
DWORD WINAPI InspectAfter(void* raw) {
    const auto* frame = static_cast<RtPlaybackSourceFrame*>(raw);
    RtPlaybackSourceFrame absent{}; RtPlaybackSourceInfo info{};
    CHECK(readReceipt(owner,frame->Sequence,frame->Token,&absent) == 0);
    CHECK(readOwner(owner,&info) == 1 && info.InFlight == 1 && info.Exited+1 == info.Entered);
    CHECK(readCurrent(frame->Kind,frame->Slot,&absent) == 0);
    return 0;
}
template<typename T> T Symbol(HMODULE module,const char* name) {
    auto symbol = GetProcAddress(module,name); CHECK(symbol != nullptr);
    T result{}; std::memcpy(&result,&symbol,sizeof(result)); return result;
}
void CheckExecutableDir64(const wchar_t* fixturePath) {
    auto* base = reinterpret_cast<unsigned char*>(fixture);
    auto* anchor = reinterpret_cast<unsigned char*>(GetProcAddress(fixture,"StockExecutableDir64Anchor"));
    auto* target = reinterpret_cast<unsigned char*>(GetProcAddress(fixture,"StockSetPitch"));
    CHECK(anchor != nullptr && target != nullptr);
    if (!anchor || !target) return;
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    CHECK(dos->e_magic == IMAGE_DOS_SIGNATURE && nt->Signature == IMAGE_NT_SIGNATURE
        && nt->OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC);
    // The loader may rewrite the mapped header's ImageBase. Read the pinned
    // file rather than mistaking that mapped value for the preferred base.
    auto file = CreateFileW(fixturePath,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    CHECK(file != INVALID_HANDLE_VALUE);
    if (file == INVALID_HANDLE_VALUE) return;
    IMAGE_DOS_HEADER diskDos{}; IMAGE_NT_HEADERS64 diskNt{}; DWORD bytes{};
    const bool readDos = ReadFile(file,&diskDos,sizeof(diskDos),&bytes,nullptr) != FALSE
        && bytes == sizeof(diskDos) && diskDos.e_magic == IMAGE_DOS_SIGNATURE;
    CHECK(readDos);
    LARGE_INTEGER offset{}; offset.QuadPart = diskDos.e_lfanew;
    const bool readNt = readDos && SetFilePointerEx(file,offset,nullptr,FILE_BEGIN) != FALSE
        && ReadFile(file,&diskNt,sizeof(diskNt),&bytes,nullptr) != FALSE && bytes == sizeof(diskNt)
        && diskNt.Signature == IMAGE_NT_SIGNATURE && diskNt.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC;
    CHECK(readNt); CloseHandle(file);
    if (!readNt) return;
    fixtureLoadedBase = reinterpret_cast<std::uint64_t>(base);
    fixturePreferredBase = diskNt.OptionalHeader.ImageBase;
    fixtureDir64Rva = static_cast<std::uint64_t>(anchor - base);
    std::memcpy(&fixtureDir64Value,anchor,sizeof(fixtureDir64Value));
    CHECK((diskNt.OptionalHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE)
        && fixtureLoadedBase != fixturePreferredBase);
    bool executableText = false;
    const auto* sections = IMAGE_FIRST_SECTION(nt);
    for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
        const auto& section = sections[i];
        if (std::memcmp(section.Name,".text\0\0\0",8) == 0
            && (section.Characteristics & IMAGE_SCN_MEM_EXECUTE)
            && fixtureDir64Rva >= section.VirtualAddress
            && fixtureDir64Rva + 8 <= section.VirtualAddress + section.Misc.VirtualSize) executableText = true;
    }
    CHECK(executableText);
    MEMORY_BASIC_INFORMATION memory{};
    CHECK(VirtualQuery(anchor,&memory,sizeof(memory)) == sizeof(memory) && memory.AllocationBase == fixture
        && memory.State == MEM_COMMIT && memory.Type == MEM_IMAGE && (memory.Protect & 0xffu) == PAGE_EXECUTE_READ);
    CHECK(fixtureDir64Value == reinterpret_cast<std::uint64_t>(target)
        && fixtureDir64Value != fixturePreferredBase + static_cast<std::uint64_t>(target - base));
}
void __cdecl Observe(void*,const RtPlaybackSourceFrame* passed) {
    ++callbacks;
    RtPlaybackSourceFrame current{};
    CHECK(readCurrent(passed->Kind,passed->Slot,&current) == 1);
    CHECK(verifyCurrent(owner,&current) == 1);
    CHECK(std::memcmp(&current,passed,sizeof(current)) == 0);
    CHECK(current.Version == 4 && current.Size == 168 && current.Owner == reinterpret_cast<std::uint64_t>(owner));
    CHECK(current.OwnerEpoch == initial.OwnerEpoch && current.Caller != 0 && current.Self != 0);
    CHECK(!(current.Flags & RtSourcePhysicalExitedFlag));
    RtPlaybackSourceFrame premature{};
    CHECK(readReceipt(owner,current.Sequence,current.Token,&premature) == 0);
    auto forged = current; ++forged.ArgBits;
    CHECK(verifyCurrent(owner,&forged) == 0);
    CHECK(readCurrent(current.Kind,current.Slot+1,&premature) == 0);
    if (observedCount < 2048) observed[observedCount++] = current;
    if (current.Stage == RtSourceBefore) ++beforeCount;
    else if (current.Stage == RtSourceOriginalReturned) ++returnedCount;
    else if (current.Stage == RtSourceAbnormal) ++abnormalCount;
    else CHECK(false);
    if (observerFault && current.Stage == RtSourceBefore) RaiseException(0xe24000f1u,0,0,nullptr);
    if (inspectAfter && current.Stage == RtSourceOriginalReturned) {
        CHECK((current.Flags & RtSourceReturnedFlag) && !(current.Flags & RtSourcePhysicalExitedFlag));
        auto thread=CreateThread(nullptr,0,InspectAfter,&current,0,nullptr); CHECK(thread != nullptr);
        CHECK(WaitForSingleObject(thread,10000) == WAIT_OBJECT_0); CloseHandle(thread);
        CHECK(verifyCurrent(owner,&current) == 1);
    }
    if (restorePitch && current.Stage == RtSourceOriginalReturned && (current.Role == RtSourceReplay || current.Role == RtSourceResume)) {
        auto* player = reinterpret_cast<StockPlayer*>(current.Self);
        std::uint32_t code = 0;
        CHECK(invoke(2,targets[1],player->AudioSource,0x40000000u,method(1),&code) == 1 && code == 0);
        CHECK(verifyCurrent(owner,&current) == 1); // Nested setter returned to the same real parent scope.
    }
    if (observerContinue && current.Stage == RtSourceOriginalReturned && current.Role == RtSourceSetPitch) {
        InterlockedExchange(&observerEntered,1);
        CHECK(WaitForSingleObject(observerContinue,10000) == WAIT_OBJECT_0);
    }
}
struct OriginalThunk {
    unsigned char* Bytes; RUNTIME_FUNCTION Function;
    void* DobbyOriginal; unsigned MovedLength, UnwindProbes, TargetUnwindProbes;
    unsigned char DobbyBytes[32];
};
OriginalThunk thunks[9]{};
bool stageAb;
bool Installed(unsigned role) { return !stageAb || role == 1 || role == 2 || role == 5 || role == 6 || role == 8; }
unsigned Prefix(unsigned role,unsigned char* bytes) {
    if(role == 6) { constexpr unsigned char p[]{0x48,0x89,0x5c,0x24,0x08,0x57}; std::memcpy(bytes,p,6); return 6; }
    if(role == 7 && !stageAb) { constexpr unsigned char p[]{0x48,0x83,0xec,0x28,0x38,0x51,0x42}; std::memcpy(bytes,p,7); return 7; }
    const unsigned char p[]{0x40,static_cast<unsigned char>(role == 4 && !stageAb ? 0x57 : 0x53),0x48,0x83,0xec,
        static_cast<unsigned char>((role == 2 || role == 8) ? 0x20 : role == 3 && !stageAb ? 0x40 : role == 4 && !stageAb ? 0x50 : 0x30)};
    std::memcpy(bytes,p,6); return 6;
}
void ProbeUnwind(unsigned role,unsigned char* code,unsigned offset,bool independent) {
    alignas(16) CONTEXT context{}; alignas(16) std::uint64_t stack[128]{};
    auto* entry = stack+65; const auto entryRsp = reinterpret_cast<DWORD64>(entry);
    constexpr DWORD64 returnPc = 0x1234567812345678ull;
    constexpr DWORD64 values[]{0x3344334433443344ull,0x1122112211221122ull,0x5566556655665566ull,
        0x7788778877887788ull,0x1212121212121212ull,0x1313131313131313ull,0x1414141414141414ull,0x1515151515151515ull};
    entry[0] = returnPc; entry[-1] = role == 4 || role == 6 ? values[3] : values[0]; entry[1] = values[0];
    context.ContextFlags = CONTEXT_ALL; context.Rip = reinterpret_cast<DWORD64>(code)+offset;
    context.Rbx=values[0]; context.Rbp=values[1]; context.Rsi=values[2]; context.Rdi=values[3];
    context.R12=values[4]; context.R13=values[5]; context.R14=values[6]; context.R15=values[7];
    auto* xmm = reinterpret_cast<unsigned char*>(&context.Xmm6); std::memset(xmm,0x5a,160);
    context.EFlags=0x246;
    std::uint64_t delta{};
    if(role == 6) {
        if(offset >= 6) delta=8;
        if(!independent && offset >= 10) delta+=0x20;
        if(!independent && offset >= 13) context.Rdi=0xbad;
        if(!independent && offset >= 16) context.Rbx=0xbad;
    } else if(role == 7 && !stageAb) {
        if(offset >= 4) delta=0x28;
        if(!independent && offset >= 39) context.Rbx=0xbad;
    } else {
        if(offset >= 2) delta=8;
        const auto allocation=(role == 2 || role == 8) ? 0x20 : role == 3 && !stageAb ? 0x40 : role == 4 && !stageAb ? 0x50 : 0x30;
        if(offset >= 6) delta+=allocation;
        if(!independent && role != 4 && offset >= (role == 3 ? 24u : role == 5 ? 29u : 9u)) context.Rbx=0xbad;
        if(!independent && role == 5 && offset >= 26) {
            std::memset(reinterpret_cast<void*>(entryRsp-8-0x30+0x20),0x5a,16);
            if(offset >= 32) std::memset(xmm,0xa5,16);
        }
        if(!independent && role == 3) {
            if(offset >= 21) std::memset(reinterpret_cast<void*>(entryRsp-8-0x40+0x30),0x5a,16);
            if(offset >= 27) std::memset(xmm,0xa5,16);
            if(offset >= 157) entry[1]=values[3];
            if(offset >= 160) context.Rdi=0xbad;
        }
        if(!independent && role == 4) {
            if(offset >= 21) std::memset(reinterpret_cast<void*>(entryRsp-8-0x50+0x40),0x5a,16);
            if(offset >= 117) context.Rdi=0xbad;
            if(offset >= 120) context.Rbx=0xbad;
            if(offset >= 123) std::memset(xmm,0xa5,16);
            if(offset >= 250) std::memset(reinterpret_cast<void*>(entryRsp-8-0x50+0x30),0x5a,16);
            if(offset >= 253) std::memset(xmm+16,0xa5,16);
        }
    }
    context.Rsp=entryRsp-delta;
    DWORD64 base{},establisher{}; auto* function=RtlLookupFunctionEntry(context.Rip,&base,nullptr);
    CHECK(function != nullptr);
    if(!function) return;
    if(independent) CHECK(function == &thunks[role].Function && base == reinterpret_cast<DWORD64>(code));
    else CHECK(base == reinterpret_cast<DWORD64>(fixture));
    void* handlerData{};
    RtlVirtualUnwind(UNW_FLAG_NHANDLER,base,context.Rip,function,&context,&handlerData,&establisher,nullptr);
    CHECK(context.Rip == returnPc && context.Rsp == entryRsp+8);
    if(context.Rbx != values[0] || context.Rbp != values[1] || context.Rsi != values[2] || context.Rdi != values[3])
        std::fprintf(stderr,"unwind GPR role=%u offset=%u independent=%u rbx=%llx rdi=%llx\n",role,offset,independent?1:0,context.Rbx,context.Rdi);
    CHECK(context.Rbx == values[0] && context.Rbp == values[1] && context.Rsi == values[2] && context.Rdi == values[3]);
    CHECK(context.R12 == values[4] && context.R13 == values[5] && context.R14 == values[6] && context.R15 == values[7]);
    unsigned char expected[160]; std::memset(expected,0x5a,sizeof(expected));
    CHECK(std::memcmp(xmm,expected,sizeof(expected)) == 0 && context.EFlags == 0x246);
    if(independent) ++thunks[role].UnwindProbes; else ++thunks[role].TargetUnwindProbes;
}
void* Thunk(unsigned role,void* target,void* dobbyOriginal) {
    unsigned char prefix[8]{}; const auto length=Prefix(role,prefix);
    auto* moved = static_cast<unsigned char*>(dobbyOriginal);
    CHECK(std::memcmp(target,prefix,length) == 0 && std::memcmp(moved,prefix,length) == 0);
    CHECK(moved[length] == 0xff && moved[length+1] == 0x25 && *reinterpret_cast<std::uint32_t*>(moved+length+2) == 0);
    CHECK(*reinterpret_cast<void**>(moved+length+6) == static_cast<unsigned char*>(target)+length);
    auto& thunk = thunks[role];
    thunk.DobbyOriginal=dobbyOriginal; thunk.MovedLength=length; std::memcpy(thunk.DobbyBytes,moved,length+14);
    thunk.Bytes = static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    CHECK(thunk.Bytes != nullptr);
    std::memcpy(thunk.Bytes,prefix,length); thunk.Bytes[length] = 0x49; thunk.Bytes[length+1] = 0xbb;
    *reinterpret_cast<void**>(thunk.Bytes+length+2) = static_cast<unsigned char*>(target)+length;
    thunk.Bytes[length+10] = 0x41; thunk.Bytes[length+11] = 0xff; thunk.Bytes[length+12] = 0xe3;
    unsigned char unwind[12]{1,6,2,0,6,0x52,2,0x30};
    unsigned unwindLength=8;
    // At PC5 the MOV has not clobbered RBX and PUSH RDI has not completed.
    // Defer its SAVE until the full six-byte frame exists (frame+16).
    if(role == 6) { constexpr unsigned char u[]{1,6,3,0,6,0x70,6,0x34,2,0,0,0}; std::memcpy(unwind,u,12); unwindLength=12; }
    else if(role == 7 && !stageAb) { constexpr unsigned char u[]{1,4,1,0,4,0x42,0,0}; std::memcpy(unwind,u,8); }
    else { unwind[5]=static_cast<unsigned char>(((prefix[5]/8)-1)*16+2); unwind[7]=prefix[1] == 0x57 ? 0x70 : 0x30; }
    std::memcpy(thunk.Bytes+32,unwind,unwindLength);
    thunk.Function.BeginAddress = 0; thunk.Function.EndAddress = length+13; thunk.Function.UnwindData = 32;
    DWORD old{}; CHECK(VirtualProtect(thunk.Bytes,4096,PAGE_EXECUTE_READ,&old));
    CHECK(FlushInstructionCache(GetCurrentProcess(),thunk.Bytes,length+13));
    CHECK(RtlAddFunctionTable(&thunk.Function,1,reinterpret_cast<DWORD64>(thunk.Bytes)));
    if(role == 6) for(const unsigned offset:{0u,5u,6u,16u}) ProbeUnwind(role,thunk.Bytes,offset,true);
    else if(length == 7) for(const unsigned offset:{0u,4u,7u,17u}) ProbeUnwind(role,thunk.Bytes,offset,true);
    else for(const unsigned offset:{0u,2u,6u,16u}) ProbeUnwind(role,thunk.Bytes,offset,true);
    if(role == 6) for(const unsigned offset:{0u,5u,6u,10u,13u,16u}) ProbeUnwind(role,static_cast<unsigned char*>(target),offset,false);
    else if(role == 5) for(const unsigned offset:{0u,2u,6u,21u,26u,29u,32u}) ProbeUnwind(role,static_cast<unsigned char*>(target),offset,false);
    else if(role == 3 && !stageAb) for(const unsigned offset:{0u,2u,6u,13u,16u,21u,24u,27u,152u,157u,160u}) ProbeUnwind(role,static_cast<unsigned char*>(target),offset,false);
    else if(role == 4 && !stageAb) for(const unsigned offset:{0u,2u,6u,13u,16u,21u,109u,114u,117u,120u,123u,245u,250u,253u}) ProbeUnwind(role,static_cast<unsigned char*>(target),offset,false);
    else if(role == 7 && !stageAb) for(const unsigned offset:{0u,4u,7u,9u,14u,20u,31u,36u,39u}) ProbeUnwind(role,static_cast<unsigned char*>(target),offset,false);
    else if(role == 1 || role == 2 || role == 8) for(const unsigned offset:{0u,2u,6u,9u}) ProbeUnwind(role,static_cast<unsigned char*>(target),offset,false);
    return thunk.Bytes;
}
void Install(HMODULE dobby) {
    const auto prepare = Symbol<Prepare>(dobby,"DobbyPrepare"); const auto commit = Symbol<Commit>(dobby,"DobbyCommit");
    const char* names[]{"", "StockSetPitch","StockPause","StockReplay","StockResume","StockPlayScheduled","StockSetClip","StockProgressPlay","StockPauseLeaf"};
    owner = createOwner(Observe,nullptr); CHECK(owner != nullptr && readOwner(owner,&initial) == 1);
    for (unsigned role=1;role<=8;++role) {
        if(!Installed(role)) continue;
        targets[role] = reinterpret_cast<void*>(GetProcAddress(fixture,names[role])); CHECK(targets[role] != nullptr);
        auto* entry = getEntry(kinds[role],role-1); CHECK(entry != nullptr);
        void* original{}; CHECK(prepare(targets[role],entry,&original) == 0 && original != nullptr); ++prepared;
        const auto independent = Thunk(role,targets[role],original);
        CHECK(configure(owner,kinds[role],role-1,role,targets[role],independent) == 1);
        RtPlaybackSourceInfo info{}; CHECK(getInfo(kinds[role],role-1,&info) == 1);
        CHECK(info.Owner == initial.Owner && info.OwnerEpoch == initial.OwnerEpoch && info.Target == reinterpret_cast<std::uint64_t>(targets[role]));
        CHECK(info.Original == reinterpret_cast<std::uint64_t>(independent) && info.Entry == reinterpret_cast<std::uint64_t>(entry));
    }
    for (unsigned role=1;role<=8;++role) {
        if(!Installed(role)) continue;
        CHECK(commit(targets[role]) == 0); ++committed;
        DWORD old{}; CHECK(VirtualProtect(targets[role],6,PAGE_EXECUTE_READ,&old));
        CHECK(FlushInstructionCache(GetCurrentProcess(),targets[role],6));
    }
}
int Call(unsigned role,void* self,std::uint64_t bits=0,void* incoming=nullptr,std::uint32_t* codeOut=nullptr) {
    std::uint32_t code{}; const auto result = invoke(kinds[role],targets[role],self,bits,incoming,&code);
    if (codeOut) *codeOut = code;
    return result;
}
RtPlaybackSourceInfo State() { RtPlaybackSourceInfo result{}; CHECK(readOwner(owner,&result) == 1); return result; }
RtPlaybackSourceFrame LastBefore(unsigned role) {
    for (std::size_t i=observedCount;i>0;--i)
        if(observed[i-1].Role == role && observed[i-1].Stage == RtSourceBefore) return observed[i-1];
    CHECK(false); return {};
}
void Completed(bool release=true) {
    for(std::size_t i=0;i<observedCount;++i) {
        const auto& before = observed[i]; if(before.Stage != RtSourceBefore) continue;
        if(!(before.Flags & RtSourceArmed)) {
            RtPlaybackSourceFrame absent{}; CHECK(readReceipt(owner,before.Sequence,before.Token,&absent) == 0); continue;
        }
        RtPlaybackSourceFrame receipt{}; CHECK(readReceipt(owner,before.Sequence,before.Token,&receipt) == 1);
        CHECK(receipt.Stage == RtSourcePhysicalExit && (receipt.Flags & RtSourcePhysicalExitedFlag));
        CHECK(receipt.Sequence == before.Sequence && receipt.Token == before.Token && receipt.ArgBits == before.ArgBits);
        CHECK(receipt.ParentSequence == before.ParentSequence && receipt.IncomingMethod == before.IncomingMethod);
        CHECK(verifyCurrent(owner,&before) == 0);
        CHECK(readReceipt(owner,before.Sequence,before.Token+1,&receipt) == 0);
        if(release) { CHECK(releaseReceipt(owner,before.Sequence,before.Token) == 1); CHECK(readReceipt(owner,before.Sequence,before.Token,&receipt) == 0); }
    }
    observedCount = 0;
}
void Normal(StockSource* source,StockPlayer* player) {
    restorePitch = true;
    CHECK(Call(1,source,0x40000000u,method(1)) == 1 && source->Pitch == 2);
    CHECK(Call(3,player,0x3e800001u,method(3)) == 1);
    const auto replay = LastBefore(3); const auto schedule = LastBefore(5); const auto reset = LastBefore(1);
    CHECK(player->ReplayCalls == 1 && source->ScheduleCalls == 1 && source->Pitch == 2 && source->PitchCalls == 3);
    CHECK(schedule.ParentSequence == replay.Sequence && reset.ParentSequence == replay.Sequence);
    CHECK(schedule.ArgBits == source->LastScheduleBits && source->LastScheduleMethod == nullptr);
    CHECK(player->OriginHasValue == 1 && player->Origin == source->Scheduled);
    const auto exactStart = source->Scheduled; CHECK(exactStart > 100.25);
    CHECK(Call(2,player,0,method(2)) == 1 && source->Playing == 0 && !player->OriginHasValue);
    source->Time = 7.25; player->Dsp = 200;
    CHECK(Call(4,player,0x3f000000u,method(4)) == 1 && source->Pitch == 2);
    CHECK(source->Time == 7.25 && source->Scheduled == 200.5 && player->Origin == 193.25);
    const auto resume = LastBefore(4); CHECK(LastBefore(5).ParentSequence == resume.Sequence);
    const auto before = source->ScheduleCalls; CHECK(Call(4,player,0x3f000000u,method(4)) == 1);
    CHECK(source->ScheduleCalls == before); // Actual stock-shaped no-op is not a schedule witness.
    CHECK(State().InFlight == 0); Completed();
}
void Bits(StockSource* source,StockPlayer* player,StockProgress* progress) {
    const std::uint32_t singles[]{0,0x80000000u,0x3f800001u,0x7f800000u,0xff800000u,0x7fc12345u,0x7fa12345u};
    for(const auto bits:singles) {
        auto* incoming = reinterpret_cast<void*>(static_cast<std::uintptr_t>(0x12340000u+bits));
        CHECK(Call(1,source,bits,incoming) == 1);
        CHECK(source->LastPitchBits == bits && source->LastPitchMethod == incoming && LastBefore(1).ArgBits == bits);
    }
    const std::uint64_t doubles[]{0,0x8000000000000000ull,0x400921fb54442d19ull,0x7ff0000000000000ull,0x7ff812345678abcdull};
    for(const auto bits:doubles) {
        CHECK(Call(5,source,bits,method(5)) == 1);
        CHECK(source->LastScheduleBits == bits && LastBefore(5).ArgBits == bits && source->LastScheduleMethod == method(5));
    }
    for(const unsigned bits:{0u,1u,2u,255u}) {
        CHECK(Call(7,progress,bits,method(7)) == 1);
        CHECK(player->LastBoolBits == bits && LastBefore(7).ArgBits == bits);
    }
    auto* clip = reinterpret_cast<void*>(0x123456789abcdef0ull);
    CHECK(Call(6,source,reinterpret_cast<std::uint64_t>(clip),method(6)) == 1 && source->Clip == clip);
    CHECK(LastBefore(6).ArgBits == reinterpret_cast<std::uint64_t>(clip) && source->LastClipMethod == method(6));
    CHECK(Call(6,source,0,nullptr) == 1 && source->Clip == nullptr && LastBefore(6).IncomingMethod == 0);
    Completed();
}
void NestedAba(StockSource* source) {
    CHECK(Call(1,source,0x40000000u,method(1)) == 1); const auto first = LastBefore(1);
    CHECK(Call(1,source,0x40000000u,method(1)) == 1); const auto equal = LastBefore(1);
    CHECK(equal.WriterSequence > first.WriterSequence && source->Pitch == 2);
    CHECK(Call(1,source,0x40e00000u,method(1)) == 1);
    CHECK(Call(1,source,0x40000000u,method(1)) == 1);
    CHECK(State().WriterSequence > equal.WriterSequence && source->Pitch == 2);
    setSource(source,StockPitchNestedEqual); const auto old = State().WriterSequence;
    CHECK(Call(1,source,0x40000000u,method(1)) == 1);
    const auto nested = LastBefore(1); CHECK(nested.ParentSequence != 0 && nested.Depth == 2);
    CHECK(State().WriterSequence == old+2 && source->Pitch == 2);
    const auto sourceFence = State().SourceSequence;
    auto* clipA = reinterpret_cast<void*>(0x11110000u); auto* clipB = reinterpret_cast<void*>(0x22220000u);
    CHECK(Call(6,source,reinterpret_cast<std::uint64_t>(clipA)) == 1);
    CHECK(Call(6,source,reinterpret_cast<std::uint64_t>(clipA)) == 1);
    CHECK(Call(6,source,reinterpret_cast<std::uint64_t>(clipB)) == 1);
    CHECK(Call(6,source,reinterpret_cast<std::uint64_t>(clipA)) == 1);
    CHECK(State().SourceSequence == sourceFence+4 && source->Clip == clipA);
    Completed();
}
void OriginalSeh(StockSource* source,StockPlayer* player) {
    std::uint32_t code{};
    setSource(source,StockPitchThrowAfterStore);
    CHECK(Call(1,source,0x40000000u,method(1),&code) == 2 && code == 0xe2400011u);
    CHECK(source->Pitch == 2 && source->PitchCalls == 1 && abnormalCount == 1 && returnedCount == 0);
    const auto pitchFrame = LastBefore(1); RtPlaybackSourceFrame receipt{};
    CHECK(readReceipt(owner,pitchFrame.Sequence,pitchFrame.Token,&receipt) == 1);
    CHECK((receipt.Flags & (RtSourceAbnormalFlag|RtSourcePhysicalExitedFlag)) == (RtSourceAbnormalFlag|RtSourcePhysicalExitedFlag));
    CHECK(!(receipt.Flags & RtSourceReturnedFlag));
    setSource(source,StockScheduleThrowAfterStore);
    CHECK(Call(3,player,0x3e800000u,method(3),&code) == 2 && code == 0xe2400012u);
    CHECK(player->ReplayCalls == 1 && source->ScheduleCalls == 1 && source->PitchCalls == 2 && abnormalCount == 3);
    CHECK(State().InFlight == 0 && State().Entered == State().Exited); Completed();
}
void StageAB(StockSource* source,StockPlayer* player) {
    for(const auto bits:{0ull,0x8000000000000000ull,0x400921fb54442d19ull,0x7ff0000000000000ull,0xfff0000000000000ull,0x7ff812345678abcdull}) {
        CHECK(Call(5,source,bits,method(5)) == 1);
        CHECK(source->LastScheduleBits == bits && source->LastScheduleMethod == method(5));
        CHECK(LastBefore(5).ArgBits == bits && LastBefore(5).IncomingMethod == reinterpret_cast<std::uint64_t>(method(5)));
    }
    const auto fence=State().SourceSequence;
    for(const auto bits:{0ull,0x11110000ull,0x11110000ull,0x22220000ull,0x11110000ull}) {
        CHECK(Call(6,source,bits,method(6)) == 1);
        CHECK(reinterpret_cast<std::uint64_t>(source->Clip) == bits && LastBefore(6).ArgBits == bits);
        CHECK(source->LastClipMethod == method(6));
    }
    CHECK(State().SourceSequence == fence+5);
    source->Playing=1; player->OriginHasValue=1;
    CHECK(Call(2,player,0,method(2)) == 1 && !source->Playing && !player->OriginHasValue);
    CHECK(LastBefore(8).ParentSequence == LastBefore(2).Sequence && source->LastPauseMethod == nullptr);
    CHECK(Call(8,source,0,method(8)) == 1 && source->LastPauseMethod == method(8));
    Completed();
    for(const unsigned role:{2u,5u,6u,8u}) {
        StockMachineState before{},after{};
        source->Playing=1;
        machineInvoke(kinds[role],targets[role],role == 2 ? static_cast<void*>(player) : static_cast<void*>(source),
            role == 5 ? 0x400921fb54442d19ull : role == 6 ? 0xabcdef0011223344ull : 0ull,method(role),&before,&after);
        CHECK(std::memcmp(before.Nonvolatile,after.Nonvolatile,sizeof(before.Nonvolatile)) == 0);
        CHECK(std::memcmp(before.Xmm,after.Xmm,sizeof(before.Xmm)) == 0);
        CHECK((before.Flags & 0x400u) == 0 && (after.Flags & 0x400u) == 0);
        CHECK(LastBefore(role).IncomingMethod == reinterpret_cast<std::uint64_t>(method(role)));
        Completed();
    }
    std::uint32_t code{};
    source->Playing=1; player->Flags=StockPlayerPauseThrowAfterStore;
    auto count=player->PauseCalls;
    CHECK(Call(2,player,0,method(2),&code) == 2 && code == 0xe2400015u && player->PauseCalls == count+1);
    CHECK(LastBefore(2).IncomingMethod == reinterpret_cast<std::uint64_t>(method(2))); player->Flags=0; Completed();
    setSource(source,StockPauseThrowAfterStore); count=source->PauseCalls;
    CHECK(Call(8,source,0,method(8),&code) == 2 && code == 0xe2400014u && source->PauseCalls == count+1); Completed();
    setSource(source,StockScheduleThrowAfterStore); count=source->ScheduleCalls;
    CHECK(Call(5,source,0x400921fb54442d19ull,method(5),&code) == 2 && code == 0xe2400012u && source->ScheduleCalls == count+1); Completed();
    setSource(source,StockClipThrowAfterStore); count=source->ClipCalls;
    CHECK(Call(6,source,0xabcdef0011223344ull,method(6),&code) == 2 && code == 0xe2400013u && source->ClipCalls == count+1); Completed();
    CHECK(abnormalCount == 4 && State().InFlight == 0);
}
void TargetSpecific(StockSource* source,StockPlayer* player,StockProgress* progress) {
    StageAB(source,player); setSource(source,0);
    std::uint32_t code{};
    for(const unsigned role:{3u,4u}) {
        player->Flags=role == 3 ? StockPlayerReplayThrowAfterStore : StockPlayerResumeThrowAfterStore;
        source->Playing=0; source->Time=7.25; player->Dsp=200;
        const auto bodyCount=role == 3 ? player->ReplayCalls : player->ResumeCalls;
        const auto scheduleCount=source->ScheduleCalls, pitchCount=source->PitchCalls;
        CHECK(Call(role,player,0x3e800001u,method(role),&code) == 2 && code == (role == 3 ? 0xe2400016u : 0xe2400017u));
        CHECK((role == 3 ? player->ReplayCalls : player->ResumeCalls) == bodyCount+1);
        CHECK(source->ScheduleCalls == scheduleCount+1 && source->PitchCalls == pitchCount+1);
        CHECK(player->LastDelayBits == 0x3e800001u && player->LastMethod == method(role));
        const auto parent=LastBefore(role), reset=LastBefore(1), schedule=LastBefore(5);
        CHECK(parent.ArgBits == 0x3e800001u && reset.ParentSequence == parent.Sequence && schedule.ParentSequence == parent.Sequence);
        CHECK(reset.ArgBits == 0x3f800000u && reset.IncomingMethod == 0 && schedule.IncomingMethod == 0);
        double expected=200.0+static_cast<double>([] { std::uint32_t bits=0x3e800001u; float value; std::memcpy(&value,&bits,4); return value; }());
        std::uint64_t expectedBits{}; std::memcpy(&expectedBits,&expected,8);
        CHECK(schedule.ArgBits == expectedBits && source->LastScheduleBits == expectedBits);
        CHECK(role == 3 ? source->Time == 0 && player->Origin == expected : source->Time == 7.25 && player->Origin == expected-7.25);
        Completed();
        player->Flags=0;
        const auto noSchedule=source->ScheduleCalls;
        CHECK(Call(role,player,0x80000000u,method(role)) == 1 && source->ScheduleCalls == noSchedule);
        CHECK(LastBefore(role).ArgBits == 0x80000000u && player->LastDelayBits == 0x80000000u); Completed();
        setSource(source,StockScheduleThrowAfterStore); source->Playing=0;
        CHECK(Call(role,player,0x3f000000u,method(role),&code) == 2 && code == 0xe2400012u);
        CHECK(LastBefore(5).ParentSequence == LastBefore(role).Sequence); Completed(); setSource(source,0);
    }
    for(const unsigned value:{0u,1u,2u,0x7fu,0x80u,0xffu}) {
        progress->FromBeginning=static_cast<unsigned char>(value); source->Playing=1; player->OriginHasValue=1;
        const auto pauseCount=source->PauseCalls, branches=progress->BranchCalls;
        CHECK(Call(7,progress,value,method(7)) == 1 && progress->LastEqual == 1 && source->PauseCalls == pauseCount);
        CHECK(progress->BranchCalls == branches && source->Playing == 1 && player->OriginHasValue == 1); Completed();
        const auto opposite=value^0xffu;
        CHECK(Call(7,progress,0x1234567800000000ull|opposite,method(7)) == 1 && progress->LastEqual == 0);
        CHECK(progress->BranchCalls == branches+1 && source->PauseCalls == pauseCount+1 && !source->Playing && !player->OriginHasValue);
        CHECK(progress->FromBeginning == opposite && progress->LastBoolBits == opposite && progress->LastMethod == method(7));
        CHECK(LastBefore(7).ArgBits == opposite && LastBefore(8).ParentSequence == LastBefore(7).Sequence); Completed();
    }
    progress->FromBeginning=0; progress->Flags=StockProgressThrowAfterStore; source->Playing=1; player->OriginHasValue=1;
    const auto calls=progress->Calls, pauses=source->PauseCalls;
    CHECK(Call(7,progress,255u,method(7),&code) == 2 && code == 0xe2400018u);
    CHECK(progress->Calls == calls+1 && source->PauseCalls == pauses+1 && progress->FromBeginning == 255 && !player->OriginHasValue);
    CHECK(LastBefore(8).ParentSequence == LastBefore(7).Sequence); Completed();
    CHECK(abnormalCount == 11 && State().Entered == State().Exited && State().InFlight == 0);
    progress->Flags=0;
    for(const unsigned role:{3u,4u,7u}) {
        StockMachineState before{},after{}; source->Playing=0; progress->FromBeginning=1;
        machineInvoke(kinds[role],targets[role],role == 7 ? static_cast<void*>(progress) : static_cast<void*>(player),
            role == 7 ? 0ull : 0x3e800001ull,method(role),&before,&after);
        CHECK(std::memcmp(before.Nonvolatile,after.Nonvolatile,sizeof(before.Nonvolatile)) == 0);
        CHECK(std::memcmp(before.Xmm,after.Xmm,sizeof(before.Xmm)) == 0);
        CHECK((before.Flags & 0x400u) == 0 && (after.Flags & 0x400u) == 0);
        CHECK(LastBefore(role).IncomingMethod == reinterpret_cast<std::uint64_t>(method(role)));
        if(role == 7) CHECK(progress->LastEqual == 0);
        else CHECK(LastBefore(5).ParentSequence == LastBefore(role).Sequence);
        Completed();
    }
}
DWORD WINAPI WorkerCall(void* raw) { return static_cast<DWORD>(Call(1,raw,0x40000000u,method(1))); }
DWORD WINAPI WorkerWithdraw(void*) {
    CHECK(waitPitch() == WAIT_OBJECT_0);
    CHECK(setArmed(owner,initial.OwnerEpoch,0) == 1);
    const auto state=State(); CHECK(state.InFlight == 1 && !(state.Flags & RtSourceArmed) && (state.Flags & RtSourceWithdrawn));
    CHECK(setArmed(owner,initial.OwnerEpoch,1) == 0);
    releasePitch(); return 0;
}
void Inflight(StockSource* source) {
    setSource(source,StockPitchBlock);
    auto thread = CreateThread(nullptr,0,WorkerCall,source,0,nullptr); CHECK(thread != nullptr);
    CHECK(waitPitch() == WAIT_OBJECT_0); const auto live = State(); CHECK(live.InFlight == 1);
    const auto entered = LastBefore(1); RtPlaybackSourceFrame receipt{};
    CHECK(readReceipt(owner,entered.Sequence,entered.Token,&receipt) == 0);
    releasePitch(); CHECK(WaitForSingleObject(thread,10000) == WAIT_OBJECT_0); CloseHandle(thread);
    CHECK(State().InFlight == 0 && (State().Flags & RtSourceUnknown)); Completed();
}
}

int wmain(int argc,wchar_t** argv) {
    if(argc != 5) return 64;
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);
    helper = LoadLibraryW(argv[2]); fixture = LoadLibraryW(argv[3]); auto dobby = LoadLibraryW(argv[4]);
    CHECK(helper && fixture && dobby); if(!helper || !fixture || !dobby) return 65;
    CheckExecutableDir64(argv[3]);
#define SOURCE_SYMBOL(variable,name) variable=Symbol<decltype(variable)>(helper,name)
    SOURCE_SYMBOL(createOwner,"RtPlaybackSourceCreateOwner"); SOURCE_SYMBOL(configure,"RtPlaybackSourceConfigure");
    SOURCE_SYMBOL(getEntry,"RtPlaybackSourceGetEntry"); SOURCE_SYMBOL(setArmed,"RtPlaybackSourceSetArmed");
    SOURCE_SYMBOL(getInfo,"RtPlaybackSourceGetInfo"); SOURCE_SYMBOL(readOwner,"RtPlaybackSourceReadOwner");
    SOURCE_SYMBOL(readCurrent,"RtPlaybackSourceReadCurrent"); SOURCE_SYMBOL(verifyCurrent,"RtPlaybackSourceVerifyCurrent");
    SOURCE_SYMBOL(readReceipt,"RtPlaybackSourceReadReceipt"); SOURCE_SYMBOL(releaseReceipt,"RtPlaybackSourceReleaseReceipt");
#define STOCK_SYMBOL(variable,name) variable=Symbol<decltype(variable)>(fixture,name)
    STOCK_SYMBOL(createSource,"StockFixtureCreateSource"); STOCK_SYMBOL(createPlayer,"StockFixtureCreatePlayer");
    STOCK_SYMBOL(createProgress,"StockFixtureCreateProgress");
    STOCK_SYMBOL(setSource,"StockFixtureConfigure"); STOCK_SYMBOL(invoke,"StockFixtureInvoke"); STOCK_SYMBOL(method,"StockMethod");
    STOCK_SYMBOL(waitPitch,"StockFixtureWaitPitch"); STOCK_SYMBOL(releasePitch,"StockFixtureReleasePitch");
    STOCK_SYMBOL(machineInvoke,"StockFixtureMachineInvoke");
    stageAb=std::wcscmp(argv[1],L"stage-ab") == 0;
    Install(dobby); auto* source = createSource(); auto* player = createPlayer(source); auto* progress=createProgress(player); CHECK(source && player && progress);
    const bool dormant = std::wcscmp(argv[1],L"dormant-withdraw") == 0;
    if(!dormant) CHECK(setArmed(owner,initial.OwnerEpoch,1) == 1);
    if(stageAb) StageAB(source,player);
    else if(std::wcscmp(argv[1],L"normal") == 0) Normal(source,player);
    else if(std::wcscmp(argv[1],L"bits") == 0) Bits(source,player,progress);
    else if(std::wcscmp(argv[1],L"nested-aba") == 0) NestedAba(source);
    else if(std::wcscmp(argv[1],L"original-seh") == 0) OriginalSeh(source,player);
    else if(std::wcscmp(argv[1],L"target-specific") == 0) TargetSpecific(source,player,progress);
    else if(std::wcscmp(argv[1],L"inline-pause") == 0) {
        source->Playing=1; player->OriginHasValue=1; player->Origin=10;
        CHECK(Call(7,progress,255u,method(7)) == 1);
        CHECK(player->PauseCalls == 0 && player->ProgressPlayCalls == 1 && source->PauseCalls == 1 && !player->OriginHasValue);
        CHECK(LastBefore(8).ParentSequence == LastBefore(7).Sequence && LastBefore(8).Depth == 2); Completed();
    } else if(dormant) {
        for(unsigned i=0;i<300;++i) CHECK(Call(1,source,0x40000000u,method(1)) == 1 && source->Pitch == 2);
        CHECK(source->PitchCalls == 300 && !(State().Flags & RtSourceUnknown));
        CHECK((LastBefore(1).Flags & RtSourceDormant) && !(LastBefore(1).Flags & RtSourceArmed)); Completed();
        CHECK(setArmed(owner,initial.OwnerEpoch,1) == 1);
        CHECK(setArmed(owner,initial.OwnerEpoch,0) == 1 && setArmed(owner,initial.OwnerEpoch,1) == 0);
        for(unsigned i=0;i<300;++i) CHECK(Call(1,source,0x40e00000u,method(1)) == 1 && source->Pitch == 7);
        CHECK(source->PitchCalls == 600 && !(State().Flags & RtSourceUnknown));
        CHECK((LastBefore(1).Flags & RtSourceWithdrawn) && (LastBefore(1).Flags & RtSourceDormant)); Completed();
    } else if(std::wcscmp(argv[1],L"foreign-thread") == 0) {
        auto thread=CreateThread(nullptr,0,WorkerCall,source,0,nullptr); CHECK(thread != nullptr);
        CHECK(WaitForSingleObject(thread,10000) == WAIT_OBJECT_0); CloseHandle(thread);
        CHECK(State().Flags & RtSourceUnknown); CHECK(LastBefore(1).ThreadId != initial.OwnerThreadId);
        CHECK(source->Pitch == 2 && source->PitchCalls == 1 && setArmed(owner,initial.OwnerEpoch,1) == 0); Completed();
    } else if(std::wcscmp(argv[1],L"observer-fault") == 0) {
        observerFault=true; CHECK(Call(1,source,0x40000000u,method(1)) == 1);
        CHECK(source->PitchCalls == 1 && source->Pitch == 2 && (State().Flags & RtSourceUnknown)); Completed();
    } else if(std::wcscmp(argv[1],L"journal-overflow") == 0) {
        for(unsigned i=0;i<257;++i) CHECK(Call(1,source,0x40000000u,method(1)) == 1);
        CHECK(source->PitchCalls == 257 && State().Entered == 257 && State().Exited == 257 && State().InFlight == 0);
        CHECK(State().Flags & RtSourceUnknown);
        const auto last=LastBefore(1); RtPlaybackSourceFrame receipt{}; CHECK(readReceipt(owner,last.Sequence,last.Token,&receipt) == 0);
        observedCount=0; // Overflow intentionally retains all 256 prior native receipt responsibilities.
    } else if(std::wcscmp(argv[1],L"inflight") == 0) Inflight(source);
    else if(std::wcscmp(argv[1],L"after-inflight") == 0) {
        inspectAfter=true; CHECK(Call(1,source,0x40000000u,method(1)) == 1); CHECK(source->PitchCalls == 1); Completed();
    } else if(std::wcscmp(argv[1],L"machine") == 0) {
        for(unsigned role=1;role<=8;++role) {
            auto* self=role == 7 ? static_cast<void*>(progress) : (role == 1 || role == 5 || role == 6 || role == 8) ? static_cast<void*>(source) : static_cast<void*>(player);
            StockMachineState before{},after{};
            machineInvoke(kinds[role],targets[role],self,role == 5 ? 0x4059000000000000ull : 0x3f000000u,method(role),&before,&after);
            CHECK(std::memcmp(before.Nonvolatile,after.Nonvolatile,sizeof(before.Nonvolatile)) == 0);
            CHECK(std::memcmp(before.Xmm,after.Xmm,sizeof(before.Xmm)) == 0);
            CHECK((before.Flags & 0x400u) == 0 && (after.Flags & 0x400u) == 0); // Windows x64 direction flag contract.
            CHECK(LastBefore(role).IncomingMethod == reinterpret_cast<std::uint64_t>(method(role)));
        }
        Completed();
    } else if(std::wcscmp(argv[1],L"worker-withdraw") == 0) {
        setSource(source,StockPitchBlock);
        auto thread=CreateThread(nullptr,0,WorkerWithdraw,nullptr,0,nullptr); CHECK(thread != nullptr);
        CHECK(Call(1,source,0x40000000u,method(1)) == 1);
        CHECK(WaitForSingleObject(thread,10000) == WAIT_OBJECT_0); CloseHandle(thread);
        CHECK(source->PitchCalls == 1 && State().InFlight == 0 && !(State().Flags & RtSourceUnknown));
        CHECK(LastBefore(1).Flags & RtSourceArmed); Completed();
        setSource(source,0);
        for(unsigned i=0;i<300;++i) CHECK(Call(1,source,0x40000000u,method(1)) == 1);
        CHECK(source->PitchCalls == 301 && !(State().Flags & RtSourceUnknown)); Completed();
    }
    else CHECK(false);
    RtPlaybackSourceFrame noCurrent{}; CHECK(readCurrent(2,0,&noCurrent) == 0);
    const auto final=State(); CHECK(final.Entered == final.Exited && final.InFlight == 0);
    std::printf("{\"mode\":\"%ls\",\"passed\":%s,\"checks\":%llu,\"failures\":%llu,\"actualDobbyPrepared\":%llu,\"actualDobbyCommitted\":%llu,\"callbacks\":%llu,\"nativeEntered\":%llu,\"nativeExited\":%llu,\"nativeInFlight\":%llu,\"before\":%llu,\"originalReturned\":%llu,\"abnormal\":%llu,\"fixtureLoadedBase\":\"0x%llx\",\"fixturePreferredBase\":\"0x%llx\",\"fixtureDir64Rva\":\"0x%llx\",\"fixtureDir64Value\":\"0x%llx\",\"stockShapedFixture\":true,\"gameCodeExecuted\":false,\"productionAdmission\":false,\"targets\":[",
        argv[1],failures?"false":"true",checks,failures,prepared,committed,callbacks,final.Entered,final.Exited,final.InFlight,beforeCount,returnedCount,abnormalCount,
        fixtureLoadedBase,fixturePreferredBase,fixtureDir64Rva,fixtureDir64Value);
    bool comma=false;
    for(unsigned role=1;role<=8;++role) {
        if(!Installed(role)) continue;
        const auto& thunk=thunks[role];
        std::printf("%s{\"role\":%u,\"target\":\"0x%llx\",\"actualDobbyOriginal\":\"0x%llx\",\"movedLength\":%u,\"continuation\":\"0x%llx\",\"actualDobbyOriginalHex\":\"",
            comma?",":"",role,reinterpret_cast<std::uint64_t>(targets[role]),reinterpret_cast<std::uint64_t>(thunk.DobbyOriginal),
            thunk.MovedLength,reinterpret_cast<std::uint64_t>(targets[role])+thunk.MovedLength);
        for(unsigned i=0;i<thunk.MovedLength+14;++i) std::printf("%02x",thunk.DobbyBytes[i]);
        std::printf("\",\"independentOriginal\":\"0x%llx\",\"originalUnwindProbes\":%u,\"targetUnwindProbes\":%u}",
            reinterpret_cast<std::uint64_t>(thunk.Bytes),thunk.UnwindProbes,thunk.TargetUnwindProbes); comma=true;
    }
    std::printf("]}\n");
    return failures ? 1 : 0;
}
