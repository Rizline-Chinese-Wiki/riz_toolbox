// Offline owned-call producer. It is not linked into the shipped helper and installs no game hook.
// The native stub models the pinned stock ordering while every audio/DSP operation calls the
// actual managed NativeRuntime accessor. Only calls through this owned lane are observed.
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <new>

using Callback = int(__cdecl*)(void*, std::uint32_t, std::uint64_t, double*);
enum Op : std::uint32_t { Active = 1, Playing, Dsp, Media, PauseAudio, StopAudio, SetTime, SetPitch, Schedule, SetClip };
enum Kind : std::uint32_t { Pause = 1, Replay = 2, Resume = 3 };

struct Snapshot {
    std::uint32_t Version, Size;
    std::uint64_t Epoch, Source, Player, SourceSequence, PitchSequence, OccurrenceSequence;
    std::uint64_t ScheduleSequence, ScheduleOccurrence, StockPitchSequence, AttemptSequence;
    std::uint32_t ActiveOccurrence, OccurrenceKind, OccurrenceStatus, ScheduleReturned;
    std::uint32_t StockPitchReturned, AttemptReturned, PitchActive, Reserved;
    double ScheduleDsp, BeforeDsp, AfterDsp;
};
static_assert(sizeof(Snapshot) == 144, "Managed/native playback owned snapshot size differs.");
struct Producer {
    Callback Call;
    void* Context;
    DWORD Thread;
    Snapshot State;
};
static volatile LONG64 nextEpoch;

static std::uint64_t Bits(double value) { std::uint64_t result; std::memcpy(&result, &value, 8); return result; }
static bool Valid(Producer* value) { return value && value->Thread == GetCurrentThreadId(); }
static bool Call(Producer* value, Op op, double input = 0, double* output = nullptr) {
    double ignored = 0;
    return value->Call(value->Context, op, Bits(input), output ? output : &ignored) == 1;
}
static bool Pitch(Producer* value, float pitch, std::uint64_t& attempt) {
    if (value->State.PitchSequence == UINT64_MAX) return false;
    attempt = ++value->State.PitchSequence; // Native Before, before actual runtime_invoke.
    ++value->State.PitchActive;
    const auto returned = Call(value, SetPitch, pitch);
    --value->State.PitchActive;
    return returned;
}
static void Origin(Producer* value, bool hasValue, double origin = 0) {
    auto* raw = reinterpret_cast<unsigned char*>(value->State.Player) + 0x18;
    std::memset(raw, 0, 16);
    raw[0] = hasValue ? 1 : 0;
    if (hasValue) std::memcpy(raw + 8, &origin, 8);
}
static bool Scheduled(Producer* value, double dsp) {
    if (value->State.ScheduleSequence == UINT64_MAX) return false;
    ++value->State.ScheduleSequence;
    value->State.ScheduleOccurrence = value->State.OccurrenceSequence;
    value->State.ScheduleDsp = dsp; // The exact same double bits are passed to the actual call.
    value->State.ScheduleReturned = 0;
    if (!Call(value, Schedule, dsp)) return false;
    value->State.ScheduleReturned = 1;
    return true;
}
static void Copy(Producer* value, Snapshot* output, std::uint64_t attempt = 0, bool returned = false) {
    if (!output) return;
    *output = value->State;
    output->AttemptSequence = attempt;
    output->AttemptReturned = returned ? 1u : 0u;
}

