#pragma once
#include <cstdint>
#include "EngineTransportPublication.h"
#include "EngineTransportAdmissionFacts.h"

namespace RizEngineTransportAdmissionCrypto {
using U8 = std::uint8_t;
using U32 = std::uint32_t;
using U64 = std::uint64_t;

struct Sha256 {
    U32 state[8];
    U8 pending[64];
    U64 bytes;
    U32 used;

    static U32 Rotate(U32 value, U32 count) {
        return (value >> count) | (value << (32 - count));
    }

    void Initialize() {
        constexpr U32 initial[8]{
            0x6a09e667u, 0xbb67ae85u, 0x3c6ef372u, 0xa54ff53au,
            0x510e527fu, 0x9b05688cu, 0x1f83d9abu, 0x5be0cd19u
        };
        for (U32 i = 0; i < 8; ++i) state[i] = initial[i];
        for (U32 i = 0; i < 64; ++i) pending[i] = 0;
        bytes = 0;
        used = 0;
    }

    void Block() {
        constexpr U32 constants[64]{
            0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
            0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u, 0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
            0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
            0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
            0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u, 0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
            0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
            0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
            0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u, 0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u
        };
        U32 words[64];
        for (U32 i = 0; i < 16; ++i) {
            words[i] = (U32(pending[i * 4]) << 24) | (U32(pending[i * 4 + 1]) << 16)
                     | (U32(pending[i * 4 + 2]) << 8) | pending[i * 4 + 3];
        }
        for (U32 i = 16; i < 64; ++i) {
            words[i] = words[i - 16]
                     + (Rotate(words[i - 15], 7) ^ Rotate(words[i - 15], 18) ^ (words[i - 15] >> 3))
                     + words[i - 7]
                     + (Rotate(words[i - 2], 17) ^ Rotate(words[i - 2], 19) ^ (words[i - 2] >> 10));
        }
        U32 a = state[0], b = state[1], c = state[2], d = state[3];
        U32 e = state[4], f = state[5], g = state[6], h = state[7];
        for (U32 i = 0; i < 64; ++i) {
            const U32 one = h + (Rotate(e, 6) ^ Rotate(e, 11) ^ Rotate(e, 25)) + ((e & f) ^ (~e & g)) + constants[i] + words[i];
            const U32 two = (Rotate(a, 2) ^ Rotate(a, 13) ^ Rotate(a, 22)) + ((a & b) ^ (a & c) ^ (b & c));
            h = g; g = f; f = e; e = d + one;
            d = c; c = b; b = a; a = one + two;
        }
        state[0] += a; state[1] += b; state[2] += c; state[3] += d;
        state[4] += e; state[5] += f; state[6] += g; state[7] += h;
    }

    void Feed(const void* source, U32 count) {
        const auto* data = static_cast<const U8*>(source);
        bytes += count;
        for (U32 i = 0; i < count; ++i) {
            pending[used++] = data[i];
            if (used == 64) {
                Block();
                used = 0;
            }
        }
    }

    void FeedU32(U32 value) {
        U8 le[4]{
            static_cast<U8>(value & 0xffu),
            static_cast<U8>((value >> 8) & 0xffu),
            static_cast<U8>((value >> 16) & 0xffu),
            static_cast<U8>((value >> 24) & 0xffu)
        };
        Feed(le, 4);
    }

    void FeedU64(U64 value) {
        U8 le[8]{};
        for (U32 i = 0; i < 8; ++i) le[i] = static_cast<U8>((value >> (i * 8)) & 0xffu);
        Feed(le, 8);
    }

    void Finish(U8* digest) {
        const U64 bits = bytes * 8;
        const U8 one = 0x80u, zero = 0u;
        Feed(&one, 1);
        while (used != 56) Feed(&zero, 1);
        U8 suffix[8];
        for (U32 i = 0; i < 8; ++i) suffix[i] = static_cast<U8>((bits >> (56 - i * 8)) & 0xffu);
        Feed(suffix, 8);
        for (U32 i = 0; i < 8; ++i) {
            for (U32 part = 0; part < 4; ++part) {
                digest[i * 4 + part] = static_cast<U8>((state[i] >> (24 - part * 8)) & 0xffu);
            }
        }
    }
};

inline bool ParseHex32(const char* text, U8* out) {
    for (U32 i = 0; i < 32; ++i) {
        U32 value = 0;
        for (U32 n = 0; n < 2; ++n) {
            const char ch = text[i * 2 + n];
            const U32 digit = (ch >= '0' && ch <= '9') ? U32(ch - '0')
                              : (ch >= 'a' && ch <= 'f') ? U32(ch - 'a' + 10)
                              : (ch >= 'A' && ch <= 'F') ? U32(ch - 'A' + 10) : 16u;
            if (digit > 15u) return false;
            value = (value << 4) | digit;
        }
        out[i] = static_cast<U8>(value);
    }
    return text[64] == 0;
}

inline bool ComputeAdmissionAuthoritySha256(
    const RizEngineTransportPublicationV5& pub,
    const char* interiorClosureHex,
    const char* coldTraceHex,
    U8* out32) {
    U8 closure[32], trace[32];
    if (!ParseHex32(interiorClosureHex, closure) || !ParseHex32(coldTraceHex, trace)) return false;
    Sha256 h;
    h.Initialize();
    h.Feed(pub.fixedContractSha256, 32);
    h.Feed(closure, 32);
    h.Feed(trace, 32);
    h.Feed(pub.dependencyGraphSha256, 32);
    for (U32 i = 0; i < 5; ++i) h.Feed(pub.modules[i].sha256, 32);
    h.FeedU32(pub.mainThread);
    h.FeedU32(pub.mainThreadProof);
    h.FeedU32(pub.startupStatic);
    h.FeedU32(pub.attachReason);
    h.Finish(out32);
    return true;
}

inline void ComputeExclusivitySha256(
    const RizEngineTransportPublicationV5& pub,
    U32 witnessFlags,
    U8* out32) {
    Sha256 h;
    h.Initialize();
    h.FeedU32(witnessFlags);
    h.FeedU32(pub.admission.earliestThreadId);
    h.FeedU32(pub.admission.preWriteThreadCount);
    h.FeedU32(pub.admission.suspendedThreadCount);
    h.FeedU32(pub.admission.nonMainInteriorRipCount);
    h.FeedU32(pub.admission.postCommitThreadCount);
    h.FeedU64(pub.committedWindows);
    h.FeedU64(pub.configuredFamilies);
    h.Feed(pub.fixedContractSha256, 32);
    h.Finish(out32);
}
} // namespace RizEngineTransportAdmissionCrypto
