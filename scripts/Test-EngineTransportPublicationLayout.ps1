#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw 'Absolute paths required.' }
    $full=[IO.Path]::GetFullPath($Path)
    if ($full -match '[\r\n"%&|<>^]') { throw 'Unsupported command path characters.' }
    $full
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$output=Absolute $OutputDirectory;$vcvarsPath=Absolute $VcVars
if (Test-Path -LiteralPath $output) { throw 'Use a fresh nonexistent output directory.' }
$cpp=Join-Path $repo 'native/RizToolbox.EngineTransportPublicationFixture/PublicationLayout.cpp'
$header=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportPublication.h'
$inputs=@($cpp,$header,$PSCommandPath) | ForEach-Object { [ordered]@{path=$_;sha256=Hash $_} }
$null=New-Item -ItemType Directory -Path $output
$result=[ordered]@{schemaVersion=1;kind='engine-transport-publication-layout-checks';passed=$false;inputs=$inputs
    startedUtc=[DateTime]::UtcNow.ToString('o');buildExitCode=$null;childExitCode=$null;child=$null;files=@()
    gameExecuted=$false;installerExecuted=$false;productionAdmissionVerified=$false;failure=$null}
try {
    $fixture=Join-Path $output 'EngineTransport.PublicationLayout.exe'
    $command=Join-Path $output 'build-layout.cmd'
    [IO.File]::WriteAllLines($command,@('@echo off','setlocal DisableDelayedExpansion',
        "call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /W4 /WX /O2 /MD /guard:cf /Fo`"$output\PublicationLayout.obj`" `"$cpp`" /link /OUT:`"$fixture`" /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF",'exit /b %errorlevel%'),[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $command 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $result.buildExitCode=$LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw 'MSVC layout/static_assert build failed.' }
    $childText=& $fixture
    $result.childExitCode=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $output 'child-result.json'),($childText -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
    if ($LASTEXITCODE -ne 0) { throw 'Owned layout fixture failed.' }
    $child=($childText -join "`n") | ConvertFrom-Json
    if ($child.kind -ne 'engine-transport-publication-layout' -or $child.passed -ne $true -or
        $child.version -ne 5 -or $child.magic -ne 0x3556425550545a52UL -or $child.publicationBytes -ne 91376 -or
        $child.alignment -ne 8 -or $child.moduleCount -ne 5 -or $child.typedCount -ne 13 -or $child.siteCount -ne 23 -or
        $child.windowCount -ne 36 -or $child.familyCount -ne 12 -or $child.installerExecuted -ne $false -or
        $child.gameExecuted -ne $false -or $child.productionAdmissionVerified -ne $false) { throw 'Actual compiled layout differs from the V5 contract.' }
    foreach ($row in $inputs) { if ((Hash $row.path) -ne $row.sha256) { throw 'Source changed during layout checks.' } }
    $result.child=$child;$result.passed=$true
} catch { $result.failure=$_.Exception.Message;throw }
finally {
    $result.files=@(Get-ChildItem -LiteralPath $output -File | ForEach-Object {
        [ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}
    })
    $result.completedUtc=[DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText((Join-Path $output 'layout-result.json'),($result | ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
}
Write-Output "V5 actual MSVC layout verified: $output"
