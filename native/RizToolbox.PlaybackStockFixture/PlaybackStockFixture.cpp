#define RIZ_PLAYBACK_STOCK_EXPORTS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <cstring>
#include <new>
#include "PlaybackStockFixture.h"
#pragma comment(linker, "/export:StockPlayerConstructor")
#pragma comment(linker, "/export:StockPlayerConstructorEnd")
extern "C" unsigned char StockFixtureInitialized = 1;
#ifdef RIZ_STOCK_PROGRESS_RESOURCE_PROFILE
extern "C" void* __cdecl StockMethodProgressClip();
#endif

namespace {
struct MethodInfo2022 {
    void* Entry; void* VirtualEntry; void* Invoker; const char* Name; void* Class; void* Returns;
    void* Parameters; void* RuntimeData; void* GenericData;
    std::uint32_t Token; std::uint16_t Flags, ImplementationFlags, Slot;
    unsigned char ParameterCount, Bitfield;
};
struct PrimitiveType { void* Data; std::uint32_t Bits, Padding; };
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
StockUnityClass sourceClass{};
std::uint64_t playerClass = 0x73746f636b6d7567ull;
#else
std::uint64_t sourceClass = 0x73746f636b617564ull, playerClass = 0x73746f636b6d7567ull;
#endif
std::uint64_t progressClass = 0x73746f636b707267ull;
std::uint64_t foreignSharedClasses[61]{};
PrimitiveType singleType{nullptr, 0x000c0000u, 0}, voidType{nullptr, 0x00010000u, 0};
PrimitiveType doubleType{nullptr, 0x000d0000u, 0}, boolType{nullptr, 0x00020000u, 0};
PrimitiveType sourceType{&sourceClass, 0x00120000u, 0}, clipType{&sourceClass, 0x00120000u, 0};
PrimitiveType* singleArgs[]{&singleType}; PrimitiveType* staticPitchArgs[]{&sourceType, &singleType};
PrimitiveType* doubleArgs[]{&doubleType}; PrimitiveType* boolArgs[]{&boolType}; PrimitiveType* clipArgs[]{&clipType};
PrimitiveType* constructorArgs[]{&sourceType};
PrimitiveType uint64Type{nullptr, 0x000b0000u, 0};
PrimitiveType* playHelperArgs[]{&sourceType, &uint64Type};
#define STOCK_METHOD(entry,name,klass,returns,args,flags,count,token) {reinterpret_cast<void*>(&entry),reinterpret_cast<void*>(&entry),nullptr,name,klass,returns,args,nullptr,nullptr,token,flags,0,0xffff,count,0}
MethodInfo2022 getTime = STOCK_METHOD(StockGetTime,"GetTime",&playerClass,&singleType,nullptr,0x86,0,0x06001000u);
MethodInfo2022 time = STOCK_METHOD(StockSetTime,"set_time",&sourceClass,&voidType,singleArgs,0x86,1,0x0600100au);
MethodInfo2022 pitch = STOCK_METHOD(StockSetPitch,"SetPitch",&sourceClass,&voidType,staticPitchArgs,0x96,2,0x06001001u);
MethodInfo2022 pitchAlias = STOCK_METHOD(StockSetPitch,"set_pitch",&sourceClass,&voidType,singleArgs,0x86,1,0x06001002u);
MethodInfo2022 pause = STOCK_METHOD(StockPause,"Pause",&playerClass,&voidType,nullptr,0x86,0,0x06001003u);
MethodInfo2022 replay = STOCK_METHOD(StockReplay,"ReplayScheduled",&playerClass,&voidType,singleArgs,0x86,1,0x06001004u);
MethodInfo2022 resume = STOCK_METHOD(StockResume,"ResumeScheduled",&playerClass,&voidType,singleArgs,0x86,1,0x06001005u);
MethodInfo2022 schedule = STOCK_METHOD(StockPlayScheduled,"PlayScheduled",&sourceClass,&voidType,doubleArgs,0x86,1,0x06001006u);
MethodInfo2022 clip = STOCK_METHOD(StockSetClip,"set_clip",&sourceClass,&voidType,clipArgs,0x86,1,0x06001007u);
MethodInfo2022 progressPlay = STOCK_METHOD(StockProgressPlay,"Play",&progressClass,&voidType,boolArgs,0x86,1,0x06001008u);
MethodInfo2022 pauseLeaf = STOCK_METHOD(StockPauseLeaf,"Pause",&sourceClass,&voidType,nullptr,0x86,0,0x06001009u);
MethodInfo2022 playerConstructor = STOCK_METHOD(StockPlayerConstructor,".ctor",&playerClass,&voidType,constructorArgs,0x86,1,0x0600100cu);
MethodInfo2022 audioPlayHelper = STOCK_METHOD(StockAudioPlayHelper,"PlayHelper",&sourceClass,&voidType,playHelperArgs,0x96,2,0x06001010u);
MethodInfo2022 audioPlayDelayed = STOCK_METHOD(StockAudioPlayDelayed,"Play",&sourceClass,&voidType,doubleArgs,0x86,1,0x06001011u);
MethodInfo2022 audioPlay = STOCK_METHOD(StockAudioPlay,"Play",&sourceClass,&voidType,nullptr,0x86,0,0x06001012u);
MethodInfo2022 audioStop = STOCK_METHOD(StockAudioStop,"Stop",&sourceClass,&voidType,nullptr,0x86,0,0x06001013u);
MethodInfo2022 audioStopBoolean = STOCK_METHOD(StockAudioStopBoolean,"Stop",&sourceClass,&voidType,boolArgs,0x86,1,0x06001014u);
MethodInfo2022 audioSetLoop = STOCK_METHOD(StockAudioSetLoop,"set_loop",&sourceClass,&voidType,boolArgs,0x86,1,0x06001015u);
struct DirectRecord { std::uint64_t Before,After,Self,Argument,Method,Thread; };
DirectRecord directRecords[6]{};
std::uint32_t directFlags;StockDirectBoundary directBoundary;
volatile LONG constructorFlags{}, constructorBeforeCalls{}, constructorAfterCalls{};
StockConstructorBoundary constructorBoundary{};
void* constructorLastSelf{}; void* constructorLastReference{}; void* constructorLastMethod{};
void* constructorLastBeforeValue{}; void* constructorLastAfterValue{};
std::uint32_t constructorLastThread{};
HANDLE pitchEntered, pitchRelease;
HANDLE getTimeEntered, getTimeRelease;
volatile LONG blockGetTime = 0;
void ClearOrigin(StockPlayer* player) { player->OriginHasValue = 0; player->Origin = 0; }
void Origin(StockPlayer* player, double value) { player->OriginHasValue = 1; player->Origin = value; }
}
static_assert(sizeof(MethodInfo2022) == 88, "Unity2022 MethodInfo");
static_assert(offsetof(StockPlayer, OriginHasValue) == 0x18, "stock nullable origin shape");
static_assert(offsetof(StockProgress, FromBeginning) == 0x42, "actual Progress compare field");

