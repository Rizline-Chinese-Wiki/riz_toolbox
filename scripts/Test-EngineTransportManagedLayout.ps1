#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$NativeLayoutResult
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-RizDotnetPath)
$generatedProfile = 'C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs'
$environmentName = 'RIZ_ENGINE_TRANSPORT_LAYOUT_JSON'
function Absolute([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw 'Absolute output and native receipt paths are required.' }
    [IO.Path]::GetFullPath($Path)
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
}
$output = Absolute $OutputDirectory
$nativeResult = Absolute $NativeLayoutResult
if (Test-Path -LiteralPath $output) { throw 'Use a fresh nonexistent output directory.' }
if (-not (Test-Path -LiteralPath $nativeResult -PathType Leaf)) { throw 'The actual native layout receipt must exist.' }
$nativeReceiptSha = Hash $nativeResult
$native = [IO.File]::ReadAllText($nativeResult) | ConvertFrom-Json
if ((Hash $nativeResult) -ne $nativeReceiptSha) { throw 'Native layout receipt changed while being read.' }
if ($native.schemaVersion -ne 1 -or $native.kind -ne 'engine-transport-publication-layout-checks' -or
    $native.passed -ne $true -or $native.buildExitCode -ne 0 -or $native.childExitCode -ne 0 -or
    $null -ne $native.failure -or $native.installerExecuted -ne $false -or $native.gameExecuted -ne $false -or
    $native.productionAdmissionVerified -ne $false -or $native.child.kind -ne 'engine-transport-publication-layout' -or
    $native.child.passed -ne $true -or $native.child.version -ne 5 -or $native.child.publicationBytes -ne 91376 -or
    $native.child.selfOwnedOnly -ne $true -or $native.child.installerExecuted -ne $false -or
    $native.child.gameExecuted -ne $false -or $native.child.productionAdmissionVerified -ne $false) {
    throw 'A successful actual native V5 layout-only receipt is required; no installer/game/admission claim is accepted.'
}
$evidencePaths = @($nativeResult) + @($native.inputs | ForEach-Object { Absolute $_.path }) +
    @($native.files | ForEach-Object { Absolute $_.path })
