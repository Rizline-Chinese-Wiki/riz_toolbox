#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [switch]$RunSelfValidation,
    [string]$CandidateDirectory,
    [switch]$AllowCandidateDowngrade,
    [ValidateSet('Runtime','Author','Validation')][string]$PackageKind = 'Runtime',
    [string]$DistributionDirectory,
    [ValidateSet('Standard','Developer')][string]$RuntimeMode = 'Standard',
    [ValidateSet('Debug','Info','Warning','Error')][string]$LogLevel = 'Info',
    [switch]$FullAuditEachTick,
    [switch]$EnableProofPlugins
)
$ErrorActionPreference = 'Stop'
$PackageKind = [Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($PackageKind.ToLowerInvariant())
$RuntimeMode = [Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($RuntimeMode.ToLowerInvariant())
$LogLevel = [Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($LogLevel.ToLowerInvariant())
if ($RuntimeMode -ne 'Developer' -and ($LogLevel -eq 'Debug' -or $FullAuditEachTick -or $EnableProofPlugins)) { throw 'Debug logging, full tick audits and proof plugins require explicit Developer mode.' }
if ($RunSelfValidation -and ($RuntimeMode -ne 'Developer' -or -not $EnableProofPlugins)) { throw 'Self-validation requires explicit Developer mode and -EnableProofPlugins.' }
if ($CandidateDirectory -and $PackageKind -ne 'Runtime') { throw 'Candidate installation uses its own package layout; PackageKind must be Runtime.' }
if ($CandidateDirectory -and $DistributionDirectory) { throw 'Choose either CandidateDirectory or DistributionDirectory.' }
. (Join-Path $PSScriptRoot 'Install.Common.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path.TrimEnd('\')
if (Get-Process -Name Rizline -ErrorAction SilentlyContinue) { throw 'Close Rizline before installation.' }
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'Rizline.exe') -PathType Leaf)) { throw 'Rizline.exe not found.' }
$installationLock = Enter-InstallLock $gameRoot
try {
$lock = Get-Content -LiteralPath (Join-Path $repoRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
if ((Get-InstallHash (Join-Path $gameRoot 'GameAssembly.dll')) -ne $lock.supportedBuild.gameAssemblySha256) { throw 'Unrecognized game build; add a validated adapter profile before installing.' }
$manifestPath = Get-InstallTarget $gameRoot 'BepInEx\RizToolbox\installation.json'
$existingManifest = Test-Path -LiteralPath $manifestPath
if ($existingManifest) {
    $manifest = Read-InstallManifest $gameRoot $manifestPath
    # Recover the previous complete file set even when the new package/dependency
    # cache is now missing or invalid. Recovery needs only its verified backups.
    $manifest = Restore-InstallTransaction $gameRoot $manifestPath $manifest
}
else {
    foreach ($foreign in @('winhttp.dll','doorstop_config.ini','.doorstop_version','BepInEx','dotnet','MelonLoader','version.dll','dinput8.dll','doorstop_libs','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')) {
        if (Test-Path -LiteralPath (Join-Path $gameRoot $foreign)) { throw "Existing loader installation detected: $foreign. Refusing to overwrite it." }
    }
    $manifest = [pscustomobject]@{ owner='riz_toolbox'; gameRoot=$gameRoot; installedAt=(Get-Date).ToUniversalTime().ToString('o'); files=@(); pending=@() }
}
$owned = Get-OwnedInstallFiles $manifest
if(-not $AllowCandidateDowngrade -and $owned.ContainsKey('.doorstop_version')){
    $versionPath=Get-InstallTarget $gameRoot '.doorstop_version'
    if((Test-Path -LiteralPath $versionPath -PathType Leaf) -and
        (Get-InstallHash $versionPath) -ceq $owned['.doorstop_version'] -and
        [IO.File]::ReadAllText($versionPath) -ceq '4.5.0-riz-engine-transport-candidate.5'){
        throw 'The installed owned V5 transport candidate requires Install-EngineTransportCandidate.ps1. Pass -AllowCandidateDowngrade explicitly to replace it with a legacy/default package.'
    }
}
if ($existingManifest) {
    foreach ($foreign in @('winhttp.dll','doorstop_config.ini','.doorstop_version','MelonLoader','version.dll','dinput8.dll','doorstop_libs','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')) {
        if ((Test-Path -LiteralPath (Join-Path $gameRoot $foreign)) -and -not $owned.ContainsKey($foreign)) { throw "Foreign loader file/directory detected: $foreign" }
    }
}
& (Join-Path $PSScriptRoot 'Prepare-Dependencies.ps1')
$loaderRoot = Join-Path $repoRoot '.tools\bepinex-789'
$packageLocation = if ($DistributionDirectory) {
    (Resolve-Path -LiteralPath $DistributionDirectory).Path
} elseif ($PackageKind -eq 'Runtime') { Join-Path $repoRoot 'artifacts' } else { Join-Path $repoRoot ('artifacts/' + $PackageKind.ToLowerInvariant()) }
$packageRoot = Join-Path $packageLocation 'package'
$packageManifest = Join-Path $packageLocation 'package-manifest.json'
$removePaths=@('RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')
if($CandidateDirectory){
    . (Join-Path $PSScriptRoot 'EngineProductionPackage.Common.ps1')
    $candidatePlan=Get-EngineCandidateInstallPlan $CandidateDirectory $gameRoot $owned
    $packageRoot=$candidatePlan.Package.PackageRoot;$package=$candidatePlan.Package.Manifest
    $removePaths=$candidatePlan.RemovePaths
}else{
    if (-not (Test-Path -LiteralPath $packageManifest -PathType Leaf)) { throw 'Run scripts/Build.ps1 first.' }
    $package = Get-Content -LiteralPath $packageManifest -Raw | ConvertFrom-Json
}
$expectedPaths = @('RizToolbox.Sdk','RizToolbox.Core','RizToolbox.Runtime','RizToolbox.Adapter','RizToolbox.Hosting','RizToolbox.Loader') | ForEach-Object { "BepInEx/plugins/RizToolbox/$_.dll" }
$expectedPaths += 'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json'
$expectedPaths += 'BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll'
$expectedPaths += 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'
$expectedPaths += 'BepInEx/plugins/RizToolbox/RizToolbox.SingleReturn.Prototype.dll'
$expectedPaths += 'BepInEx/plugins/RizToolbox/RizToolbox.PlaybackSourcePrototype.dll'
$expectedPaths += 'winhttp.dll','.doorstop_version','BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip','BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt'
if($CandidateDirectory){$expectedPaths=@(Get-EngineCandidatePackagePaths)}
elseif ($package.schemaVersion -eq 2) {
    # Read older owned packages for recovery/update compatibility. New builds use schema 3.
    if ($PackageKind -ne 'Runtime') { throw 'Legacy packages have no Author/Validation identity.' }
    foreach ($pluginId in @('example.panel','example.events','example.color-low','example.color-high','example.scores')) {
        $expectedPaths += "BepInEx/RizToolbox/mods/$pluginId/plugin.json", "BepInEx/RizToolbox/mods/$pluginId/RizToolbox.Examples.dll"
    }
} elseif ($package.schemaVersion -eq 3) {
    . (Join-Path $PSScriptRoot 'PackageDistribution.Common.ps1')
    Assert-DistributionPathWithoutLinks $packageManifest
    Assert-RuntimeDistribution $packageRoot $package $PackageKind
    $expectedPaths = @(Get-RuntimeDistributionPaths $package.basicPlugins)
} else { throw 'Invalid package manifest.' }
if (-not $CandidateDirectory -and (@($package.files).Count -ne $expectedPaths.Count -or @($package.files.path | Sort-Object -Unique).Count -ne $expectedPaths.Count)) { throw 'Invalid package manifest.' }
$plan = [Collections.Generic.List[object]]::new()
function Add-PlanFile([string]$Source, [string]$Relative, [string]$ExpectedHash) {
    $hash = Get-InstallHash $Source
    if ($ExpectedHash -and $hash -ne $ExpectedHash) { throw "Package hash mismatch: $Relative. Rebuild before installing." }
    $plan.Add([pscustomobject]@{ source=$Source; path=$Relative; sha256=$hash })
}
foreach ($relative in @('BepInEx\core\dobby.dll')) { Add-PlanFile (Join-Path $loaderRoot $relative) $relative }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $loaderRoot 'dotnet') -File -Recurse) { Add-PlanFile $file.FullName ([IO.Path]::GetRelativePath($loaderRoot, $file.FullName)) }
foreach ($entry in $package.files) {
    if ($entry.path -cnotin $expectedPaths -or $entry.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Unexpected package entry.' }
    $source = Join-Path $packageRoot $entry.path
    if ((Get-InstallHash $source) -ne $entry.sha256) { throw "Package hash mismatch: $($entry.path). Rebuild before installing." }
    if ($entry.path.StartsWith('BepInEx/RizToolbox/mods/',[StringComparison]::Ordinal)) {
        # Schema 3 installs only the basic plugins explicitly selected in its
        # validated manifest. The common transaction rejects modified/unowned
        # targets before writing any part of the update.
        if ($CandidateDirectory) {
            if (Test-Path -LiteralPath (Get-InstallTarget $gameRoot $entry.path)) { continue }
        } elseif ($package.schemaVersion -ne 3) { continue }
    }
    Add-PlanFile $source $entry.path.Replace('/', '\') $entry.sha256
}
$staging = Join-Path $repoRoot ('artifacts\install-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $doorstopPath = Join-Path $staging 'doorstop_config.ini'
    $doorstop = Get-Content -LiteralPath (Join-Path $loaderRoot 'doorstop_config.ini') -Raw
    $doorstop = $doorstop -replace '(?m)^target_assembly\s*=.*$', 'target_assembly = BepInEx\plugins\RizToolbox\RizToolbox.Loader.dll'
    $doorstop | Set-Content -LiteralPath $doorstopPath -Encoding utf8
    Add-PlanFile $doorstopPath 'doorstop_config.ini'
    $settingsRelative = 'BepInEx\RizToolbox\settings.json'
    # User settings stay intact across updates; unchanged owned settings can be uninstalled.
    if (-not (Test-Path -LiteralPath (Get-InstallTarget $gameRoot $settingsRelative))) {
        $settingsPath = Join-Path $staging 'settings.json'
        @{ enableExamples=[bool]$RunSelfValidation; runSelfValidation=[bool]$RunSelfValidation } | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8
        Add-PlanFile $settingsPath $settingsRelative
    }
    $runtimeRelative = 'BepInEx\RizToolbox\runtime.json'
    if (-not (Test-Path -LiteralPath (Get-InstallTarget $gameRoot $runtimeRelative))) {
        $runtimePath = Join-Path $staging 'runtime.json'
        @{schemaVersion=1;mode=$RuntimeMode;logLevel=$LogLevel;fullAuditEachTick=[bool]$FullAuditEachTick;enableProofPlugins=[bool]$EnableProofPlugins} |
            ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding utf8
        Add-PlanFile $runtimePath $runtimeRelative
    } elseif ($PSBoundParameters.ContainsKey('RuntimeMode') -or $PSBoundParameters.ContainsKey('LogLevel') -or $FullAuditEachTick -or $EnableProofPlugins) {
        Write-Output 'Existing runtime.json preserved; startup mode options apply only when creating a new configuration.'
    }
    Invoke-InstallTransaction $gameRoot $manifestPath $manifest $plan $owned $removePaths
} finally {
    foreach ($file in Get-ChildItem -LiteralPath $staging -File) { Remove-Item -LiteralPath $file.FullName }
    Remove-Item -LiteralPath $staging
}
if($CandidateDirectory){Write-Output "Installed explicit candidate bytes to $gameRoot; production first-install permission remains unavailable; manifest: $manifestPath"}
else{Write-Output "Installed $PackageKind runtime bytes to $gameRoot; development resources stay in the distribution and proof requires explicit startup configuration; manifest: $manifestPath"}
} finally { $installationLock.Dispose() }