extern "C" __declspec(dllexport) Producer* __cdecl RizPlaybackOwned_Create(
    Callback call, void* context, std::uint64_t source, std::uint64_t player) {
    if (!call || !source || !player) return nullptr;
    auto* value = new(std::nothrow) Producer{};
    if (!value) return nullptr;
    value->Call = call; value->Context = context; value->Thread = GetCurrentThreadId();
    value->State.Version = 1; value->State.Size = sizeof(Snapshot);
    value->State.Epoch = static_cast<std::uint64_t>(InterlockedIncrement64(&nextEpoch));
    value->State.Source = source; value->State.Player = player;
    value->State.SourceSequence = value->State.PitchSequence = value->State.OccurrenceSequence = 1;
    return value;
}
extern "C" __declspec(dllexport) int __cdecl RizPlaybackOwned_Destroy(Producer* value) {
    if (!Valid(value) || value->State.ActiveOccurrence || value->State.PitchActive) return 0;
    delete value; return 1;
}
extern "C" __declspec(dllexport) int __cdecl RizPlaybackOwned_Read(Producer* value, Snapshot* output) {
    if (!Valid(value) || !output) return 0;
    Copy(value, output); return 1;
}
extern "C" __declspec(dllexport) int __cdecl RizPlaybackOwned_WritePitch(Producer* value,
    std::uint64_t epoch, std::uint64_t sourceSequence, std::uint64_t occurrenceSequence,
    std::uint64_t pitchSequence, float pitch, Snapshot* output) {
    if (!Valid(value) || !output || value->State.Epoch != epoch || value->State.SourceSequence != sourceSequence
        || value->State.OccurrenceSequence != occurrenceSequence || value->State.PitchSequence != pitchSequence
        || !std::isfinite(pitch) || pitch <= 0) return 0;
    std::uint64_t attempt = 0;
    const auto returned = Pitch(value, pitch, attempt);
    Copy(value, output, attempt, returned);
    return returned ? 1 : 2; // Callback failure remains an entered attempt, never a false precondition.
}
extern "C" __declspec(dllexport) int __cdecl RizPlaybackOwned_SetClip(Producer* value,
    std::uint64_t clip, Snapshot* output) {
    if (!Valid(value) || !output || value->State.SourceSequence == UINT64_MAX) return 0;
    ++value->State.SourceSequence; // Even same-source/same-clip writes invalidate the old source fence.
    double ignored = 0;
    const auto returned = value->Call(value->Context, SetClip, clip, &ignored) == 1;
    Copy(value, output); return returned ? 1 : 2;
}
extern "C" __declspec(dllexport) int __cdecl RizPlaybackOwned_Run(Producer* value,
    std::uint32_t kind, float delay, Snapshot* output) {
    if (!Valid(value) || !output || value->State.ActiveOccurrence || value->State.PitchActive
        || kind < Pause || kind > Resume || !std::isfinite(delay) || delay < 0
        || kind != Pause && delay < 0.10000000149011612f
        || value->State.OccurrenceSequence == UINT64_MAX) return 0;
    ++value->State.OccurrenceSequence; // One private occurrence even when the original is a no-op.
    value->State.ActiveOccurrence = 1; value->State.OccurrenceKind = kind; value->State.OccurrenceStatus = 1;
    value->State.ScheduleReturned = value->State.StockPitchReturned = 0;
    value->State.StockPitchSequence = 0;
    bool returned = [&]() {
        double active = 0, playing = 0;
        if (!Call(value, Active, 0, &active) || active != 1 || !Call(value, Dsp, 0, &value->State.BeforeDsp)
            || !Call(value, Playing, 0, &playing)) return false;
        if (kind == Pause) {
            if (playing == 1) { if (!Call(value, PauseAudio)) return false; Origin(value, false); }
            return Call(value, Dsp, 0, &value->State.AfterDsp);
        }
        if (playing == 1) return true; // Stock skipped: managed consumer requires an actual schedule witness.
        double saved = 0;
        if (kind == Resume && !Call(value, Media, 0, &saved)) return false;
        if (!Call(value, StopAudio) || kind == Resume && !Call(value, SetTime, saved)) return false;
        double dsp = 0;
        if (!Call(value, Dsp, 0, &dsp) || !std::isfinite(dsp) || dsp < 0) return false;
        const auto start = dsp + static_cast<double>(delay);
        if (!std::isfinite(start)) return false;
        Origin(value, true, start);
        std::uint64_t reset = 0;
        if (!Pitch(value, 1.0f, reset)) { value->State.StockPitchSequence = reset; return false; }
        value->State.StockPitchSequence = reset; value->State.StockPitchReturned = 1;
        if (!Scheduled(value, start)) return false;
        if (kind == Resume) {
            double actual = 0;
            if (!Call(value, Media, 0, &actual)) return false;
            Origin(value, true, start - actual);
        }
        return Call(value, Dsp, 0, &value->State.AfterDsp);
    }();
    value->State.OccurrenceStatus = returned ? 2u : 3u;
    value->State.ActiveOccurrence = 0;
    Copy(value, output); return returned ? 1 : 2;
}
