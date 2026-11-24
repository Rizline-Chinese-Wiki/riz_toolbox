#pragma once
// Independent source protocol V4: the exact Start BooleanReturn0 parent.
// Frame/info are 168/136; earlier helpers/frames are rejected. Not foundation V7.
#include <cstdint>
#include "../PlaybackInitialization.h"

#ifdef RIZ_PLAYBACK_SOURCE_EXPORTS
#define RIZ_SOURCE_API extern "C" __declspec(dllexport)
#else
#define RIZ_SOURCE_API extern "C" __declspec(dllimport)
#endif

enum RtPlaybackSourceKind : std::uint32_t {
    RtSourceVoid0 = 1, RtSourceSingle1 = 2, RtSourceDouble1 = 3,
    RtSourceRef1 = 4, RtSourceBool1 = 5, RtSourceBooleanReturn0 = 6,
    RtSourceUInt64_1 = 7, // RCX self, RDX unchanged UInt64, R8 MethodInfo.
    RtSourceIcallSingle1 = 8 // RCX wrapper (role20/22) or native backing (role21), no MethodInfo.
};
enum RtPlaybackSourceRole : std::uint32_t {
    RtSourceSetPitch = 1, RtSourcePause = 2, RtSourceReplay = 3,
    RtSourceResume = 4, RtSourcePlayScheduled = 5, RtSourceSetClip = 6,
    RtSourceProgressPlay = 7, RtSourcePauseLeaf = 8, RtSourceProgressMusicPlayer = 9, RtSourceSetTime = 10, RtSourceProgressStartMoveNext = 11, RtSourcePlayerConstructor = 12,
    RtSourceAudioPlayHelper = 13, RtSourceAudioPlayDelayed = 14, RtSourceAudioPlay = 15,
    RtSourceAudioStop = 16, RtSourceAudioStopBoolean = 17, RtSourceAudioSetLoop = 18,
    RtSourceProgressClip = 19, // Reference-only Progress+0x30; no current-source fence.
    RtSourceLowerSetPitch = 20,
    RtSourceEngineSetPitch = 21, // Native backing self, never an IL2CPP object.
    RtSourceLowerSetTime = 22
};
enum RtPlaybackSourceStage : std::uint32_t {
    RtSourceBefore = 1, RtSourceOriginalReturned = 2, RtSourceAbnormal = 3,
    RtSourcePhysicalExit = 4 // Journal only; never delivered as an observer callback.
};
enum RtPlaybackSourceFlags : std::uint32_t {
    RtSourceArmed = 1, RtSourceDormant = 2, RtSourceUnknown = 4,
    RtSourceReturnedFlag = 8, RtSourceAbnormalFlag = 16,
    RtSourcePhysicalExitedFlag = 32, RtSourceWithdrawn = 64,
    RtSourceStockStopFlag = 128, // Private StockStop V1; actual first child only.
    // Private frame-only evidence of a journaled, known resource initializer
    // responsibility while the logical gate is closed. Never an owner flag.
    RtSourceInitializationResponsibilityFlag = 256,
    // Native exact wrapper-original CALL window consumed this one physical child.
    // It is not another semantic leaf or writer reservation.
    RtSourceLowerPitchChildFlag = 512,
    RtSourceEnginePitchChildFlag = 1024,
    RtSourceLowerTimeChildFlag = 2048
};

