#requires -Version 7.2
[CmdletBinding()]
param([string]$VcVars = '')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
$vc = Get-RizVcVarsPath -Path $VcVars
$sourcePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'native/RizToolbox.EngineBootstrap/EngineTransportFirstInstall.cpp'
$source = [IO.File]::ReadAllText($sourcePath)
# Compile the repository implementation without linking or starting the game.
$start = $source.IndexOf('U32 Rotate(', [StringComparison]::Ordinal)
$end = $source.IndexOf('const IMAGE_NT_HEADERS64* ImageHeader(', [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Could not locate the native SHA-256 implementation.' }
$implementation = $source.Substring($start, $end - $start)

$prefix = @"
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#include <bcrypt.h>
#include <intrin.h>
#include <immintrin.h>
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

using U8 = std::uint8_t;
using U32 = std::uint32_t;
using U64 = std::uint64_t;
void Copy(void* destination, const void* source, size_t bytes) {
    __movsb(static_cast<U8*>(destination), static_cast<const U8*>(source), bytes);
}
void Zero(void* destination, size_t bytes) {
    __stosb(static_cast<U8*>(destination), 0, bytes);
}
U8 fileReadBuffer[65536]{};
"@

$fixture = @"
int main() {
    constexpr U32 lengths[]{0, 1, 55, 56, 57, 63, 64, 65, 119, 120, 121, 127, 128, 129,
        4095, 4096, 4097, 65535, 65536, 65537, 131071, 131072, 131073, 1048576};
    constexpr U32 chunks[]{1, 63, 64, 65, 8191, 65536};
    std::vector<U8> data(lengths[_countof(lengths) - 1]);
    for (U32 i = 0; i < data.size(); ++i)
        data[i] = static_cast<U8>(i * 73 + i / 7919);

    BCRYPT_ALG_HANDLE algorithm{};
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0)
        return 1;

    const bool hardware = HasHardwareSha256();
    for (const U32 length : lengths) {
        U8 expected[32], actual[32];
        if (BCryptHash(algorithm, nullptr, 0, data.data(), length, expected, sizeof(expected)) < 0)
            return 2;

        Hash(data.data(), length, actual);
        if (std::memcmp(expected, actual, sizeof(actual)) != 0) return 3;
        for (const U32 chunk : chunks) {
            for (unsigned method = 0; method < (hardware ? 2u : 1u); ++method) {
                Sha256 hash;
                hash.Initialize();
                for (U32 offset = 0; offset < length;) {
                    const U32 count = std::min(chunk, length - offset);
                    hash.FeedFile(data.data() + offset, count, method == 1);
                    offset += count;
                }
                hash.Finish(actual);
                if (std::memcmp(expected, actual, sizeof(actual)) != 0) {
                    std::printf("Digest mismatch for length %u, chunk %u, hardware %u\n", length, chunk, method);
                    return 4;
                }
            }
        }

        HANDLE file = CreateFileW(L"input.bin", GENERIC_READ | GENERIC_WRITE, 0, nullptr,
            CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr);
        if (file == INVALID_HANDLE_VALUE) return 5;
        DWORD written = 0;
        const bool valid = WriteFile(file, data.data(), length, &written, nullptr) && written == length
            && !HashFile(file, U64(length) + 1, actual)
            && (!length || !HashFile(file, length - 1, actual))
            && (HashFile(file, length, actual) == (length != 0))
            && (!length || std::memcmp(expected, actual, sizeof(actual)) == 0);
        CloseHandle(file);
        if (!valid) {
            std::printf("Unexpected file result for length %u\n", length);
            return 6;
        }
    }
    BCryptCloseAlgorithmProvider(algorithm, 0);
    std::printf("SHA-256 equivalence passed for %zu lengths using BCrypt reference digests; SHA-NI %s.\n",
        _countof(lengths), hardware ? "tested" : "unavailable");
    return 0;
}
"@

$work = (New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('riz-bootstrap-sha-' + [guid]::NewGuid()))).FullName
[IO.File]::WriteAllText((Join-Path $work 'test.cpp'), ($prefix, $implementation, $fixture -join [Environment]::NewLine))
[IO.File]::WriteAllLines((Join-Path $work 'test.cmd'), @(
    '@echo off', 'setlocal DisableDelayedExpansion', ('call "' + $vc + '" >nul'), 'if errorlevel 1 exit /b 1',
    'cl /nologo /std:c++17 /O2 /Oi /W4 /WX /EHsc test.cpp bcrypt.lib', 'if errorlevel 1 exit /b 1', 'test.exe'))
$startInfo = [Diagnostics.ProcessStartInfo]::new($env:ComSpec, '/d /v:off /c test.cmd')
$startInfo.WorkingDirectory = $work
$startInfo.UseShellExecute = $false
$process = [Diagnostics.Process]::Start($startInfo)
try {
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "SHA-256 verification failed with exit code $($process.ExitCode); files at $work" }
} finally {
    $process.Dispose()
}
Write-Output "Standalone fixture files are at $work"
