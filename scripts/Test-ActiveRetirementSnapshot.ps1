#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$ExcludeObserverRetirementDraft,
    [string]$InitializationContextHelper
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
if(-not [IO.Path]::IsPathFullyQualified($OutputDirectory)){throw 'An absolute fresh output directory is required.'}
$output=[IO.Path]::GetFullPath($OutputDirectory);if(Test-Path -LiteralPath $output){throw 'Preserve earlier runs; use a fresh directory.'}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
$null=New-Item -ItemType Directory -Path $output;$snapshot=Join-Path $output 'source-snapshot'
$runner=Join-Path $repo 'scripts/Test-EngineWaitManagedChecks.ps1'
$manifest=[ordered]@{schemaVersion=1;kind='active-retirement-snapshot-checks';passed=$false;snapshotCaptured=$false;runnerExitCode=$null;inputs=@();snapshotFiles=@();excludedDraftFiles=@();child=$null;failure=$null
    gameExecuted=$false;installed=$false;productionAdmissionVerified=$false;fullEngineCancellationVerified=$false;fullActiveRetirementVerified=$false}
$oldLocation=Get-Location;$locks=[Collections.Generic.List[IDisposable]]::new()
try{
    Set-Location -LiteralPath $repo
    $relative=@(& rg --files src tests -g '*.cs' -g '*.csproj' -g '*.json' -g '!**/obj/**' -g '!**/bin/**');if($LASTEXITCODE){throw 'Source snapshot inventory failed.'}
    if($ExcludeObserverRetirementDraft){
        $excluded=@('NativeLocalObserverRetirement.cs','NativeLocalTerminalCoroutineTracker.Retirement.cs',
            'NativeCoroutineCancellationObserver.Retirement.cs','NativeLocalUnityEventLedger.Retirement.cs',
            'NativeLocalCutoutCoroutineTracker.Retirement.cs','NativeLocalCutoutEventSource.Retirement.cs')
        $manifest.excludedDraftFiles=@($relative|Where-Object{[IO.Path]::GetFileName($_) -in $excluded})
        $relative=@($relative|Where-Object{[IO.Path]::GetFileName($_) -notin $excluded})
    }
    $toolchain = Join-Path $PSScriptRoot 'Toolchain.Common.ps1'
    $paths=@(@($relative|ForEach-Object{Join-Path $repo $_})+@($PSCommandPath,$runner,$toolchain,(Join-Path $repo 'Directory.Build.props'),(Join-Path $repo 'global.json'),(Join-Path $repo 'dependencies.lock.json'))|Sort-Object -Unique)
    foreach($path in $paths){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    $manifest.inputs=@($paths|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length}})
    foreach($row in $manifest.inputs){$relativePath=[IO.Path]::GetRelativePath($repo,$row.path)
        $destination=Join-Path $snapshot $relativePath;$null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $row.path -Destination $destination
        if((Hash $destination) -ne $row.sha256 -or (Hash $row.path) -ne $row.sha256){throw "Snapshot changed while being captured: $($row.path)"}
    }
    $manifest.snapshotCaptured=$true
    # After this point the compiler only reads the independent snapshot. Other
    # agents may edit the live worktree without changing this run's provenance.
    foreach($lock in $locks){$lock.Dispose()};$locks.Clear()
    $copiedRunner=Join-Path $snapshot 'scripts/Test-EngineWaitManagedChecks.ps1'
    $body=[IO.File]::ReadAllText($copiedRunner)
    if($body.Split(@('--no-restore'),[StringSplitOptions]::None).Count -ne 2){throw 'The existing regression runner build seam changed.'}
    $body=$body.Replace('--no-restore','-p:RestoreIgnoreFailedSources=true')
    $inventorySeam="rg --files src tests -g '*.cs' -g '*.csproj'"
    if($body.Split(@($inventorySeam),[StringSplitOptions]::None).Count -ne 2){throw 'The frozen regression source inventory seam changed.'}
    $body=$body.Replace($inventorySeam,($inventorySeam+" -g '!**/obj/**' -g '!**/bin/**'"))
    [IO.File]::WriteAllText($copiedRunner,$body,[Text.UTF8Encoding]::new($false))
    $manifest.runnerTransformation='Restore this fresh isolated source tree; explicitly exclude compiler-generated obj/bin from before/after source inventory because the snapshot has no enclosing Git repository.'
    $manifest.snapshotFiles=@(Get-ChildItem -LiteralPath $snapshot -Recurse -File|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}})
    WriteJson $manifest.inputs (Join-Path $output 'worktree-source-before.json');WriteJson $manifest.snapshotFiles (Join-Path $output 'snapshot-source-before.json')
    $archive=Join-Path $output 'source-snapshot.zip';[IO.Compression.ZipFile]::CreateFromDirectory($snapshot,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
    $manifest.sourceArchive=[ordered]@{path=$archive;sha256=Hash $archive}
    $checks=Join-Path $output 'checks'
    $runnerArguments = @('-NoProfile', '-File', $copiedRunner, '-OutputDirectory', $checks)
    if ($InitializationContextHelper) { $runnerArguments += @('-InitializationContextHelper', $InitializationContextHelper) }
    & (Join-Path $PSHOME 'pwsh.exe') @runnerArguments
    $manifest.runnerExitCode=$LASTEXITCODE
    $childPath=Join-Path $checks 'test-result.json';if(Test-Path -LiteralPath $childPath){$manifest.child=Get-Content -LiteralPath $childPath -Raw|ConvertFrom-Json}
    if($manifest.runnerExitCode -ne 0 -or -not $manifest.child -or $manifest.child.passed -ne $true){throw 'The actual frozen active-retirement regression build or tests failed.'}
    foreach($row in $manifest.snapshotFiles){if((Hash $row.path) -ne $row.sha256){throw "Frozen compiled source changed: $($row.path)"}}
    $manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{foreach($lock in $locks){$lock.Dispose()};Set-Location -LiteralPath $oldLocation;WriteJson $manifest (Join-Path $output 'test-result.json')}
Write-Output "Frozen Runtime+Audio/content regressions passed: $output"