#pragma pack(push, 8)
struct RtPlaybackSourceFrame {
    std::uint32_t Version, Size, Kind, Role, Slot, Stage, ThreadId, Depth;
    std::uint64_t Owner, OwnerEpoch, Sequence, Token, ParentSequence;
    std::uint64_t Self, IncomingMethod, Caller, ArgBits;
    std::uint64_t WriterSequence, SourceSequence, SchedulingSequence;
    std::uint64_t Entered, Exited, InFlight;
    std::uint32_t Flags, Reserved;
    // Return evidence is distinct from the immutable argument bits.
    std::uint32_t RawReturnByte, ReturnPresent;
};
struct RtPlaybackSourceInfo {
    std::uint32_t Version, Size, Kind, Role, Slot, Flags, OwnerThreadId, Reserved;
    std::uint64_t Owner, OwnerEpoch, Target, Original, Entry;
    std::uint64_t Entered, Exited, InFlight;
    std::uint64_t WriterSequence, SourceSequence, SchedulingSequence, Faults;
    std::uint32_t JournalCapacity, ConfiguredSlots;
};
#pragma pack(pop)
static_assert(sizeof(RtPlaybackSourceFrame) == 168, "Source frame wire size");
static_assert(sizeof(RtPlaybackSourceInfo) == 136, "Source info wire size");

using RtPlaybackSourceObserver = void(__cdecl*)(void*, const RtPlaybackSourceFrame*);
using RtPlaybackProgressPlayEnter = std::uintptr_t(__cdecl*)(void*, void*, std::uint32_t, void*, void*, std::uint64_t);
using RtPlaybackProgressPlayExit = void(__cdecl*)(void*, void*, std::uint32_t, void*, void*, std::uint64_t, std::uintptr_t, std::uint32_t);
struct RtPlaybackProgressPlayObserverV1 {
    std::uint32_t Version, Size, Slot, Reserved;
    std::uint64_t OwnerEpoch;
    void* Target;
    void* Original;
    void* Entry;
    RtPlaybackProgressPlayEnter Enter;
    RtPlaybackProgressPlayExit Exit;
    void* Context;
};
static_assert(sizeof(RtPlaybackProgressPlayObserverV1) == 72, "Shared physical Play observer wire size");
RIZ_SOURCE_API int __cdecl RtPlaybackSourceBindProgressPlayObserverV1(void* owner, const RtPlaybackProgressPlayObserverV1* observer);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceBindDormantProgressPlayObserverV1(void* owner, const RtPlaybackProgressPlayObserverV1* observer);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceVerifyProgressPlayObserverV1(void* owner, const RtPlaybackProgressPlayObserverV1* observer);
// The promotion gate returns 2 for a known in-flight source original, 1 for
// a reservation and 0 for invalid evidence. End releases any waiting new Play.
RIZ_SOURCE_API int __cdecl RtPlaybackSourceBeginDormantPlayPromotionV1(void* owner, std::uint64_t epoch);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceEndDormantPlayPromotionV1(void* owner, std::uint64_t epoch, int completed);
// All other int exports return exactly 1 on success and 0 on rejection; failed reads
// leave output untouched. No export accepts fabricated events or completions.
// Configure permanently consumes the physical slot even when it fails.
// SetArmed(false) permanently withdraws. MarkFault is sticky across all slots.
// Receipt release acknowledges only an existing exact physical completion.
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackSourceVersion();
RIZ_SOURCE_API void* __cdecl RtPlaybackSourceCreateOwner(RtPlaybackSourceObserver observer, void* context);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceConfigure(void* owner, std::uint32_t kind, std::uint32_t slot,
    std::uint32_t role, void* target, void* original);
RIZ_SOURCE_API void* __cdecl RtPlaybackSourceGetEntry(std::uint32_t kind, std::uint32_t slot);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceSetArmed(void* owner, std::uint64_t epoch, int armed);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceMarkFault(void* owner, std::uint64_t epoch);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceGetInfo(std::uint32_t kind, std::uint32_t slot, RtPlaybackSourceInfo* output);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadOwner(void* owner, RtPlaybackSourceInfo* output);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadCurrent(std::uint32_t kind, std::uint32_t slot, RtPlaybackSourceFrame* output);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceVerifyCurrent(void* owner, const RtPlaybackSourceFrame* frame);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadReceipt(void* owner, std::uint64_t sequence,
    std::uint64_t token, RtPlaybackSourceFrame* output);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReleaseReceipt(void* owner, std::uint64_t sequence, std::uint64_t token);

