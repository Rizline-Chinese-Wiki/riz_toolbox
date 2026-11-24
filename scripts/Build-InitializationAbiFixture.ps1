#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [string]$OutputDirectory
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot 'native\RizToolbox.InitializationAbiFixture'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\native-initialization-abi' }
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
if (-not (Test-Path -LiteralPath $vcvarsPath -PathType Leaf)) { throw "Missing existing VS x64 environment: $vcvarsPath" }
foreach ($path in @($sourceRoot, $nativeOutput, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$dll = Join-Path $nativeOutput 'RizToolbox.InitializationAbiFixture.dll'
$probe = Join-Path $nativeOutput 'InitializationAbi.Probe.exe'
$cmdPath = Join-Path $nativeOutput 'build-native.cmd'
$commands = @(
    '@echo off',
    "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\Entries.obj`" `"$sourceRoot\Entries.asm`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /LD /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRT_INIT_BUILD /Fo`"$nativeOutput\Fixture.obj`" /Fd`"$nativeOutput\Fixture.compiler.pdb`" `"$sourceRoot\Fixture.cpp`" `"$nativeOutput\Entries.obj`" /link /OUT:`"$dll`" /IMPLIB:`"$nativeOutput\RizToolbox.InitializationAbiFixture.lib`" /PDB:`"$nativeOutput\Fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\Probe.obj`" /Fd`"$nativeOutput\Probe.compiler.pdb`" `"$sourceRoot\Probe.cpp`" `"$nativeOutput\RizToolbox.InitializationAbiFixture.lib`" /link /OUT:`"$probe`" /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /exports `"$dll`" > `"$nativeOutput\exports.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /disasm `"$dll`" > `"$nativeOutput\disassembly.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$dll`" > `"$nativeOutput\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native initialization ABI build failed: $LASTEXITCODE" }
$taskExportText = Get-Content -LiteralPath (Join-Path $nativeOutput 'exports.txt') -Raw
foreach ($taskExportName in @('RtVoidTarget', 'RtVoidExpectedCaller', 'RtVoidControl', 'RtVoidInvoke', 'RtVoidInvokeWorker', 'RtVoidRead', 'RtEventMidConfigure', 'RtEventMidExpectedReturn', 'RtEventMidControlBefore', 'RtEventMidBeforeState', 'RtEventMidResumeBefore')) {
    if ($taskExportText -notmatch ('(?m)\b' + [regex]::Escape($taskExportName) + '\s*$')) {
        throw "Joint native fixture export is missing: $taskExportName"
    }
}
& $probe 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'native-probe-result.json')
if ($LASTEXITCODE -ne 0) { throw "Native initialization ABI probe failed: $LASTEXITCODE" }
$files = @($dll, $probe) | ForEach-Object {
    [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ schemaVersion=1; kind='offline-initialization-native-fixture'; managedTransportVerified=$false; gameRuntimeVerified=$false; compilerEnvironment=$vcvarsPath; files=@($files) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $nativeOutput 'native-manifest.json') -Encoding utf8
Write-Output "Native output: $nativeOutput"
