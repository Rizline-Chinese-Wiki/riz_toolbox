#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [Parameter(Mandatory)][string]$OutputDirectory
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$source = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/fixture/EngineNodeFixture.cpp'
$managedSource = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/fixture/EngineNodeManagedFixture.cpp'
$header = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineNodeContext.h'
$managedHeader = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/fixture/EngineNodeManagedFixture.h'
foreach ($path in @($source, $managedSource, $header, $managedHeader, $output, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($source, $managedSource, $header, $managedHeader, $vcvarsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required engine node fixture input is missing: $path" }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$fixture = Join-Path $output 'EngineNode.Fixture.exe'
$managedFixture = Join-Path $output 'RizToolbox.EngineNodeManagedFixture.dll'
$cmdPath = Join-Path $output 'build-engine-node-fixture.cmd'
$commands = @(
    '@echo off', 'chcp 65001 >nul', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\EngineNodeFixture.obj`" /Fd`"$output\EngineNodeFixture.compiler.pdb`" `"$source`" /link bcrypt.lib /OUT:`"$fixture`" /PDB:`"$output\EngineNodeFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "cl /nologo /LD /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\EngineNodeManagedFixture.obj`" /Fd`"$output\EngineNodeManagedFixture.compiler.pdb`" `"$managedSource`" /link /OUT:`"$managedFixture`" /IMPLIB:`"$output\RizToolbox.EngineNodeManagedFixture.lib`" /PDB:`"$output\EngineNodeManagedFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /exports `"$managedFixture`" > `"$output\managed-exports.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$fixture`" > `"$output\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /disasm `"$fixture`" > `"$output\disassembly.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /disasm `"$managedFixture`" > `"$output\managed-disassembly.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native engine node fixture build failed: $LASTEXITCODE" }
$exports = Get-Content -LiteralPath (Join-Path $output 'managed-exports.txt') -Raw
foreach ($name in @('RizEngineNodeManaged_GetTarget', 'RizEngineNodeManaged_Invoke', 'RizEngineNodeManaged_GetCurrent', 'RizEngineNodeManaged_SetBodyObserver')) {
    if ($exports -notmatch ('(?m)\b' + [regex]::Escape($name) + '\s*$')) { throw "Missing joint engine node fixture export: $name" }
}
function Hash([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }
[ordered]@{
    schemaVersion=1; kind='native-engine-node-fixture-build'; fixture=$fixture; fixtureSha256=(Hash $fixture)
    sourceSha256=(Hash $source); headerSha256=(Hash $header); managedSourceSha256=(Hash $managedSource); managedHeaderSha256=(Hash $managedHeader)
    managedFixture=$managedFixture; managedFixtureSha256=(Hash $managedFixture)
    fixtureExecuted=$false; gameCodeExecuted=$false; installed=$false
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'fixture-build-manifest.json') -Encoding utf8
Write-Output "Native engine node fixture: $fixture"