// Private lower-time V1 binds actual registered channels before the first Arm.
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackLowerTimeVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceConfigureLowerTime(void* owner,
    std::uint32_t wrapperSlot, std::uint32_t lowerSlot);

#pragma pack(push, 8)
struct RtPlaybackAudioInterruptionInfoV1 {
    std::uint32_t Version, Size, Invalidated, Reason, FenceKind, AttemptRole, ActualFlags, Reserved;
    std::uint64_t Owner, OwnerEpoch, Initializer, InitializerOrdinal, Enumerator, Progress, YieldSequence;
    std::uint64_t AttemptSequence, AttemptToken, Writer, Source, Scheduling, ActualStamp, PlayerWatchReceipt;
};
#pragma pack(pop)
static_assert(sizeof(RtPlaybackAudioInterruptionInfoV1) == 144, "Audio interruption V1 wire size");
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackAudioInterruptionVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadAudioInterruption(void* owner, void* enumerator,
    std::uint64_t yieldSequence, RtPlaybackAudioInterruptionInfoV1* info);
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackSourceRetryAudioInterruptedInitialization(void* logicalGate,
    void* priorInitializer, void* freshInitializer, std::uint32_t action, void* request);

// Orthogonal lower-pitch V1. Slot identities must be this owner's actual,
// immutable SetPitch/LowerSetPitch registrations, bound before its first Arm.
// No API accepts a caller event, completion, delegation flag or clear request.
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackLowerPitchVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceConfigureLowerPitch(void* owner,
    std::uint32_t wrapperSlot, std::uint32_t lowerSlot);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadLowerPitchInitializationInvalidation(void* owner,
    void* enumerator, std::uint64_t yieldSequence, RtPlaybackLowerPitchInitializationInfoV1* info);
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackSourceRetryLowerPitchInitialization(void* logicalGate,
    void* priorInitializer, void* freshInitializer, std::uint32_t action, void* request);

// Engine pitch V1 is orthogonal to the unchanged source V4 frame. Configure
// validates a real E8 CALL in the 495-byte registered lower function. It cannot
// issue a child: only the helper's actual lower-original TLS window can do so.
// Association data is diagnostic; no input DTO or raw address grants authority.
// Kind: 1 active backing watch, 2 pending watch, 3 retained actual initializer.
// State: 1 exact fresh identity, 2 readable backing boundary, 3 physical Unknown.
#pragma pack(push, 8)
struct RtPlaybackEnginePitchAssociationInfoV1 {
    std::uint32_t Version, Size, Kind, State;
    std::uint64_t OwnerAddress, OwnerEpoch, Sequence, Token, Wrapper, WrapperClass,
        NativeSelf, Receipt, Ordinal;
    std::int32_t InstanceIdOffset, SourceId;
};
#pragma pack(pop)
static_assert(sizeof(RtPlaybackEnginePitchAssociationInfoV1) == 96, "Engine pitch association V1 wire size");
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackEnginePitchVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceConfigureEnginePitch(void* owner,
    std::uint32_t lowerSlot, std::uint32_t engineSlot, void* exactCallReturn);
// Only the actual active callback scope can read its private association.
// Output is untouched on rejection, including copied frames outside that scope.
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadEnginePitchAssociation(void* owner,
    std::uint64_t sequence, std::uint64_t token, RtPlaybackEnginePitchAssociationInfoV1* info);

// The fixed PlayScheduled original may reapply its current native pitch. This
// separate pre-Arm binding grants no child outside that original's TLS window.
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackScheduledEnginePitchVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceConfigureScheduledEnginePitch(void* owner,
    std::uint32_t scheduleSlot, std::uint32_t engineSlot, void* exactEngineCallReturn,
    void* reapplyBody, std::uint32_t reapplyLength, void* playCache, void* nativePlayTarget);

