#pragma once
#include <cstdint>
#ifdef RIZ_PLAYBACK_STOCK_EXPORTS
#define RIZ_STOCK_API extern "C" __declspec(dllexport)
#else
#define RIZ_STOCK_API extern "C" __declspec(dllimport)
#endif

// Owned native storage and stock-shaped control flow, not installed game code.
struct StockSource {
    void* Class; void* Monitor;
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
    // This member exists only in the separately issued Unity-backed profile.
    // The historical owned fixture has Pitch at +0x10 and does not model a
    // UnityEngine.Object wrapper or a Unity native-object lifetime.
    void* CachedPtr;
#endif
    float Pitch; std::uint32_t PitchCalls;
    double Time, Scheduled;
    void* Clip;
    std::uint32_t Playing, Active, PauseCalls, ScheduleCalls, ClipCalls, Flags;
    void* LastPitchMethod; void* LastScheduleMethod; void* LastPauseMethod; void* LastClipMethod;
    std::uint64_t LastPitchBits, LastScheduleBits;
    std::uint32_t TimeCalls, TimeReserved; void* LastTimeMethod; std::uint64_t LastTimeBits;
    std::uint32_t DirectCalls, LoopRaw;
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
    std::uint64_t GetTimeOriginalCalls;
    void* LastGetTimeMethod;
#endif
};
#ifdef RIZ_STOCK_UNITY_BACKING_PROFILE
struct StockUnityClass {
    unsigned char Prefix[0xb8];
    void* StaticFields;
};
struct StockUnityClip {
    void* Class; void* Monitor; void* CachedPtr;
    float Length; std::uint32_t Reserved;
    std::uint64_t Tag;
};
// The first three operations preserve both the wrapper and its exact class.
// Detached/replaced allocations remain owned until known fixture cleanup, so
// pointer replacement cannot accidentally obtain the same allocation address.
enum StockUnityBackingMutation : std::uint32_t {
    StockUnityDetach = 1, StockUnityReplace = 2,
    StockUnityReattach = 3, StockUnityChangeInstanceId = 4
};
enum StockUnityStorageKind : std::uint32_t {
    StockUnitySourceStorage = 1, StockUnityClipStorage = 2,
    StockUnityClassStorage = 3, StockUnityPlayerStorage = 4,
    StockUnityProgressStorage = 5, StockUnityStartStorage = 6,
    StockUnityResourceStorage = 7
};
RIZ_STOCK_API int __cdecl StockFixtureUnityRegisterStorage(void*, std::uint32_t kind);
RIZ_STOCK_API int __cdecl StockFixtureUnityInitializeWrapper(void*, std::uint32_t kind);
RIZ_STOCK_API StockUnityClip* __cdecl StockFixtureUnityCreateClip(float length, std::int32_t instanceId, std::uint64_t tag);
RIZ_STOCK_API StockUnityClass* __cdecl StockFixtureUnityCreateClass(std::uint32_t staticBytes, std::int32_t firstStaticInt32);
RIZ_STOCK_API int __cdecl StockFixtureUnityMutate(void* wrapper, std::uint32_t kind, std::uint32_t operation, std::int32_t instanceId);
RIZ_STOCK_API int __cdecl StockFixtureUnityDestroy(void* storage, std::uint32_t kind);
RIZ_STOCK_API std::uint64_t __cdecl StockFixtureUnityRead(std::uint32_t field);
RIZ_STOCK_API float __cdecl StockFixtureNativeGetPitch(StockSource* source);
#endif
struct StockPlayer {
    void* Class; void* Monitor; StockSource* AudioSource;
    unsigned char OriginHasValue; unsigned char Reserved[7]; double Origin;
    std::uint32_t Active, Flags; double Dsp;
    std::uint32_t ReplayCalls, ResumeCalls, PauseCalls, ProgressPlayCalls;
    std::uint64_t LastDelayBits, LastBoolBits; void* LastMethod;
};
// Progress is a distinct object. The first compare in StockProgressPlay really
// reads this byte; a music-player pointer is not interchangeable with it.
struct StockProgress {
    void* Class; void* Monitor;
    unsigned char Reserved[0x28 - 0x10];
    StockSource* AudioSource; void* ClipReference; StockPlayer* Player;
    unsigned char ReservedFlags[2];
    unsigned char FromBeginning, LastEqual;
    std::uint32_t Calls, BranchCalls, Flags;
    std::uint64_t LastBoolBits; void* LastMethod;
};
struct StockMachineState {
    std::uint64_t Nonvolatile[8], Flags, Reserved;
    unsigned char Xmm[160];
};
enum StockSourceFlags : std::uint32_t {
    StockPitchThrowAfterStore = 1, StockPitchNestedEqual = 2,
    StockScheduleThrowAfterStore = 4, StockClipThrowAfterStore = 8,
    StockPitchBlock = 16, StockPauseThrowAfterStore = 32, StockTimeThrowAfterStore = 64
};
enum StockPlayerFlags : std::uint32_t {
    StockPlayerPauseThrowAfterStore = 1, StockPlayerReplayThrowAfterStore = 2,
    StockPlayerResumeThrowAfterStore = 4, StockProgressThrowAfterStore = 8
};
enum StockConstructorFlags : std::uint32_t {
    StockConstructorThrowBeforeStore = 1, StockConstructorThrowAfterStore = 2
};
using StockConstructorBoundary = void(__cdecl*)(void* self, void* reference, void* incomingMethod, std::uint32_t at);
RIZ_STOCK_API void __cdecl StockPlayerConstructor(void* self, void* reference, void* incomingMethod);
RIZ_STOCK_API void* __cdecl StockMethodPlayerConstructor();
RIZ_STOCK_API StockPlayer* __cdecl StockFixtureCreateForeignSharedReceiver(std::uint32_t classIndex, void* initialReference);
RIZ_STOCK_API void __cdecl StockFixtureConfigureConstructor(std::uint32_t flags, StockConstructorBoundary boundary);
RIZ_STOCK_API std::uint64_t __cdecl StockFixtureConstructorRead(std::uint32_t field);
RIZ_STOCK_API float __cdecl StockGetTime(StockPlayer* self, void* method);
RIZ_STOCK_API void __cdecl StockSetTime(StockSource* self, float time, void* method);
RIZ_STOCK_API void __cdecl StockSetPitch(StockSource* self, float pitch, void* method);
RIZ_STOCK_API void __cdecl StockPause(StockPlayer* self, void* method);
RIZ_STOCK_API void __cdecl StockReplay(StockPlayer* self, float delay, void* method);
RIZ_STOCK_API void __cdecl StockResume(StockPlayer* self, float delay, void* method);
RIZ_STOCK_API void __cdecl StockPlayScheduled(StockSource* self, double start, void* method);
RIZ_STOCK_API void __cdecl StockSetClip(StockSource* self, void* clip, void* method);
RIZ_STOCK_API void __cdecl StockProgressPlay(StockProgress* self, unsigned char fromBeginning, void* method);
RIZ_STOCK_API void __cdecl StockPauseLeaf(StockSource* self, void* method);
RIZ_STOCK_API void* __cdecl StockMethodGetTime();
RIZ_STOCK_API void* __cdecl StockMethodSetPitch();
RIZ_STOCK_API void* __cdecl StockMethodSetPitchAlias();
RIZ_STOCK_API void* __cdecl StockMethodPause();
RIZ_STOCK_API void* __cdecl StockMethodReplay();
RIZ_STOCK_API void* __cdecl StockMethodResume();
RIZ_STOCK_API void* __cdecl StockMethodProgressPlay();
RIZ_STOCK_API void* __cdecl StockMethodPauseLeaf();
RIZ_STOCK_API void* __cdecl StockMethodPlayScheduled();
RIZ_STOCK_API void* __cdecl StockMethodSetClip();
RIZ_STOCK_API void* __cdecl StockMethodSetTime();
RIZ_STOCK_API void* __cdecl StockMethod(std::uint32_t role);
RIZ_STOCK_API std::uint32_t __cdecl StockFixtureGetBodyLength(std::uint32_t role);
RIZ_STOCK_API StockSource* __cdecl StockFixtureCreateSource();
RIZ_STOCK_API StockPlayer* __cdecl StockFixtureCreatePlayer(StockSource* source);
RIZ_STOCK_API StockProgress* __cdecl StockFixtureCreateProgress(StockPlayer* player);
RIZ_STOCK_API void __cdecl StockFixtureConfigure(StockSource* source, std::uint32_t flags);
RIZ_STOCK_API std::uint32_t __cdecl StockFixtureWaitPitch();
RIZ_STOCK_API void __cdecl StockFixtureReleasePitch();
RIZ_STOCK_API int __cdecl StockFixtureInvoke(std::uint32_t kind, void* target, void* self,
    std::uint64_t argBits, void* method, std::uint32_t* exceptionCode);
