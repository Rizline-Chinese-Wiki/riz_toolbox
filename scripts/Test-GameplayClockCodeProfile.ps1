#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceSnapshotManifest,[Parameter(Mandatory)][string]$OutputDirectory,
    [string]$GameAssemblyPath='D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll',
    [string]$DobbyPath='C:/Users/71957/Desktop/riz_toolbox/.tools/bepinex-789/BepInEx/core/dobby.dll',
    [string]$HelperPath='D:/riz-build-cache-next/single-return-managed-native-r4-20261030/RizToolbox.SingleReturn.Prototype.dll')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
foreach($path in @($SourceSnapshotManifest,$OutputDirectory,$GameAssemblyPath,$DobbyPath,$HelperPath)){if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Absolute paths required.'}}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve previous runs; use a fresh output directory.'}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))}
$proof=Get-Content -LiteralPath $SourceSnapshotManifest -Raw|ConvertFrom-Json
if($proof.kind -ne 'active-retirement-snapshot-checks' -or $proof.passed -ne $true){throw 'A successful actual current source snapshot is required.'}
$root=Join-Path (Split-Path -Parent $SourceSnapshotManifest) 'source-snapshot'
$pins=@{ $GameAssemblyPath='853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79';
    $DobbyPath='8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded';
    $HelperPath='90b6133cf6ecd98dd00cd34472d81cd91493419ea4cb06b0aa59ffb0f7f0b355' }
foreach($path in $pins.Keys){if((Hash $path) -ne $pins[$path]){throw "Pinned native input differs: $path"}}
$null=New-Item -ItemType Directory -Path $output
$result=[ordered]@{kind='mapped-clock-broker-profile-checks';passed=$false;buildExitCode=$null;checks=0;modes=@();failure=$null
    sourceProof=[ordered]@{path=$SourceSnapshotManifest;sha256=Hash $SourceSnapshotManifest};sources=@();inputs=@();artifacts=@()
    script=[ordered]@{path=$PSCommandPath;sha256=Hash $PSCommandPath}
    mappedPinnedImage=$true;gameExecuted=$false;gameDllMainExecuted=$false;installed=$false;productionAdmissionVerified=$false}
try{
    $result.inputs=@($pins.Keys|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_}})
    foreach($row in $proof.snapshotFiles){
        if((Hash $row.path) -ne $row.sha256){throw "Frozen input changed: $($row.path)"}
        $destination=Join-Path $output ('source/'+[IO.Path]::GetRelativePath($root,$row.path))
        $null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $row.path -Destination $destination
        if((Hash $destination) -ne $row.sha256){throw 'A copied source differs.'}
        $result.sources+=,[ordered]@{path=$destination;sha256=$row.sha256}
    }
    WriteJson $result.sources (Join-Path $output 'source-before.json')
    $image=Join-Path $output 'OwnedClockImage.dll';Copy-Item -LiteralPath $GameAssemblyPath -Destination $image
    if((Hash $image) -ne $pins[$GameAssemblyPath]){throw 'The isolated image copy differs.'}
    $config=Join-Path $output 'NuGet.Config'
    [IO.File]::WriteAllText($config,'<configuration><packageSources><clear/></packageSources></configuration>')
    $project=Join-Path $output 'source/tests/RizToolbox.Gameplay.Tests/RizToolbox.Gameplay.Tests.csproj'
    $buildLog=Join-Path $output 'build.log'
    & (Get-RizDotnetPath) build $project -c Release --configfile $config -p:UseSharedCompilation=false --nologo -v:minimal 2>&1|Out-File -LiteralPath $buildLog
    $result.buildExitCode=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $buildLog -Tail 12;throw 'Actual mapped-profile fixture build failed.'}
    $assembly=Join-Path (Split-Path -Parent $project) 'bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll'
    foreach($mode in @('unhooked','normal','head','tail','consumer','original')){
        $arguments=if($mode -eq 'unhooked'){@('--mapped-clock-code-checks',$image)}else{@('--mapped-clock-hook-checks',$image,$DobbyPath,$HelperPath,$mode)}
        $log=Join-Path $output ($mode+'.log')
        & (Get-RizDotnetPath) $assembly @arguments 2>&1|Out-File -LiteralPath $log
        $exitCode=$LASTEXITCODE;$body=[IO.File]::ReadAllText($log)
        $row=[ordered]@{mode=$mode;exitCode=$exitCode;checks=0;log=[ordered]@{path=$log;sha256=Hash $log}}
        $result.modes+=,$row
        if($exitCode -ne 0){Get-Content -LiteralPath $log -Tail 10;throw "Actual mapped-profile $mode process failed."}
        if($mode -eq 'unhooked'){
            if($body -notmatch '7/7 mapped clock code checks passed'){throw 'The mapped source result has no exact passing summary.'}
            $row.checks=7
        }else{
            $actual=$body.Trim()|ConvertFrom-Json
            if($actual.passed -ne $true -or $actual.mode -cne $mode -or $actual.originalCalls -ne 0 -or $actual.gameCodeExecuted -ne $false -or $actual.dllMainExecuted -ne $false){throw 'Unexpected actual mapped hook proof.'}
            $row.checks=$actual.checks
        }
        $result.checks+=$row.checks;Write-Output "$mode`: $($row.checks) checks passed."
    }
    foreach($row in @($result.sources)+@($result.inputs)+@($result.script)){if((Hash $row.path) -ne $row.sha256){throw "A declared input changed: $($row.path)"}}
    $result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{
    $artifactFiles=@(Get-ChildItem -LiteralPath $output -File|Where-Object Name -ne 'test-result.json')
    $binaryDirectory=Join-Path $output 'source/tests/RizToolbox.Gameplay.Tests/bin/Release/net8.0'
    if(Test-Path -LiteralPath $binaryDirectory){$artifactFiles+=@(Get-ChildItem -LiteralPath $binaryDirectory -File -Recurse)}
    $result.artifacts=@($artifactFiles|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}})
    WriteJson $result (Join-Path $output 'test-result.json')
}
Write-Output "Actual mapped clock profile: $($result.modes.Count) processes/$($result.checks) checks; no game methods or DllMain executed."