// Orthogonal private PlayerWatch V2. Source below remains the source fence.
#pragma pack(push, 8)
struct RtPlaybackPlayerWatchInfoV2 {
    std::uint32_t Version, Size, Phase, Active;
    std::uint64_t OwnerAddress, OwnerEpoch, Receipt, Ordinal, Player, Class,
        Expected, Cut, Writer, Source, Scheduling, AudioSource, AudioClass;
};
#pragma pack(pop)
static_assert(sizeof(RtPlaybackPlayerWatchInfoV2) == 120, "PlayerWatch V2 wire size");
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackPlayerWatchVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceConfigurePlayerWatch(void* owner, void* playerClass, void* audioClass);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceStagePlayerWatch(void* owner, void* player, void* audioSource, void** receipt);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadPlayerWatch(void* owner, void* receipt, RtPlaybackPlayerWatchInfoV2* info);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceActivateInitialPlayerWatch(void* owner, void* receipt);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceRetirePlayerWatch(void* owner, void* receipt);

// Unity-backed profiles additionally bind this private, independently retained
// source/clip watch. A projection or a copied pointer pair is never its receipt.
#pragma pack(push, 8)
struct RtPlaybackUnityBackingInfoV1 {
    std::uint32_t Version, Size, Phase, Current;
    std::uint64_t OwnerAddress, OwnerEpoch, Receipt, Ordinal, PlayerWatchReceipt,
        Source, SourceClass, SourceNative, Clip, ClipClass, ClipNative, SourceFence;
    std::int32_t InstanceIdOffset, SourceId, ClipId;
    std::uint32_t Reason;
};
#pragma pack(pop)
static_assert(sizeof(RtPlaybackUnityBackingInfoV1) == 128, "Unity backing watch V1 wire size");
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackUnityBackingWatchVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceConfigureUnityBackingWatch(void* owner, void* objectClass,
    void* clipClass, std::int32_t instanceIdOffset);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceStageUnityBackingWatch(void* owner, void* playerWatchReceipt,
    void* independentlyRootedClip, void** receipt);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadUnityBackingWatch(void* owner, void* receipt, RtPlaybackUnityBackingInfoV1* info);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceRetireUnityBackingWatch(void* owner, void* receipt);
// The query matches native-owned initializer records by the actual retained
// enumerator and its yield sequence, including an older replaced initializer.
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadBackingInitializationInvalidation(void* owner,
    void* enumerator, std::uint64_t yieldSequence, RtPlaybackBackingInitializationInfoV1* info);
// Only called by the bound SingleReturn coordinator with authorityGate held.
// Its own native badge certifies action12/13; resource-write proof stays separate.
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackSourceRetryBackingInitialization(void* logicalGate,
    void* priorInitializer, void* freshInitializer, std::uint32_t action, void* request);

// Independent one-time protocol; it grants no Clock or publication by itself.
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackStockStopVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceConfigureStockStop(void* owner,
    void* replayCaller, void* resumeCaller, void* replayMethod, void* resumeMethod);

// Orthogonal private ProgressWatch V1. It borrows an actual four-root host or
// seven-root source Progress handle; it grants no source/Clock completion.
#pragma pack(push, 8)
struct RtPlaybackProgressWatchInfoV1 {
    std::uint32_t Version, Size, Phase, Active;
    std::uint64_t OwnerAddress, OwnerEpoch, Receipt, Ordinal, Progress, Class,
        Expected, PlayerWatchReceipt, Player, AudioSource;
};
#pragma pack(pop)
static_assert(sizeof(RtPlaybackProgressWatchInfoV1) == 96, "ProgressWatch V1 wire size");
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackProgressWatchVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceConfigureProgressWatch(void* owner, void* progressClass, void* startClass);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceBindHostProgressWatch(void* owner, void* progress, void** receipt);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceStageProgressWatch(void* owner, void* progress, void* playerWatchReceipt, void** receipt);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadProgressWatch(void* owner, void* receipt, RtPlaybackProgressWatchInfoV1* info);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceRetireProgressWatch(void* owner, void* receipt);

