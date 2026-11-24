#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$HelperDirectory,
    [string]$DobbyPath
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\void-admission-native' }
if (-not $DobbyPath) { $DobbyPath = Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll' }
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$helperDll = Join-Path ([IO.Path]::GetFullPath($HelperDirectory)) 'RizToolbox.JudgeContext.dll'
$dobbyDll = [IO.Path]::GetFullPath($DobbyPath)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$source = Join-Path $repoRoot 'native\RizToolbox.JudgeContext\fixture\VoidGateFixture.cpp'
foreach ($path in @($source, $nativeOutput, $helperDll, $dobbyDll, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($vcvarsPath, $helperDll, $dobbyDll)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Build the native helper first; required file is missing: $path" }
}
if ((Get-FileHash -LiteralPath $dobbyDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') {
    throw 'UnsupportedBuild: the void admission fixture requires the inspected Dobby image.'
}
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$fixture = Join-Path $nativeOutput 'VoidAdmission.Fixture.exe'
$cmdPath = Join-Path $nativeOutput 'build-void-admission-fixture.cmd'
$commands = @(
    '@echo off', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\VoidGateFixture.obj`" /Fd`"$nativeOutput\VoidGateFixture.compiler.pdb`" `"$source`" /link /OUT:`"$fixture`" /PDB:`"$nativeOutput\VoidAdmission.Fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$fixture`" > `"$nativeOutput\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native void admission fixture build failed: $LASTEXITCODE" }
& $fixture $helperDll $dobbyDll 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'fixture-result.json')
if ($LASTEXITCODE -ne 0) { throw "Native void admission fixture failed: $LASTEXITCODE" }
$files = @($helperDll, $dobbyDll, $fixture) | ForEach-Object {
    [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ schemaVersion = 1; kind = 'native-void-admission-gate-fixture'; initialInstallSafetyVerified = $false; gameRuntimeVerified = $false; threadSuspensionUsed = $false; files = @($files) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-manifest.json') -Encoding utf8
Write-Output "Native void admission fixture: $nativeOutput"
