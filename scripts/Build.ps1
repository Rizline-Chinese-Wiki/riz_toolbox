#requires -Version 7.2
[CmdletBinding()]
param([switch]$SkipTests, [string]$NativeOutputDirectory, [string]$ValidationGameAssemblyPath,
    [ValidateSet('Runtime','Author','Validation')][string]$PackageKind = 'Runtime',
    [string]$DistributionOutputDirectory,
    [string[]]$BasicPluginIds = @(),
    [switch]$EngineTransport,
    [string]$EngineTransportProfileSource = '',
    [string]$EngineProductionProfileSource = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageDistribution.Common.ps1')
Assert-DistributionBasicPluginIds $BasicPluginIds
if (-not $DistributionOutputDirectory) { $DistributionOutputDirectory = Join-Path $repoRoot 'artifacts' }
$DistributionOutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DistributionOutputDirectory)
Assert-DistributionPathWithoutLinks $DistributionOutputDirectory
$initializationFixtureBefore = [Environment]::GetEnvironmentVariable('RIZ_INITIALIZATION_ABI_FIXTURE')
$initializationDobbyBefore = [Environment]::GetEnvironmentVariable('RIZ_INITIALIZATION_ABI_DOBBY')
$initializationHelperBefore = [Environment]::GetEnvironmentVariable('RIZ_INITIALIZATION_CONTEXT_HELPER')
$playbackOwnedBefore = [Environment]::GetEnvironmentVariable('RIZ_PLAYBACK_OWNED_FIXTURE')
Push-Location $repoRoot
try {
    . (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
    if (-not $SkipTests) { $null = Assert-RizPythonModules }
    & (Join-Path $PSScriptRoot 'Prepare-Dependencies.ps1')
    if (-not $NativeOutputDirectory) {
        $NativeOutputDirectory = Join-Path $DistributionOutputDirectory 'native-judge-context'
        if (Test-Path -LiteralPath $NativeOutputDirectory) { $NativeOutputDirectory += '-' + [Guid]::NewGuid().ToString('N') }
    }
    & (Join-Path $PSScriptRoot 'Build-JudgeContext.ps1') -OutputDirectory $NativeOutputDirectory -SkipFixture:($SkipTests -and $PackageKind -ne 'Validation') -DobbyPath (Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll')
    $engineKernelOutput = Join-Path $NativeOutputDirectory 'engine-node-kernel'
    & (Join-Path $PSScriptRoot 'Build-EngineNodeKernel.ps1') -OutputDirectory $engineKernelOutput -SkipEntryFixture:($SkipTests -and $PackageKind -ne 'Validation')
    $playbackRuntimeOutput = Join-Path $NativeOutputDirectory 'playback-runtime'
    & (Join-Path $PSScriptRoot 'Build-PlaybackRuntime.ps1') -OutputDirectory $playbackRuntimeOutput
    $doorstopOutput = Join-Path $NativeOutputDirectory 'doorstop'
    if ($EngineTransport) {
        & (Join-Path $PSScriptRoot 'Build-Doorstop.ps1') -OutputDirectory $doorstopOutput -FixedSystemWinHttp -EngineTransport
    } else {
        & (Join-Path $PSScriptRoot 'Build-Doorstop.ps1') -OutputDirectory $doorstopOutput
    }
    if (-not $SkipTests) {
        $playbackOwnedOutput = Join-Path $NativeOutputDirectory 'playback-owned-fixture'
        & (Join-Path $PSScriptRoot 'Build-PlaybackOwnedFixture.ps1') -OutputDirectory $playbackOwnedOutput
        $env:RIZ_PLAYBACK_OWNED_FIXTURE = Join-Path $playbackOwnedOutput 'RizToolbox.PlaybackOwnedFixture.dll'
        & (Join-Path $PSScriptRoot 'Test-DoorstopInitNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'doorstop-init')
        & (Join-Path $PSScriptRoot 'Test-InitializationContextNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'initialization-context') -HelperDirectory $NativeOutputDirectory -SkipHelperBuild -DobbyPath (Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll')
        & (Join-Path $PSScriptRoot 'Test-EventMidContextNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'event-mid-context') -HelperDirectory $NativeOutputDirectory
        & (Join-Path $PSScriptRoot 'Test-VoidAdmissionNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'void-admission') -HelperDirectory $NativeOutputDirectory
        & (Join-Path $PSScriptRoot 'Test-BootstrapVoidNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'bootstrap-void') -HelperDirectory $NativeOutputDirectory
        & (Join-Path $PSScriptRoot 'Test-ListenerContextNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'listener-context') -HelperDirectory $NativeOutputDirectory
        $operationOutput = Join-Path $NativeOutputDirectory 'operation-fixture'
        & (Join-Path $PSScriptRoot 'Build-OperationFixture.ps1') -OutputDirectory $operationOutput
        & (Join-Path $PSScriptRoot 'Test-OperationFixture.ps1') -HelperDirectory $NativeOutputDirectory -FixturePath (Join-Path $operationOutput 'OperationContext.Fixture.exe') -OutputDirectory (Join-Path $NativeOutputDirectory 'operation-native')
        $operationDobbyOutput = Join-Path $NativeOutputDirectory 'operation-dobby-fixture'
        & (Join-Path $PSScriptRoot 'Build-OperationDobbyFixture.ps1') -OutputDirectory $operationDobbyOutput
        & (Join-Path $PSScriptRoot 'Test-OperationDobby.ps1') -HelperDirectory $NativeOutputDirectory -FixturePath (Join-Path $operationDobbyOutput 'OperationDobby.Fixture.exe') -DobbyPath (Join-Path $repoRoot '.tools/bepinex-789/BepInEx/core/dobby.dll') -OutputDirectory (Join-Path $NativeOutputDirectory 'operation-dobby')
        $operationBootstrapOutput = Join-Path $NativeOutputDirectory 'operation-bootstrap-fixture'
        & (Join-Path $PSScriptRoot 'Build-OperationBootstrapFixture.ps1') -OutputDirectory $operationBootstrapOutput
        & (Join-Path $PSScriptRoot 'Test-OperationBootstrapNative.ps1') -HelperDirectory $NativeOutputDirectory -FixturePath (Join-Path $operationBootstrapOutput 'OperationBootstrap.Fixture.exe') -DobbyPath (Join-Path $repoRoot '.tools/bepinex-789/BepInEx/core/dobby.dll') -OutputDirectory (Join-Path $NativeOutputDirectory 'operation-bootstrap-native')
        $engineNodeOutput = Join-Path $NativeOutputDirectory 'engine-node-fixture'
        & (Join-Path $PSScriptRoot 'Build-EngineNodeFixture.ps1') -OutputDirectory $engineNodeOutput
        & (Join-Path $PSScriptRoot 'Test-EngineNodeNative.ps1') -HelperDirectory $NativeOutputDirectory -FixturePath (Join-Path $engineNodeOutput 'EngineNode.Fixture.exe') -ManagedFixturePath (Join-Path $engineNodeOutput 'RizToolbox.EngineNodeManagedFixture.dll') -DobbyPath (Join-Path $repoRoot '.tools/bepinex-789/BepInEx/core/dobby.dll') -OutputDirectory (Join-Path $NativeOutputDirectory 'engine-node-native')
        $earlyLoaderOutput = Join-Path $NativeOutputDirectory 'early-loader-fixture'
        & (Join-Path $PSScriptRoot 'Build-EarlyLoaderFixture.ps1') -HelperDirectory $NativeOutputDirectory -OutputDirectory $earlyLoaderOutput
        & (Join-Path $PSScriptRoot 'Test-EarlyLoaderFixture.ps1') -BinaryDirectory $earlyLoaderOutput -ExpectedManifestPath (Join-Path $earlyLoaderOutput 'fixture-build-manifest.json') -OutputDirectory (Join-Path $NativeOutputDirectory 'early-loader-cases')
        & (Join-Path $PSScriptRoot 'Test-RuntimeInvokeNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'runtime-invoke') -HelperDirectory $NativeOutputDirectory
        & (Join-Path $PSScriptRoot 'Test-FirstInstallRejectedNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'first-install-rejected') -HelperDirectory $NativeOutputDirectory
        if ($ValidationGameAssemblyPath) {
            & (Join-Path $PSScriptRoot 'Test-FirstInstallMappedNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'first-install-mapped') -HelperDirectory $NativeOutputDirectory -GameAssemblyPath $ValidationGameAssemblyPath -DobbyPath (Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll')
            & (Join-Path $PSScriptRoot 'Test-FirstInstallUnknownNative.ps1') -OutputDirectory (Join-Path $NativeOutputDirectory 'first-install-unknown') -HelperDirectory $NativeOutputDirectory -GameAssemblyPath $ValidationGameAssemblyPath -DobbyPath (Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll')
        }
        $initializationOutput = Join-Path $NativeOutputDirectory 'initialization-abi'
        & (Join-Path $PSScriptRoot 'Build-InitializationAbiFixture.ps1') -OutputDirectory $initializationOutput
        $listenerManagedOutput = Join-Path $NativeOutputDirectory 'listener-managed-fixture'
        & (Join-Path $PSScriptRoot 'Build-ListenerManagedFixture.ps1') -OutputDirectory $listenerManagedOutput
        $env:RIZ_INITIALIZATION_ABI_FIXTURE = Join-Path $initializationOutput 'RizToolbox.InitializationAbiFixture.dll'
        $env:RIZ_INITIALIZATION_ABI_DOBBY = Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll'
        $env:RIZ_INITIALIZATION_CONTEXT_HELPER = Join-Path $NativeOutputDirectory 'RizToolbox.JudgeContext.dll'
    }
    $buildExtraArgs = @('-m:1', '-p:UseSharedCompilation=false', '-p:BuildInParallel=false', '--disable-build-servers')
    if ($EngineTransportProfileSource) { $buildExtraArgs += "-p:RizEngineTransportProfileSource=$EngineTransportProfileSource" }
    if ($EngineProductionProfileSource) { $buildExtraArgs += "-p:RizEngineProductionProfileSource=$EngineProductionProfileSource" }
    dotnet build 'src\RizToolbox.Loader\RizToolbox.Loader.csproj' -c Release --verbosity minimal @buildExtraArgs
    if ($LASTEXITCODE -ne 0) { throw 'Loader build failed.' }
    dotnet build 'examples\RizToolbox.Examples\RizToolbox.Examples.csproj' -c Release --verbosity minimal -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Example packages build failed.' }
    dotnet build 'examples\RizToolbox.BasicPlugins\RizToolbox.BasicPlugins.csproj' -c Release --verbosity minimal -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Basic plugins build failed.' }
    if (-not $SkipTests) {
        dotnet run --project 'tests\RizToolbox.RuntimeConfiguration.Tests\RizToolbox.RuntimeConfiguration.Tests.csproj' -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Runtime configuration tests failed.' }
        dotnet build 'tests\RizToolbox.Loader.Tests\RizToolbox.Loader.Tests.csproj' -c Release --verbosity minimal -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Loader host test build failed.' }
        & (Join-Path $repoRoot 'tests/RizToolbox.Loader.Tests/bin/Release/net8.0/RizToolbox.Loader.Tests.exe')
        if ($LASTEXITCODE -ne 0) { throw 'Loader host tests failed.' }
        dotnet run --project 'tests\RizToolbox.Core.Tests\RizToolbox.Core.Tests.csproj' -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
        dotnet run --project 'tests\RizToolbox.Adapter.Tests\RizToolbox.Adapter.Tests.csproj' -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Adapter tests failed.' }
        dotnet run --project 'tests\RizToolbox.Hosting.Tests\RizToolbox.Hosting.Tests.csproj' -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Plugin package tests failed.' }
        dotnet build 'tests\RizToolbox.BasicPlugins.Tests\RizToolbox.BasicPlugins.Tests.csproj' -c Release --verbosity minimal -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Basic plugin lifecycle test build failed.' }
        & dotnet (Join-Path $repoRoot 'tests/RizToolbox.BasicPlugins.Tests/bin/Release/net8.0/RizToolbox.BasicPlugins.Tests.dll')
        if ($LASTEXITCODE -ne 0) { throw 'Basic plugin lifecycle tests failed.' }
        dotnet run --project 'tests\RizToolbox.Game.Tests\RizToolbox.Game.Tests.csproj' -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Game state and profile tests failed.' }
        # Runtime includes the exact Unity backing metadata/lifetime observation cases.
        # Full native backing/Start/SDK checks require the separately pinned owned fixture.
        foreach ($project in @('Runtime','Sdk','Scope','Diagnostics','PackageLifecycle','Module','Mutation','Data','Storage','Services','GameResources','GameAppearance','GameCamera','Hooks','Gameplay','GameplaySourceGeneration','Settings','Visual','Performance','AudioContent','UiInput','RunModification','Judgements','SingleEvent','ProbeControl')) {
            if ($project -eq 'Judgements') {
                $judgementsProject = 'tests\RizToolbox.Judgements.Tests\RizToolbox.Judgements.Tests.csproj'
                dotnet build $judgementsProject -c Release --verbosity minimal -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
                if ($LASTEXITCODE -ne 0) { throw 'Judgements test assembly build failed.' }
                $judgementsAssembly = Join-Path $repoRoot 'tests/RizToolbox.Judgements.Tests/bin/Release/net8.0/RizToolbox.Judgements.Tests.dll'
                if (-not (Test-Path -LiteralPath $judgementsAssembly -PathType Leaf)) { throw 'Judgements test assembly is missing after build.' }
                $listingText = & dotnet $judgementsAssembly '--list-legacy-cases'
                if ($LASTEXITCODE -ne 0) { throw 'Judgements legacy listing failed.' }
                try { $legacyCases = @(ConvertFrom-Json -InputObject ($listingText -join [Environment]::NewLine) -ErrorAction Stop) }
                catch { throw 'Judgements legacy listing is not valid JSON.' }
                $expectedLegacyCaseCount = 329
                if ($legacyCases.Count -ne $expectedLegacyCaseCount) { throw "Judgements legacy listing has $($legacyCases.Count) entries; expected $expectedLegacyCaseCount." }
                $legacyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $expectedLegacyIndex = 1
                foreach ($entry in $legacyCases) {
                    if ($null -eq $entry -or $expectedLegacyIndex -ne $entry.index -or [string]::IsNullOrWhiteSpace([string]$entry.name) -or -not $legacyNames.Add([string]$entry.name)) {
                        throw "Judgements legacy listing has a missing, duplicate, or out-of-order case at $expectedLegacyIndex."
                    }
                    $expectedLegacyIndex++
                }
                # The source range is 30 cases and the Canvas basis range is 18. Review either when its suite grows.
                foreach ($selector in @('--line-ring-source-case', '--canvas-basis-case')) {
                    $lastCase = if ($selector -eq '--line-ring-source-case') { 30 } else { 18 }
                    for ($caseIndex = 1; $caseIndex -le $lastCase; $caseIndex++) {
                        & dotnet $judgementsAssembly $selector $caseIndex
                        if ($LASTEXITCODE -ne 0) { throw "Judgements $selector $caseIndex failed with exit code $LASTEXITCODE." }
                    }
                }
                # The 13 pure managed numeric checks run together in one separate process.
                & dotnet $judgementsAssembly '--line-ring-numeric-checks'
                if ($LASTEXITCODE -ne 0) { throw "Judgements numeric checks failed with exit code $LASTEXITCODE." }
                # The 7 Ring X numeric checks run together in one separate process.
                & dotnet $judgementsAssembly '--line-ring-ring-x-numeric-checks'
                if ($LASTEXITCODE -ne 0) { throw "Judgements Ring X numeric checks failed with exit code $LASTEXITCODE." }
                # Six endpoint-local model cases use a controlled reflection fixture; they do not certify a live native initialization.
                & dotnet $judgementsAssembly '--line-ring-endpoint-local-checks'
                if ($LASTEXITCODE -ne 0) { throw "Judgements endpoint-local checks failed with exit code $LASTEXITCODE." }
                # Five occurrence model cases cover all 34 assertions without enabling live Seek.
                & dotnet $judgementsAssembly '--line-chart-occurrence-checks'
                if ($LASTEXITCODE -ne 0) { throw "Judgements line chart occurrence checks failed with exit code $LASTEXITCODE." }
                # Wide chart captures validate the native list and keep the physical line ordinals.
                & dotnet $judgementsAssembly '--judgement-chart-lines-checks'
                if ($LASTEXITCODE -ne 0) { throw "Judgements wide chart checks failed with exit code $LASTEXITCODE." }
                # Retained reader provider; original source cases remain separate.
                & dotnet $judgementsAssembly '--line-ring-target-provider-checks'
                if ($LASTEXITCODE -ne 0) { throw 'Judgements target provider checks failed.' }
                # Parent-equal local-position command; observed prestate and open external effects do not admit full Seek.
                & dotnet $judgementsAssembly '--line-ring-endpoint-writer-checks'
                if ($LASTEXITCODE -ne 0) { throw "Judgements endpoint writer checks failed with exit code $LASTEXITCODE." }
                # Original retained scratch reader/owner; external consumer effects remain open.
                & dotnet $judgementsAssembly '--line-ring-buffer-checks'
                if ($LASTEXITCODE -ne 0) { throw "Judgements original line buffer checks failed with exit code $LASTEXITCODE." }
                # Original retained renderer consumer; each native fixture runs in a fresh process.
                # Ordered storage recovery does not close Unity effects or admit full Seek.
                for ($caseIndex = 1; $caseIndex -le 12; $caseIndex++) {
                    & dotnet $judgementsAssembly '--line-renderer-consumer-case' $caseIndex
                    if ($LASTEXITCODE -ne 0) { throw "Judgements renderer consumer case $caseIndex failed with exit code $LASTEXITCODE." }
                }
                # Original producer control and physical element word stores; full Seek remains closed.
                for ($caseIndex = 1; $caseIndex -le 12; $caseIndex++) {
                    & dotnet $judgementsAssembly '--line-producer-control-case' $caseIndex
                    if ($LASTEXITCODE -ne 0) { throw "Judgements producer control case $caseIndex failed with exit code $LASTEXITCODE." }
                }
                # Exact current Hold guards; engine terminal/Idle admission remains separate.
                for ($caseIndex = 1; $caseIndex -le 15; $caseIndex++) {
                    & dotnet $judgementsAssembly '--hold-native-owner-case' $caseIndex
                    if ($LASTEXITCODE -ne 0) { throw "Hold native owner case $caseIndex failed with exit code $LASTEXITCODE." }
                }
                for ($caseIndex = 1; $caseIndex -le 14; $caseIndex++) {
                    & dotnet $judgementsAssembly '--hold-source-guard-case' $caseIndex
                    if ($LASTEXITCODE -ne 0) { throw "Hold source guard case $caseIndex failed with exit code $LASTEXITCODE." }
                }
                for ($caseIndex = 1; $caseIndex -le 6; $caseIndex++) {
                    & dotnet $judgementsAssembly '--hold-alive-guard-case' $caseIndex
                    if ($LASTEXITCODE -ne 0) { throw "Hold alive guard case $caseIndex failed with exit code $LASTEXITCODE." }
                }
                for ($caseIndex = 1; $caseIndex -le 16; $caseIndex++) {
                    & dotnet $judgementsAssembly '--hold-discovery-guard-case' $caseIndex
                    if ($LASTEXITCODE -ne 0) { throw "Hold discovery guard case $caseIndex failed with exit code $LASTEXITCODE." }
                }
                foreach ($entry in $legacyCases) {
                    & dotnet $judgementsAssembly '--legacy-case' $entry.index
                    if ($LASTEXITCODE -ne 0) { throw "Judgements legacy case $($entry.index) failed with exit code $LASTEXITCODE." }
                }
            } else {
                dotnet run --project "tests\RizToolbox.$project.Tests\RizToolbox.$project.Tests.csproj" -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false --disable-build-servers
                if ($LASTEXITCODE -ne 0) { throw "$project foundation tests failed." }
            }
            if ($project -eq 'Runtime') {
                & (Join-Path $PSScriptRoot 'Test-RuntimeTickAdmission.ps1') -NativeOnly `
                    -ManagedAssemblyPath (Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll') `
                    -HelperPath $env:RIZ_INITIALIZATION_CONTEXT_HELPER
                & (Join-Path $PSScriptRoot 'Test-AdoptedBrokerManaged.ps1') -ManagedAssemblyPath (Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll') -HelperDirectory $NativeOutputDirectory -FixturePath $env:RIZ_INITIALIZATION_ABI_FIXTURE -DobbyPath $env:RIZ_INITIALIZATION_ABI_DOBBY -OutputDirectory (Join-Path $NativeOutputDirectory 'adopted-broker-managed')
                & (Join-Path $PSScriptRoot 'Test-ListenerManaged.ps1') -ManagedAssemblyPath (Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll') -HelperDirectory $NativeOutputDirectory -FixturePath (Join-Path $listenerManagedOutput 'RizToolbox.ListenerManagedFixture.dll') -DobbyPath $env:RIZ_INITIALIZATION_ABI_DOBBY -OutputDirectory (Join-Path $NativeOutputDirectory 'listener-managed')
                & (Join-Path $PSScriptRoot 'Test-OperationManaged.ps1') -ManagedAssemblyPath (Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll') -HelperDirectory $NativeOutputDirectory -FixturePath (Join-Path $operationOutput 'RizToolbox.OperationManagedFixture.dll') -OutputDirectory (Join-Path $NativeOutputDirectory 'operation-managed')
                & (Join-Path $PSScriptRoot 'Test-OperationBootstrapManaged.ps1') -ManagedAssemblyPath (Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll') -HelperDirectory $NativeOutputDirectory -FixturePath (Join-Path $operationOutput 'RizToolbox.OperationManagedFixture.dll') -DobbyPath $env:RIZ_INITIALIZATION_ABI_DOBBY -OutputDirectory (Join-Path $NativeOutputDirectory 'operation-bootstrap-managed')
                & (Join-Path $PSScriptRoot 'Test-EngineNodeManaged.ps1') -ManagedAssemblyPath (Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll') -HelperDirectory $NativeOutputDirectory -FixturePath (Join-Path $engineNodeOutput 'RizToolbox.EngineNodeManagedFixture.dll') -DobbyPath $env:RIZ_INITIALIZATION_ABI_DOBBY -OutputDirectory (Join-Path $NativeOutputDirectory 'engine-node-managed')
                & (Join-Path $PSScriptRoot 'Test-EngineNodeKernel.ps1') -HelperPath (Join-Path $engineKernelOutput 'RizToolbox.EngineNodeKernel.dll') -ManagedAssemblyPath (Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll') -FixturePath (Join-Path $engineNodeOutput 'EngineNode.Fixture.exe') -ManagedFixturePath (Join-Path $engineNodeOutput 'RizToolbox.EngineNodeManagedFixture.dll') -DobbyPath $env:RIZ_INITIALIZATION_ABI_DOBBY -OutputDirectory (Join-Path $NativeOutputDirectory 'engine-node-kernel-cases')
                & (Join-Path $PSScriptRoot 'Test-OperationLedger.ps1') -ManagedAssemblyPath (Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll') -HelperDirectory $NativeOutputDirectory -FixturePath (Join-Path $operationOutput 'RizToolbox.OperationManagedFixture.dll') -OutputDirectory (Join-Path $NativeOutputDirectory 'operation-ledger')
            }
            if ($project -eq 'Runtime' -and $ValidationGameAssemblyPath) {
                & (Join-Path $PSScriptRoot 'Test-FirstInstallManaged.ps1') -ManagedAssemblyPath (Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll') -GameAssemblyPath $ValidationGameAssemblyPath -DobbyPath (Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll') -HelperDirectory $NativeOutputDirectory -OutputDirectory (Join-Path $NativeOutputDirectory 'first-install-managed')
            }
        }
    }
    $sdkDirectory = Join-Path $DistributionOutputDirectory 'sdk'
    & (Join-Path $PSScriptRoot 'Pack-Sdk.ps1') -SkipBuild -OutputDirectory $sdkDirectory
    if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'Test-SdkDistribution.ps1') -SkipPack -FeedDirectory $sdkDirectory }
    $distribution = New-RuntimeDistribution -RepoRoot $repoRoot -PackageKind $PackageKind -NativeOutputDirectory $NativeOutputDirectory -DoorstopOutput $doorstopOutput -EngineKernelOutput $engineKernelOutput -PlaybackRuntimeOutput $playbackRuntimeOutput -OutputDirectory $DistributionOutputDirectory -SdkDirectory $sdkDirectory -BasicPluginIds $BasicPluginIds
    Write-Output "$($distribution.PackageKind) package (preview/unverified): $($distribution.PackageRoot)"
    Write-Output "Package manifest: $($distribution.ManifestPath)"
} finally {
    [Environment]::SetEnvironmentVariable('RIZ_INITIALIZATION_ABI_FIXTURE', $initializationFixtureBefore)
    [Environment]::SetEnvironmentVariable('RIZ_INITIALIZATION_ABI_DOBBY', $initializationDobbyBefore)
    [Environment]::SetEnvironmentVariable('RIZ_INITIALIZATION_CONTEXT_HELPER', $initializationHelperBefore)
    [Environment]::SetEnvironmentVariable('RIZ_PLAYBACK_OWNED_FIXTURE', $playbackOwnedBefore)
    Pop-Location
}