extern "C" float __cdecl StockGetTimeBody(StockPlayer* self,
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
    void* method
#else
    void*
#endif
    ) {
    if (!self || !self->AudioSource) RaiseException(0xe2400001u, 0, 0, nullptr);
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
    ++self->AudioSource->GetTimeOriginalCalls; self->AudioSource->LastGetTimeMethod = method;
#endif
    if (InterlockedCompareExchange(&blockGetTime, 0, 0)) {
        SetEvent(getTimeEntered);
        // The one-process test owner must release on both the semantic path
        // and every finally path, then join the actual native borrower before
        // disposing its owner or backing storage. A wall-clock deadline must
        // not inject an unrelated SEH failure while the test proves pending.
        if (WaitForSingleObject(getTimeRelease, INFINITE) != WAIT_OBJECT_0) RaiseException(0xe2400020u, 0, 0, nullptr);
    }
    const auto* source = self->AudioSource;
    if (!self->OriginHasValue) return static_cast<float>(source->Time);
    if (self->Dsp < source->Scheduled) return static_cast<float>(source->Time);
    // This fixture's lower/native writer deliberately leaves wrapper Pitch independent.
#ifdef RIZ_STOCK_LOWER_PITCH_PROFILE
    const auto nativeTimerPitch = StockFixtureNativeGetPitch(self->AudioSource);
#else
    const auto nativeTimerPitch = source->Pitch;
#endif
    return static_cast<float>((self->Dsp - self->Origin) * static_cast<double>(nativeTimerPitch));
}
extern "C" void __cdecl StockSetTimeBody(StockSource* self, float value, void* method) {
    if (!self) RaiseException(0xe2400019u, 0, 0, nullptr);
    ++self->TimeCalls; self->LastTimeMethod = method; self->Time = static_cast<double>(value);
    std::uint32_t bits; std::memcpy(&bits, &value, 4); self->LastTimeBits = bits;
    if (self->Flags & StockTimeThrowAfterStore) RaiseException(0xe2400019u, 0, 0, nullptr);
}
extern "C" void __cdecl StockPlayerConstructorBefore(void* self, void* reference, void* method) {
    InterlockedIncrement(&constructorBeforeCalls);
    constructorLastSelf = self; constructorLastReference = reference; constructorLastMethod = method;
    constructorLastBeforeValue = self ? static_cast<void**>(self)[2] : nullptr;
    constructorLastThread = GetCurrentThreadId();
    if (constructorBoundary) constructorBoundary(self, reference, method, 1);
    if (InterlockedCompareExchange(&constructorFlags, 0, 0) & StockConstructorThrowBeforeStore)
        RaiseException(0xe2400041u, 0, 0, nullptr);
}
extern "C" void __cdecl StockPlayerConstructorAfter(void* self, void* reference, void* method) {
    InterlockedIncrement(&constructorAfterCalls);
    constructorLastSelf = self; constructorLastReference = reference; constructorLastMethod = method;
    constructorLastAfterValue = self ? static_cast<void**>(self)[2] : nullptr;
    constructorLastThread = GetCurrentThreadId();
    if (constructorBoundary) constructorBoundary(self, reference, method, 2);
    if (InterlockedCompareExchange(&constructorFlags, 0, 0) & StockConstructorThrowAfterStore)
        RaiseException(0xe2400042u, 0, 0, nullptr);
}
extern "C" void __cdecl StockSetPitchBody(StockSource* self, float value, void* method) {
    if (!self) RaiseException(0xe2400002u, 0, 0, nullptr);
    ++self->PitchCalls; self->LastPitchMethod = method; self->Pitch = value;
    std::uint32_t bits; std::memcpy(&bits, &value, 4); self->LastPitchBits = bits;
    if (self->Flags & StockPitchBlock) {
        SetEvent(pitchEntered);
        if (WaitForSingleObject(pitchRelease, 10000) != WAIT_OBJECT_0) RaiseException(0xe2400010u, 0, 0, nullptr);
    }
    if (self->Flags & StockPitchNestedEqual) {
        self->Flags &= ~StockPitchNestedEqual;
        StockSetPitch(self, value, &pitchAlias); // The real hooked setter, never a producer call.
    }
    if (self->Flags & StockPitchThrowAfterStore) RaiseException(0xe2400011u, 0, 0, nullptr);
}
extern "C" void __cdecl StockPauseLeafBody(StockSource* self, void* method) {
    if (!self) RaiseException(0xe2400003u, 0, 0, nullptr);
    ++self->PauseCalls; self->LastPauseMethod = method; self->Playing = 0;
    if (self->Flags & StockPauseThrowAfterStore) RaiseException(0xe2400014u, 0, 0, nullptr);
}
extern "C" void __cdecl StockPauseBody(StockPlayer* self, void* method) {
    ++self->PauseCalls; self->LastMethod = method;
    if (!self->Active || !self->AudioSource->Playing) return;
    StockPauseLeaf(self->AudioSource, nullptr); ClearOrigin(self);
    if (self->Flags & StockPlayerPauseThrowAfterStore) RaiseException(0xe2400015u, 0, 0, nullptr);
}
extern "C" void __cdecl StockPlayScheduledBody(StockSource* self, double start, void* method) {
    ++self->ScheduleCalls; self->LastScheduleMethod = method;
    std::memcpy(&self->LastScheduleBits, &start, 8);
    self->Scheduled = start < 0 ? 0 : start; self->Playing = 1;
    if (self->Flags & StockScheduleThrowAfterStore) RaiseException(0xe2400012u, 0, 0, nullptr);
}
extern "C" void __cdecl StockReplayBody(StockPlayer* self, float delay, void* method) {
    ++self->ReplayCalls; self->LastMethod = method;
    std::uint32_t bits; std::memcpy(&bits, &delay, 4); self->LastDelayBits = bits;
    auto* source = self->AudioSource;
    if (!self->Active || source->Playing) return;
    StockAudioStop(source, nullptr); // actual game Replay CALL before DSP/reset/schedule
    source->Playing = 0; source->Time = 0;
    const auto start = self->Dsp + static_cast<double>(delay); Origin(self, start);
    StockSetPitch(source, 1.0f, nullptr); StockPlayScheduled(source, start, nullptr);
    if (self->Flags & StockPlayerReplayThrowAfterStore) RaiseException(0xe2400016u, 0, 0, nullptr);
}
extern "C" void __cdecl StockResumeBody(StockPlayer* self, float delay, void* method) {
    ++self->ResumeCalls; self->LastMethod = method;
    std::uint32_t bits; std::memcpy(&bits, &delay, 4); self->LastDelayBits = bits;
    auto* source = self->AudioSource;
    if (!self->Active || source->Playing) return;
    const auto saved = source->Time; StockAudioStop(source, nullptr);
    source->Playing = 0; source->Time = 0; StockSetTime(source, static_cast<float>(saved), nullptr);
    const auto start = self->Dsp + static_cast<double>(delay); Origin(self, start);
    StockSetPitch(source, 1.0f, nullptr); StockPlayScheduled(source, start, nullptr);
    Origin(self, start - source->Time);
    if (self->Flags & StockPlayerResumeThrowAfterStore) RaiseException(0xe2400017u, 0, 0, nullptr);
}
extern "C" void __cdecl StockSetClipBody(StockSource* self, void* value, void* method) {
    ++self->ClipCalls; self->LastClipMethod = method; self->Clip = value;
    if (self->Flags & StockClipThrowAfterStore) RaiseException(0xe2400013u, 0, 0, nullptr);
}
extern "C" void __cdecl StockProgressPlayBody(StockProgress* self, unsigned char fromBeginning, void* method, unsigned equal) {
    ++self->Calls; self->LastMethod = method; self->LastBoolBits = fromBeginning;
    self->LastEqual = static_cast<unsigned char>(equal);
    auto* player = self->Player;
    ++player->ProgressPlayCalls; player->LastMethod = method; player->LastBoolBits = fromBeginning;
    if (equal) return;
    ++self->BranchCalls;
    // Stock's proven inline Pause path bypasses MUGMusicPlayer.Pause.
    StockPauseLeaf(player->AudioSource, nullptr); ClearOrigin(player);
    self->FromBeginning = fromBeginning;
    if (self->Flags & StockProgressThrowAfterStore) RaiseException(0xe2400018u, 0, 0, nullptr);
}
void* __cdecl StockMethodGetTime() { return &getTime; }
void* __cdecl StockMethodSetPitch() { return &pitch; }
void* __cdecl StockMethodSetPitchAlias() { return &pitchAlias; }
void* __cdecl StockMethodPause() { return &pause; }
void* __cdecl StockMethodReplay() { return &replay; }
void* __cdecl StockMethodResume() { return &resume; }
void* __cdecl StockMethodProgressPlay() { return &progressPlay; }
void* __cdecl StockMethodPauseLeaf() { return &pauseLeaf; }
void* __cdecl StockMethodPlayScheduled() { return &schedule; }
void* __cdecl StockMethodSetClip() { return &clip; }
void* __cdecl StockMethodSetTime() { return &time; }
void* __cdecl StockMethodPlayerConstructor() { return &playerConstructor; }
void* __cdecl StockMethod(std::uint32_t role) {
    switch (role) {
    case 1: return &pitchAlias; case 2: return &pause; case 3: return &replay;
    case 4: return &resume; case 5: return &schedule; case 6: return &clip;
    case 7: return &progressPlay; case 8: return &pauseLeaf; case 10: return &time;
    case 12: return &playerConstructor;
    case 13: return &audioPlayHelper; case 14: return &audioPlayDelayed; case 15: return &audioPlay;
    case 16: return &audioStop; case 17: return &audioStopBoolean; case 18: return &audioSetLoop;
#ifdef RIZ_STOCK_PROGRESS_RESOURCE_PROFILE
    case 19: return StockMethodProgressClip();
#endif
    default: return nullptr;
    }
}
std::uint32_t __cdecl StockFixtureGetBodyLength(std::uint32_t role) {
    constexpr std::uint32_t lengths[]{640,320,160,560,720,96,80,272,64,13,80};
#ifdef RIZ_STOCK_PROGRESS_RESOURCE_PROFILE
    if (role == 19) return 13;
#endif
    return role == 12 ? 64 : role >= 13 && role <= 18 ? (role==15 || role==16 ? 64u:80u) : role <= 10 ? lengths[role] : 0;
}
StockSource* __cdecl StockFixtureCreateSource() {
    auto* source = new(std::nothrow) StockSource{};
    if (source) { source->Class = &sourceClass; source->Pitch = 1; source->Active = 1; }
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
    if (source && !StockFixtureUnityInitializeWrapper(source, StockUnitySourceStorage)) { delete source; return nullptr; }
#endif
    return source;
}
StockPlayer* __cdecl StockFixtureCreatePlayer(StockSource* source) {
    auto* player = new(std::nothrow) StockPlayer{};
    if (player) { player->Class = &playerClass; player->AudioSource = source; player->Active = 1; player->Dsp = 100; }
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
    if (player && !StockFixtureUnityRegisterStorage(player, StockUnityPlayerStorage)) { delete player; return nullptr; }
#endif
    return player;
}
StockPlayer* __cdecl StockFixtureCreateForeignSharedReceiver(std::uint32_t classIndex, void* initialReference) {
    if (classIndex >= 61) return nullptr;
    auto* receiver = new(std::nothrow) StockPlayer{};
    if (receiver) { receiver->Class = &foreignSharedClasses[classIndex]; receiver->AudioSource = static_cast<StockSource*>(initialReference); }
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
    if (receiver && !StockFixtureUnityRegisterStorage(receiver, StockUnityPlayerStorage)) { delete receiver; return nullptr; }
#endif
    return receiver;
}
void __cdecl StockFixtureConfigureConstructor(std::uint32_t flags, StockConstructorBoundary boundary) {
    InterlockedExchange(&constructorFlags, static_cast<LONG>(flags)); constructorBoundary = boundary;
}
std::uint64_t __cdecl StockFixtureConstructorRead(std::uint32_t field) {
    switch (field) {
    case 0: return static_cast<std::uint64_t>(InterlockedCompareExchange(&constructorBeforeCalls, 0, 0));
    case 1: return static_cast<std::uint64_t>(InterlockedCompareExchange(&constructorAfterCalls, 0, 0));
    case 2: return reinterpret_cast<std::uint64_t>(constructorLastSelf);
    case 3: return reinterpret_cast<std::uint64_t>(constructorLastReference);
    case 4: return reinterpret_cast<std::uint64_t>(constructorLastMethod);
    case 5: return reinterpret_cast<std::uint64_t>(constructorLastBeforeValue);
    case 6: return reinterpret_cast<std::uint64_t>(constructorLastAfterValue);
    case 7: return constructorLastThread;
    default: return 0;
    }
}
StockProgress* __cdecl StockFixtureCreateProgress(StockPlayer* player) {
    auto* progress = new(std::nothrow) StockProgress{};
    if (progress) { progress->Class = &progressClass; progress->Player = player; progress->AudioSource = player->AudioSource; }
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
    if (progress && !StockFixtureUnityRegisterStorage(progress, StockUnityProgressStorage)) { delete progress; return nullptr; }
#endif
    return progress;
}
void __cdecl StockFixtureConfigure(StockSource* source, std::uint32_t flags) {
    if (!pitchEntered) pitchEntered = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!pitchRelease) pitchRelease = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    ResetEvent(pitchEntered); ResetEvent(pitchRelease); source->Flags = flags;
}
std::uint32_t __cdecl StockFixtureWaitPitch() { return WaitForSingleObject(pitchEntered, 10000); }
void __cdecl StockFixtureReleasePitch() { SetEvent(pitchRelease); }
int __cdecl StockFixtureInvoke(std::uint32_t kind, void* target, void* self, std::uint64_t arg,
    void* method, std::uint32_t* exceptionCode) {
    if (!target || !exceptionCode) return 0;
    __try {
        switch (kind) {
        case 1: reinterpret_cast<void(__cdecl*)(void*,void*)>(target)(self,method); break;
        case 2: { float value; const auto bits = static_cast<std::uint32_t>(arg); std::memcpy(&value,&bits,4);
            reinterpret_cast<void(__cdecl*)(void*,float,void*)>(target)(self,value,method); break; }
        case 3: { double value; std::memcpy(&value,&arg,8); reinterpret_cast<void(__cdecl*)(void*,double,void*)>(target)(self,value,method); break; }
        case 4: reinterpret_cast<void(__cdecl*)(void*,void*,void*)>(target)(self,reinterpret_cast<void*>(arg),method); break;
        case 5: reinterpret_cast<void(__cdecl*)(void*,unsigned char,void*)>(target)(self,static_cast<unsigned char>(arg),method); break;
        case 6: static_cast<void>(reinterpret_cast<float(__cdecl*)(void*,void*)>(target)(self,method)); break;
        // Fixture caller kind8 is UInt64. kind7 remains the machine caller's
        // BooleanReturn0 selector; neither is the Source protocol kind enum.
        case 8: reinterpret_cast<void(__cdecl*)(void*,std::uint64_t,void*)>(target)(self,arg,method); break;
        default: return 0;
        }
        *exceptionCode = 0; return 1;
    } __except(EXCEPTION_EXECUTE_HANDLER) { *exceptionCode = GetExceptionCode(); return 2; }
}