// Orthogonal private Progress resource continuity V1. Existing source/watch
// layouts stay unchanged. Reference is diagnostic pointer bits, never a root or
// authority: the private managed issuer independently roots it before Stage.
// Phase: 0 state projection, 1 future staged, 2 actual initializer bound,
// 3 consumed by source-open, 4 invalidated, 5 retired. Records are never reused.
#pragma pack(push, 8)
struct RtPlaybackProgressResourceInfoV1 {
    std::uint32_t Version, Size, Phase, Current;
    std::uint64_t OwnerAddress, OwnerEpoch, Receipt, Ordinal, ProgressWatchReceipt,
        Progress, Class, ContinuityIdentity, Revision, Reference, Initializer, InitializerOrdinal;
};
#pragma pack(pop)
static_assert(sizeof(RtPlaybackProgressResourceInfoV1) == 112, "ProgressResource V1 wire size");
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackProgressResourceVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadProgressResource(void* owner, void* watchReceipt,
    RtPlaybackProgressResourceInfoV1* info);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceStageProgressResource(void* owner, void* watchReceipt,
    std::uint64_t expectedRevision, void* independentlyRootedReference, void** receipt);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceReadProgressResourceTicket(void* owner, void* receipt,
    RtPlaybackProgressResourceInfoV1* info);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceBindInitializationResource(void* owner, void* receipt);
RIZ_SOURCE_API int __cdecl RtPlaybackSourceRetireProgressResource(void* owner, void* receipt);
// Called only by the GetTime native coordinator while it holds authorityGate.
// action12/13 mint a fresh first-capture/handoff receipt in that coordinator;
// the old initializer and old receipt remain immutable invalid tombstones.
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackSourceRetryResourceInitialization(void* logicalGate,
    void* priorInitializer, void* freshInitializer, std::uint32_t action, void* request);

// Independent writer subprotocol V1. Its 224-byte packet is unchanged.
// A reservation is process-retained, once-only and bound to a real root/current
// return or physical journal, optionally the exact private predecessor attempt.
#pragma pack(push, 8)
struct RtPlaybackWriterInfo {
    std::uint32_t Version, Size, State, Reserved;
    std::uint64_t Owner, OwnerEpoch, Reservation, Nonce, RootSequence, RootToken;
    std::uint64_t Predecessor, PredecessorNonce;
    std::uint64_t Source, Target, Caller, IncomingMethod, ArgBits;
    std::uint64_t ExpectedWriter, ExpectedSource, ExpectedScheduling;
    std::uint64_t AttemptSequence, AttemptToken, ParentSequence;
    std::uint64_t PostWriter, PostSource, PostScheduling;
    std::uint32_t Returned, Abnormal, PhysicalExited, Contended, Current, ExceptionCode, Reserved0, Reserved1;
};
#pragma pack(pop)
static_assert(sizeof(RtPlaybackWriterInfo) == 224, "Independent writer v1 wire");
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackWriterVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackWriterReserve(void* owner, RtPlaybackWriterInfo* info);
// Separate issuer selects the installed SetTime/LowerSetTime pair and requires
// an actual physically exited paused root. The existing 224-byte wire stays V1.
RIZ_SOURCE_API std::uint32_t __cdecl RtPlaybackTimeWriterVersion();
RIZ_SOURCE_API int __cdecl RtPlaybackTimeWriterReserve(void* owner, RtPlaybackWriterInfo* info);
RIZ_SOURCE_API int __cdecl RtPlaybackWriterRun(void* reservation, std::uint64_t nonce, RtPlaybackWriterInfo* info);
RIZ_SOURCE_API int __cdecl RtPlaybackWriterRead(void* reservation, std::uint64_t nonce, RtPlaybackWriterInfo* info);
RIZ_SOURCE_API int __cdecl RtPlaybackWriterRelease(void* reservation, std::uint64_t nonce);
RIZ_SOURCE_API int __cdecl RtPlaybackWriterVerifyCurrent(void* reservation, std::uint64_t nonce,
    const RtPlaybackSourceFrame* frame, RtPlaybackWriterInfo* info);
