#requires -Version 7.2
[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$proofRoot = Join-Path $repoRoot ('artifacts/reproducible-build-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $proofRoot 'source'
$cacheRoot = Join-Path $proofRoot 'nuget-cache'
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
Push-Location $repoRoot
try {
    # rg honors the repository's bin/obj/artifact exclusions. Only declared compiler inputs are copied.
    $inputs = @(& rg --files --hidden src examples/RizToolbox.Examples packaging/sdk global.json Directory.Build.props)
    if ($LASTEXITCODE -ne 0 -or $inputs.Count -lt 10) { throw 'Cannot enumerate source-only build inputs.' }
    $fingerprints = foreach ($relative in $inputs | Sort-Object) {
        $original = [IO.Path]::GetFullPath((Join-Path $repoRoot $relative))
        $destination = [IO.Path]::GetFullPath((Join-Path $sourceRoot $relative))
        if (-not $original.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
            -not $destination.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Build input escaped its declared source root.' }
        $hash = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash.ToLowerInvariant()
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $original -Destination $destination
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Source changed while copying: $relative" }
        @{path=$relative.Replace('\','/');sha256=$hash}
    }
    foreach ($relative in @('src/RizToolbox.Loader/RizToolbox.Loader.csproj','examples/RizToolbox.Examples/RizToolbox.Examples.csproj')) {
        & dotnet build (Join-Path $sourceRoot $relative) -c Release "-p:RestorePackagesPath=$cacheRoot" --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Clean-source build failed: $relative. Evidence: $proofRoot" }
    }
    $comparisons = foreach ($name in @('Sdk','Core','Runtime','Adapter','Hosting','Loader','Examples')) {
        $prefix = if ($name -eq 'Examples') { 'examples' } else { 'src' }
        $relative = "$prefix/RizToolbox.$name/bin/Release/net6.0/RizToolbox.$name.dll"
        $original = Join-Path $repoRoot $relative; $rebuilt = Join-Path $sourceRoot $relative
        if (-not (Test-Path -LiteralPath $original -PathType Leaf)) { throw "Build the primary package first: $relative" }
        $before = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash.ToLowerInvariant()
        $after = (Get-FileHash -LiteralPath $rebuilt -Algorithm SHA256).Hash.ToLowerInvariant()
        @{assembly="RizToolbox.$name";primarySha256=$before;cleanSourceSha256=$after;equal=($before -eq $after)}
    }
    foreach ($entry in $fingerprints) {
        if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Primary source changed during proof: $($entry.path)" }
    }
    $allEqual = @($comparisons | Where-Object { -not $_.equal }).Count -eq 0
    @{schemaVersion=1;scope='same-machine, same installed .NET SDK; new source directory without obj/bin and isolated package cache';
        dotnetSdk=(& dotnet --version | Out-String).Trim();createdAtUtc=[DateTimeOffset]::UtcNow.ToString('o');sources=@($fingerprints);assemblies=@($comparisons);allEqual=$allEqual;
        freshOperatingSystemVerified=$false;nativeRuntimeBundleRebuilt=$false;gameRuntimeVerified=$false} |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $proofRoot 'result.json') -Encoding utf8
    if (-not $allEqual) { throw "Different-directory managed build is not reproducible. Inspect $proofRoot/result.json" }
    Write-Output "7/7 managed assemblies reproduce from source and an isolated package cache. Evidence: $proofRoot"
} finally { Pop-Location }
