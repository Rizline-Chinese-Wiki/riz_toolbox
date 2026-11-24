#include "../RizToolbox.EngineBootstrap/EngineTransportPublication.h"
#include <cstdio>
#include <type_traits>

static_assert(std::is_standard_layout<RizEngineTransportPublicationV5>::value);
static_assert(std::is_trivially_copyable<RizEngineTransportPublicationV5>::value);
static_assert(RizEngineTransportPublicationLayout::TypedCount == 13);
static_assert(RizEngineTransportPublicationLayout::SiteCount == 23);
static_assert(RizEngineTransportPublicationLayout::WindowCount == 36);
static_assert(RizEngineTransportPublicationLayout::FamilyCount == 12);

int main()
{
    using namespace RizEngineTransportPublicationLayout;
    const int written = std::printf(
        "{\"schemaVersion\":1,\"kind\":\"engine-transport-publication-layout\",\"passed\":true,"
        "\"version\":%u,\"magic\":%llu,\"publicationBytes\":%zu,\"alignment\":%zu,"
        "\"moduleBytes\":%zu,\"typedBytes\":%zu,\"siteBytes\":%zu,\"familyBytes\":%zu,"
        "\"windowBytes\":%zu,\"runtimeFunctionBytes\":%zu,\"resourceBytes\":%zu,\"eventBytes\":%zu,"
        "\"modulesOffset\":%zu,\"typedOffset\":%zu,\"sitesOffset\":%zu,\"familiesOffset\":%zu,"
        "\"windowsOffset\":%zu,\"runtimeFunctionsOffset\":%zu,\"resourcesOffset\":%zu,\"eventsOffset\":%zu,"
        "\"moduleCount\":%u,\"typedCount\":%u,\"siteCount\":%u,\"windowCount\":%u,\"familyCount\":%u,"
        "\"selfOwnedOnly\":true,\"installerExecuted\":false,\"gameExecuted\":false,\"productionAdmissionVerified\":false}\n",
        Version, static_cast<unsigned long long>(Magic), sizeof(RizEngineTransportPublicationV5), alignof(RizEngineTransportPublicationV5),
        sizeof(RizEngineTransportModuleV5), sizeof(RizEngineTransportTypedRowV5), sizeof(RizEngineTransportSiteV5),
        sizeof(RizEngineTransportFamilyV5), sizeof(RizEngineTransportWindowV5), sizeof(RizEngineTransportRuntimeFunctionV5),
        sizeof(RizEngineTransportResourceV5), sizeof(RizEngineTransportEventV5),
        offsetof(RizEngineTransportPublicationV5, modules), offsetof(RizEngineTransportPublicationV5, typed),
        offsetof(RizEngineTransportPublicationV5, sites), offsetof(RizEngineTransportPublicationV5, families),
        offsetof(RizEngineTransportPublicationV5, windows), offsetof(RizEngineTransportPublicationV5, runtimeFunctions),
        offsetof(RizEngineTransportPublicationV5, resources), offsetof(RizEngineTransportPublicationV5, events),
        ModuleCount, TypedCount, SiteCount, WindowCount, FamilyCount);
    return written > 0 && std::fflush(stdout) == 0 ? 0 : 1;
}