RIZ_STOCK_API void __cdecl StockFixtureMachineInvoke(std::uint32_t kind, void* target, void* self,
    std::uint64_t argBits, void* method, StockMachineState* before, StockMachineState* after);

// Separate physical wrappers: each reaches its own body, never another public
// entry. Raw arguments are recorded before this fixture's simple state store.
using StockDirectBoundary = void(__cdecl*)(void*,std::uint32_t,std::uint64_t,void*,std::uint32_t);
RIZ_STOCK_API void __cdecl StockAudioPlayHelper(StockSource*,std::uint64_t,void*);
RIZ_STOCK_API void __cdecl StockAudioPlayDelayed(StockSource*,double,void*);
RIZ_STOCK_API void __cdecl StockAudioPlay(StockSource*,void*);
RIZ_STOCK_API void __cdecl StockAudioStop(StockSource*,void*);
RIZ_STOCK_API void __cdecl StockAudioStopBoolean(StockSource*,unsigned char,void*);
RIZ_STOCK_API void __cdecl StockAudioSetLoop(StockSource*,unsigned char,void*);
RIZ_STOCK_API void* __cdecl StockMethodAudioPlayHelper();
RIZ_STOCK_API void* __cdecl StockMethodAudioPlayDelayed();
RIZ_STOCK_API void* __cdecl StockMethodAudioPlay();
RIZ_STOCK_API void* __cdecl StockMethodAudioStop();
RIZ_STOCK_API void* __cdecl StockMethodAudioStopBoolean();
RIZ_STOCK_API void* __cdecl StockMethodAudioSetLoop();
RIZ_STOCK_API void __cdecl StockFixtureConfigureDirect(std::uint32_t,StockDirectBoundary);
RIZ_STOCK_API std::uint64_t __cdecl StockFixtureDirectRead(std::uint32_t,std::uint32_t);