extern "C" __declspec(dllexport) void __cdecl StockFixtureBlockGetTime() {
    if (!getTimeEntered) getTimeEntered = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!getTimeRelease) getTimeRelease = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    ResetEvent(getTimeEntered); ResetEvent(getTimeRelease); InterlockedExchange(&blockGetTime, 1);
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl StockFixtureWaitGetTime() { return WaitForSingleObject(getTimeEntered, 10000); }
extern "C" __declspec(dllexport) void __cdecl StockFixtureReleaseGetTime() { InterlockedExchange(&blockGetTime, 0); SetEvent(getTimeRelease); }

namespace {
void DirectStore(StockSource* self,std::uint32_t role,std::uint64_t bits,void* method) {
    if(!self || role<13 || role>18) RaiseException(0xE2420100,0,0,nullptr);
    auto& record=directRecords[role-13];++record.Before;
    record.Self=reinterpret_cast<std::uint64_t>(self);record.Argument=bits;
    record.Method=reinterpret_cast<std::uint64_t>(method);record.Thread=GetCurrentThreadId();
    if(directBoundary)directBoundary(self,role,bits,method,1);
    if(directFlags&1)RaiseException(0xE2420101,0,0,nullptr);
    ++self->DirectCalls;
    if(role<=15)self->Playing=1;
    else if(role<=17)self->Playing=0;
    else self->LoopRaw=static_cast<unsigned char>(bits);
    // This owned model records delay bits rather than emulating an engine's
    // opaque scheduling conversion or mixer progression.
    ++record.After;
    if(directBoundary)directBoundary(self,role,bits,method,2);
    if(directFlags&2)RaiseException(0xE2420102,0,0,nullptr);
}
}
extern "C" void __cdecl StockAudioPlayHelperBody(StockSource* s,std::uint64_t b,void* m) {DirectStore(s,13,b,m);}
extern "C" void __cdecl StockAudioPlayDelayedBody(StockSource* s,double v,void* m) {std::uint64_t b;std::memcpy(&b,&v,8);DirectStore(s,14,b,m);}
extern "C" void __cdecl StockAudioPlayBody(StockSource* s,void* m) {DirectStore(s,15,0,m);}
extern "C" void __cdecl StockAudioStopBody(StockSource* s,void* m) {DirectStore(s,16,0,m);}
extern "C" void __cdecl StockAudioStopBooleanBody(StockSource* s,unsigned char v,void* m) {DirectStore(s,17,v,m);}
extern "C" void __cdecl StockAudioSetLoopBody(StockSource* s,unsigned char v,void* m) {DirectStore(s,18,v,m);}
void* __cdecl StockMethodAudioPlayHelper(){return &audioPlayHelper;}
void* __cdecl StockMethodAudioPlayDelayed(){return &audioPlayDelayed;}
void* __cdecl StockMethodAudioPlay(){return &audioPlay;}
void* __cdecl StockMethodAudioStop(){return &audioStop;}
void* __cdecl StockMethodAudioStopBoolean(){return &audioStopBoolean;}
void* __cdecl StockMethodAudioSetLoop(){return &audioSetLoop;}
void __cdecl StockFixtureConfigureDirect(std::uint32_t flags,StockDirectBoundary boundary){directFlags=flags;directBoundary=boundary;}
std::uint64_t __cdecl StockFixtureDirectRead(std::uint32_t role,std::uint32_t field){
    if(role<13 || role>18)return 0;const auto& record=directRecords[role-13];
    switch(field){case 0:return record.Before;case 1:return record.After;case 2:return record.Self;case 3:return record.Argument;case 4:return record.Method;case 5:return record.Thread;default:return 0;}
}
