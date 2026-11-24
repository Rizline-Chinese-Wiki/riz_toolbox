#define RIZ_PLAYBACK_SOURCE_EXPORTS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <intrin.h>
#include <cstring>
#include <cmath>
#include <new>
#include "PlaybackSourceContext.h"
#include "../PlaybackLogicalHandoff.h"
#include "../PlaybackInitialization.h"
#include "../PlaybackFreshSource.h"
#include "../PlaybackFreshClock.h"
extern "C" void __cdecl RtPlaybackWriterCall(void*, void*, std::uint32_t, void*);
extern "C" const unsigned char RtPlaybackWriterCallReturned;
extern "C" void __cdecl RtPlaybackLowerPitchWrapperCall(void*, void*, std::uint32_t, void*);
extern "C" const unsigned char RtPlaybackLowerPitchWrapperCallReturned;
extern "C" void __cdecl RtPlaybackLowerTimeWrapperCall(void*, void*, std::uint32_t, void*);
extern "C" const unsigned char RtPlaybackLowerTimeWrapperCallReturned;

namespace {
constexpr std::uint32_t SlotCount = 32, OwnerCount = 8, JournalCount = 256;
struct JournalRow { std::uint32_t State; RtPlaybackSourceFrame Frame; };
struct WriterReservation;
struct LogicalGate;
struct Initialization;
struct PlayerWatch;
struct UnityBackingWatch;
struct ProgressWatch;
struct ProgressResourceState;
struct ProgressResourceTicket;
struct Owner {
    LONG Allocated;
    SRWLOCK Lock;
    RtPlaybackSourceObserver Observer;
    void* Context;
    DWORD Thread;
    std::uint64_t Epoch, Entered, Exited, InFlight, Writer, Source, Scheduling, Faults;
    std::uint32_t Flags, Slots;
    bool WasArmed;
    std::uint64_t DormantPlayInFlight;
    CONDITION_VARIABLE PlayPromotionChanged;
    bool PlayPromotion;
    bool LowerPitchEnabled, LowerPitchBlocked;
    bool LowerTimeEnabled, AudioBlocked;
    std::uint32_t LowerTimeWrapperSlot, LowerTimeLowerSlot;
    std::uint32_t LowerPitchWrapperSlot, LowerPitchLowerSlot;
    bool EnginePitchEnabled;
    std::uint32_t EnginePitchLowerSlot, EnginePitchEngineSlot;
    void* EnginePitchCaller;
    bool ScheduledEnginePitchEnabled;
    std::uint32_t ScheduledEnginePitchSlot, ScheduledEngineReapplyLength;
    void* ScheduledEnginePitchCaller; void* ScheduledEngineReapplyBody;
    void* ScheduledEnginePlayCache; void* ScheduledEngineNativePlay;
    LogicalGate* Logical;
    Initialization* Initializer;
    PlayerWatch* Watch; PlayerWatch* PendingWatch; PlayerWatch* WatchRecords;
    void* WatchClass; void* WatchAudioClass;
    UnityBackingWatch* BackingWatch; UnityBackingWatch* PendingBackingWatch; UnityBackingWatch* BackingRecords;
    std::uint64_t UnityObjectClass, UnityClipClass, BackingOrdinal;
    std::int32_t UnityInstanceIdOffset;
    bool BackingBlocked;
    ProgressWatch* ProgressWatchValue; ProgressWatch* PendingProgressWatch; ProgressWatch* ProgressWatchRecords;
    void* WatchProgressClass; void* WatchStartClass;
    std::uint64_t ProgressWatchOrdinal;
    ProgressResourceTicket* ResourceTickets;
    std::uint64_t ResourceStateOrdinal, ResourceTicketOrdinal;
    bool ResourceEnabled;
    std::uint64_t WatchOrdinal, DirectAudioAttempt;
    bool FreshBlocked;
    std::uint64_t InitializationOrdinal;
    std::uint64_t StartClipCaller;
    std::uint64_t StockStopCallers[2], StockStopMethods[2];
    JournalRow Journal[JournalCount];
    WriterReservation* Writers[JournalCount];
    WriterReservation* RetiredWriters;
};
struct LogicalGate { Owner* Value; RizLogicalCommit Commit; void* Context; bool Closed; std::uint64_t CutSequence; Initialization* Initializer; void* FreshClock = nullptr; };
// Retained, private records are never recycled into a later source identity.
struct Initialization {
    RizPlaybackInitializationInfoV2 Info;
    Initialization* Previous;
    std::uint64_t EntryWriter, EntrySource, EntryScheduling;
    unsigned Children;
    bool ChildNormal;
    ProgressResourceState* Resource;
    ProgressResourceTicket* ResourceTicket;
    std::uint64_t ResourceRevision, ResourceReference, PhysicalInFlight;
    bool ResourceInvalidated;
    // Orthogonal proof of an interrupted staged Unity backing capture. A
    // resource writer never supplies this badge and physical duty survives it.
    bool BackingInvalidated;
    UnityBackingWatch* BackingInvalidationWatch;
    std::uint64_t BackingInvalidationSourceFence;
    bool LowerPitchInvalidated, LowerPitchInvalidationExited;
    std::uint64_t LowerPitchInvalidationSequence, LowerPitchInvalidationToken;
    std::uint64_t LowerPitchInvalidationWriter, LowerPitchInvalidationSource;
    PlayerWatch* LowerPitchInvalidationPlayer;
    std::uint32_t LowerPitchInvalidationRole;
    bool AudioInvalidated;
    RtPlaybackSourceFrame AudioAttempt;
    std::uint64_t AudioWriterBefore, AudioSourceBefore, AudioStampBefore;
    PlayerWatch* AudioPlayer;
    // Minted only from the actual retained Progress initializer graph.
    bool EnginePitchIdentity;
    std::uint64_t EnginePitchSourceNative, EnginePitchClipNative;
    std::int32_t EnginePitchOffset, EnginePitchSourceId, EnginePitchClipId;
};
struct WriterReservation {
    Owner* OwnerValue; RtPlaybackWriterInfo Info; bool Consumed, Retired;
    std::uint32_t Role;
    WriterReservation* Successor; WriterReservation* RetiredNext;
};
struct Slot {
    LONG Configured;
    std::uint32_t Kind, Role, Number;
    Owner* OwnerValue;
    void* Target;
    void* Original;
    RtPlaybackProgressPlayObserverV1 PlayObserver;
    volatile LONG PlayObserverBound, PlayObserverFault;
    bool PlayObserverAllowsDormant;
};
struct Scope {
    RtPlaybackSourceFrame Frame;
    Scope* Parent;
    Owner* OwnerValue;
    Slot* SlotValue;
    std::uint32_t JournalIndex;
    bool CallbackActive;
    bool SuppressObserver;
    bool StockStopSeen;
    Initialization* InitializerValue;
    // Private delegation is scoped to the actual original CALL, never callbacks.
    bool LowerPitchWindow, LowerPitchConsumed, LowerPitchChild, LowerPitchChildExited;
    bool LowerPitchChildReturned, LowerPitchChildAbnormal;
    Scope* LowerPitchParent;
    std::uint64_t LowerPitchChildSequence, LowerPitchChildToken;
    std::uint64_t LowerPitchWindowWriter, LowerPitchWindowSource, LowerPitchWindowScheduling;
    Initialization* LowerPitchInvalidatedInitializer;
    RtPlaybackEnginePitchAssociationInfoV1 EnginePitchAssociation;
    bool EnginePitchWindow, EnginePitchConsumed, EnginePitchChild, EnginePitchChildExited;
    bool EnginePitchChildReturned, EnginePitchChildAbnormal;
    Scope* EnginePitchParent;
    std::uint64_t EnginePitchNativeSelf, EnginePitchChildSequence, EnginePitchChildToken;
    std::uint64_t EnginePitchWindowWriter, EnginePitchWindowSource, EnginePitchWindowScheduling;
    std::uint32_t EnginePitchBeginTested, EnginePitchBeginMatched;
    std::uint32_t EnginePitchChildTested, EnginePitchChildMatched;
    std::uint32_t EnginePitchWindowMode, ScheduledEnginePitchBits;
    std::uint64_t ScheduledEngineInputBits;
    RtPlaybackEnginePitchAssociationInfoV1 ScheduledEngineAssociation;
    bool LowerTimeWindow, LowerTimeConsumed, LowerTimeChild, LowerTimeChildExited;
    bool LowerTimeChildReturned, LowerTimeChildAbnormal;
    Scope* LowerTimeParent;
    std::uint64_t LowerTimeChildSequence, LowerTimeChildToken;
    std::uint64_t LowerTimeWindowWriter, LowerTimeWindowSource, LowerTimeWindowScheduling;
    Initialization* AudioInvalidatedInitializer;
};
Owner owners[OwnerCount]{};
Slot slots[SlotCount]{};
SRWLOCK configureLock = SRWLOCK_INIT;
volatile LONG64 nextEpoch{}, nextToken{}, nextPlayObservation{};
__declspec(thread) Scope* currentScope;
__declspec(thread) WriterReservation* currentWriter;
volatile LONG64 writerNonce{};

Owner* FindOwner(void* raw) {
    for (auto& owner : owners)
        if (raw == &owner && InterlockedCompareExchange(&owner.Allocated, 0, 0) == 1) return &owner;
    return nullptr;
}
void FaultLocked(Owner* owner) {
    owner->Flags = (owner->Flags & ~RtSourceArmed) | RtSourceUnknown;
    if (owner->Faults != UINT64_MAX) ++owner->Faults;
}
bool PlayerWatchRelevantLocked(Owner* owner, void* self);
bool StockStopRelevantLocked(Owner* owner, Slot* slot, void* self, void* method, void* caller);
bool AudioWatchRelevantLocked(Owner* owner, void* self);
bool LegacyAudioRelevantLocked(Owner* owner, void* self);
bool ProgressRelevantLocked(Owner* owner, void* self);
bool StartProgressRelevantLocked(Owner* owner, void* self);
bool ProgressWatchReadyLocked(Owner* owner, const PlayerWatch* player);
bool ProgressWatchOpenedLocked(Owner* owner, const PlayerWatch* player);
void CommitProgressWatchLocked(Owner* owner, PlayerWatch* player);
bool SnapshotInitializationResourceLocked(Owner* owner, Initialization* value);
bool InitializationResourceMatchesLocked(Owner* owner, const Initialization* value);
bool InitializationResourceReadyLocked(Owner* owner, const Initialization* value);
bool LowerTimeBindingMatchesLocked(const Owner* owner);
void ConsumeInitializationResourceLocked(Owner* owner, Initialization* value);
void AdvanceProgressResourceLocked(Owner* owner, void* self);
bool AttachProgressResourceLocked(Owner* owner, ProgressWatch* watch);
void RetireProgressResourceWatchLocked(Owner* owner, const ProgressWatch* watch);
bool ResourcePendingPlayerReplaceLocked(Owner* owner, const PlayerWatch* pending);
bool ResourcePendingProgressReplaceLocked(Owner* owner, const ProgressWatch* pending,
    const PlayerWatch* player, std::uint64_t progress);
bool PlayerWatchReadyLocked(Owner* owner);
bool PlayerWatchOpenedLocked(Owner* owner);
void CommitPlayerWatchLocked(Owner* owner);
int CheckUnityBackingLocked(Owner* owner);
bool UnityBackingWatchReadyLocked(Owner* owner, const PlayerWatch* player);
bool UnityBackingWatchOpenedLocked(Owner* owner);
void CommitUnityBackingWatchLocked(Owner* owner, PlayerWatch* player);
bool UnityBackingPendingPlayerReplaceLocked(Owner* owner, const PlayerWatch* pending);
bool UnityBackingPendingProgressReplaceLocked(Owner* owner, const ProgressWatch* pending,
    const PlayerWatch* player, std::uint64_t progress);
bool LowerPitchPendingPlayerReplaceLocked(Owner* owner, const PlayerWatch* pending);
bool LowerPitchPendingProgressReplaceLocked(Owner* owner, const ProgressWatch* pending,
    const PlayerWatch* player, std::uint64_t progress);
bool LowerPitchPendingBackingReplaceLocked(Owner* owner, const UnityBackingWatch* pending, const PlayerWatch* player);
bool LowerPitchChildLocked(Owner* owner, Slot* slot, void* self, void* caller, std::uint64_t arg);
void InvalidateLowerPitchLocked(Owner* owner, Scope* scope);
bool BeginLowerPitchWrapperOriginal(Scope* scope);
void EndLowerPitchWrapperOriginal(Scope* scope, bool abnormal);
void LowerPitchPhysicalExitLocked(Scope* scope);
bool CaptureEnginePitchInitializationLocked(Owner* owner, Initialization* value);
bool EnginePitchRelevantLocked(Owner* owner, void* self, Scope* scope);
bool EnginePitchChildLocked(Owner* owner, Slot* slot, void* self, void* caller, std::uint64_t arg, Scope* child);
bool BeginEnginePitchLowerOriginal(Scope* scope);
void EndEnginePitchLowerOriginal(Scope* scope, bool abnormal);
void EnginePitchPhysicalExitLocked(Scope* scope);
bool BeginScheduledOriginal(Scope* scope);
void EndScheduledOriginal(Scope* scope, bool abnormal);
bool ScheduledEnginePitchChildLocked(Owner* owner, Slot* slot, void* self, void* caller,
    std::uint64_t arg, const Scope* child);
bool LowerTimeChildLocked(Owner*, Slot*, void*, void*, std::uint64_t);
bool BeginLowerTimeWrapperOriginal(Scope*);
void EndLowerTimeWrapperOriginal(Scope*, bool);
void InvalidateAudioInterruptionLocked(Owner*, Scope*, std::uint64_t, std::uint64_t, std::uint64_t);
void CaptureAudioInterruptionFrameLocked(Scope*);
void LowerTimePhysicalExitLocked(Scope*);
bool AudioPendingPlayerReplaceLocked(Owner*, const PlayerWatch*);
bool AudioPendingProgressReplaceLocked(Owner*, const ProgressWatch*, const PlayerWatch*, std::uint64_t);
bool AudioPendingBackingReplaceLocked(Owner*, const UnityBackingWatch*, const PlayerWatch*);
bool DirectAudioRole(std::uint32_t role) {
    return role >= RtSourceAudioPlayHelper && role <= RtSourceAudioSetLoop;
}
bool LegacyAudioRole(std::uint32_t role) {
    return role == RtSourceSetPitch || role == RtSourcePlayScheduled || role == RtSourceSetClip
        || role == RtSourcePauseLeaf || role == RtSourceSetTime || role == RtSourceLowerSetPitch || role == RtSourceLowerSetTime;
}
bool AudioSourceRole(std::uint32_t role) { return DirectAudioRole(role) || LegacyAudioRole(role); }
bool PlayerControlRole(std::uint32_t role) {
    return role == RtSourcePause || role == RtSourceReplay || role == RtSourceResume;
}
bool RoleMatches(std::uint32_t kind, std::uint32_t role) {
    return (kind == RtSourceVoid0 && (role == RtSourcePause || role == RtSourcePauseLeaf || role == RtSourceAudioPlay || role == RtSourceAudioStop))
        || (kind == RtSourceSingle1 && (role == RtSourceSetPitch || role == RtSourceReplay || role == RtSourceResume || role == RtSourceSetTime))
        || (kind == RtSourceDouble1 && (role == RtSourcePlayScheduled || role == RtSourceAudioPlayDelayed))
        || (kind == RtSourceRef1 && (role == RtSourceSetClip || role == RtSourceProgressMusicPlayer || role == RtSourcePlayerConstructor || role == RtSourceProgressClip))
        || (kind == RtSourceBool1 && (role == RtSourceProgressPlay || role == RtSourceAudioStopBoolean || role == RtSourceAudioSetLoop))
        || (kind == RtSourceBooleanReturn0 && role == RtSourceProgressStartMoveNext)
        || (kind == RtSourceUInt64_1 && role == RtSourceAudioPlayHelper)
        || (kind == RtSourceIcallSingle1 && (role == RtSourceLowerSetPitch || role == RtSourceEngineSetPitch || role == RtSourceLowerSetTime));
}

bool ReadStart(void* self, int* state, std::uint64_t* progress, std::uint64_t* source) {
    __try {
        if (!self) return false;
        *state = *reinterpret_cast<const int*>(static_cast<unsigned char*>(self) + 0x10);
        *progress = *reinterpret_cast<const std::uint64_t*>(static_cast<unsigned char*>(self) + 0x20);
        if (!*progress) return false;
        *source = *reinterpret_cast<const std::uint64_t*>(*progress + 0x28);
        return *source != 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool ReadFinalGraph(Initialization* value) {
    __try {
        const auto& info = value->Info;
        if (*reinterpret_cast<const std::uint64_t*>(info.progress + 0x28) != info.source) return false;
        const auto player = *reinterpret_cast<const std::uint64_t*>(info.progress + 0x38);
        if (!player || (info.player && player != info.player)
            || *reinterpret_cast<const std::uint64_t*>(player + 0x10) != info.source) return false;
        // Only the actual parent's first completed graph records its player.
        // Subsequent transfer stages verify that identity rather than rebind it.
        if (!info.player) value->Info.player = player;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void InitializationEnter(Owner* owner, Scope* scope) {
    auto& frame = scope->Frame;
    if (frame.Role == RtSourceProgressStartMoveNext) {
        // This hold is independent of LogicalGate.Closed and observer Armed.
        // It begins under the same lock as the actual attempt sequence.
        owner->FreshBlocked = true;
        int state = -2; std::uint64_t progress = 0, source = 0;
        const bool readable = ReadStart(reinterpret_cast<void*>(frame.Self), &state, &progress, &source);
        auto* value = owner->Initializer;
        const bool continuation = readable && state == 1 && value && value->Info.phase == 2
            && value->Info.enumerator == frame.Self && value->Info.progress == progress && value->Info.source == source
            && frame.ParentSequence == 0 && frame.Depth == 1
            && owner->Writer == value->Info.writer && owner->Source == value->Info.sourceFence + 1
            && owner->Scheduling == value->Info.scheduling && InitializationResourceMatchesLocked(owner, value);
        if (continuation) {
            value->Info.phase = 3; value->Info.finalSequence = frame.Sequence; value->Info.finalToken = frame.Token;
        } else {
            if (value && value->Info.phase < 5) value->Info.phase = 5;
            value = new(std::nothrow) Initialization{};
            if (!value || owner->InitializationOrdinal == UINT64_MAX) { FaultLocked(owner); return; }
            value->Previous = owner->Initializer; owner->Initializer = value;
            auto& info = value->Info;
            info.version = 2; info.size = sizeof(info); info.blocked = 1;
            info.owner = reinterpret_cast<std::uint64_t>(owner); info.ownerEpoch = owner->Epoch;
            info.ordinal = ++owner->InitializationOrdinal; info.enumerator = frame.Self;
            info.progress = progress; info.source = source;
            info.phase = readable && state == 0 && frame.ParentSequence == 0 && frame.Depth == 1 ? 1u : 5u;
            info.yieldSequence = frame.Sequence; info.yieldToken = frame.Token;
            if (info.phase == 1 && !SnapshotInitializationResourceLocked(owner, value)) info.phase = 5;
        }
        scope->InitializerValue = value;
        if (value->PhysicalInFlight == UINT64_MAX) FaultLocked(owner); else ++value->PhysicalInFlight;
        value->EntryWriter = owner->Writer; value->EntrySource = owner->Source; value->EntryScheduling = owner->Scheduling;
    } else if (owner->FreshBlocked && owner->Initializer && owner->Initializer->Info.phase == 3) {
        auto* value = owner->Initializer;
        const bool child = scope->Parent && scope->Parent->OwnerValue == owner
            && frame.ParentSequence == value->Info.finalSequence && scope->Parent->Frame.Role == RtSourceProgressStartMoveNext;
        if (!child || frame.Role != RtSourceSetClip || ++value->Children != 1
            || frame.Self != value->Info.source || frame.IncomingMethod != 0
            || !owner->StartClipCaller || frame.Caller != owner->StartClipCaller || frame.ArgBits == 0) {
            value->Info.phase = 5; return;
        }
        value->Info.childSequence = frame.Sequence; value->Info.childToken = frame.Token; value->Info.clip = frame.ArgBits;
    }
}
void InitializationExit(Owner* owner, const Scope* scope) {
    const auto& frame = scope->Frame;
    auto* value = scope->InitializerValue ? scope->InitializerValue : owner->Initializer;
    // A resource setter can invalidate the candidate before its native Start
    // exits, and nested reentry can install a successor. Drain that exact old
    // physical responsibility even though it can never complete or reopen.
    if (scope->InitializerValue) {
        if (!value->PhysicalInFlight) FaultLocked(owner); else --value->PhysicalInFlight;
        if (value != owner->Initializer) return;
    }
    if (!value || value->Info.phase >= 5) return;
    const bool normal = (frame.Flags & (RtSourceReturnedFlag | RtSourcePhysicalExitedFlag))
        == (RtSourceReturnedFlag | RtSourcePhysicalExitedFlag)
        && !(frame.Flags & (RtSourceAbnormalFlag | RtSourceUnknown | RtSourceWithdrawn));
    if (frame.Sequence == value->Info.childSequence && frame.Token == value->Info.childToken) {
        value->ChildNormal = normal && frame.Role == RtSourceSetClip;
        if (!value->ChildNormal) value->Info.phase = 5;
    } else if (frame.Role == RtSourceProgressStartMoveNext && frame.Self == value->Info.enumerator) {
        const bool fences = owner->Writer == value->EntryWriter && owner->Scheduling == value->EntryScheduling
            && InitializationResourceMatchesLocked(owner, value);
        if (value->Info.phase == 1 && frame.Sequence == value->Info.yieldSequence
            && normal && frame.ReturnPresent == 1 && frame.RawReturnByte == 1
            && owner->Source == value->EntrySource && fences) value->Info.phase = 2;
        else if (value->Info.phase == 3 && frame.Sequence == value->Info.finalSequence
            && normal && frame.ReturnPresent == 1 && frame.RawReturnByte == 0
            && value->Children == 1 && value->ChildNormal && owner->Source == value->EntrySource + 1
            && fences && ReadFinalGraph(value)) {
            value->Info.phase = 4;
            if (!CaptureEnginePitchInitializationLocked(owner, value)) value->Info.phase = 5;
        }
        else value->Info.phase = 5;
        value->Info.writer = owner->Writer; value->Info.sourceFence = owner->Source; value->Info.scheduling = owner->Scheduling;
    }
}

bool Enter(Slot* slot, Scope* scope, void* self, void* method, void* caller, std::uint64_t arg) {
    if (InterlockedCompareExchange(&slot->Configured, 0, 0) != 1) return false;
    auto* owner = slot->OwnerValue;
    AcquireSRWLockExclusive(&owner->Lock);
    const bool armedBeforeClassification = (owner->Flags & RtSourceArmed) != 0;
    // The shared body belongs to many unrelated classes. Classify by the exact
    // rooted graph receiver under the SAME lock as in-flight acquisition.
    // Foreign calls allocate no token, scope, journal row or playback fence.
    const bool directAudio = DirectAudioRole(slot->Role);
    // Earlier fixture profiles have no configured watch. The current profile
    // classifies every AudioSource leaf by its exact watched/initializer source.
    const bool legacyAudio = LegacyAudioRole(slot->Role) && (owner->WatchClass || slot->Role == RtSourceLowerSetPitch || slot->Role == RtSourceLowerSetTime);
    // The MUG wrappers are shared by other retained player instances too.
    // Borrow the exact active/pending player identity from the same watch,
    // before any journal, fence, thread fault or managed observation exists.
    const bool playerControl = PlayerControlRole(slot->Role) && owner->WatchClass;
    const bool progressControl = slot->Role == RtSourceProgressClip || (owner->WatchProgressClass
        && (slot->Role == RtSourceProgressPlay || slot->Role == RtSourceProgressMusicPlayer));
    const bool startControl = owner->WatchProgressClass && slot->Role == RtSourceProgressStartMoveNext;
    const bool enginePitch = slot->Role == RtSourceEngineSetPitch;
    const bool filtered = slot->Role == RtSourcePlayerConstructor || directAudio || legacyAudio || playerControl || progressControl || startControl || enginePitch;
    if ((slot->Role == RtSourcePlayerConstructor && !PlayerWatchRelevantLocked(owner, self))
        || (directAudio && !AudioWatchRelevantLocked(owner, self))
        || (legacyAudio && !LegacyAudioRelevantLocked(owner, self))
        || (playerControl && !PlayerWatchRelevantLocked(owner, self))
        || (progressControl && !ProgressRelevantLocked(owner, self))
        || (startControl && !StartProgressRelevantLocked(owner, self))
        || (enginePitch && !EnginePitchRelevantLocked(owner, self, scope))) {
        ReleaseSRWLockExclusive(&owner->Lock); return false;
    }
    // This fact is derived from actual TLS, retained graph and closed CALL
    // profile before any child counter moves. Callback reentry cannot borrow it.
    // Foreign receivers were already excluded. A readable lifetime change is
    // a source boundary, while worker/unreadable access remains sticky Unknown.
    CheckUnityBackingLocked(owner);
    const bool stockStop = StockStopRelevantLocked(owner, slot, self, method, caller);
    if (stockStop) currentScope->StockStopSeen = true;
    scope->OwnerValue = owner; scope->SlotValue = slot; scope->Parent = currentScope;
    scope->JournalIndex = UINT32_MAX; scope->CallbackActive = false;
    scope->SuppressObserver = filtered && GetCurrentThreadId() != owner->Thread;
    auto& frame = scope->Frame;
    frame.Version = 4; frame.Size = sizeof(frame); frame.Kind = slot->Kind; frame.Role = slot->Role;
    frame.Slot = slot->Number; frame.Stage = RtSourceBefore; frame.ThreadId = GetCurrentThreadId();
    frame.Depth = currentScope ? currentScope->Frame.Depth + 1 : 1;
    frame.Owner = reinterpret_cast<std::uint64_t>(owner); frame.OwnerEpoch = owner->Epoch;
    frame.ParentSequence = currentScope && currentScope->OwnerValue == owner ? currentScope->Frame.Sequence : 0;
    frame.Self = reinterpret_cast<std::uint64_t>(self); frame.IncomingMethod = reinterpret_cast<std::uint64_t>(method);
    frame.Caller = reinterpret_cast<std::uint64_t>(caller); frame.ArgBits = arg;
    frame.Token = static_cast<std::uint64_t>(InterlockedIncrement64(&nextToken));
    scope->LowerPitchChild = LowerPitchChildLocked(owner, slot, self, caller, arg);
    if (scope->LowerPitchChild) scope->LowerPitchParent = currentScope;
    scope->EnginePitchChild = EnginePitchChildLocked(owner, slot, self, caller, arg, scope);
    if (scope->EnginePitchChild) scope->EnginePitchParent = currentScope;
    scope->LowerTimeChild = LowerTimeChildLocked(owner, slot, self, caller, arg);
    if (scope->LowerTimeChild) scope->LowerTimeParent = currentScope;
    if (enginePitch) {
        scope->EnginePitchAssociation.Sequence = frame.Sequence;
        scope->EnginePitchAssociation.Token = frame.Token;
    }
    const auto entryFlags = owner->Logical && owner->Logical->Closed
        ? (owner->Flags & ~RtSourceArmed) | RtSourceDormant : owner->Flags;
    // A matching receiver whose class was tampered is faulted during
    // classification, but its originally armed physical attempt still owns a
    // completion row. Foreign receivers never reach this point.
    const bool resourcePhysical = owner->ResourceEnabled && owner->FreshBlocked
        && (slot->Role == RtSourceProgressStartMoveNext || slot->Role == RtSourceSetClip || slot->Role == RtSourceProgressClip);
    const bool audioPhysical = owner->LowerTimeEnabled && (slot->Role == RtSourceSetTime || slot->Role == RtSourceLowerSetTime);
    const bool requiresReceipt = filtered
        ? armedBeforeClassification && (!(entryFlags & RtSourceDormant) || resourcePhysical || enginePitch || audioPhysical)
        : (entryFlags & RtSourceArmed) != 0;
    if (owner->Entered == UINT64_MAX || owner->InFlight == UINT64_MAX || frame.Depth > 64 || frame.Token == 0)
        FaultLocked(owner);
    frame.Sequence = ++owner->Entered; ++owner->InFlight;
    if (frame.ThreadId != owner->Thread) FaultLocked(owner);
    const auto beforeWriter = owner->Writer, beforeSource = owner->Source, beforeScheduling = owner->Scheduling;
    if (slot->Role == RtSourceProgressClip) AdvanceProgressResourceLocked(owner, self);
    else if (!scope->LowerPitchChild && !scope->EnginePitchChild && !scope->LowerTimeChild) {
        auto* fence = slot->Role == RtSourceSetPitch || slot->Role == RtSourceLowerSetPitch || enginePitch ? &owner->Writer
            : (slot->Role == RtSourceSetClip || slot->Role == RtSourceProgressMusicPlayer || slot->Role == RtSourceProgressStartMoveNext || slot->Role == RtSourcePlayerConstructor) ? &owner->Source : &owner->Scheduling;
        if (*fence == UINT64_MAX) FaultLocked(owner); else ++*fence;
    }
    if ((slot->Role == RtSourceLowerSetPitch && !scope->LowerPitchChild) || (enginePitch && !scope->EnginePitchChild)) InvalidateLowerPitchLocked(owner, scope);
    InvalidateAudioInterruptionLocked(owner, scope, beforeWriter, beforeSource, beforeScheduling);
    // Pause/schedule may advance Scheduling after open. This private fence
    // makes a direct audio attempt independently reject completion action3/11.
    if (directAudio && !stockStop) {
        if (owner->DirectAudioAttempt == UINT64_MAX) FaultLocked(owner);
        else ++owner->DirectAudioAttempt;
    }
    // Resource-only entry already invalidated its matching initializer. It is
    // never mistaken for the initializer's one actual AudioSource.set_clip.
    if (slot->Role != RtSourceProgressClip && slot->Role != RtSourceLowerSetPitch && slot->Role != RtSourceLowerSetTime && !enginePitch) InitializationEnter(owner, scope);
    if (enginePitch) scope->EnginePitchAssociation.Sequence = frame.Sequence;
    frame.WriterSequence = owner->Writer; frame.SourceSequence = owner->Source;
    frame.SchedulingSequence = owner->Scheduling;
    frame.Entered = owner->Entered; frame.Exited = owner->Exited; frame.InFlight = owner->InFlight;
    frame.Flags = entryFlags | (owner->Flags & (RtSourceUnknown | RtSourceWithdrawn));
    if (stockStop) frame.Flags |= RtSourceStockStopFlag;
    if (scope->LowerPitchChild) {
        frame.Flags |= RtSourceLowerPitchChildFlag;
        auto* parent = scope->LowerPitchParent;
        parent->LowerPitchConsumed = true;
        parent->LowerPitchChildSequence = frame.Sequence; parent->LowerPitchChildToken = frame.Token;
    }
    if (scope->EnginePitchChild) {
        frame.Flags |= RtSourceEnginePitchChildFlag;
        auto* parent = scope->EnginePitchParent;
        parent->EnginePitchConsumed = true;
        parent->EnginePitchChildSequence = frame.Sequence; parent->EnginePitchChildToken = frame.Token;
    }
    if (scope->LowerTimeChild) {
        frame.Flags |= RtSourceLowerTimeChildFlag;
        auto* parent = scope->LowerTimeParent;
        parent->LowerTimeConsumed = true;
        parent->LowerTimeChildSequence = frame.Sequence; parent->LowerTimeChildToken = frame.Token;
    }
    if (!scope->LowerPitchChild && !scope->EnginePitchChild && !scope->LowerTimeChild && currentWriter && currentWriter->OwnerValue == owner) {
        auto& writer = *currentWriter; auto& evidence = writer.Info;
        const auto expectedParent = evidence.ParentSequence;
        if (!writer.Consumed && slot->Role == writer.Role && reinterpret_cast<std::uint64_t>(slot->Target) == evidence.Target
            && frame.Caller == reinterpret_cast<std::uint64_t>(&RtPlaybackWriterCallReturned)
            && frame.Self == evidence.Source && frame.IncomingMethod == evidence.IncomingMethod && frame.ArgBits == evidence.ArgBits
            && frame.ParentSequence == expectedParent
            && owner->Writer == evidence.ExpectedWriter + (writer.Role == RtSourceSetPitch ? 1u : 0u)
            && owner->Source == evidence.ExpectedSource
            && owner->Scheduling == evidence.ExpectedScheduling + (writer.Role == RtSourceSetTime ? 1u : 0u)) {
            writer.Consumed = true; evidence.AttemptSequence = frame.Sequence; evidence.AttemptToken = frame.Token;
            evidence.Caller = frame.Caller;
        } else evidence.Contended = 1;
    }
    if (!(frame.Flags & RtSourceArmed)) frame.Flags |= RtSourceDormant;
    for (std::uint32_t i = 0; requiresReceipt && i < JournalCount; ++i) {
        if (owner->Journal[i].State != 0) continue;
        // Dormant alone can also mean an ordinary closed-gate observation
        // with no completion row. Publish this private qualification only
        // after a real row is available and native physical state is known.
        if ((resourcePhysical || enginePitch || audioPhysical) && (frame.Flags & RtSourceDormant)
            && owner->Flags == RtSourceArmed && owner->Faults == 0)
            frame.Flags |= RtSourceInitializationResponsibilityFlag;
        scope->JournalIndex = i; owner->Journal[i].State = 1; owner->Journal[i].Frame = frame; break;
    }
    if (requiresReceipt && scope->JournalIndex == UINT32_MAX) { FaultLocked(owner); frame.Flags |= RtSourceUnknown; }
    CaptureAudioInterruptionFrameLocked(scope);
    ReleaseSRWLockExclusive(&owner->Lock);
    currentScope = scope;
    return true;
}
void Notify(Scope* scope, std::uint32_t stage) {
    auto* owner = scope->OwnerValue;
    scope->Frame.Stage = stage;
    if (stage == RtSourceOriginalReturned) scope->Frame.Flags |= RtSourceReturnedFlag;
    if (stage == RtSourceAbnormal) {
        scope->Frame.Flags = (scope->Frame.Flags & ~RtSourceReturnedFlag) | RtSourceAbnormalFlag;
        scope->Frame.RawReturnByte = scope->Frame.ReturnPresent = 0;
    }
    if (currentWriter && currentWriter->OwnerValue == owner && currentWriter->Info.AttemptSequence == scope->Frame.Sequence) {
        if (stage == RtSourceOriginalReturned) currentWriter->Info.Returned = 1;
        if (stage == RtSourceAbnormal) currentWriter->Info.Abnormal = 1;
    }
    AcquireSRWLockShared(&owner->Lock);
    scope->Frame.Flags |= owner->Flags & (RtSourceUnknown | RtSourceWithdrawn);
    ReleaseSRWLockShared(&owner->Lock);
    // Give observers a copy. Mutating their argument cannot mutate the actual native scope.
    auto copied = scope->Frame;
    // A relevant worker has already closed native source/Clock authority at
    // Enter. Preserve its original and physical journal, without entering a
    // managed observer on the wrong thread.
    if (scope->SuppressObserver) return;
    scope->CallbackActive = true;
    __try { owner->Observer(owner->Context, &copied); }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        AcquireSRWLockExclusive(&owner->Lock); FaultLocked(owner); ReleaseSRWLockExclusive(&owner->Lock);
    }
    scope->CallbackActive = false;
}
void Exit(Scope* scope, bool abnormal) {
    if (abnormal) Notify(scope, RtSourceAbnormal);
    auto* owner = scope->OwnerValue;
    // The callback window and TLS authority end before the completion receipt exists.
    scope->CallbackActive = false;
    currentScope = scope->Parent;
    AcquireSRWLockExclusive(&owner->Lock);
    if (abnormal && (scope->Frame.Role == RtSourceProgressClip || scope->Frame.Role == RtSourceLowerSetPitch || scope->Frame.Role == RtSourceEngineSetPitch || scope->Frame.Role == RtSourceLowerSetTime)) FaultLocked(owner);
    --owner->InFlight; ++owner->Exited;
    scope->Frame.Stage = RtSourcePhysicalExit;
    scope->Frame.Flags |= RtSourcePhysicalExitedFlag | (owner->Flags & (RtSourceUnknown | RtSourceWithdrawn));
    scope->Frame.Entered = owner->Entered; scope->Frame.Exited = owner->Exited; scope->Frame.InFlight = owner->InFlight;
    if (currentWriter && currentWriter->OwnerValue == owner && currentWriter->Info.AttemptSequence == scope->Frame.Sequence) {
        auto& info = currentWriter->Info; info.PhysicalExited = 1;
        info.PostWriter = owner->Writer; info.PostSource = owner->Source; info.PostScheduling = owner->Scheduling;
    }
    LowerPitchPhysicalExitLocked(scope);
    EnginePitchPhysicalExitLocked(scope);
    LowerTimePhysicalExitLocked(scope);
    if (scope->Frame.Role != RtSourceLowerSetPitch && scope->Frame.Role != RtSourceEngineSetPitch && scope->Frame.Role != RtSourceLowerSetTime) InitializationExit(owner, scope);
    if (scope->JournalIndex != UINT32_MAX) {
        auto& row = owner->Journal[scope->JournalIndex]; row.Frame = scope->Frame; row.State = 2;
    }
    ReleaseSRWLockExclusive(&owner->Lock);
}

// An ignored original still executes, but it cannot lend the surrounding
// retained call's TLS parent to its own children. In particular, a foreign
// player can share the retained AudioSource: those writes are independent
// current-source interference, never children of the outer stock occurrence.
// SEH and raw return bytes cross this transparent boundary unchanged.
template<class Original, class... Args>
__declspec(noinline) auto ForwardIgnored(Original original, Args... args) -> decltype(original(args...)) {
    auto* previous = currentScope;
    auto* previousWriter = currentWriter;
    currentScope = nullptr;
    currentWriter = nullptr;
    __try { return original(args...); }
    __finally { currentScope = previous; currentWriter = previousWriter; }
}

std::uintptr_t NotifyProgressPlayObserver(Slot* slot, void* self, std::uint32_t value, void* method, void* caller,
    std::uint64_t sequence, std::uintptr_t token, bool returned) {
    const auto& observer = slot->PlayObserver;
    auto* previousScope = currentScope;
    auto* previousWriter = currentWriter;
    currentScope = nullptr;
    currentWriter = nullptr;
    __try {
        __try {
            if (token == 0) token = observer.Enter(observer.Context, self, value, method, caller, sequence);
            else observer.Exit(observer.Context, self, value, method, caller, sequence, token, returned ? 1u : 0u);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            InterlockedExchange(&slot->PlayObserverFault, 1);
            token = 0;
        }
    } __finally { currentScope = previousScope; currentWriter = previousWriter; }
    return token;
}

// The optional observer borrows a boundary around the original only. It
// cannot replace it, claim a stock source scope, or normalize the bool byte.
bool InvokeObservedProgressPlay(Slot* slot, void* self, unsigned char value, void* method, void* caller, bool ignored) {
    if (slot->Role != RtSourceProgressPlay || InterlockedCompareExchange(&slot->PlayObserverBound, 0, 0) != 1
        || InterlockedCompareExchange(&slot->PlayObserverFault, 0, 0) != 0) return false;
    auto* owner = slot->OwnerValue;
    AcquireSRWLockShared(&owner->Lock);
    const bool known = (owner->Flags & RtSourceUnknown) == 0 && owner->Faults == 0;
    ReleaseSRWLockShared(&owner->Lock);
    if (!known) return false;
    const auto sequence = InterlockedIncrement64(&nextPlayObservation);
    if (sequence <= 0) { InterlockedExchange(&slot->PlayObserverFault, 1); return false; }
    const auto token = NotifyProgressPlayObserver(slot, self, value, method, caller, sequence, 0, false);
    const auto original = reinterpret_cast<void(__cdecl*)(void*, unsigned char, void*)>(slot->Original);
    __try {
        if (ignored) ForwardIgnored(original, self, value, method);
        else original(self, value, method);
    } __finally {
        if (token) NotifyProgressPlayObserver(slot, self, value, method, caller, sequence, token, AbnormalTermination() == FALSE);
    }
    return true;
}

template<unsigned N> __declspec(noinline) void __cdecl EntryVoid0(void* self, void* method) {
    Scope scope{}; auto* slot = &slots[N]; auto* caller = _ReturnAddress();
    if (!Enter(slot, &scope, self, method, caller, 0)) {
        if ((AudioSourceRole(slot->Role) || PlayerControlRole(slot->Role)) && InterlockedCompareExchange(&slot->Configured, 0, 0) == 1)
            ForwardIgnored(reinterpret_cast<void(__cdecl*)(void*, void*)>(slot->Original), self, method);
        return;
    }
    __try {
        Notify(&scope, RtSourceBefore);
        reinterpret_cast<void(__cdecl*)(void*, void*)>(slot->Original)(self, method);
        Notify(&scope, RtSourceOriginalReturned);
    } __finally { Exit(&scope, AbnormalTermination() != FALSE); }
}
template<unsigned N> __declspec(noinline) void __cdecl EntrySingle1(void* self, float value, void* method) {
    Scope scope{}; std::uint32_t bits; std::memcpy(&bits, &value, sizeof(bits));
    auto* slot = &slots[N]; auto* caller = _ReturnAddress();
    if (!Enter(slot, &scope, self, method, caller, bits)) {
        if ((AudioSourceRole(slot->Role) || PlayerControlRole(slot->Role)) && InterlockedCompareExchange(&slot->Configured, 0, 0) == 1)
            ForwardIgnored(reinterpret_cast<void(__cdecl*)(void*, float, void*)>(slot->Original), self, value, method);
        return;
    }
    __try {
        Notify(&scope, RtSourceBefore);
        if (BeginLowerPitchWrapperOriginal(&scope)) {
            __try { RtPlaybackLowerPitchWrapperCall(slot->Original, self, bits, method); }
            __finally { EndLowerPitchWrapperOriginal(&scope, AbnormalTermination() != FALSE); }
        } else if (BeginLowerTimeWrapperOriginal(&scope)) {
            __try { RtPlaybackLowerTimeWrapperCall(slot->Original, self, bits, method); }
            __finally { EndLowerTimeWrapperOriginal(&scope, AbnormalTermination() != FALSE); }
        } else reinterpret_cast<void(__cdecl*)(void*, float, void*)>(slot->Original)(self, value, method);
        Notify(&scope, RtSourceOriginalReturned);
    } __finally { Exit(&scope, AbnormalTermination() != FALSE); }
}
template<unsigned N> __declspec(noinline) void __cdecl EntryIcallSingle1(void* self, float value) {
    Scope scope{}; std::uint32_t bits; std::memcpy(&bits, &value, sizeof(bits));
    auto* slot = &slots[N]; auto* caller = _ReturnAddress();
    if (!Enter(slot, &scope, self, nullptr, caller, bits)) {
        if ((slot->Role == RtSourceLowerSetPitch || slot->Role == RtSourceEngineSetPitch || slot->Role == RtSourceLowerSetTime) && InterlockedCompareExchange(&slot->Configured, 0, 0) == 1)
            ForwardIgnored(reinterpret_cast<void(__cdecl*)(void*, float)>(slot->Original), self, value);
        return;
    }
    __try {
        Notify(&scope, RtSourceBefore);
        if (BeginEnginePitchLowerOriginal(&scope)) {
            __try { reinterpret_cast<void(__cdecl*)(void*, float)>(slot->Original)(self, value); }
            __finally { EndEnginePitchLowerOriginal(&scope, AbnormalTermination() != FALSE); }
        } else reinterpret_cast<void(__cdecl*)(void*, float)>(slot->Original)(self, value);
        Notify(&scope, RtSourceOriginalReturned);
    } __finally { Exit(&scope, AbnormalTermination() != FALSE); }
}
template<unsigned N> __declspec(noinline) void __cdecl EntryDouble1(void* self, double value, void* method) {
    Scope scope{}; std::uint64_t bits; std::memcpy(&bits, &value, sizeof(bits));
    auto* slot = &slots[N]; auto* caller = _ReturnAddress();
    if (!Enter(slot, &scope, self, method, caller, bits)) {
        if (AudioSourceRole(slot->Role) && InterlockedCompareExchange(&slot->Configured, 0, 0) == 1)
            ForwardIgnored(reinterpret_cast<void(__cdecl*)(void*, double, void*)>(slot->Original), self, value, method);
        return;
    }
    __try {
        Notify(&scope, RtSourceBefore);
        if (BeginScheduledOriginal(&scope)) {
            __try { reinterpret_cast<void(__cdecl*)(void*, double, void*)>(slot->Original)(self, value, method); }
            __finally { EndScheduledOriginal(&scope, AbnormalTermination() != FALSE); }
        } else reinterpret_cast<void(__cdecl*)(void*, double, void*)>(slot->Original)(self, value, method);
        Notify(&scope, RtSourceOriginalReturned);
    } __finally { Exit(&scope, AbnormalTermination() != FALSE); }
}
template<unsigned N> __declspec(noinline) void __cdecl EntryRef1(void* self, void* value, void* method) {
    Scope scope{}; auto* slot = &slots[N]; auto* caller = _ReturnAddress();
    if (!Enter(slot, &scope, self, method, caller, reinterpret_cast<std::uint64_t>(value))) {
        // Ignored is a transparent call, not rejection of the actual original.
        if ((slot->Role == RtSourcePlayerConstructor || slot->Role == RtSourceProgressMusicPlayer || slot->Role == RtSourceProgressClip || AudioSourceRole(slot->Role))
            && InterlockedCompareExchange(&slot->Configured, 0, 0) == 1)
            ForwardIgnored(reinterpret_cast<void(__cdecl*)(void*, void*, void*)>(slot->Original), self, value, method);
        return;
    }
    __try {
        Notify(&scope, RtSourceBefore);
        reinterpret_cast<void(__cdecl*)(void*, void*, void*)>(slot->Original)(self, value, method);
        Notify(&scope, RtSourceOriginalReturned);
    } __finally { Exit(&scope, AbnormalTermination() != FALSE); }
}
// This count spans the real native original even when no managed binding
// claims it. Arm uses the same lock, so it cannot race an earlier borrower.
bool BorrowDormantProgressPlay(Slot* slot) {
    if (InterlockedCompareExchange(&slot->Configured, 0, 0) != 1 || slot->Role != RtSourceProgressPlay
        || InterlockedCompareExchange(&slot->PlayObserverBound, 0, 0) != 1 || !slot->PlayObserverAllowsDormant) return false;
    auto* owner = slot->OwnerValue;
    AcquireSRWLockExclusive(&owner->Lock);
    // A new call cannot cross the final Play/Clock publication boundary.
    // The owner never needs to wait for a borrower after admitting promotion.
    // Reentry or a stalled publisher is Unknown and still runs the original.
    const auto until = owner->PlayPromotion ? GetTickCount64() + 5000 : 0;
    while (owner->PlayPromotion && !owner->Faults) {
        const auto now = GetTickCount64();
        if (owner->Thread == GetCurrentThreadId() || now >= until
            || !SleepConditionVariableSRW(&owner->PlayPromotionChanged, &owner->Lock,
                static_cast<DWORD>(until - now), 0)) FaultLocked(owner);
    }
    const bool dormant = !owner->WasArmed && owner->Flags == RtSourceDormant && owner->Faults == 0;
    const bool admitted = dormant && owner->DormantPlayInFlight != UINT64_MAX;
    if (admitted) ++owner->DormantPlayInFlight;
    else if (dormant) FaultLocked(owner);
    ReleaseSRWLockExclusive(&owner->Lock);
    return admitted;
}

void ReturnDormantProgressPlay(Slot* slot) {
    auto* owner = slot->OwnerValue;
    AcquireSRWLockExclusive(&owner->Lock);
    --owner->DormantPlayInFlight;
    ReleaseSRWLockExclusive(&owner->Lock);
}

template<unsigned N> __declspec(noinline) void __cdecl EntryBool1(void* self, unsigned char value, void* method) {
    Scope scope{}; auto* slot = &slots[N]; auto* caller = _ReturnAddress();
    if (BorrowDormantProgressPlay(slot)) {
        __try {
            if (!InvokeObservedProgressPlay(slot, self, value, method, caller, true))
                ForwardIgnored(reinterpret_cast<void(__cdecl*)(void*, unsigned char, void*)>(slot->Original), self, value, method);
        } __finally { ReturnDormantProgressPlay(slot); }
        return;
    }
    if (!Enter(slot, &scope, self, method, caller, value)) {
        if ((DirectAudioRole(slot->Role) || slot->Role == RtSourceProgressPlay) && InterlockedCompareExchange(&slot->Configured, 0, 0) == 1
            && !InvokeObservedProgressPlay(slot, self, value, method, caller, true))
            ForwardIgnored(reinterpret_cast<void(__cdecl*)(void*, unsigned char, void*)>(slot->Original), self, value, method);
        return;
    }
    __try {
        Notify(&scope, RtSourceBefore);
        if (!InvokeObservedProgressPlay(slot, self, value, method, caller, false))
            reinterpret_cast<void(__cdecl*)(void*, unsigned char, void*)>(slot->Original)(self, value, method);
        Notify(&scope, RtSourceOriginalReturned);
    } __finally { Exit(&scope, AbnormalTermination() != FALSE); }
}
// Never declare either side as C++ bool: that would normalize AL (80/ff).
template<unsigned N> __declspec(noinline) void __cdecl EntryUInt64_1(void* self, std::uint64_t value, void* method) {
    Scope scope{}; auto* slot = &slots[N]; auto* caller = _ReturnAddress();
    if (!Enter(slot, &scope, self, method, caller, value)) {
        if (DirectAudioRole(slot->Role) && InterlockedCompareExchange(&slot->Configured, 0, 0) == 1)
            ForwardIgnored(reinterpret_cast<void(__cdecl*)(void*, std::uint64_t, void*)>(slot->Original), self, value, method);
        return;
    }
    __try {
        Notify(&scope, RtSourceBefore);
        reinterpret_cast<void(__cdecl*)(void*, std::uint64_t, void*)>(slot->Original)(self, value, method);
        Notify(&scope, RtSourceOriginalReturned);
    } __finally { Exit(&scope, AbnormalTermination() != FALSE); }
}
template<unsigned N> __declspec(noinline) std::uint8_t __cdecl EntryBooleanReturn0(void* self, void* method) {
    Scope scope{}; auto* slot = &slots[N]; auto* caller = _ReturnAddress();
    if (!Enter(slot, &scope, self, method, caller, 0)) {
        if (slot->Role == RtSourceProgressStartMoveNext && InterlockedCompareExchange(&slot->Configured, 0, 0) == 1)
            return ForwardIgnored(reinterpret_cast<std::uint8_t(__cdecl*)(void*, void*)>(slot->Original), self, method);
        return 0;
    }
    std::uint8_t raw = 0;
    __try {
        Notify(&scope, RtSourceBefore);
        raw = reinterpret_cast<std::uint8_t(__cdecl*)(void*, void*)>(slot->Original)(self, method);
        scope.Frame.RawReturnByte = raw; scope.Frame.ReturnPresent = 1;
        Notify(&scope, RtSourceOriginalReturned);
    } __finally { Exit(&scope, AbnormalTermination() != FALSE); }
    return raw;
}
#define RT_SOURCE_ENTRIES(T) reinterpret_cast<void*>(&T<0>),reinterpret_cast<void*>(&T<1>),reinterpret_cast<void*>(&T<2>),reinterpret_cast<void*>(&T<3>),reinterpret_cast<void*>(&T<4>),reinterpret_cast<void*>(&T<5>),reinterpret_cast<void*>(&T<6>),reinterpret_cast<void*>(&T<7>),reinterpret_cast<void*>(&T<8>),reinterpret_cast<void*>(&T<9>),reinterpret_cast<void*>(&T<10>),reinterpret_cast<void*>(&T<11>),reinterpret_cast<void*>(&T<12>),reinterpret_cast<void*>(&T<13>),reinterpret_cast<void*>(&T<14>),reinterpret_cast<void*>(&T<15>),reinterpret_cast<void*>(&T<16>),reinterpret_cast<void*>(&T<17>),reinterpret_cast<void*>(&T<18>),reinterpret_cast<void*>(&T<19>),reinterpret_cast<void*>(&T<20>),reinterpret_cast<void*>(&T<21>),reinterpret_cast<void*>(&T<22>),reinterpret_cast<void*>(&T<23>),reinterpret_cast<void*>(&T<24>),reinterpret_cast<void*>(&T<25>),reinterpret_cast<void*>(&T<26>),reinterpret_cast<void*>(&T<27>),reinterpret_cast<void*>(&T<28>),reinterpret_cast<void*>(&T<29>),reinterpret_cast<void*>(&T<30>),reinterpret_cast<void*>(&T<31>)
void* entries[8][SlotCount] = {
    { RT_SOURCE_ENTRIES(EntryVoid0) }, { RT_SOURCE_ENTRIES(EntrySingle1) },
    { RT_SOURCE_ENTRIES(EntryDouble1) }, { RT_SOURCE_ENTRIES(EntryRef1) }, { RT_SOURCE_ENTRIES(EntryBool1) },
    { RT_SOURCE_ENTRIES(EntryBooleanReturn0) }, { RT_SOURCE_ENTRIES(EntryUInt64_1) }, { RT_SOURCE_ENTRIES(EntryIcallSingle1) }
};
void InfoLocked(Owner* owner, Slot* slot, RtPlaybackSourceInfo* info) {
    *info = {}; info->Version = 4; info->Size = sizeof(*info); info->Slot = UINT32_MAX;
    info->Owner = reinterpret_cast<std::uint64_t>(owner); info->OwnerEpoch = owner->Epoch;
    info->OwnerThreadId = owner->Thread; info->Flags = owner->Flags;
    if (!(info->Flags & RtSourceArmed)) info->Flags |= RtSourceDormant;
    info->Entered = owner->Entered; info->Exited = owner->Exited; info->InFlight = owner->InFlight;
    info->WriterSequence = owner->Writer; info->SourceSequence = owner->Source;
    info->SchedulingSequence = owner->Scheduling; info->Faults = owner->Faults;
    info->JournalCapacity = JournalCount; info->ConfiguredSlots = owner->Slots;
    if (slot) {
        info->Kind = slot->Kind; info->Role = slot->Role; info->Slot = slot->Number;
        info->Target = reinterpret_cast<std::uint64_t>(slot->Target);
        info->Original = reinterpret_cast<std::uint64_t>(slot->Original);
        info->Entry = reinterpret_cast<std::uint64_t>(entries[slot->Kind - 1][slot->Number]);
    }
}
}

std::uint32_t __cdecl RtPlaybackSourceVersion() { return 4; }
void* __cdecl RtPlaybackSourceCreateOwner(RtPlaybackSourceObserver observer, void* context) {
    if (!observer) return nullptr;
    for (auto& owner : owners) {
        if (InterlockedCompareExchange(&owner.Allocated, -1, 0) != 0) continue;
        InitializeSRWLock(&owner.Lock); owner.Observer = observer; owner.Context = context;
        owner.Thread = GetCurrentThreadId(); owner.Epoch = static_cast<std::uint64_t>(InterlockedIncrement64(&nextEpoch));
        owner.Writer = owner.Source = owner.Scheduling = owner.DirectAudioAttempt = 1; owner.Flags = RtSourceDormant;
        InterlockedExchange(&owner.Allocated, 1); return &owner;
    }
    return nullptr;
}
int __cdecl RtPlaybackSourceConfigure(void* raw, std::uint32_t kind, std::uint32_t number,
    std::uint32_t role, void* target, void* original) {
    auto* owner = FindOwner(raw); if (!owner || number >= SlotCount) return 0;
    auto* slot = &slots[number];
    AcquireSRWLockExclusive(&configureLock);
    const bool vacant = InterlockedCompareExchange(&slot->Configured, -1, 0) == 0;
    bool duplicate = false;
    for (const auto& other : slots) if (&other != slot && other.Target == target && other.Configured != 0) duplicate = true;
    AcquireSRWLockExclusive(&owner->Lock);
    const bool valid = vacant && kind >= RtSourceVoid0 && kind <= RtSourceIcallSingle1 && RoleMatches(kind, role)
        && target && original && target != original && !duplicate && !owner->WasArmed && owner->InFlight == 0
        && !(owner->Flags & (RtSourceUnknown | RtSourceWithdrawn));
    if (valid) {
        slot->Kind = kind; slot->Role = role; slot->Number = number; slot->OwnerValue = owner;
        slot->Target = target; slot->Original = original; ++owner->Slots;
        if (role == RtSourceProgressClip) owner->ResourceEnabled = true;
        InterlockedExchange(&slot->Configured, 1);
    } else FaultLocked(owner);
    ReleaseSRWLockExclusive(&owner->Lock); ReleaseSRWLockExclusive(&configureLock);
    return valid ? 1 : 0;
}
void* __cdecl RtPlaybackSourceGetEntry(std::uint32_t kind, std::uint32_t slot) {
    return kind >= RtSourceVoid0 && kind <= RtSourceIcallSingle1 && slot < SlotCount ? entries[kind - 1][slot] : nullptr;
}
namespace {
bool ValidProgressPlayObserver(Owner* owner, const RtPlaybackProgressPlayObserverV1* observer) {
    if (!observer || observer->Version != 1 || observer->Size != sizeof(*observer) || observer->Reserved
        || observer->Slot >= SlotCount || !observer->Enter || !observer->Exit || !observer->Context
        || observer->OwnerEpoch != owner->Epoch || (owner->Flags & RtSourceUnknown) || owner->Faults) return false;
    const auto& slot = slots[observer->Slot];
    return InterlockedCompareExchange(&slots[observer->Slot].Configured, 0, 0) == 1
        && slot.OwnerValue == owner && slot.Kind == RtSourceBool1 && slot.Role == RtSourceProgressPlay
        && slot.Target == observer->Target && slot.Original == observer->Original
        && entries[RtSourceBool1 - 1][observer->Slot] == observer->Entry;
}
}
namespace {
int BindProgressPlayObserver(Owner* owner, const RtPlaybackProgressPlayObserverV1* observer, bool allowDormant) {
    if (!owner || owner->Thread != GetCurrentThreadId()) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    const bool phase = allowDormant
        ? !owner->WasArmed && owner->Flags == RtSourceDormant && owner->Slots == 1
            && owner->Entered == 0 && owner->InFlight == 0 && owner->DormantPlayInFlight == 0
        : owner->WasArmed && (owner->Flags & RtSourceArmed) != 0;
    const bool valid = phase && ValidProgressPlayObserver(owner, observer)
        && InterlockedCompareExchange(&slots[observer->Slot].PlayObserverBound, 0, 0) == 0;
    if (valid) {
        auto& slot = slots[observer->Slot];
        slot.PlayObserver = *observer;
        slot.PlayObserverAllowsDormant = allowDormant;
        InterlockedExchange(&slot.PlayObserverBound, 1);
    }
    ReleaseSRWLockExclusive(&owner->Lock);
    return valid ? 1 : 0;
}
}

int __cdecl RtPlaybackSourceBindProgressPlayObserverV1(void* raw, const RtPlaybackProgressPlayObserverV1* observer) {
    return BindProgressPlayObserver(FindOwner(raw), observer, false);
}

int __cdecl RtPlaybackSourceBindDormantProgressPlayObserverV1(void* raw, const RtPlaybackProgressPlayObserverV1* observer) {
    return BindProgressPlayObserver(FindOwner(raw), observer, true);
}

int __cdecl RtPlaybackSourceVerifyProgressPlayObserverV1(void* raw, const RtPlaybackProgressPlayObserverV1* observer) {
    auto* owner = FindOwner(raw);
    if (!owner) return 0;
    AcquireSRWLockShared(&owner->Lock);
    const bool valid = ValidProgressPlayObserver(owner, observer)
        && InterlockedCompareExchange(&slots[observer->Slot].PlayObserverBound, 0, 0) == 1
        && InterlockedCompareExchange(&slots[observer->Slot].PlayObserverFault, 0, 0) == 0
        && std::memcmp(&slots[observer->Slot].PlayObserver, observer, sizeof(*observer)) == 0;
    ReleaseSRWLockShared(&owner->Lock);
    return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceBeginDormantPlayPromotionV1(void* raw, std::uint64_t epoch) {
    auto* owner = FindOwner(raw);
    if (!owner || owner->Thread != GetCurrentThreadId()) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    bool play = false;
    for (auto& slot : slots) if (slot.OwnerValue == owner && slot.Configured == 1 && slot.Role == RtSourceProgressPlay)
        play |= slot.PlayObserverAllowsDormant && InterlockedCompareExchange(&slot.PlayObserverBound, 0, 0) == 1
            && InterlockedCompareExchange(&slot.PlayObserverFault, 0, 0) == 0;
    const bool ready = owner->Epoch == epoch && play && !owner->PlayPromotion && !owner->WasArmed
        && owner->Flags == RtSourceDormant && !owner->Faults;
    const bool busy = ready && (owner->DormantPlayInFlight || owner->InFlight);
    if (ready && !busy) owner->PlayPromotion = true;
    ReleaseSRWLockExclusive(&owner->Lock);
    return busy ? 2 : ready ? 1 : 0;
}

int __cdecl RtPlaybackSourceEndDormantPlayPromotionV1(void* raw, std::uint64_t epoch, int completed) {
    auto* owner = FindOwner(raw);
    if (!owner || owner->Thread != GetCurrentThreadId()) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    const bool owned = owner->Epoch == epoch && owner->PlayPromotion;
    const bool valid = owned && (completed == 0
        || (completed == 1 && owner->WasArmed && owner->Flags == RtSourceArmed && !owner->Faults));
    if (owned) {
        if (!completed || !valid) FaultLocked(owner);
        owner->PlayPromotion = false;
        WakeAllConditionVariable(&owner->PlayPromotionChanged);
    }
    ReleaseSRWLockExclusive(&owner->Lock);
    return valid ? 1 : 0;
}

int __cdecl RtPlaybackSourceSetArmed(void* raw, std::uint64_t epoch, int armed) {
    auto* owner = FindOwner(raw); if (!owner) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    bool success = owner->Epoch == epoch;
    if (success && armed) {
        success = GetCurrentThreadId() == owner->Thread && !owner->WasArmed && owner->Slots > 0 && owner->InFlight == 0
            && owner->DormantPlayInFlight == 0
            && !(owner->Flags & (RtSourceUnknown | RtSourceWithdrawn));
        for (const auto& slot : slots) if (slot.OwnerValue == owner && slot.Configured == 1 && slot.Role == RtSourceLowerSetPitch)
            success = success && owner->LowerPitchEnabled && slot.Number == owner->LowerPitchLowerSlot;
        for (const auto& slot : slots) if (slot.OwnerValue == owner && slot.Configured == 1 && slot.Role == RtSourceEngineSetPitch)
            success = success && owner->EnginePitchEnabled && slot.Number == owner->EnginePitchEngineSlot;
        for (const auto& slot : slots) if (slot.OwnerValue == owner && slot.Configured == 1 && slot.Role == RtSourceLowerSetTime)
            success = success && owner->LowerTimeEnabled && slot.Number == owner->LowerTimeLowerSlot;
        if (success) { owner->WasArmed = true; owner->Flags = RtSourceArmed; }
    } else if (success) owner->Flags = (owner->Flags & ~RtSourceArmed) | RtSourceWithdrawn;
    ReleaseSRWLockExclusive(&owner->Lock); return success ? 1 : 0;
}
int __cdecl RtPlaybackSourceMarkFault(void* raw, std::uint64_t epoch) {
    auto* owner = FindOwner(raw); if (!owner) return 0;
    AcquireSRWLockExclusive(&owner->Lock); const bool match = owner->Epoch == epoch;
    if (match) FaultLocked(owner);
    ReleaseSRWLockExclusive(&owner->Lock); return match ? 1 : 0;
}
int __cdecl RtPlaybackSourceGetInfo(std::uint32_t kind, std::uint32_t number, RtPlaybackSourceInfo* output) {
    if (!output || number >= SlotCount) return 0;
    auto* slot = &slots[number];
    if (InterlockedCompareExchange(&slot->Configured, 0, 0) != 1 || slot->Kind != kind) return 0;
    auto* owner = slot->OwnerValue;
    AcquireSRWLockShared(&owner->Lock); InfoLocked(owner, slot, output); ReleaseSRWLockShared(&owner->Lock); return 1;
}
int __cdecl RtPlaybackSourceReadOwner(void* raw, RtPlaybackSourceInfo* output) {
    auto* owner = FindOwner(raw); if (!owner || !output) return 0;
    AcquireSRWLockShared(&owner->Lock); InfoLocked(owner, nullptr, output); ReleaseSRWLockShared(&owner->Lock); return 1;
}
int __cdecl RtPlaybackSourceReadCurrent(std::uint32_t kind, std::uint32_t number, RtPlaybackSourceFrame* output) {
    auto* scope = currentScope;
    if (!output || !scope || !scope->CallbackActive || scope->Frame.Kind != kind || scope->Frame.Slot != number) return 0;
    *output = scope->Frame; return 1;
}
int __cdecl RtPlaybackSourceVerifyCurrent(void* raw, const RtPlaybackSourceFrame* frame) {
    auto* scope = currentScope;
    return frame && scope && scope->CallbackActive && scope->OwnerValue == FindOwner(raw)
        && scope->Frame.ThreadId == GetCurrentThreadId() && std::memcmp(frame, &scope->Frame, sizeof(*frame)) == 0 ? 1 : 0;
}
int __cdecl RtPlaybackSourceReadReceipt(void* raw, std::uint64_t sequence, std::uint64_t token, RtPlaybackSourceFrame* output) {
    auto* owner = FindOwner(raw); if (!owner || !output || !sequence || !token) return 0;
    int found = 0; AcquireSRWLockShared(&owner->Lock);
    for (const auto& row : owner->Journal) if (row.State == 2 && row.Frame.Sequence == sequence && row.Frame.Token == token) {
        *output = row.Frame; found = 1; break;
    }
    ReleaseSRWLockShared(&owner->Lock); return found;
}
int __cdecl RtPlaybackSourceReleaseReceipt(void* raw, std::uint64_t sequence, std::uint64_t token) {
    auto* owner = FindOwner(raw); if (!owner || !sequence || !token) return 0;
    int found = 0; AcquireSRWLockExclusive(&owner->Lock);
    for (auto& row : owner->Journal) if (row.State == 2 && row.Frame.Sequence == sequence && row.Frame.Token == token) {
        row.State = 0; row.Frame = {}; found = 1; break;
    }
    ReleaseSRWLockExclusive(&owner->Lock); return found;
}

namespace {
WriterReservation* FindWriter(void* raw, std::uint64_t nonce) {
    for (auto& owner : owners) {
        if (InterlockedCompareExchange(&owner.Allocated, 0, 0) != 1) continue;
        AcquireSRWLockShared(&owner.Lock); WriterReservation* found = nullptr;
        for (auto* writer : owner.Writers) if (writer == raw && writer && writer->Info.Nonce == nonce) { found = writer; break; }
        ReleaseSRWLockShared(&owner.Lock); if (found) return found;
    }
    return nullptr;
}
bool Healthy(Owner* owner) { return owner->Flags == RtSourceArmed && owner->Faults == 0
    && !owner->FreshBlocked && !owner->BackingBlocked && !owner->LowerPitchBlocked && !owner->AudioBlocked && (!owner->Logical || !owner->Logical->Closed); }
bool CurrentWriterLocked(const WriterReservation* writer) {
    const auto& info = writer->Info; const auto* owner = writer->OwnerValue;
    return (!owner->Logical || info.RootSequence > owner->Logical->CutSequence)
        && !writer->Retired && Healthy(writer->OwnerValue) && info.State == 3 && info.Returned && !info.Abnormal && info.PhysicalExited && !info.Contended
        && owner->Writer == info.PostWriter && owner->Source == info.PostSource && owner->Scheduling == info.PostScheduling;
}
bool WriterPostMatches(const WriterReservation* writer) {
    const auto& info = writer->Info;
    return info.PostWriter == info.ExpectedWriter + (writer->Role == RtSourceSetPitch ? 1u : 0u)
        && info.PostSource == info.ExpectedSource
        && info.PostScheduling == info.ExpectedScheduling + (writer->Role == RtSourceSetTime ? 1u : 0u);
}
void WriterInformation(WriterReservation* writer, RtPlaybackWriterInfo* output) {
    *output = writer->Info; output->Current = CurrentWriterLocked(writer) ? 1u : 0u;
}
bool RootValidLocked(Owner* owner, const RtPlaybackWriterInfo& request, std::uint64_t* parent) {
    const RtPlaybackSourceFrame* root = nullptr;
    if (currentScope && currentScope->OwnerValue == owner && currentScope->CallbackActive
        && currentScope->Frame.Sequence == request.RootSequence && currentScope->Frame.Token == request.RootToken
        && currentScope->Frame.Stage == RtSourceOriginalReturned
        && (currentScope->Frame.Role == RtSourceReplay || currentScope->Frame.Role == RtSourceResume)) {
        root = &currentScope->Frame; *parent = root->Sequence;
    } else if (!currentScope) {
        for (const auto& row : owner->Journal) if (row.State == 2 && row.Frame.Sequence == request.RootSequence && row.Frame.Token == request.RootToken
            && (row.Frame.Role == RtSourcePause || row.Frame.Role == RtSourceProgressPlay)) { root = &row.Frame; break; }
        *parent = 0;
    }
    if (owner->Logical && request.RootSequence <= owner->Logical->CutSequence) return false;
    if (!root || !(root->Flags & RtSourceReturnedFlag) || (root->Flags & (RtSourceAbnormalFlag | RtSourceUnknown | RtSourceWithdrawn))) return false;
    unsigned leaves = 0;
    const auto role = root->Role == RtSourcePause || root->Role == RtSourceProgressPlay ? RtSourcePauseLeaf : RtSourcePlayScheduled;
    for (const auto& row : owner->Journal) if (row.State == 2 && row.Frame.ParentSequence == root->Sequence && row.Frame.Role == role) {
        if (row.Frame.Self != request.Source || !(row.Frame.Flags & RtSourceReturnedFlag)
            || (row.Frame.Flags & (RtSourceAbnormalFlag | RtSourceUnknown | RtSourceWithdrawn))) return false;
        ++leaves;
    }
    return leaves == 1;
}
}
std::uint32_t __cdecl RtPlaybackWriterVersion() { return 1; }
std::uint32_t __cdecl RtPlaybackTimeWriterVersion() { return 1; }
namespace {
int ReserveWriter(void* raw, RtPlaybackWriterInfo* packet, std::uint32_t role) {
    auto* owner = FindOwner(raw);
    if (!owner || !packet || packet->Version != 1 || packet->Size != sizeof(*packet) || packet->Reserved || packet->Reserved0 || packet->Reserved1
        || packet->Reservation || packet->Nonce || packet->Owner || packet->OwnerEpoch || packet->State || packet->ArgBits > UINT32_MAX) return 0;
    float rate{}; auto bits = static_cast<std::uint32_t>(packet->ArgBits); std::memcpy(&rate, &bits, 4);
    if (!std::isfinite(rate) || (role == RtSourceSetTime ? rate < 0 : rate <= 0)
        || !packet->Source || !packet->IncomingMethod) return 0;
    auto* predecessor = packet->Predecessor ? FindWriter(reinterpret_cast<void*>(packet->Predecessor), packet->PredecessorNonce) : nullptr;
    AcquireSRWLockExclusive(&owner->Lock);
    if (GetCurrentThreadId() == owner->Thread) CheckUnityBackingLocked(owner);
    unsigned vacant = JournalCount; bool target = false; std::uint64_t parent{};
    for (const auto& slot : slots) if (slot.OwnerValue == owner && slot.Configured == 1 && slot.Role == role
        && slot.Kind == RtSourceSingle1 && reinterpret_cast<std::uint64_t>(slot.Target) == packet->Target) target = true;
    bool valid = target && Healthy(owner) && GetCurrentThreadId() == owner->Thread && !currentWriter
        && owner->Writer == packet->ExpectedWriter && owner->Source == packet->ExpectedSource && owner->Scheduling == packet->ExpectedScheduling
        && RootValidLocked(owner, *packet, &parent);
    // Time is a paused operation only. The actual exited Pause/ProgressPlay
    // journal, exact lower-time pair and idle owner are checked here; callers
    // cannot turn a live Replay/Resume callback into a Seek reservation.
    if (role == RtSourceSetTime) valid = valid && LowerTimeBindingMatchesLocked(owner)
        && !currentScope && !owner->InFlight && parent == 0 && PlayerWatchOpenedLocked(owner)
        && packet->ExpectedScheduling != UINT64_MAX;
    else valid = valid && packet->ExpectedWriter != UINT64_MAX;
    if (packet->Predecessor) valid = valid && predecessor && predecessor->OwnerValue == owner && !predecessor->Successor
        && CurrentWriterLocked(predecessor) && predecessor->Info.RootSequence == packet->RootSequence && predecessor->Info.RootToken == packet->RootToken
        && predecessor->Info.Source == packet->Source;
    for (unsigned i = 0; i < JournalCount; ++i) {
        if (!owner->Writers[i] && vacant == JournalCount) vacant = i;
        if (!packet->Predecessor && owner->Writers[i] && owner->Writers[i]->Info.RootSequence == packet->RootSequence) valid = false;
    }
    if (valid && vacant != JournalCount) {
        auto* writer = new(std::nothrow) WriterReservation{};
        if (writer) {
            writer->OwnerValue = owner; writer->Role = role; writer->Info = *packet; auto& info = writer->Info;
            info.State = 1; info.Owner = reinterpret_cast<std::uint64_t>(owner); info.OwnerEpoch = owner->Epoch;
            info.Reservation = reinterpret_cast<std::uint64_t>(writer); info.Nonce = static_cast<std::uint64_t>(InterlockedIncrement64(&writerNonce));
            info.ParentSequence = parent; owner->Writers[vacant] = writer; if (predecessor) predecessor->Successor = writer;
            *packet = info; ReleaseSRWLockExclusive(&owner->Lock); return 1;
        }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return 0;
}
}
int __cdecl RtPlaybackWriterReserve(void* raw, RtPlaybackWriterInfo* packet) {
    return ReserveWriter(raw, packet, RtSourceSetPitch);
}
int __cdecl RtPlaybackTimeWriterReserve(void* raw, RtPlaybackWriterInfo* packet) {
    return ReserveWriter(raw, packet, RtSourceSetTime);
}
int __cdecl RtPlaybackWriterRead(void* raw, std::uint64_t nonce, RtPlaybackWriterInfo* output) {
    auto* writer = FindWriter(raw, nonce); if (!writer || !output || output->Version != 1 || output->Size != sizeof(*output)) return 0;
    AcquireSRWLockShared(&writer->OwnerValue->Lock);
    if (writer->Retired) { ReleaseSRWLockShared(&writer->OwnerValue->Lock); return 0; }
    WriterInformation(writer, output); ReleaseSRWLockShared(&writer->OwnerValue->Lock); return 1;
}
int __cdecl RtPlaybackWriterRelease(void* raw, std::uint64_t nonce) {
    auto* writer = FindWriter(raw, nonce); if (!writer) return 0;
    auto* owner = writer->OwnerValue;
    AcquireSRWLockExclusive(&owner->Lock);
    if (GetCurrentThreadId() != owner->Thread || writer->Retired || writer->Info.State == 2 || currentWriter == writer
        || (writer->Info.AttemptSequence && !writer->Info.PhysicalExited)) {
        ReleaseSRWLockExclusive(&owner->Lock); return 0;
    }
    for (auto& slot : owner->Writers) if (slot == writer) {
        slot = nullptr; writer->Retired = true; writer->Info.State = 4; writer->Info.Current = 0;
        // Keep a tombstone at the old address. Borrowed pointers cannot become
        // dangling or name a reused reservation; its active slot is recyclable.
        writer->RetiredNext = owner->RetiredWriters; owner->RetiredWriters = writer;
        ReleaseSRWLockExclusive(&owner->Lock); return 1;
    }
    ReleaseSRWLockExclusive(&owner->Lock); return 0;
}
int __cdecl RtPlaybackWriterVerifyCurrent(void* raw, std::uint64_t nonce, const RtPlaybackSourceFrame* frame, RtPlaybackWriterInfo* output) {
    auto* writer = FindWriter(raw, nonce);
    if (!writer || !frame || !output || !currentScope || !currentScope->CallbackActive || currentWriter != writer || writer->Retired || writer->Info.State != 2
        || !writer->Consumed || currentScope->OwnerValue != writer->OwnerValue || std::memcmp(frame, &currentScope->Frame, sizeof(*frame))
        || frame->Sequence != writer->Info.AttemptSequence || frame->Token != writer->Info.AttemptToken
        || frame->Caller != reinterpret_cast<std::uint64_t>(&RtPlaybackWriterCallReturned)) return 0;
    AcquireSRWLockShared(&writer->OwnerValue->Lock); WriterInformation(writer, output); ReleaseSRWLockShared(&writer->OwnerValue->Lock); return 1;
}
int __cdecl RtPlaybackWriterRun(void* raw, std::uint64_t nonce, RtPlaybackWriterInfo* output) {
    auto* writer = FindWriter(raw, nonce); if (!writer || !output || output->Version != 1 || output->Size != sizeof(*output)) return 0;
    auto* owner = writer->OwnerValue;
    AcquireSRWLockExclusive(&owner->Lock); auto& info = writer->Info; std::uint64_t parent{};
    if (GetCurrentThreadId() == owner->Thread) CheckUnityBackingLocked(owner);
    const bool valid = !writer->Retired && info.State == 1 && Healthy(owner) && GetCurrentThreadId() == owner->Thread && !currentWriter
        && owner->Writer == info.ExpectedWriter && owner->Source == info.ExpectedSource && owner->Scheduling == info.ExpectedScheduling
        && RootValidLocked(owner, info, &parent) && parent == info.ParentSequence
        && (writer->Role != RtSourceSetTime || (!currentScope && !owner->InFlight
            && LowerTimeBindingMatchesLocked(owner) && PlayerWatchOpenedLocked(owner)));
    if (valid) info.State = 2; else if (info.State == 1) info.State = 5;
    ReleaseSRWLockExclusive(&owner->Lock); if (!valid) { RtPlaybackWriterRead(raw, nonce, output); return 0; }
    __try {
        __try { currentWriter = writer; RtPlaybackWriterCall(reinterpret_cast<void*>(info.Target), reinterpret_cast<void*>(info.Source), static_cast<std::uint32_t>(info.ArgBits), reinterpret_cast<void*>(info.IncomingMethod)); }
        __finally { currentWriter = nullptr; }
    } __except(EXCEPTION_EXECUTE_HANDLER) { info.ExceptionCode = GetExceptionCode(); info.Abnormal = 1; }
    AcquireSRWLockExclusive(&owner->Lock);
    info.State = writer->Consumed && info.Returned && !info.Abnormal && info.PhysicalExited && !info.Contended
        && WriterPostMatches(writer)
        && Healthy(owner) && owner->Writer == info.PostWriter && owner->Source == info.PostSource && owner->Scheduling == info.PostScheduling ? 3u : 5u;
    WriterInformation(writer, output); ReleaseSRWLockExclusive(&owner->Lock); return info.State == 3 ? 1 : 2;
}

// The only nested lock order is GetTime.authorityGate -> Owner.Lock. Entry,
// Exit, writer and journal code release Owner.Lock before any original/managed
// callback. This callback is a fixed native coordinator, never managed code.
extern "C" __declspec(dllexport) void* __cdecl RtPlaybackSourceBindLogicalGate(void* raw,
    RizLogicalCommit commit, void* context) {
    auto* owner = FindOwner(raw); if (!owner || !commit || !context) return nullptr;
    AcquireSRWLockExclusive(&owner->Lock);
    LogicalGate* gate = nullptr;
    if (owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed && owner->Faults == 0 && !owner->Logical) {
        gate = new(std::nothrow) LogicalGate{owner, commit, context, false, 0, nullptr};
        if (gate) owner->Logical = gate;
    }
    ReleaseSRWLockExclusive(&owner->Lock); return gate;
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackSourceLogicalAction(void* raw,
    std::uint32_t action, void* request) {
    LogicalGate* gate = nullptr;
    for (auto& owner : owners) if (owner.Logical == raw && owner.Logical) { gate = owner.Logical; break; }
    if (!gate || action > 11 || action == 5 || action == 7 || !request) return 1;
    auto* owner = gate->Value; AcquireSRWLockExclusive(&owner->Lock);
    std::uint32_t result = 0;
    if (owner->Thread != GetCurrentThreadId()) result = 10;
    else if (owner->Flags != RtSourceArmed || owner->Faults) result = 3;
    // A generic caller handoff cannot reopen a changed Unity object. Only a
    // completed actual Start may adopt the recognized boundary and stage roots.
    else if (owner->BackingBlocked && (!gate->Initializer || gate->Initializer != owner->Initializer
        || gate->Initializer->Info.phase != 6)) result = 8;
    else if (owner->FreshBlocked && (!gate->Initializer || gate->Initializer != owner->Initializer
        || gate->Initializer->Info.phase != 6)) result = 8;
    else if (owner->LowerPitchBlocked && (!gate->Initializer || gate->Initializer != owner->Initializer
        || gate->Initializer->Info.phase != 6)) result = 8;
    else if (owner->AudioBlocked && (!gate->Initializer || gate->Initializer != owner->Initializer
        || gate->Initializer->Info.phase != 6)) result = 8;
    else if ((action == 1 || action == 4 || action == 6 || action == 8 || action == 9 || action == 10) && gate->Initializer
        && (owner->Writer != gate->Initializer->Info.writer || owner->Source != gate->Initializer->Info.sourceFence
            || owner->Scheduling != gate->Initializer->Info.scheduling || !ReadFinalGraph(gate->Initializer)
            || !InitializationResourceReadyLocked(owner, gate->Initializer))) result = 8;
    else if ((action == 4 || action == 10) && owner->WatchClass && !PlayerWatchReadyLocked(owner)) result = 8;
    else if ((action == 3 || action == 11) && owner->WatchClass && !PlayerWatchOpenedLocked(owner)) result = 8;
    else if (action == 0 && gate->Closed) result = 2;
    else if ((action == 1 || action == 4 || action == 6 || action == 8 || action == 9 || action == 10) && !gate->Closed) result = 2;
    else if ((action == 2 || action == 3 || action == 11) && gate->Closed) result = 2;
    else if (action != 0 && owner->InFlight != 0) result = 8;
    else {
        const RizLogicalSourceSnapshot snapshot{reinterpret_cast<std::uint64_t>(owner), owner->Epoch,
            owner->Entered, owner->Exited, owner->InFlight, owner->Writer, owner->Source, owner->Scheduling,
            reinterpret_cast<std::uint64_t>(gate->Initializer), gate->Initializer ? gate->Initializer->Info.player : 0,
            gate->Initializer ? gate->Initializer->Info.source : 0, gate->Initializer ? gate->Initializer->Info.clip : 0};
        result = gate->Commit(gate->Context, gate, action, &snapshot, request);
        if (result == 0 && action == 0) gate->Closed = true;
        if (result == 0 && (action == 1 || action == 8)) gate->CutSequence = owner->Entered;
        if (result == 0 && (action == 3 || action == 11)) gate->Initializer = nullptr;
        if (result == 0 && (action == 4 || action == 10)) {
            ConsumeInitializationResourceLocked(owner, gate->Initializer);
            if (owner->WatchClass) CommitPlayerWatchLocked(owner);
            gate->Closed = false;
            if (gate->Initializer) { owner->FreshBlocked = false; owner->LowerPitchBlocked = false; owner->AudioBlocked = false; gate->Initializer->Info.phase = 7; }
        }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return result;
}

// Only the closed group issuer supplies the already pinned exact clip CALL.
extern "C" __declspec(dllexport) int __cdecl RtPlaybackSourceConfigureInitialization(void* raw, void* caller) {
    auto* owner = FindOwner(raw); if (!owner || !caller) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    bool valid = owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed && owner->Faults == 0
        && !owner->StartClipCaller && owner->InFlight == 0 && !owner->FreshBlocked;
    void* target = nullptr;
    for (const auto& slot : slots) if (slot.OwnerValue == owner && slot.Configured == 1 && slot.Role == RtSourceSetClip) target = slot.Target;
    __try {
        const auto* bytes = static_cast<const unsigned char*>(caller) - 5;
        valid = valid && target && bytes[0] == 0xe8 && bytes + 5 + *reinterpret_cast<const std::int32_t*>(bytes + 1) == target;
    } __except(EXCEPTION_EXECUTE_HANDLER) { valid = false; }
    if (valid) owner->StartClipCaller = reinterpret_cast<std::uint64_t>(caller);
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
extern "C" __declspec(dllexport) int __cdecl RtPlaybackSourceReadInitialization(void* raw, RizPlaybackInitializationInfoV2* output) {
    auto* owner = FindOwner(raw);
    if (!owner || !output || output->version != 2 || output->size != sizeof(*output)) return 0;
    AcquireSRWLockShared(&owner->Lock);
    auto* value = owner->Initializer;
    if (value) {
        *output = value->Info; output->blocked = owner->FreshBlocked ? 1u : 0u;
        // A candidate projection is not consumable authority. The eventual
        // private joint transfer must also bind roots, host run and this record.
        output->receipt = value->Info.phase == 4 && owner->Flags == RtSourceArmed && !owner->Faults && !owner->InFlight
            && owner->Writer == value->Info.writer && owner->Source == value->Info.sourceFence
            && owner->Scheduling == value->Info.scheduling && InitializationResourceReadyLocked(owner, value)
            ? reinterpret_cast<std::uint64_t>(value) : 0;
    } else {
        *output = {}; output->version = 2; output->size = sizeof(*output);
        output->owner = reinterpret_cast<std::uint64_t>(owner); output->ownerEpoch = owner->Epoch;
        output->blocked = owner->FreshBlocked ? 1u : 0u;
    }
    ReleaseSRWLockShared(&owner->Lock); return 1;
}
extern "C" __declspec(dllexport) int __cdecl RtPlaybackSourceEnterFresh(void* raw) {
    auto* owner = FindOwner(raw); if (!owner) return 0;
    // The only nested order is GetTime.authorityGate -> Owner.Lock. This leaf
    // performs raw SEH reads only; the borrower selects its result then leaves.
    AcquireSRWLockExclusive(&owner->Lock);
    const auto backing = CheckUnityBackingLocked(owner);
    if (backing != 1) { ReleaseSRWLockExclusive(&owner->Lock); return backing; }
    if ((owner->LowerPitchEnabled || owner->LowerTimeEnabled) && (owner->Faults || (owner->Flags & RtSourceUnknown))) {
        ReleaseSRWLockExclusive(&owner->Lock); return 3;
    }
    if (owner->LowerPitchBlocked || owner->AudioBlocked) { ReleaseSRWLockExclusive(&owner->Lock); return 2; }
    if (owner->Flags != RtSourceArmed || owner->Faults || owner->FreshBlocked) {
        ReleaseSRWLockExclusive(&owner->Lock); return 0;
    }
    return 1;
}
extern "C" __declspec(dllexport) void __cdecl RtPlaybackSourceLeaveFresh(void* raw) {
    auto* owner = FindOwner(raw); if (owner) ReleaseSRWLockExclusive(&owner->Lock);
}

extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackInitializationVersion() { return 2; }
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackSourceBeginInitializationHandoff(
    void* raw, void* candidate, void* request) {
    LogicalGate* gate = nullptr;
    for (auto& owner : owners) if (owner.Logical && owner.Logical == raw) { gate = owner.Logical; break; }
    if (!gate || !candidate || !request) return 1;
    auto* owner = gate->Value; AcquireSRWLockExclusive(&owner->Lock);
    auto* value = owner->Initializer; std::uint32_t result = 0;
    if (owner->Flags != RtSourceArmed || owner->Faults || owner->Thread != GetCurrentThreadId()) result = 3;
    else if (gate->Closed || !owner->FreshBlocked || value != candidate || value->Info.phase != 4) result = 2;
    else if (owner->InFlight || owner->Writer != value->Info.writer || owner->Source != value->Info.sourceFence
        || owner->Scheduling != value->Info.scheduling || !ReadFinalGraph(value)
        || !InitializationResourceReadyLocked(owner, value)) result = 8;
    else {
        const RizLogicalSourceSnapshot snapshot{reinterpret_cast<std::uint64_t>(owner), owner->Epoch,
            owner->Entered, owner->Exited, owner->InFlight, owner->Writer, owner->Source, owner->Scheduling,
            reinterpret_cast<std::uint64_t>(value), value->Info.player, value->Info.source, value->Info.clip};
        result = gate->Commit(gate->Context, gate, 5, &snapshot, request);
        if (!result) { gate->Initializer = value; gate->Closed = true; value->Info.phase = 6; }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return result;
}

// Separate first-capture entry. Only the native initializer record supplies the
// graph; the coordinator cannot supply source identity or a source epoch.
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackInitialCaptureVersion() { return 1; }
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackSourceBeginInitialCapture(
    void* raw, void* candidate, void* request) {
    LogicalGate* gate = nullptr;
    for (auto& owner : owners) if (owner.Logical && owner.Logical == raw) { gate = owner.Logical; break; }
    if (!gate || !candidate || !request) return 1;
    auto* owner = gate->Value; AcquireSRWLockExclusive(&owner->Lock);
    auto* value = owner->Initializer; std::uint32_t result = 0;
    if (owner->Flags != RtSourceArmed || owner->Faults || owner->Thread != GetCurrentThreadId()) result = 3;
    else if (gate->Closed || !owner->FreshBlocked || value != candidate || value->Info.phase != 4) result = 2;
    else if (owner->InFlight || owner->Writer != value->Info.writer || owner->Source != value->Info.sourceFence
        || owner->Scheduling != value->Info.scheduling || !ReadFinalGraph(value)
        || !InitializationResourceReadyLocked(owner, value)) result = 8;
    else {
        const RizLogicalSourceSnapshot snapshot{reinterpret_cast<std::uint64_t>(owner), owner->Epoch,
            owner->Entered, owner->Exited, owner->InFlight, owner->Writer, owner->Source, owner->Scheduling,
            reinterpret_cast<std::uint64_t>(value), value->Info.player, value->Info.source, value->Info.clip};
        result = gate->Commit(gate->Context, gate, 7, &snapshot, request);
        if (!result) { gate->Initializer = value; gate->Closed = true; value->Info.phase = 6; }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return result;
}

#include "PlaybackPlayerWatch.inc"
#include "PlaybackProgressWatch.inc"
#include "PlaybackUnityBackingWatch.inc"
#include "PlaybackLowerPitch.inc"
#include "PlaybackEnginePitch.inc"
#include "PlaybackScheduledEnginePitch.inc"
#include "PlaybackLowerTime.inc"
#include "PlaybackFreshSource.inc"
#include "PlaybackFreshClock.inc"
