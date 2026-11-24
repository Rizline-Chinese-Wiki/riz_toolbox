#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceSnapshotManifest,[Parameter(Mandatory)][string]$NativeBuildManifest,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$DobbyPath='C:/Users/71957/Desktop/riz_toolbox/.tools/bepinex-789/BepInEx/core/dobby.dll')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
foreach($path in @($SourceSnapshotManifest,$NativeBuildManifest,$OutputDirectory,$DobbyPath)){
    if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Absolute paths are required.'}
}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
$proof=Get-Content -LiteralPath $SourceSnapshotManifest -Raw|ConvertFrom-Json
$native=Get-Content -LiteralPath $NativeBuildManifest -Raw|ConvertFrom-Json
if($proof.kind -ne 'active-retirement-snapshot-checks' -or $proof.passed -ne $true -or $proof.runnerExitCode -ne 0){throw 'An actual successful current source snapshot is required.'}
if($native.kind -ne 'independent-stock-shaped-source-prototype' -or $native.passed -ne $true -or $native.actualExitCode -ne 0){throw 'An actual successful exact source helper/owned fixture build is required.'}
if((Hash $DobbyPath) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded'){throw 'The inspected Dobby input differs.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve all previous results; use a new output directory.'}
$nativeDirectory=Split-Path -Parent $NativeBuildManifest
$helper=Join-Path $nativeDirectory 'RizToolbox.PlaybackSourcePrototype.dll'
$fixture=Join-Path $nativeDirectory 'RizToolbox.PlaybackStockFixture.dll'
$snapshot=Join-Path (Split-Path -Parent $SourceSnapshotManifest) 'source-snapshot'
$null=New-Item -ItemType Directory -Path $output
$result=[ordered]@{kind='current-native-source-clr-bridge-checks';passed=$false;buildExitCode=$null;checks=0;modes=@();failure=$null
    sourceProof=[ordered]@{path=$SourceSnapshotManifest;sha256=Hash $SourceSnapshotManifest}
    nativeProof=[ordered]@{path=$NativeBuildManifest;sha256=Hash $NativeBuildManifest}
    sources=@();inputs=@();artifacts=@();gameExecuted=$false;installed=$false;productionAdmission=$false}
try{
    foreach($row in @($native.sources)+@($native.frozenSources)+@($native.files)){
        if((Hash $row.path) -ne $row.sha256){throw "A declared native build input/output changed: $($row.path)"}
    }
    $result.inputs=@(@($PSCommandPath,$SourceSnapshotManifest,$NativeBuildManifest,$DobbyPath,$helper,$fixture)|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_}})
    foreach($row in $proof.snapshotFiles){
        if((Hash $row.path) -ne $row.sha256){throw "The current frozen input changed: $($row.path)"}
        $destination=Join-Path $output ('source/'+[IO.Path]::GetRelativePath($snapshot,$row.path))
        $null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $row.path -Destination $destination
        $result.sources+=,[ordered]@{path=$destination;sha256=$row.sha256}
    }
    Json $result.sources (Join-Path $output 'source-before.json')
    $archive=Join-Path $output 'source-snapshot.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $output 'source'),$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
    $result.sourceArchive=[ordered]@{path=$archive;sha256=Hash $archive}
    $project=Join-Path $output 'source/tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj'
    $buildLog=Join-Path $output 'build.log'
    & (Get-RizDotnetPath) build $project -c Release -m:1 -p:RestoreIgnoreFailedSources=true -p:UseSharedCompilation=false --nologo -v:minimal 2>&1|Out-File -LiteralPath $buildLog
    $result.buildExitCode=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $buildLog -Tail 12;throw 'Actual source bridge fixture build failed.'}
    $assembly=Join-Path (Split-Path -Parent $project) 'bin/Release/net8.0/RizToolbox.Runtime.Tests.dll'
    foreach($mode in @('normal','callback-fault','deferred-release','withdraw-inflight')){
        $log=Join-Path $output ($mode+'.log')
        & (Get-RizDotnetPath) $assembly --playback-stock-bridge-checks $helper $fixture $DobbyPath $mode 2>&1|Out-File -LiteralPath $log
        $actualExit=$LASTEXITCODE
        $row=[ordered]@{mode=$mode;actualExitCode=$actualExit;checks=0;log=[ordered]@{path=$log;sha256=Hash $log}}
        $result.modes+=,$row
        if($actualExit -ne 0){Get-Content -LiteralPath $log -Tail 10;throw "Actual source bridge $mode failed."}
        $actual=[IO.File]::ReadAllText($log).Trim()|ConvertFrom-Json
        if($actual.passed -ne $true -or $actual.mode -cne $mode -or $actual.actualDobbyPrepares -ne 8 -or $actual.actualDobbyCommits -ne 8 -or $actual.gameExecuted -ne $false -or $actual.installed -ne $false -or $actual.productionAdmission -ne $false){throw 'Unexpected actual source bridge proof.'}
        $row.checks=$actual.checks;$result.checks+=$row.checks
        Write-Output "$mode`: $($row.checks) actual native/CLR checks passed."
    }
    foreach($row in @($result.sources)+@($result.inputs)){if((Hash $row.path) -ne $row.sha256){throw "A source bridge input changed: $($row.path)"}}
    $result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{
    $artifactFiles=@(Get-ChildItem -LiteralPath $output -File|Where-Object Name -ne 'test-result.json')
    $binaryDirectory=Join-Path $output 'source/tests/RizToolbox.Runtime.Tests/bin/Release/net8.0'
    if(Test-Path -LiteralPath $binaryDirectory){$artifactFiles+=@(Get-ChildItem -LiteralPath $binaryDirectory -File -Recurse)}
    $result.artifacts=@($artifactFiles|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}})
    Json $result (Join-Path $output 'test-result.json')
}
Write-Output "Current source bridge: $($result.modes.Count) processes/$($result.checks) checks."
