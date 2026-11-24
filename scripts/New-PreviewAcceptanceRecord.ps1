#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$DeclarationPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$InstalledRoot,
    [string[]]$BasicPluginFiles = @(),
    [string[]]$OwnedWavFiles = @(),
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This sibling defines the same path, declaration, hashing, and finish checks as validation.
$collectorInputs = [ordered]@{
    packageRoot=$PackageRoot;manifestPath=$ManifestPath;declarationPath=$DeclarationPath;outputDirectory=$OutputDirectory
    installedRoot=$InstalledRoot;basicPluginFiles=$BasicPluginFiles;ownedWavFiles=$OwnedWavFiles;repositoryRoot=$RepositoryRoot
}
$library = Join-Path $PSScriptRoot 'Test-PreviewAcceptanceRecord.ps1'
for ($cursor = [IO.Path]::GetFullPath($library); $cursor; $cursor = Split-Path -Parent $cursor) {
    $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
    $linkType = $item.PSObject.Properties['LinkType']
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        ($null -ne $linkType -and -not [string]::IsNullOrEmpty([string]$linkType.Value))) { throw "Linked script path: $cursor" }
}
. $library -LibraryOnly
. (Get-PreviewRepositoryHelper $collectorInputs.repositoryRoot)

$request = [ordered]@{
    repositoryRoot=(Resolve-PreviewPath $collectorInputs.repositoryRoot)
    packageRoot=(Resolve-PreviewPath $collectorInputs.packageRoot)
    manifestPath=(Resolve-PreviewPath $collectorInputs.manifestPath)
    declarationPath=(Resolve-PreviewPath $collectorInputs.declarationPath)
    installedRoot=$(if ($collectorInputs.installedRoot) { Resolve-PreviewPath $collectorInputs.installedRoot } else { $null })
    basicPluginFiles=@($collectorInputs.basicPluginFiles | ForEach-Object { Resolve-PreviewPath $_ })
    ownedWavFiles=@($collectorInputs.ownedWavFiles | ForEach-Object { Resolve-PreviewPath $_ })
}
$output = Resolve-PreviewPath $collectorInputs.outputDirectory
$declarationCapture = Read-PreviewFile $request.declarationPath -CaptureBytes
$declaration = Get-PreviewDeclaration (ConvertFrom-PreviewJson $declarationCapture.contentBytes)
$sourceFiles = @($request.manifestPath,$request.declarationPath,$library,
    (Get-PreviewRepositoryHelper $request.repositoryRoot),
    (Join-Path $request.repositoryRoot 'scripts/Install.Common.ps1'),
    (Join-Path $PSScriptRoot 'New-PreviewAcceptanceRecord.ps1')) +
    $request.basicPluginFiles + $request.ownedWavFiles + @($declaration.evidence | ForEach-Object { $_.path })
Assert-PreviewOutputIsolation $output @($request.packageRoot,$request.installedRoot) $sourceFiles
New-PreviewEvidenceDirectory $output
$recordId = [Guid]::NewGuid().ToString('N')
$startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
$partial = $null
try {
    Write-PreviewNewJson (Join-Path $output 'collection-start.json') ([ordered]@{
        schemaVersion=1;kind='RizToolbox.PreviewCollectionStart';recordId=$recordId;startedUtc=$startedUtc
        acceptance='preview/unverified';productionAdmission=$false
    })
    $capture = Get-PreviewCurrentSources $request $PSScriptRoot $output
    $partial = $capture.sources
    Assert-PreviewEqual $declarationCapture.identity $capture.sources.declaration.origin 'declaration.initial-identity'
    Write-PreviewNewBytes (Join-Path $output 'declaration.json') $capture.declarationBytes
    $record = [ordered]@{
        schemaVersion=1;kind='RizToolbox.PreviewAcceptanceRecord';recordId=$recordId
        startedUtc=$startedUtc;snapshotUtc=[DateTimeOffset]::UtcNow.ToString('o')
        collectionStatus='snapshot-awaiting-final-source-seal';programsExecuted=$false
        acceptance='preview/unverified';productionAdmission=$false;realMachineAcceptanceVerified=$false;sources=$capture.sources
    }
    $recordPath = Join-Path $output 'acceptance-record.json'
    Write-PreviewNewJson $recordPath $record
    $savedRecord = Read-PreviewFile $recordPath -CaptureBytes
    Assert-PreviewEqual $record (ConvertFrom-PreviewJson $savedRecord.contentBytes) 'saved-record'
    # A saved snapshot alone is never a completed collection. Re-read every source first.
    $finish = Get-PreviewCurrentSources $request $PSScriptRoot $output
    Assert-PreviewEqual $capture.sources $finish.sources 'finish.sources'
    $recordFinish = Read-PreviewFile $recordPath -CaptureBytes
    Assert-PreviewEqual $savedRecord.identity $recordFinish.identity 'record.identity'
    $bundle = Assert-PreviewBundle $output $record $recordFinish.identity
    Write-PreviewNewJson (Join-Path $output 'collection-complete.json') ([ordered]@{
        schemaVersion=1;kind='RizToolbox.PreviewCollectionComplete';recordId=$recordId;completedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        recordSha256=$recordFinish.identity.sha256;declarationSha256=$bundle.declaration.sha256;startSha256=$bundle.start.sha256
        sourceConsistency='consistent-at-collection-finish';acceptance='preview/unverified';productionAdmission=$false;realMachineAcceptanceVerified=$false
    })
} catch {
    Write-PreviewFailure $output 'collection-failure.json' 'collect-record' $_ $partial
    throw
}
Write-Output "Preview record collected; acceptance remains preview/unverified. Evidence: $output"
