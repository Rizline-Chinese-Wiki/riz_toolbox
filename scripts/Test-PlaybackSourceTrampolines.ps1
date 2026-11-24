#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceSnapshotManifest,[Parameter(Mandatory)][string]$NativeBuildManifest,
    [Parameter(Mandatory)][string]$OutputDirectory,[switch]$UseCurrentTrampolineOverlay,
    [string[]]$Modes=@('normal','seh','dobby-tamper','thunk-tamper','unwind-tamper'),
    [string]$DobbyPath='C:/Users/71957/Desktop/riz_toolbox/.tools/bepinex-789/BepInEx/core/dobby.dll')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
foreach($path in @($SourceSnapshotManifest,$NativeBuildManifest,$OutputDirectory,$DobbyPath)){
    if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Absolute input/output paths are required.'}
}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 22),[Text.UTF8Encoding]::new($false))}
$proof=Get-Content -LiteralPath $SourceSnapshotManifest -Raw|ConvertFrom-Json
$native=Get-Content -LiteralPath $NativeBuildManifest -Raw|ConvertFrom-Json
if($proof.kind -ne 'active-retirement-snapshot-checks' -or $proof.passed -ne $true -or $proof.runnerExitCode -ne 0){throw 'An actual successful frozen base source proof is required.'}
if($native.kind -ne 'independent-stock-shaped-source-prototype' -or $native.passed -ne $true -or $native.actualExitCode -ne 0){throw 'An actual successful native build is required.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve previous failures; use a fresh output directory.'}
if(-not $Modes.Count -or @($Modes|Sort-Object -Unique).Count -ne $Modes.Count -or
    @($Modes|Where-Object{$_ -notin @('normal-ab','normal','seh','dobby-tamper','thunk-tamper','unwind-tamper')}).Count){throw 'Choose unique known exact-prologue modes.'}
$null=New-Item -ItemType Directory -Path $output
$snapshot=Join-Path (Split-Path -Parent $SourceSnapshotManifest) 'source-snapshot'
$source=Join-Path $output 'source'
$nativeDirectory=Split-Path -Parent $NativeBuildManifest
$fixture=Join-Path $nativeDirectory 'RizToolbox.PlaybackStockFixture.dll'
$helper=Join-Path $nativeDirectory 'RizToolbox.PlaybackSourcePrototype.dll'
$result=[ordered]@{kind='exact-native-source-trampoline-checks';passed=$false;failure=$null;buildExitCode=$null
    sourceProof=[ordered]@{path=$SourceSnapshotManifest;sha256=Hash $SourceSnapshotManifest}
    nativeProof=[ordered]@{path=$NativeBuildManifest;sha256=Hash $NativeBuildManifest}
    sourceOverlayUsed=[bool]$UseCurrentTrampolineOverlay;sources=@();overlayInputs=@();inputs=@();modes=@();checks=0;artifacts=@()
    gameCodeExecuted=$false;gameDllMainExecuted=$false;installed=$false;productionAdmission=$false}
