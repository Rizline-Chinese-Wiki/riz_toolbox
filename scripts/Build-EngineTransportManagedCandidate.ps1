#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$SourceSnapshotManifest,
    [Parameter(Mandatory)][string]$NativeBuildDirectory
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
foreach($path in @($OutputDirectory,$SourceSnapshotManifest,$NativeBuildDirectory)){if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Absolute paths required.'}}
$output=[IO.Path]::GetFullPath($OutputDirectory);if(Test-Path -LiteralPath $output){throw 'Preserve earlier candidates; use a fresh directory.'}
$snapshotManifestPath=[IO.Path]::GetFullPath($SourceSnapshotManifest);$sourceSnapshot=Get-Content -LiteralPath $snapshotManifestPath -Raw|ConvertFrom-Json
if($sourceSnapshot.kind -ne 'active-retirement-snapshot-checks' -or $sourceSnapshot.passed -ne $true -or $sourceSnapshot.runnerExitCode -ne 0){throw 'An actual successful complete source snapshot regression is required.'}
$snapshotRoot=Join-Path (Split-Path -Parent $snapshotManifestPath) 'source-snapshot'
$nativeDirectory=[IO.Path]::GetFullPath($NativeBuildDirectory);$nativeManifestPath=Join-Path $nativeDirectory 'production-native-build.json'
$native=Get-Content -LiteralPath $nativeManifestPath -Raw|ConvertFrom-Json
if($native.kind -ne 'engine-transport-production-native-candidate-build' -or $native.passed -ne $true){throw 'An actual successful exact fixed-production V5 native build is required.'}
$admitted = ($native.productionAdmissionGranted -eq $true)
$profilePath=Join-Path $nativeDirectory 'NativeEngineTransportProfile.Generated.cs'
$profileHash=Hash $profilePath
if($profileHash -cne $native.managedProfile.sha256 -or $native.managedProfile.productionAdmissionGranted -ne $admitted){
    throw 'The generated V5 profile differs from its native build manifest.'
}
$locks=[Collections.Generic.List[IDisposable]]::new();$null=New-Item -ItemType Directory -Path $output
$manifest=[ordered]@{schemaVersion=1;kind='engine-transport-production-managed-candidate-build';passed=$false;buildExitCode=$null;sourceHashesUnchanged=$false
    generatedSource=[ordered]@{path=$profilePath;sha256=$profileHash};nativeBuild=[ordered]@{path=$nativeManifestPath;sha256=Hash $nativeManifestPath}
    sourceSnapshot=[ordered]@{manifestPath=$snapshotManifestPath;sha256=Hash $snapshotManifestPath};files=@();sources=@();failure=$null
    productionAdmissionGranted=$admitted;executionWindowProved=$admitted;gameExecuted=$false;installed=$false}
try{
    $paths=@(@($sourceSnapshot.snapshotFiles|ForEach-Object{$_.path})+@($native.files|ForEach-Object{$_.path})+@($nativeManifestPath,$snapshotManifestPath,$profilePath,$PSCommandPath)|Sort-Object -Unique)
    foreach($path in $paths){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    if((Hash $profilePath) -cne $profileHash){throw 'The generated V5 profile changed before it could be held for compilation.'}
    foreach($row in @($sourceSnapshot.snapshotFiles)+@($native.files)){if((Hash $row.path) -ne $row.sha256){throw "A declared frozen candidate input changed: $($row.path)"}}
    $manifest.sources=@($paths|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length}})
    $manifest.sourceArchive=$sourceSnapshot.sourceArchive
    WriteJson $manifest.sources (Join-Path $output 'source-before.json')
    $project=Join-Path $snapshotRoot 'src/RizToolbox.Loader/RizToolbox.Loader.csproj'
    & (Get-RizDotnetPath) build $project -c Release --nologo -m:1 -p:RestoreIgnoreFailedSources=true -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers -v:minimal `
        "-p:RizEngineTransportProfileSource=$profilePath" -o (Join-Path $output 'managed') 2>&1|
        Tee-Object -FilePath (Join-Path $output 'managed-build.log')
    $manifest.buildExitCode=$LASTEXITCODE
    if($manifest.buildExitCode -ne 0){throw "The actual V5 managed production candidate build failed: $($manifest.buildExitCode)"}
    $manifest.files=@('Sdk','Core','Runtime','Adapter','Hosting','Loader'|ForEach-Object{
        $path=Join-Path $output ('managed/RizToolbox.'+$_+'.dll');[ordered]@{path=$path;sha256=Hash $path;bytes=(Get-Item -LiteralPath $path).Length}
    })
    foreach($row in $manifest.sources){if((Hash $row.path) -ne $row.sha256){throw "A frozen production managed input changed: $($row.path)"}}
    $manifest.sourceHashesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{foreach($lock in $locks){$lock.Dispose()};WriteJson $manifest (Join-Path $output 'managed-build-manifest.json')}
Write-Output "Complete six-assembly V5 managed candidate built (productionAdmissionGranted=$admitted): $output"
