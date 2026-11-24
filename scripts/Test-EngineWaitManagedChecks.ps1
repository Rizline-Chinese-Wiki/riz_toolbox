#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$InitializationContextHelper
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathFullyQualified($OutputDirectory)) { throw 'An absolute output directory is required.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
$dotnet = (Get-RizDotnetPath)
$profile = 'C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs'
$fixtureDirectory = 'C:/Users/71957/riz-build-cache/engine-production-audio-checks-r2-20261026'
$native = @('RizToolbox.InitializationAbiFixture.dll', 'RizToolbox.JudgeContext.dll', 'dobby.dll') |
    ForEach-Object { Join-Path $fixtureDirectory $_ }
if ($InitializationContextHelper) {
    if (-not [IO.Path]::IsPathFullyQualified($InitializationContextHelper)) {
        throw 'InitializationContextHelper must be an absolute file path.'
    }
    $native[1] = (Resolve-Path -LiteralPath $InitializationContextHelper).Path
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
}
function Sources {
    $relative = @(& rg --files src tests -g '*.cs' -g '*.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Source inventory failed.' }
    @($relative | ForEach-Object { Join-Path $repo $_ }) + @(
        (Join-Path $repo 'Directory.Build.props'), (Join-Path $repo 'global.json'),
        (Join-Path $repo 'dependencies.lock.json'), $profile, $PSCommandPath) + $native
}
$manifest = [ordered]@{ schemaVersion=1; kind='engine-wait-managed-checks'; passed=$false
    sourceHashesUnchanged=$false; coordinatorChecks=0; receiverChecks=0; audioChecks=0
    actualNativeWaitClrIntegration=$false; actualNativeSchedulerClrIntegration=$false; gameExecuted=$false; installed=$false
    productionAdmissionVerified=$false; engineCancellationCompleted=$false; inputs=@(); artifacts=@(); failure=$null }
$locks = [Collections.Generic.List[IDisposable]]::new()
$oldLocation = Get-Location
$environmentNames = @('RIZ_INITIALIZATION_ABI_FIXTURE','RIZ_INITIALIZATION_CONTEXT_HELPER','RIZ_INITIALIZATION_ABI_DOBBY')
$oldEnvironment = @{}
try {
    Set-Location -LiteralPath $repo
    $null = New-Item -ItemType Directory -Path $output
    $inputPaths = @(Sources | Sort-Object -Unique)
    $manifest.inputs = @($inputPaths | ForEach-Object { [ordered]@{path=$_;sha256=Hash $_} })
    WriteJson $manifest.inputs (Join-Path $output 'source-before.json')
    # Preserve exact source bytes before any compiler or later agent edit.
    $archive = Join-Path $output 'managed-project-sources.zip'
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    $entryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    try {
        foreach ($row in $manifest.inputs) {
            $relative = [IO.Path]::GetRelativePath($repo, $row.path).Replace('\','/')
            $entryName = if ($relative.StartsWith('../')) { 'external/' + [IO.Path]::GetFileName($row.path) } else { 'repo/' + $relative }
            if (-not $entryNames.Add($entryName)) { throw "Duplicate archived input: $entryName" }
            $entry = $zip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $target = $entry.Open(); $source = [IO.File]::OpenRead($row.path)
            try { $source.CopyTo($target) } finally { $source.Dispose(); $target.Dispose() }
            if ((Hash $row.path) -ne $row.sha256) { throw "Input changed while archiving: $($row.path)" }
        }
    } finally { $zip.Dispose() }
    $manifest.inputArchive = [ordered]@{ path=$archive;sha256=Hash $archive;entries=$entryNames.Count }
    foreach ($path in $native) { $locks.Add([IO.File]::Open($path,'Open','Read','Read')) }
    foreach ($name in $environmentNames) { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
    for ($index=0;$index -lt 3;++$index) { [Environment]::SetEnvironmentVariable($environmentNames[$index],$native[$index],'Process') }
    foreach ($project in @('Runtime','AudioContent')) {
        $binary = Join-Path $output ($project + '/bin')
        $buildLog = Join-Path $output "$project-build.log"
        & $dotnet build "tests/RizToolbox.$project.Tests/RizToolbox.$project.Tests.csproj" --no-restore -m:1 `
            -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers "-p:RizEngineProductionProfileSource=$profile" -o $binary 2>&1 |
            Out-File -LiteralPath $buildLog
        Get-Content -LiteralPath $buildLog -Tail 6
        if ($LASTEXITCODE -ne 0) { throw "$project build failed: $LASTEXITCODE" }
        $assembly = Join-Path $binary "RizToolbox.$project.Tests.dll"
        $modes = if ($project -eq 'Runtime') { @('--engine-runtime-coordinator-checks','--unity-coroutine-owner-checks') } else { @('') }
        foreach ($mode in $modes) {
            $name = if ($mode -eq '') { 'audio' } elseif ($mode -eq '--unity-coroutine-owner-checks') { 'receiver' } else { 'coordinator' }
            $log = Join-Path $output "$name-tests.log"
            if ($mode -eq '') { & $dotnet $assembly 2>&1 | Out-File -LiteralPath $log }
            else { & $dotnet $assembly $mode 2>&1 | Out-File -LiteralPath $log }
            Get-Content -LiteralPath $log -Tail 1
            if ($LASTEXITCODE -ne 0) { throw "$name checks failed: $LASTEXITCODE" }
            $text = [IO.File]::ReadAllText($log)
            $pattern = if ($name -eq 'audio') { '(\d+)/(\d+) audio/content checks passed\.' }
                else { 'Engine managed checks:\s*(\d+)/(\d+) passed\.' }
            $match = [regex]::Match($text, $pattern)
            if (-not $match.Success -or $match.Groups[1].Value -ne $match.Groups[2].Value -or [int]$match.Groups[1].Value -le 0) {
                throw "No exact passing $name summary in actual child output."
            }
            $manifest["${name}Checks"] = [int]$match.Groups[1].Value
        }
    }
    $afterPaths = @(Sources | Sort-Object -Unique)
    if ($afterPaths.Count -ne $inputPaths.Count -or @(Compare-Object $inputPaths $afterPaths).Count -ne 0) { throw 'Source inventory changed.' }
    foreach ($row in $manifest.inputs) { if ((Hash $row.path) -ne $row.sha256) { throw "Source changed: $($row.path)" } }
    $manifest.sourceHashesUnchanged=$true; $manifest.passed=$true
} catch { $manifest.failure=$_.Exception.Message; throw }
finally {
    foreach ($item in $locks) { $item.Dispose() }
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name,$oldEnvironment[$name],'Process') }
    Set-Location -LiteralPath $oldLocation
    if (Test-Path -LiteralPath $output) {
        $manifest.artifacts = @(Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object { $_.Extension -in '.dll','.json','.log','.zip' } |
            ForEach-Object { [ordered]@{ path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length } })
        WriteJson $manifest (Join-Path $output 'test-result.json')
    }
}