try{
    foreach($row in @($native.sources)+@($native.frozenSources)+@($native.files)){
        if((Hash $row.path) -ne $row.sha256){throw "An actual native input/output changed: $($row.path)"}
    }
    foreach($row in $proof.snapshotFiles){
        if((Hash $row.path) -ne $row.sha256){throw "The frozen source changed: $($row.path)"}
        $relative=[IO.Path]::GetRelativePath($snapshot,$row.path)
        if([IO.Path]::IsPathFullyQualified($relative) -or $relative.StartsWith('..')){throw 'A frozen input escapes the source root.'}
        $destination=Join-Path $source $relative
        $null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $row.path -Destination $destination
    }
    if($UseCurrentTrampolineOverlay){
        $repo=Split-Path -Parent $PSScriptRoot
        foreach($relative in @('src/RizToolbox.Runtime/Hooks/NativePlaybackSourceTrampolineProof.cs',
            'src/RizToolbox.Runtime/Hooks/NativePlaybackSourceTrampolineUnwind.cs',
            'tests/RizToolbox.SourceTrampoline.Tests/Program.cs','tests/RizToolbox.SourceTrampoline.Tests/RizToolbox.SourceTrampoline.Tests.csproj')){
            $live=Join-Path $repo $relative;$destination=Join-Path $source $relative
            $null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
            $old=if(Test-Path -LiteralPath $destination){Hash $destination}else{$null}
            Copy-Item -LiteralPath $live -Destination $destination
            $result.overlayInputs+=,[ordered]@{source=$live;path=$destination;priorCoreSha256=$old;sha256=Hash $destination}
        }
    }
    $result.sources=@(Get-ChildItem -LiteralPath $source -File -Recurse|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName}})
    Json $result.sources (Join-Path $output 'source-before.json')
    $archive=Join-Path $output 'source-snapshot.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($source,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
    $result.sourceArchive=[ordered]@{path=$archive;sha256=Hash $archive}
    $result.inputs=@(@($PSCommandPath,$SourceSnapshotManifest,$NativeBuildManifest,$DobbyPath,$fixture,$helper)|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_}})
    $project=Join-Path $source 'tests/RizToolbox.SourceTrampoline.Tests/RizToolbox.SourceTrampoline.Tests.csproj'
    $build=Join-Path $output 'build.log'
    & (Get-RizDotnetPath) build $project -c Release -m:1 -p:RestoreIgnoreFailedSources=true -p:UseSharedCompilation=false --nologo -v:minimal 2>&1|Out-File -LiteralPath $build
    $result.buildExitCode=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $build -Tail 14;throw 'The exact source trampoline fixture did not compile.'}
    $assembly=Join-Path (Split-Path -Parent $project) 'bin/Release/net8.0/RizToolbox.Runtime.Tests.dll'
    foreach($mode in $Modes){
        $log=Join-Path $output ($mode+'.log')
        & (Get-RizDotnetPath) $assembly $fixture $helper $DobbyPath $mode 2>&1|Out-File -LiteralPath $log
        $exit=$LASTEXITCODE
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.exit.txt')),[string]$exit)
        $row=[ordered]@{mode=$mode;actualExitCode=$exit;checks=0;log=[ordered]@{path=$log;sha256=Hash $log}}
        $result.modes+=,$row
        if($exit -ne 0){Get-Content -LiteralPath $log -Tail 14;throw "Actual source prologue $mode failed."}
        $lines=@([IO.File]::ReadAllLines($log)|Where-Object{$_})
        $actual=$lines[-1]|ConvertFrom-Json
        $expected=if($mode -eq 'normal-ab'){5}else{8}
        if($actual.passed -ne $true -or $actual.mode -cne $mode -or $actual.physicalTargets -ne $expected -or
            $actual.actualDobbyPrepare -ne $expected -or $actual.actualDobbyCommit -ne $expected -or
            $actual.gameCodeExecuted -ne $false -or $actual.gameDllMainExecuted -ne $false -or $actual.productionAdmission -ne $false){throw 'The actual exact-source proof has an unexpected scope.'}
        $row.checks=$actual.checks;$row.actual=$actual;$result.checks+=$actual.checks
        Write-Output "$mode`: $($actual.checks) exact-source trampoline checks passed."
    }
    foreach($row in @($result.sources)+@($result.inputs)){
        if((Hash $row.path) -ne $row.sha256){throw "An exact-source compiled input changed: $($row.path)"}
    }
    $result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{
    $files=@(Get-ChildItem -LiteralPath $output -File|Where-Object Name -ne 'test-result.json')
    $binary=Join-Path $source 'tests/RizToolbox.SourceTrampoline.Tests/bin/Release/net8.0'
    if(Test-Path -LiteralPath $binary){$files+=@(Get-ChildItem -LiteralPath $binary -File -Recurse)}
    $result.artifacts=@($files|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}})
    Json $result (Join-Path $output 'test-result.json')
}
