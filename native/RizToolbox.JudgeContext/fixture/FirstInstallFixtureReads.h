#pragma once
#include <cstring>
#include "../FirstInstall.h"

// Fixture-only projections over the single actual V7 export. These helpers are
// not production exports or alternate adoption protocols. Keeping the common
// and listener field assertions independent makes the nested packet testable.
namespace RizFixturePublication {
using PacketReader = std::uint32_t(__cdecl*)(RizFirstInstallInfoV7*);
inline PacketReader reader = nullptr;
inline RizFirstInstallInfoV7 Header()
{
    RizFirstInstallInfoV7 value{};
    value.publication.common.abiVersion = 7;
    value.publication.common.byteSize = sizeof(value);
    return value;
}
inline std::uint32_t Common(RizFirstInstallInfoV1* output)
{
    if (!reader || !output || output->abiVersion != 1 || output->byteSize != sizeof(*output)
        || output->reserved0 != 0 || output->reserved1 != 0) return 1;
    auto packet = Header(); const auto status = reader(&packet); if (status != 0) return status;
    *output = packet.publication.common; output->abiVersion = 1; output->byteSize = sizeof(*output);
    output->reserved1 = 0;
    return 0;
}
inline std::uint32_t Listener(RizFirstInstallInfoV2* output)
{
    if (!reader || !output || output->common.abiVersion != 2 || output->common.byteSize != sizeof(*output)
        || output->common.reserved0 != 0 || output->common.reserved1 != 0 || output->reserved2 != 0) return 1;
    auto packet = Header(); const auto status = reader(&packet); if (status != 0) return status;
    *output = packet.publication; output->common.abiVersion = 2; output->common.byteSize = sizeof(*output);
    output->common.reserved1 = 0;
    return 0;
}
}