if (@($native.inputs).Count -eq 0 -or @($native.files).Count -eq 0) { throw 'Native source/artifact provenance must not be empty.' }
foreach ($row in @($native.inputs) + @($native.files)) {
    if ((Hash $row.path) -ne $row.sha256) { throw "Native layout evidence changed: $($row.path)" }
}
function Sources {
    $relative = @(& rg --files src tests -g '*.cs' -g '*.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Source inventory failed.' }
    @($relative | ForEach-Object { Join-Path $repo $_ }) + @(
        (Join-Path $repo 'Directory.Build.props'), (Join-Path $repo 'global.json'),
        (Join-Path $repo 'dependencies.lock.json'), $generatedProfile, $PSCommandPath) + $evidencePaths
}
$manifest = [ordered]@{
    schemaVersion=1; kind='engine-transport-managed-layout-checks'; passed=$false
    startedUtc=[DateTime]::UtcNow.ToString('o'); completedUtc=$null; buildExitCode=$null
    sourceHashesUnchanged=$false; layoutChecks=0; coordinatorChecks=0; receiverChecks=0; publicationChecks=0
    actualNativeLayoutCompared=$false; missingEvidenceRefused=$false; missingEvidenceChecks=0
    actualNativeWaitClrIntegration=$false; actualNativeSchedulerClrIntegration=$false
    installerExecuted=$false; gameExecuted=$false; installed=$false; productionAdmissionVerified=$false
    nativeLayoutReceipt=[ordered]@{path=$nativeResult;sha256=$nativeReceiptSha;version=5;publicationBytes=91376}
    inputs=@(); sourceAfter=@(); childRuns=@(); artifacts=@(); failure=$null
}
$locks = [Collections.Generic.List[IDisposable]]::new()
$oldLocation = Get-Location
$oldEnvironment = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
$inputPaths = @()
$runFailure = $null
try {
    Set-Location -LiteralPath $repo
    $null = New-Item -ItemType Directory -Path $output
    $inputPaths = @(Sources | Sort-Object -Unique)
    # Freeze every inventoried source and receipt while the compiler/children
    # read it. No caller source file, proof or native binary is overwritten.
    foreach ($path in $inputPaths) { $locks.Add([IO.File]::Open($path, 'Open', 'Read', 'Read')) }
    $manifest.inputs = @($inputPaths | ForEach-Object { [ordered]@{path=$_;sha256=Hash $_} })
    WriteJson $manifest.inputs (Join-Path $output 'source-before.json')
    $archive = Join-Path $output 'managed-project-sources.zip'
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    $entryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $externalIndex = 0
    try {
        foreach ($row in $manifest.inputs) {
            $relative = [IO.Path]::GetRelativePath($repo, $row.path).Replace('\', '/')
            $entryName = if ($relative.StartsWith('../') -or [IO.Path]::IsPathRooted($relative)) {
                'external/' + ('{0:D4}-' -f $externalIndex++) + [IO.Path]::GetFileName($row.path)
            } else { 'repo/' + $relative }
            if (-not $entryNames.Add($entryName)) { throw "Duplicate archived input: $entryName" }
            $entry = $zip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $destination = $entry.Open(); $source = [IO.File]::OpenRead($row.path)
            try { $source.CopyTo($destination) } finally { $source.Dispose(); $destination.Dispose() }
            if ((Hash $row.path) -ne $row.sha256) { throw "Input changed while archiving: $($row.path)" }
        }
    } finally { $zip.Dispose() }
    $manifest.inputArchive = [ordered]@{path=$archive;sha256=Hash $archive;entries=$entryNames.Count}
    # The receipt originally inspected above must itself still be the frozen one.
    if ((Hash $nativeResult) -ne $manifest.nativeLayoutReceipt.sha256) { throw 'Native layout receipt changed before the source freeze.' }
    foreach ($row in @($native.inputs) + @($native.files)) {
        if ((Hash $row.path) -ne $row.sha256) { throw "Native layout evidence changed before the source freeze: $($row.path)" }
    }
    [Environment]::SetEnvironmentVariable($environmentName, $nativeResult, 'Process')
    $binary = Join-Path $output 'Runtime/bin'
    $buildLog = Join-Path $output 'Runtime-build.log'
    & $dotnet build 'tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj' --no-restore -m:1 `
        -p:UseSharedCompilation=false "-p:RizEngineProductionProfileSource=$generatedProfile" -o $binary 2>&1 |
        Out-File -LiteralPath $buildLog
    $buildExit = $LASTEXITCODE; $manifest.buildExitCode = $buildExit
    Get-Content -LiteralPath $buildLog -Tail 6
    if ($buildExit -ne 0) { throw "Runtime build failed: $buildExit" }
    $assembly = Join-Path $binary 'RizToolbox.Runtime.Tests.dll'
    $modes = @(
        [ordered]@{name='layout';mode='--engine-transport-layout-checks';count=7;prefix='Engine transport layout checks:'},
        [ordered]@{name='publication';mode='--engine-transport-publication-checks';count=6;prefix='Engine transport publication checks:'},
        [ordered]@{name='coordinator';mode='--engine-runtime-coordinator-checks';count=28;prefix='Engine managed checks:'},
        [ordered]@{name='receiver';mode='--unity-coroutine-owner-checks';count=10;prefix='Engine managed checks:'}
    )
    foreach ($run in $modes) {
        $log = Join-Path $output ($run.name + '-tests.log')
        & $dotnet $assembly $run.mode 2>&1 | Out-File -LiteralPath $log
        $childExit = $LASTEXITCODE
        $manifest.childRuns += [ordered]@{name=$run.name;mode=$run.mode;exitCode=$childExit;log=$log}
        Get-Content -LiteralPath $log -Tail 1
        $text = [IO.File]::ReadAllText($log)
        $pattern = '(?m)^' + [regex]::Escape($run.prefix) + '\s*(\d+)/(\d+) passed\.\s*$'
        $summaryMatches = [regex]::Matches($text, $pattern)
        if ($childExit -ne 0 -or $summaryMatches.Count -ne 1 -or
            [int]$summaryMatches[0].Groups[1].Value -ne $run.count -or [int]$summaryMatches[0].Groups[2].Value -ne $run.count) {
            throw "Exact passing $($run.name) summary $($run.count)/$($run.count) was not produced by the actual child."
        }
        $manifest["$($run.name)Checks"] = $run.count
        if ($run.name -eq 'layout') { $manifest.actualNativeLayoutCompared = $true }
    }
    # Mandatory evidence must be a real dependency of the same CLI: the six
    # ordinary managed tests still pass, while exactly the seventh rejects it.
    [Environment]::SetEnvironmentVariable($environmentName, $null, 'Process')
    $negativeLog = Join-Path $output 'layout-missing-evidence-tests.log'
    & $dotnet $assembly '--engine-transport-layout-checks' 2>&1 | Out-File -LiteralPath $negativeLog
    $negativeExit = $LASTEXITCODE
    [Environment]::SetEnvironmentVariable($environmentName, $nativeResult, 'Process')
    $manifest.childRuns += [ordered]@{name='layout-missing-evidence';mode='--engine-transport-layout-checks';exitCode=$negativeExit;log=$negativeLog}
    $negative = [IO.File]::ReadAllText($negativeLog)
    $summary = [regex]::Matches($negative, '(?m)^Engine transport layout checks:\s*6/7 passed\.\s*$')
    if ($negativeExit -ne 1 -or $summary.Count -ne 1 -or
        [regex]::Matches($negative, '(?m)^PASS Managed V5 ').Count -ne 6 -or
        [regex]::Matches($negative, '(?m)^FAIL ').Count -ne 1 -or
        $negative -notmatch ('Native layout evidence is mandatory: set ' + [regex]::Escape($environmentName))) {
        throw 'Missing native evidence must produce exit 1, six managed passes and only the required-evidence failure.'
    }
    $manifest.missingEvidenceRefused = $true; $manifest.missingEvidenceChecks = 1
    $manifest.passed = $true
} catch {
    $runFailure = $_.Exception.Message; $manifest.failure = $runFailure; $manifest.passed = $false
} finally {
    # Record a second independent inventory/hash list even if the build or child
    # fails. Source movement/addition, as well as content changes, invalidates it.
    if ($inputPaths.Count -gt 0) {
        try {
            $afterPaths = @(Sources | Sort-Object -Unique)
            $manifest.sourceAfter = @($afterPaths | ForEach-Object { [ordered]@{path=$_;sha256=Hash $_} })
            WriteJson $manifest.sourceAfter (Join-Path $output 'source-after.json')
            if ($afterPaths.Count -ne $inputPaths.Count -or @(Compare-Object $inputPaths $afterPaths).Count -ne 0) { throw 'Source inventory changed.' }
            foreach ($row in $manifest.inputs) { if ((Hash $row.path) -ne $row.sha256) { throw "Source changed: $($row.path)" } }
            $manifest.sourceHashesUnchanged = $true
        } catch {
            $manifest.passed = $false
            if ($null -eq $manifest.failure) { $manifest.failure = $_.Exception.Message }
        }
    }
    foreach ($item in $locks) { $item.Dispose() }
    [Environment]::SetEnvironmentVariable($environmentName, $oldEnvironment, 'Process')
    Set-Location -LiteralPath $oldLocation
    if (Test-Path -LiteralPath $output) {
        $manifest.artifacts = @(Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object {
            [ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}
        })
        $manifest.completedUtc = [DateTime]::UtcNow.ToString('o')
        WriteJson $manifest (Join-Path $output 'test-result.json')
    }
}
if (-not $manifest.passed -or -not $manifest.sourceHashesUnchanged) { throw $manifest.failure }
Write-Output "V5 native/managed layout 7/7, publication boundaries 5/5, coordinator 28/28, receiver 10/10; missing evidence refused: $output"
