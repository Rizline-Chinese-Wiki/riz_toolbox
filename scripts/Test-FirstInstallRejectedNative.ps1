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
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\first-install-rejection-native' }
if (-not $DobbyPath) { $DobbyPath = Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll' }
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$helperDll = Join-Path ([IO.Path]::GetFullPath($HelperDirectory)) 'RizToolbox.JudgeContext.dll'
$dobbyDll = [IO.Path]::GetFullPath($DobbyPath)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$source = Join-Path $repoRoot 'native\RizToolbox.JudgeContext\fixture\FirstInstallRejectionFixture.cpp'
foreach ($path in @($source, $nativeOutput, $helperDll, $dobbyDll, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($source, $vcvarsPath, $helperDll, $dobbyDll)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required existing build input is missing: $path" }
}
if ((Get-FileHash -LiteralPath $dobbyDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') {
    throw 'UnsupportedBuild: the first-install rejection fixture requires the inspected Dobby image.'
}
if(Test-Path -LiteralPath $nativeOutput){throw 'Preserve earlier native fixture evidence; use a fresh output directory.'}
New-Item -ItemType Directory -Path $nativeOutput | Out-Null
$fixture = Join-Path $nativeOutput 'FirstInstall.Rejection.Fixture.exe'
$cmdPath = Join-Path $nativeOutput 'build-first-install-rejection.cmd'
$commands = @(
    '@echo off', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\FirstInstallRejectionFixture.obj`" /Fd`"$nativeOutput\FirstInstallRejectionFixture.compiler.pdb`" `"$source`" /link /OUT:`"$fixture`" /PDB:`"$nativeOutput\FirstInstall.Rejection.Fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native first-install rejection build failed: $LASTEXITCODE" }
$results = foreach ($mode in @('null', 'private', 'module', 'unmapped', 'badpath')) {
    $output = & $fixture $helperDll $dobbyDll $mode 2>&1
    $exitCode = $LASTEXITCODE
    $output | Tee-Object -FilePath (Join-Path $nativeOutput "fixture-$mode.json") | Write-Output | Out-Host
    if ($exitCode -ne 0) { throw "Native first-install rejection mode $mode failed: $exitCode" }
    $result = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    if (-not $result.passed -or $result.writesStarted -ne 0 -or $result.gameLoaded -or $result.gameCodeCalled -or
        $result.v2DisabledVerified -ne $true -or $result.invalidV2HeaderCases -ne 9 -or
        $result.v2CallerIdentityCanonicalized -ne $true -or $result.listenerConfigured -ne $false) {
        throw "Native first-install rejection mode $mode did not prove no-write refusal."
    }
    $result
}
$checks = ($results | Measure-Object -Property checks -Sum).Sum
[ordered]@{ schemaVersion = 1; kind = 'first-install-native-rejections'; passed = $true; checks = $checks; failures = 0; independentProcesses = @($results).Count; writesStarted = 0; gameLoaded = $false; gameCodeCalled = $false; modes = @($results) } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-result.json') -Encoding utf8
$files = @($helperDll, $dobbyDll, $fixture) | ForEach-Object {
    [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ schemaVersion = 1; kind = 'first-install-native-rejections'; files = @($files) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-manifest.json') -Encoding utf8
Write-Output "Native first-install rejection fixture: $nativeOutput"
