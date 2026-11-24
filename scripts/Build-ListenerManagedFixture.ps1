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
$sourceRoot = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/fixture'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/listener-managed-fixture' }
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$source = Join-Path $sourceRoot 'ListenerManagedFixture.cpp'
$header = Join-Path $sourceRoot 'ListenerManagedFixture.h'
$asmSource = Join-Path $sourceRoot 'ListenerCaller.asm'
foreach ($path in @($source, $header, $asmSource, $nativeOutput, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($source, $header, $asmSource, $vcvarsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required listener CLR fixture input is missing: $path" }
}
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$dll = Join-Path $nativeOutput 'RizToolbox.ListenerManagedFixture.dll'
$cmdPath = Join-Path $nativeOutput 'build-listener-managed-fixture.cmd'
$commands = @(
    '@echo off', 'chcp 65001 >nul', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\ListenerManagedCaller.obj`" `"$asmSource`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /LD /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\ListenerManagedFixture.obj`" /Fd`"$nativeOutput\ListenerManagedFixture.compiler.pdb`" `"$source`" `"$nativeOutput\ListenerManagedCaller.obj`" /link /OUT:`"$dll`" /IMPLIB:`"$nativeOutput\RizToolbox.ListenerManagedFixture.lib`" /PDB:`"$nativeOutput\ListenerManagedFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /exports `"$dll`" > `"$nativeOutput\exports.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /disasm `"$dll`" > `"$nativeOutput\disassembly.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$dll`" > `"$nativeOutput\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native listener CLR fixture build failed: $LASTEXITCODE" }
$exports = Get-Content -LiteralPath (Join-Path $nativeOutput 'exports.txt') -Raw
foreach ($name in @('Setup', 'GetInfo', 'GetMethod', 'CreateWork', 'InvokeWork', 'ReadWork', 'WaitHeld', 'Resume')) {
    $exportName = 'RizListenerManaged_' + $name
    if ($exports -notmatch ('(?m)\b' + [regex]::Escape($exportName) + '\s*$')) { throw "Missing fixture export: $exportName" }
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$inputs = @($source, $header, $asmSource, (Join-Path $sourceRoot 'ListenerFixture.h')) | ForEach-Object {
    [ordered]@{ path=$_; sha256=(Hash $_) }
}
[ordered]@{
    schemaVersion=1; kind='self-owned-listener-managed-fixture-build'; fixture=$dll; sha256=(Hash $dll)
    inputs=@($inputs); observerAttached=$false; fixtureExecuted=$false; gameMapped=$false
    gameCodeExecuted=$false; firstInstallReceiptUsed=$false; installed=$false
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-build-manifest.json') -Encoding utf8
Write-Output "Native listener CLR fixture DLL: $dll"
