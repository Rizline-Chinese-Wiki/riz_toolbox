#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "../CoroutineContext.h"

// This executable owns every receiver, original and observer it invokes. It
// loads only the supplied helper; it does not load or call a game runtime.
namespace {
constexpr DWORD OriginalSeh = 0xE173C071;
constexpr ULONG_PTR ExceptionPayload = 0xC0710123456789ABULL;
constexpr std::uint64_t OwnerSeal = 0xC071A11CE1234567ULL;
constexpr std::uint8_t MoveResult = 0xA7;
unsigned checks{}, failures{}, replacementCalls{}, exceptionCases{}, frameGuardCases{};
HMODULE fixtureModule{}, helperModule{};
decltype(&RizCoroutineContext_GetStats) getStats{};
decltype(&RizCoroutineContext_ReadConfigurationV1) readConfiguration{};

void Check(bool value, const char* expression, int line)
{
    ++checks;
    if (!value) { ++failures; std::fprintf(stderr, "FAILED coroutine configuration %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)

template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto address = GetProcAddress(module, name); CHECK(address != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(address));
    std::memcpy(&result, &address, sizeof(result)); return result;
}
HMODULE ModuleOf(const void* address)
{
    HMODULE module{};
    if (address) GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(address), &module);
    return module;
}
bool GameModulesAbsent()
{
    return !GetModuleHandleW(L"GameAssembly.dll") && !GetModuleHandleW(L"UnityPlayer.dll");
}
struct Role;
struct OwnedObject { Role* role; std::uint64_t seal; };
struct Invocation {
    bool abnormal, returned;
    unsigned phase, enterCalls, originalCalls, exitCalls;
    std::uintptr_t token, result;
    std::uint32_t flags;
    RizCoroutineFrameV1 enteredFrame;
};
struct Owner { Role* role; std::uint64_t seal, completed, originals, enters, exits; Invocation* active; };
struct Role {
    std::uint32_t kind, slot, beforeStatus, configureStatus, readStatus, duplicateStatus, duplicateReadStatus;
    const char* name;
    void* entry;
    bool configured;
    unsigned normalCalls, abnormalCalls;
    Owner owner;
    OwnedObject self, enumerator, method, resultObject;
    RizCoroutineConfigV1 expected;
    RizCoroutineConfigurationV1 actual;
    RizCoroutineStatsV1 statistics;
};
Role roles[3]{};
OwnedObject replacementUser{};

RizCoroutineConfigurationV1 ReadHeader()
{
    RizCoroutineConfigurationV1 value{}; value.abiVersion = 1; value.byteSize = sizeof(value); return value;
}
RizCoroutineStatsV1 Stats(const Role& role)
{
    RizCoroutineStatsV1 value{}; value.abiVersion = 1; value.byteSize = sizeof(value);
    CHECK(getStats(role.kind, role.slot, &value) == 0); return value;
}
void CheckInFlight(const Role& role)
{
    const auto value = Stats(role);
    CHECK(value.entered == role.owner.completed + 1 && value.exited == role.owner.completed && value.inFlight == 1);
    CHECK(value.enterFaults == 0 && value.exitFaults == 0);
}
bool WellFormedFrame(const RizCoroutineFrameV1* frame)
{
    return frame && frame->abiVersion == 1 && frame->byteSize == sizeof(*frame)
        && frame->kind <= RizCoroutineFactory && frame->reserved == 0 && frame->caller;
}
void CheckFrame(const Role& role, const RizCoroutineFrameV1* frame)
{
    CHECK(WellFormedFrame(frame));
    if (!WellFormedFrame(frame)) return;
    CHECK(frame->kind == role.kind && frame->self == &role.self && frame->method == &role.method);
    CHECK(frame->enumerator == (role.kind == RizCoroutineStart ? &role.enumerator : nullptr));
    CHECK(ModuleOf(frame->caller) == fixtureModule);
}
Owner* FindOwner(void* user)
{
    for (auto& role : roles) if (user == &role.owner) return &role.owner;
    CHECK(false); return nullptr;
}
std::uintptr_t ExpectedResult(const Role& role)
{
    return role.kind == RizCoroutineMoveNext ? MoveResult : reinterpret_cast<std::uintptr_t>(&role.resultObject);
}
std::uintptr_t __cdecl Enter(const RizCoroutineFrameV1* frame, void* user)
{
    auto* owner = FindOwner(user); if (!owner) return 0;
    auto* call = owner->active; CHECK(call && owner->seal == OwnerSeal); if (!call) return 0;
    const auto& role = *owner->role; CheckFrame(role, frame); if (!WellFormedFrame(frame)) return 0;
    CHECK(call->phase == 0 && call->enterCalls == 0); call->phase = 1; ++call->enterCalls; ++owner->enters;
    call->enteredFrame = *frame; CheckInFlight(role); return call->token;
}
void __cdecl Exit(const RizCoroutineFrameV1* frame, std::uintptr_t token, std::uintptr_t result,
    std::uint32_t flags, void* user)
{
    auto* owner = FindOwner(user); if (!owner) return;
    auto* call = owner->active; CHECK(call && owner->seal == OwnerSeal); if (!call) return;
    const auto& role = *owner->role; CheckFrame(role, frame); if (!WellFormedFrame(frame)) return;
    CHECK(std::memcmp(frame, &call->enteredFrame, sizeof(*frame)) == 0);
    CHECK(call->phase == 2 && call->exitCalls == 0 && token == call->token);
    CHECK(flags == (call->abnormal ? RizCoroutineAbnormal : RizCoroutineReturned));
    CHECK(result == (call->abnormal ? 0 : ExpectedResult(role)));
    call->phase = 3; ++call->exitCalls; ++owner->exits; call->flags = flags; call->result = result; CheckInFlight(role);
}
Role* Original(std::uint32_t kind, void* self, void* enumerator, const void* method)
{
    Role* role{}; for (auto& candidate : roles) if (self == &candidate.self) role = &candidate;
    CHECK(role != nullptr); if (!role) return nullptr;
    auto* call = role->owner.active; CHECK(call != nullptr); if (!call) return nullptr;
    CHECK(kind == role->kind && method == &role->method && role->self.seal == OwnerSeal);
    CHECK(enumerator == (kind == RizCoroutineStart ? &role->enumerator : nullptr));
    CHECK(call->phase == 1 && call->originalCalls == 0); call->phase = 2; ++call->originalCalls; ++role->owner.originals;
    CheckInFlight(*role);
    if (call->abnormal) { const ULONG_PTR payload[]{ ExceptionPayload, kind }; RaiseException(OriginalSeh, 0, 2, payload); }
    return role;
}
__declspec(noinline) void* __cdecl StartOriginal(void* self, void* enumerator, const void* method)
{
    auto* role = Original(RizCoroutineStart, self, enumerator, method); return role ? &role->resultObject : nullptr;
}
__declspec(noinline) std::uint8_t __cdecl MoveOriginal(void* self, const void* method)
{
    if (!Original(RizCoroutineMoveNext, self, nullptr, method)) return 0; return MoveResult;
}
__declspec(noinline) void* __cdecl FactoryOriginal(void* self, const void* method)
{
    auto* role = Original(RizCoroutineFactory, self, nullptr, method); return role ? &role->resultObject : nullptr;
}
void* __cdecl ReplacementStart(void*, void*, const void*) { ++replacementCalls; return nullptr; }
std::uint8_t __cdecl ReplacementMove(void*, const void*) { ++replacementCalls; return 0; }
void* __cdecl ReplacementFactory(void*, const void*) { ++replacementCalls; return nullptr; }
std::uintptr_t __cdecl ReplacementEnter(const RizCoroutineFrameV1*, void*) { ++replacementCalls; return 0; }
void __cdecl ReplacementExit(const RizCoroutineFrameV1*, std::uintptr_t, std::uintptr_t, std::uint32_t, void*) { ++replacementCalls; }

void CheckReadback(const Role& role, const RizCoroutineConfigurationV1& value)
{
    CHECK(value.abiVersion == 1 && value.byteSize == sizeof(value) && value.configured == 1);
    CHECK(value.kind == role.kind && value.slot == role.slot && value.reserved == 0 && value.entry == role.entry);
    const auto& actual = value.configuration; const auto& expected = role.expected;
    CHECK(actual.abiVersion == expected.abiVersion && actual.byteSize == expected.byteSize);
    CHECK(actual.original == expected.original && actual.enter == expected.enter && actual.exit == expected.exit && actual.user == expected.user);
}
void CheckUnconfigured(const RizCoroutineConfigurationV1& value)
{
    CHECK(value.abiVersion == 1 && value.byteSize == sizeof(value));
    const RizCoroutineConfigV1 zero{};
    CHECK(value.configured == 0 && value.kind == 0 && value.slot == 0 && value.reserved == 0 && value.entry == nullptr);
    CHECK(std::memcmp(&value.configuration, &zero, sizeof(zero)) == 0);
}
void ConfigureRole(Role& role, decltype(&RizCoroutineContext_Configure) configure,
    decltype(&RizCoroutineContext_GetEntry) getEntry, std::uint32_t limit)
{
    role.entry = getEntry(role.kind, role.slot); CHECK(role.entry && ModuleOf(role.entry) == helperModule);
    auto value = ReadHeader(); std::memset(reinterpret_cast<unsigned char*>(&value) + 8, 0xA5, sizeof(value) - 8);
    role.beforeStatus = readConfiguration(role.kind, role.slot, &value); CHECK(role.beforeStatus == 2); CheckUnconfigured(value);
    CHECK(readConfiguration(role.kind, role.slot, nullptr) == 1);
    value = ReadHeader(); value.abiVersion = 0; CHECK(readConfiguration(role.kind, role.slot, &value) == 1);
    value = ReadHeader(); --value.byteSize; CHECK(readConfiguration(role.kind, role.slot, &value) == 1);
    value = ReadHeader(); CHECK(readConfiguration(3, role.slot, &value) == 1);
    value = ReadHeader(); CHECK(readConfiguration(role.kind, limit, &value) == 1);
    CHECK(configure(role.kind, role.slot, nullptr) == 1);
    auto invalid = role.expected; invalid.abiVersion = 0; CHECK(configure(role.kind, role.slot, &invalid) == 1);
    invalid = role.expected; --invalid.byteSize; CHECK(configure(role.kind, role.slot, &invalid) == 1);
    CHECK(configure(3, role.slot, &role.expected) == 1 && configure(role.kind, limit, &role.expected) == 1);
    value = ReadHeader(); CHECK(readConfiguration(role.kind, role.slot, &value) == 2); CheckUnconfigured(value);
    role.configureStatus = configure(role.kind, role.slot, &role.expected); CHECK(role.configureStatus == 0);
    if (role.configureStatus != 0 || !role.entry) return;
    role.configured = true; role.actual = ReadHeader();
    role.readStatus = readConfiguration(role.kind, role.slot, &role.actual); CHECK(role.readStatus == 0); CheckReadback(role, role.actual);
    auto replacement = role.expected;
    replacement.original = role.kind == RizCoroutineStart ? reinterpret_cast<void*>(&ReplacementStart)
        : role.kind == RizCoroutineMoveNext ? reinterpret_cast<void*>(&ReplacementMove) : reinterpret_cast<void*>(&ReplacementFactory);
    replacement.enter = &ReplacementEnter; replacement.exit = &ReplacementExit; replacement.user = &replacementUser;
    role.duplicateStatus = configure(role.kind, role.slot, &replacement); CHECK(role.duplicateStatus == 2);
    role.actual = ReadHeader(); role.duplicateReadStatus = readConfiguration(role.kind, role.slot, &role.actual);
    CHECK(role.duplicateReadStatus == 0); CheckReadback(role, role.actual);
    role.statistics = Stats(role); CHECK(role.statistics.entered == 0 && role.statistics.exited == 0 && role.statistics.inFlight == 0);
}
// POD locals keep the native original's SEH on the genuine helper unwind path.
__declspec(noinline) void Execute(Role& role, bool abnormal)
{
    Invocation call{}; call.abnormal = abnormal;
    call.token = static_cast<std::uintptr_t>(0xC071000000000000ULL | (static_cast<std::uint64_t>(role.kind) << 8) | (role.owner.completed + 1));
    role.owner.active = &call; std::uintptr_t result{};
    __try {
        if (role.kind == RizCoroutineStart) result = reinterpret_cast<std::uintptr_t>(reinterpret_cast<void*(__cdecl*)(void*, void*, const void*)>(role.entry)(&role.self, &role.enumerator, &role.method));
        else if (role.kind == RizCoroutineFactory) result = reinterpret_cast<std::uintptr_t>(reinterpret_cast<void*(__cdecl*)(void*, const void*)>(role.entry)(&role.self, &role.method));
        else result = reinterpret_cast<std::uint8_t(__cdecl*)(void*, const void*)>(role.entry)(&role.self, &role.method);
        call.returned = true; CHECK(result == ExpectedResult(role));
    }
    __finally {
        CHECK(call.enterCalls == 1 && call.originalCalls == 1 && call.exitCalls == 1 && call.phase == 3);
        CHECK(call.returned == !abnormal); role.owner.active = nullptr; ++role.owner.completed;
        if (abnormal) ++role.abnormalCalls; else ++role.normalCalls;
        role.statistics = Stats(role);
        CHECK(role.statistics.entered == role.owner.completed && role.statistics.exited == role.owner.completed && role.statistics.inFlight == 0);
        CHECK(role.statistics.enterFaults == 0 && role.statistics.exitFaults == 0);
    }
}
int OriginalFilter(EXCEPTION_POINTERS* exception)
{
    const auto* record = exception->ExceptionRecord;
    CHECK(record->ExceptionCode == OriginalSeh);
    if (record->ExceptionCode != OriginalSeh) return EXCEPTION_CONTINUE_SEARCH;
    CHECK(record->NumberParameters == 2 && record->ExceptionInformation[0] == ExceptionPayload
        && record->ExceptionInformation[1] == RizCoroutineMoveNext);
    return EXCEPTION_EXECUTE_HANDLER;
}
void ExecuteOriginalException(Role& role)
{
    bool caught{};
    __try { Execute(role, true); }
    __except (OriginalFilter(GetExceptionInformation())) { caught = true; ++exceptionCases; }
    CHECK(caught && role.owner.active == nullptr && Stats(role).inFlight == 0);
}
void CheckLocalFrameGuards()
{
    // These guard tests exercise this fixture's observer validation, not a raw
    // frame injection API: native GetEntry constructs its own immutable frame.
    RizCoroutineFrameV1 frame{ 1, sizeof(frame), RizCoroutineMoveNext, 0, &roles[1].self, nullptr, &roles[1].method, &roles[1] };
    CHECK(WellFormedFrame(&frame)); CHECK(!WellFormedFrame(nullptr)); ++frameGuardCases;
    frame.abiVersion = 0; CHECK(!WellFormedFrame(&frame)); ++frameGuardCases; frame.abiVersion = 1;
    --frame.byteSize; CHECK(!WellFormedFrame(&frame)); ++frameGuardCases; frame.byteSize = sizeof(frame);
    frame.kind = 3; CHECK(!WellFormedFrame(&frame)); ++frameGuardCases;
}
void PrintResult(bool modulesAbsent)
{
    std::uint64_t originals{}, enters{}, exits{}, inFlight{}; unsigned configured{}, readbacks{}, duplicates{}, normal{};
    for (const auto& role : roles) {
        originals += role.owner.originals; enters += role.owner.enters; exits += role.owner.exits; inFlight += role.statistics.inFlight;
        configured += role.configured ? 1u : 0u; readbacks += role.configured && role.readStatus == 0 && role.duplicateReadStatus == 0 ? 1u : 0u;
        duplicates += role.configured && role.duplicateStatus == 2 ? 1u : 0u; normal += role.normalCalls;
    }
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-coroutine-configuration-fixture\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"configurationBytes\":%zu,\"configBytes\":%zu,\"frameBytes\":%zu,\"slotsPerKind\":32,\"configuredKinds\":%u,\"readbackMatches\":%u,\"duplicateConfigureRejected\":%u,\"normalEntryCalls\":%u,\"originalExceptionCases\":%u,\"localMalformedFrameGuardCases\":%u,\"originalCalls\":%llu,\"entered\":%llu,\"exited\":%llu,\"inFlight\":%llu,\"replacementCalls\":%u,\"gameModulesAbsent\":%s,\"gameCodeExecuted\":false,\"gameRuntimeVerified\":false,\"productionFirstWriteAdmission\":false,\"installed\":false,\"roles\":[",
        failures ? "false" : "true", checks, failures, sizeof(RizCoroutineConfigurationV1), sizeof(RizCoroutineConfigV1), sizeof(RizCoroutineFrameV1),
        configured, readbacks, duplicates, normal, exceptionCases, frameGuardCases, static_cast<unsigned long long>(originals),
        static_cast<unsigned long long>(enters), static_cast<unsigned long long>(exits), static_cast<unsigned long long>(inFlight), replacementCalls, modulesAbsent ? "true" : "false");
    for (unsigned index = 0; index != 3; ++index) {
        const auto& role = roles[index]; const auto& value = role.actual; const auto& config = value.configuration; const auto& stats = role.statistics;
        std::printf("%s{\"name\":\"%s\",\"kind\":%u,\"slot\":%u,\"beforeStatus\":%u,\"configureStatus\":%u,\"readStatus\":%u,\"duplicateConfigureStatus\":%u,\"duplicateReadStatus\":%u,\"configured\":%u,\"readbackKind\":%u,\"readbackSlot\":%u,\"reserved\":%u,\"entry\":\"%p\",\"configAbiVersion\":%u,\"configByteSize\":%u,\"original\":\"%p\",\"enter\":\"%p\",\"exit\":\"%p\",\"user\":\"%p\",\"originalCalls\":%llu,\"normalCalls\":%u,\"exceptionCalls\":%u,\"entered\":%llu,\"exited\":%llu,\"inFlight\":%llu,\"enterFaults\":%llu,\"exitFaults\":%llu}",
            index ? "," : "", role.name, role.kind, role.slot, role.beforeStatus, role.configureStatus, role.readStatus, role.duplicateStatus,
            role.duplicateReadStatus, value.configured, value.kind, value.slot, value.reserved, value.entry, config.abiVersion, config.byteSize,
            config.original, reinterpret_cast<void*>(config.enter), reinterpret_cast<void*>(config.exit), config.user,
            static_cast<unsigned long long>(role.owner.originals), role.normalCalls, role.abnormalCalls,
            static_cast<unsigned long long>(stats.entered), static_cast<unsigned long long>(stats.exited), static_cast<unsigned long long>(stats.inFlight),
            static_cast<unsigned long long>(stats.enterFaults), static_cast<unsigned long long>(stats.exitFaults));
    }
    std::printf("]}\n");
}
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) { std::fprintf(stderr, "Expected the owned helper DLL path.\n"); return 2; }
    fixtureModule = GetModuleHandleW(nullptr); CHECK(fixtureModule && GameModulesAbsent());
    roles[0].kind = RizCoroutineStart; roles[0].slot = 5; roles[0].name = "Start";
    roles[1].kind = RizCoroutineMoveNext; roles[1].slot = 9; roles[1].name = "MoveNext";
    roles[2].kind = RizCoroutineFactory; roles[2].slot = 13; roles[2].name = "Factory";
    for (auto& role : roles) {
        role.owner.role = &role; role.owner.seal = OwnerSeal;
        role.self = role.enumerator = role.method = role.resultObject = { &role, OwnerSeal };
        role.expected = { 1, sizeof(role.expected), role.kind == RizCoroutineStart ? reinterpret_cast<void*>(&StartOriginal)
            : role.kind == RizCoroutineMoveNext ? reinterpret_cast<void*>(&MoveOriginal) : reinterpret_cast<void*>(&FactoryOriginal), &Enter, &Exit, &role.owner };
        CHECK(ModuleOf(role.expected.original) == fixtureModule && ModuleOf(reinterpret_cast<void*>(role.expected.enter)) == fixtureModule
            && ModuleOf(reinterpret_cast<void*>(role.expected.exit)) == fixtureModule);
    }
    helperModule = LoadLibraryW(argv[1]); CHECK(helperModule != nullptr);
    if (!helperModule) { PrintResult(GameModulesAbsent()); return 1; }
    const auto getLimit = Symbol<decltype(&RizCoroutineContext_GetLimit)>(helperModule, "RizCoroutineContext_GetLimit");
    const auto getFrameSize = Symbol<decltype(&RizCoroutineContext_GetFrameSize)>(helperModule, "RizCoroutineContext_GetFrameSize");
    const auto getEntry = Symbol<decltype(&RizCoroutineContext_GetEntry)>(helperModule, "RizCoroutineContext_GetEntry");
    const auto configure = Symbol<decltype(&RizCoroutineContext_Configure)>(helperModule, "RizCoroutineContext_Configure");
    getStats = Symbol<decltype(getStats)>(helperModule, "RizCoroutineContext_GetStats");
    readConfiguration = Symbol<decltype(readConfiguration)>(helperModule, "RizCoroutineContext_ReadConfigurationV1");
    if (getLimit && getFrameSize && getEntry && configure && getStats && readConfiguration) {
        const auto limit = getLimit(); CHECK(limit == 32 && getFrameSize() == sizeof(RizCoroutineFrameV1));
        CHECK(getEntry(3, 0) == nullptr && getEntry(RizCoroutineStart, limit) == nullptr);
        CheckLocalFrameGuards();
        for (auto& role : roles) ConfigureRole(role, configure, getEntry, limit);
        for (auto& role : roles) if (role.configured) Execute(role, false);
        if (roles[1].configured) ExecuteOriginalException(roles[1]);
        for (auto& role : roles) if (role.configured) {
            role.actual = ReadHeader(); CHECK(readConfiguration(role.kind, role.slot, &role.actual) == 0); CheckReadback(role, role.actual);
            CHECK(role.owner.originals == role.owner.completed && role.owner.enters == role.owner.completed && role.owner.exits == role.owner.completed);
        }
        CHECK(replacementCalls == 0 && exceptionCases == 1);
    }
    const bool modulesAbsent = GameModulesAbsent(); CHECK(modulesAbsent);
    CHECK(FreeLibrary(helperModule)); PrintResult(modulesAbsent); return failures ? 1 : 0;
}
