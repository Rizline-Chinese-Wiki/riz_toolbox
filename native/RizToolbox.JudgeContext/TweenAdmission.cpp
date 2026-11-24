#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <array>
#include <cstring>
#include <limits>
#include "TweenAdmission.h"
#include "FirstInstall.h"
#include "OperationContext.h"
#include "TweenRetirementContext.h"

#if defined(RIZ_TWEEN_ADMISSION_FIXTURE)
extern "C" bool RizTweenAdmissionFixture_Validate(const RizTweenAdmissionConfigV1*);
extern "C" bool RizTweenAdmissionFixture_Ready();
#endif
namespace {
constexpr std::uint32_t capacity = 128;
SRWLOCK gate = SRWLOCK_INIT;
struct Owner {
    void* object{}; const void* cached{};
    std::uint64_t epoch{}, issuers{}, entries{}, admitted{}, denied{}, dead{};
    std::uint32_t phase = RizTweenEmpty;
};
std::array<Owner, capacity> owners{};
RizTweenAdmissionConfigV1 policy{};
std::uint32_t configured = 0, unknown = 0;
std::uint64_t epochSource = 0, configurationEpoch = 0, calls = 0, originals = 0;
std::uint64_t skippedDead = 0, skippedSealed = 0, unknownReads = 0, claims = 0;
struct Locked {
    Locked() { AcquireSRWLockExclusive(&gate); }
    ~Locked() { ReleaseSRWLockExclusive(&gate); }
};
bool Ready()
{
#if defined(RIZ_TWEEN_ADMISSION_FIXTURE)
    return RizTweenAdmissionFixture_Ready();
#else
    RizFirstInstallInfoV7 info{}; info.publication.common.abiVersion = 7;
    info.publication.common.byteSize = sizeof(info);
    return RizFirstInstall_GetInfoV7(&info) == 0 && info.publication.common.phase == RizFirstInstallReady
        && info.publication.common.unknown == 0 && info.publication.common.writesStarted == 64
        && info.operationCapabilityVersion == 4 && info.operationCount == 26 && info.expectedWrites == 64
        && info.configuredMask == 0x3ffffff && info.publishedMask == 0x3ffffff
        && reinterpret_cast<HMODULE>(info.publication.common.moduleBase) == GetModuleHandleW(L"GameAssembly.dll");
#endif
}
// Caller object references are borrowed only for the duration of the real native
// method entry. A registered owner separately retains its actual strong root.
// Never use a failing read, null argument or nonexact class as a dead object.
bool ReadObject(void* object, const void*& actual, const void*& cached)
{
    actual = nullptr; cached = nullptr;
    if (!object || (reinterpret_cast<std::uintptr_t>(object) & 7u) != 0) return false;
    __try {
        actual = *static_cast<void* volatile*>(object);
        if (actual != policy.exactTweenClass) return true;
        cached = *reinterpret_cast<void* volatile*>(static_cast<unsigned char*>(object) + 16);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
#if !defined(RIZ_TWEEN_ADMISSION_FIXTURE)
template<class T> T Api(HMODULE module, const char* name, std::uintptr_t rva)
{
    auto pointer = GetProcAddress(module, name);
    if (reinterpret_cast<std::uintptr_t>(pointer) != reinterpret_cast<std::uintptr_t>(module) + rva) return nullptr;
    T result{}; static_assert(sizeof(result) == sizeof(pointer)); std::memcpy(&result, &pointer, sizeof(result)); return result;
}
bool Equal(const char* value, const char* expected)
{
    if (!value) return false;
    for (std::size_t i = 0; i != 128; ++i) {
        if (value[i] != expected[i]) return false;
        if (expected[i] == 0) return true;
    }
    return false;
}
bool ValidateMetadata(const RizTweenAdmissionConfigV1& config)
{
    using Ptr = const void*(__cdecl*)(const void*);
    using Text = const char*(__cdecl*)(const void*);
    using Pred = bool(__cdecl*)(const void*);
    using Flags = std::uint32_t(__cdecl*)(const void*);
    using Offset = std::size_t(__cdecl*)(const void*);
    using FindClass = const void*(__cdecl*)(const void*, const char*, const char*);
    using FindField = const void*(__cdecl*)(const void*, const char*);
    using ValueSize = std::int32_t(__cdecl*)(const void*, std::uint32_t*);
    const auto module = GetModuleHandleW(L"GameAssembly.dll"); if (!module) return false;
    const auto className = Api<Text>(module,"il2cpp_class_get_name",0x283b90);
    const auto classNamespace = Api<Text>(module,"il2cpp_class_get_namespace",0x283ba0);
    const auto classImage = Api<Ptr>(module,"il2cpp_class_get_image",0x283a30);
    const auto imageName = Api<Text>(module,"il2cpp_image_get_name",0x283a30);
    const auto classFromName = Api<FindClass>(module,"il2cpp_class_from_name",0x283a90);
    const auto parent = Api<Ptr>(module,"il2cpp_class_get_parent",0x283bc0);
    const auto findField = Api<FindField>(module,"il2cpp_class_get_field_from_name",0x283b30);
    const auto fieldName = Api<Text>(module,"il2cpp_field_get_name",0x283a30);
    const auto fieldParent = Api<Ptr>(module,"il2cpp_field_get_parent",0x283b90);
    const auto fieldType = Api<Ptr>(module,"il2cpp_field_get_type",0x283e50);
    const auto fieldFlags = Api<Flags>(module,"il2cpp_field_get_flags",0x283e30);
    const auto fieldOffset = Api<Offset>(module,"il2cpp_field_get_offset",0x283e40);
    const auto byref = Api<Pred>(module,"il2cpp_type_is_byref",0x284e60);
    const auto typeCode = Api<Flags>(module,"il2cpp_type_get_type",0x284e50);
    const auto typeClass = Api<Ptr>(module,"il2cpp_class_from_type",0x2646d0);
    const auto generic = Api<Pred>(module,"il2cpp_class_is_generic",0x283c80);
    const auto inflated = Api<Pred>(module,"il2cpp_class_is_inflated",0x283c90);
    const auto valueType = Api<Pred>(module,"il2cpp_class_is_valuetype",0x2baf0);
    const auto valueSize = Api<ValueSize>(module,"il2cpp_class_value_size",0x283d00);
    if (!className || !classNamespace || !classImage || !imageName || !classFromName || !parent || !findField
        || !fieldName || !fieldParent || !fieldType || !fieldFlags || !fieldOffset || !byref || !typeCode || !typeClass || !generic || !inflated
        || !valueType || !valueSize) return false;
    const auto tween = config.exactTweenClass;
    const auto assembly = classImage(tween);
    if (!Equal(className(tween),"TweenInfo") || !Equal(classNamespace(tween),"")
        || !Equal(imageName(assembly),"Assembly-CSharp.dll") || classFromName(assembly,"","TweenInfo") != tween
        || generic(tween) || inflated(tween)) return false;
    const auto mono = parent(tween); const auto unity = classImage(mono);
    if (!Equal(imageName(unity),"UnityEngine.CoreModule.dll")) return false;
    const char* chain[]{"MonoBehaviour","Behaviour","Component","Object"};
    auto current = mono;
    for (const auto* name : chain) {
        if (!current || classImage(current) != unity || !Equal(classNamespace(current),"UnityEngine")
            || !Equal(className(current),name) || classFromName(unity,"UnityEngine",name) != current) return false;
        if (!Equal(name,"Object")) current = parent(current);
    }
    const auto field = config.cachedPointerField;
    if (findField(current,"m_CachedPtr") != field || findField(tween,"m_CachedPtr") != field
        || fieldParent(field) != current || !Equal(fieldName(field),"m_CachedPtr")
        || (fieldFlags(field) & (0x10u | 0x40u)) != 0 || fieldOffset(field) != 16) return false;
    const auto type = fieldType(field); const auto cls = typeClass(type);
    std::uint32_t alignment{};
    return type && cls && !byref(type) && typeCode(type) == 0x18 && valueType(cls) && valueSize(cls,&alignment) == 8 && alignment == 8
        && Equal(className(cls),"IntPtr") && Equal(classNamespace(cls),"System")
        && Equal(imageName(classImage(cls)),"mscorlib.dll") && classFromName(classImage(cls),"System","IntPtr") == cls;
}
#endif
bool Validate(const RizTweenAdmissionConfigV1& config)
{
    __try {
#if defined(RIZ_TWEEN_ADMISSION_FIXTURE)
        return RizTweenAdmissionFixture_Validate(&config);
#else
        return ValidateMetadata(config);
#endif
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool Header(const RizTweenAdmissionReceiptV1* value)
{ return value && value->abiVersion == 1 && value->byteSize == sizeof(*value) && value->reserved == 0; }
void Fault()
{ unknown = 1; RizTweenRetirementFault(); }
void Fault(Owner& owner)
{ owner.phase = RizTweenUnknown; Fault(); }
bool Increment(std::uint64_t& value)
{
    if (value == (std::numeric_limits<std::uint64_t>::max)()) { Fault(); return false; }
    ++value; return true;
}
void Copy(std::uint32_t slot, const Owner& owner, RizTweenAdmissionReceiptV1* output)
{
    *output = {1,sizeof(*output),slot,owner.phase,owner.epoch,owner.object,policy.exactTweenClass,owner.cached,
        owner.issuers,owner.entries,owner.admitted,owner.denied,owner.dead,owner.phase == RizTweenUnknown ? 1u : 0u,0};
}
bool Observe(Owner& owner)
{
    const void* actual{}; const void* cached{};
    if (!ReadObject(owner.object,actual,cached) || actual != policy.exactTweenClass) { ++unknownReads; Fault(owner); return false; }
    owner.cached = cached; return true;
}
// 0 idle, 1 genuinely busy, 2 invalid/unknown. Never call an unknown lane a
// retryable pending interval; that would conceal permanent native faults.
unsigned FiveLanesState()
{
    constexpr std::uint32_t kinds[]{0,1,1,0,0}, slots[]{25,20,19,24,23};
    bool busy = false;
    for (unsigned i = 0; i != 5; ++i) {
        RizOperationBootstrapInfoV1 info{1,sizeof(info)};
        if (RizOperationBootstrap_GetInfo(kinds[i],slots[i],&info) != 0 || info.unknown != 0
            || info.enterFaults != 0 || info.exitFaults != 0) return 2;
        if (info.inFlight != 0) busy = true;
    }
    return busy ? 1u : 0u;
}
}
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Configure(const RizTweenAdmissionConfigV1* config)
{
    if (!config || config->abiVersion != 1 || config->byteSize != sizeof(*config) || !config->exactTweenClass
        || !config->cachedPointerField || config->cachedPointerOffset != 16 || config->reserved != 0) return 1;
    if (!Ready()) return 2;
    const auto candidate = *config;
    if (!Validate(candidate) || !Ready()) return 1;
    Locked locked;
    if (unknown) return 5;
    if (configured) return 4;
    policy = candidate; configured = 1; configurationEpoch = 1; return 0;
}
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Claim(void* object, RizTweenAdmissionReceiptV1* receipt)
{
    if (!Header(receipt) || !object) return 1;
    if (!Ready()) return 2;
    Locked locked;
    if (!configured) return 2;
    if (unknown) return 5;
    for (const auto& owner : owners) if (owner.object == object) return 3;
    // An invocation that crossed dispatch before Claim has no owner token.
    // Reject that cut; future dispatch entrants increment physical activity
    // before taking this same lock and will see the new owner after Claim.
    const auto activity = FiveLanesState();
    if (activity == 2) { Fault(); return 5; }
    if (activity != 0) return 6;
    const void* actual{}; const void* cached{};
    if (!ReadObject(object,actual,cached) || actual != policy.exactTweenClass || !cached) return 1;
    if (epochSource == (std::numeric_limits<std::uint64_t>::max)()) { Fault(); return 5; }
    for (std::uint32_t slot = 0; slot != capacity; ++slot) {
        auto& owner = owners[slot]; if (owner.phase != RizTweenEmpty && owner.phase != RizTweenRetired) continue;
        owner = {}; owner.object = object; owner.cached = cached; owner.epoch = ++epochSource; owner.phase = RizTweenOpen;
        Increment(claims); Copy(slot,owner,receipt); return 0;
    }
    return 4;
}
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Read(std::uint32_t slot, std::uint64_t epoch, RizTweenAdmissionReceiptV1* receipt)
{
    if (!Header(receipt) || slot >= capacity || epoch == 0) return 1;
    Locked locked; if (!configured) return 2;
    auto& owner = owners[slot]; if (owner.epoch != epoch) return 2;
    if (owner.object) Observe(owner);
    Copy(slot,owner,receipt); return unknown || owner.phase == RizTweenUnknown ? 5u : 0u;
}
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Seal(std::uint32_t slot, std::uint64_t epoch, RizTweenAdmissionReceiptV1* receipt)
{
    if (!Header(receipt) || slot >= capacity || epoch == 0) return 1;
    if (!Ready()) return 2;
    Locked locked; if (!configured) return 2;
    auto& owner = owners[slot]; if (owner.epoch != epoch || !owner.object) return 2;
    if (!Observe(owner)) { Copy(slot,owner,receipt); return 5; }
    if (unknown) { Copy(slot,owner,receipt); return 5; }
    if (owner.phase == RizTweenOpen) owner.phase = RizTweenClosing;
    if (owner.phase == RizTweenClosing && owner.issuers == 0) owner.phase = RizTweenSealed;
    Copy(slot,owner,receipt); return owner.phase == RizTweenUnknown ? 5u : owner.phase == RizTweenSealed ? 0u : 3u;
}
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Unregister(std::uint32_t slot, std::uint64_t epoch, RizTweenAdmissionReceiptV1* receipt)
{
    if (!Header(receipt) || slot >= capacity || epoch == 0) return 1;
    if (!Ready()) return 2;
    Locked locked; if (!configured) return 2;
    auto& owner = owners[slot]; if (owner.epoch != epoch || !owner.object) return 2;
    if (!Observe(owner)) { Copy(slot,owner,receipt); return 5; }
    if (unknown || owner.phase == RizTweenUnknown) { Copy(slot,owner,receipt); return 5; }
    const auto activity = FiveLanesState();
    if (activity == 2) { Fault(owner); Copy(slot,owner,receipt); return 5; }
    if (owner.phase != RizTweenSealed || owner.cached || owner.issuers != 0 || owner.entries != 0 || activity != 0) {
        Copy(slot,owner,receipt); return 3;
    }
    owner.phase = RizTweenRetired; Copy(slot,owner,receipt);
    owner.object = nullptr; owner.cached = nullptr; return 0;
}
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_GetInfo(RizTweenAdmissionInfoV1* info)
{
    if (!info || info->abiVersion != 1 || info->byteSize != sizeof(*info)) return 1;
    Locked locked; std::uint64_t active = 0;
    for (const auto& owner : owners) if (owner.object) ++active;
    *info = {1,sizeof(*info),configured,unknown,policy.exactTweenClass,policy.cachedPointerField,16,capacity,
        configurationEpoch,calls,originals,skippedDead,skippedSealed,unknownReads,claims,active}; return 0;
}
RizTweenAdmissionTicket RizTweenAdmissionEnter(std::uint32_t kind, std::uint32_t slot, void* self, void* argument)
{
    RizTweenAdmissionTicket ticket{};
    const bool play = kind == 0 && slot == 25, add = kind == 1 && slot == 20, execute = kind == 1 && slot == 19;
    if (!play && !add && !execute) return ticket;
    Locked locked; if (!configured) return ticket;
    ticket.relevant = true; ticket.issuer = play || add; Increment(calls);
    void* object = play ? self : argument;
    Owner* match = nullptr;
    for (std::uint32_t index = 0; index != capacity; ++index) if (object && owners[index].object == object) {
        match = &owners[index]; ticket.slot = index; ticket.epoch = match->epoch; break;
    }
    const void* actual{}; const void* cached{};
    const bool known = object == nullptr || ReadObject(object,actual,cached);
    if (!known) { Increment(unknownReads); if (match) Fault(*match); return ticket; }
    if (actual != policy.exactTweenClass) {
        if (match) Fault(*match);
        return ticket;
    }
    if (match) {
        match->cached = cached;
        if (!Increment(match->entries)) { Fault(*match); return ticket; }
        ticket.registered = true;
    }
    if (!cached) {
        ticket.skipped = true; Increment(skippedDead); if (match) Increment(match->dead); return ticket;
    }
    if (match && ticket.issuer && match->phase != RizTweenOpen) {
        ticket.skipped = true; Increment(match->denied); Increment(skippedSealed); return ticket;
    }
    if (match && ticket.issuer) {
        // Exhaustion is permanent Unknown. Retain any partially acquired
        // counter responsibility; never normalize it into a false idle proof.
        if (!Increment(match->issuers) || !Increment(match->admitted)) { Fault(*match); ticket.skipped = true; return ticket; }
    }
    return ticket;
}
void RizTweenAdmissionExit(const RizTweenAdmissionTicket& ticket, bool originalCalled, bool returned, bool observerFault)
{
    if (!ticket.relevant) return;
    Locked locked; if (originalCalled) Increment(originals);
    if (!ticket.registered || ticket.slot >= capacity) return;
    auto& owner = owners[ticket.slot];
    if (owner.epoch != ticket.epoch || !owner.object || owner.entries == 0) { Fault(); return; }
    --owner.entries;
    if (ticket.issuer && !ticket.skipped) {
        if (owner.issuers == 0) Fault(owner); else --owner.issuers;
    }
    if ((originalCalled && !returned) || observerFault) Fault(owner);
    if (owner.phase == RizTweenClosing && owner.issuers == 0) owner.phase = RizTweenSealed;
}

std::uint32_t RizTweenAdmissionRetirementBatch(std::uint64_t expectedConfigurationEpoch,
    std::uint32_t count,const RizTweenRetirementClaimV1* groupClaims,RizTweenAdmissionReceiptV1* receipts,bool commit)
{
    if(count>capacity || count&&(!groupClaims || commit&&!receipts))return 1;
    if(!Ready())return 2;
    Locked locked;if(!configured || configurationEpoch!=expectedConfigurationEpoch)return 2;if(unknown)return 5;
    // Validate every owner and every output before ANY registry address is
    // forgotten. The same admission lock serializes even new dead-source calls.
    for(unsigned i=0;i!=count;++i){
        const auto& claim=groupClaims[i];
        if(claim.slot>=capacity || claim.reserved || !claim.epoch || !claim.object || claim.actualClass!=policy.exactTweenClass)return 1;
        for(unsigned j=0;j!=i;++j)if(claim.slot==groupClaims[j].slot || claim.object==groupClaims[j].object)return 1;
        auto& owner=owners[claim.slot];
        if(owner.epoch!=claim.epoch || owner.object!=claim.object)return 2;
        if(!Observe(owner) || unknown || owner.phase==RizTweenUnknown)return 5;
        if(owner.phase!=RizTweenSealed || owner.cached || owner.issuers || owner.entries)return 3;
        if(commit && (!Header(receipts+i)))return 1;
    }
    if(!commit)return 0;
    for(unsigned i=0;i!=count;++i){
        auto& owner=owners[groupClaims[i].slot];owner.phase=RizTweenRetired;Copy(groupClaims[i].slot,owner,receipts+i);
    }
    for(unsigned i=0;i!=count;++i){auto& owner=owners[groupClaims[i].slot];owner.object=nullptr;owner.cached=nullptr;}
    return 0;
}
