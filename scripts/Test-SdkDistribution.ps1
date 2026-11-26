#requires -Version 7.2
[CmdletBinding()]
param([switch]$SkipPack, [string]$FeedDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $FeedDirectory) { $FeedDirectory = Join-Path $repoRoot 'artifacts/sdk' }
$feed = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($FeedDirectory)
if (-not $SkipPack) { & (Join-Path $PSScriptRoot 'Pack-Sdk.ps1') -OutputDirectory $feed }
$sdkProject = Join-Path $repoRoot 'src/RizToolbox.Sdk/RizToolbox.Sdk.csproj'
$sdkProperties = ([xml](Get-Content -LiteralPath $sdkProject -Raw)).Project.PropertyGroup
$package = Join-Path $feed "$($sdkProperties.PackageId).$([version]$sdkProperties.PackageVersion).nupkg"
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw 'Build the SDK reference package first.' }
$proofRoot = Join-Path $repoRoot ('artifacts/sdk-distribution-' + [Guid]::NewGuid().ToString('N'))
$hive = Join-Path $proofRoot 'template-engine'
$projects = Join-Path $proofRoot 'generated'
$mods = Join-Path $proofRoot 'mods'
New-Item -ItemType Directory -Path $proofRoot,$projects,$mods -Force | Out-Null
$escapedFeed = [Security.SecurityElement]::Escape($feed)
$escapedCache = [Security.SecurityElement]::Escape((Join-Path $proofRoot 'nuget-cache'))
@"
<configuration><packageSources><clear/><add key="SdkLocal" value="$escapedFeed"/><add key="NuGet" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="SdkLocal"><package pattern="RizToolbox.Sdk"/></packageSource><packageSource key="NuGet"><package pattern="Microsoft.*"/></packageSource></packageSourceMapping><config><add key="globalPackagesFolder" value="$escapedCache"/></config></configuration>
"@ | Set-Content -LiteralPath (Join-Path $proofRoot 'NuGet.config') -Encoding utf8
& dotnet new install (Join-Path $repoRoot 'templates/riz-plugin') --debug:custom-hive $hive
if ($LASTEXITCODE -ne 0) { throw 'Template installation in isolated engine failed.' }
# Add the same SDK-only probe to both independently compiled template packages.
# The shipped template stays a read-only scene example; these additions exercise
# the actual NuGet reference and formal loader rather than in-process test types.
$errorProbe = @'
using RizToolbox.Sdk;

namespace DistributionProbeNamespace;

public static class DistributionProbe
{
    public static IPluginServices Services { get; private set; } = null!;
    public static OperationError PrivateContractFailure { get; private set; } = null!;
    public static int ProviderCalls { get; private set; }

    public static void Enable(IPluginContext context)
    {
        ProviderCalls = 0;
        var chart = context.GetService<IOfficialChartSelectionService>(UiInputCapabilityIds.OfficialChartSelection);
        if (chart.Available && chart.Service?.ReadCurrent() is { Success: true, Value: { } selection })
        {
            var playFocus = selection.PlayButton?.Focused.ToString() ?? "unknown";
            context.Log.Log(LogLevel.Info, $"{context.PluginId}: selected official chart {selection.ChartId} with Play focus {playFocus}");
        }
        var lookup = context.GetService<IPluginServices>(InterPluginCapabilityIds.Services);
        if (!lookup.Available || lookup.Service is null)
            throw new InvalidOperationException(lookup.Error?.Message ?? "Services unavailable.");
        Services = lookup.Service;
        var rejected = Services.Publish<IPrivateContract>(context.PluginId + ".private", new(1, 0), new PrivateProvider());
        if (rejected.Success || rejected.Error?.Code != ErrorCode.ContractIdentityMismatch)
            throw new InvalidOperationException("A private package contract was accepted.");
        PrivateContractFailure = rejected.Error;
        var published = Services.Publish<IPluginDataService>(context.PluginId + ".errors", new(1, 0), new Provider());
        if (!published.Success) throw new InvalidOperationException(published.Error?.Message);
    }

    private sealed class Provider : IPluginDataService
    {
        public OperationResult<ServiceData> Invoke(ServiceRequest request)
        {
            ProviderCalls++;
            return request.Operation switch
            {
                // Deliberately misleading text: the declared code must survive.
                "known" => OperationResult<ServiceData>.Fail(new(ErrorCode.Conflict,
                    "TargetMissing: a translated explanation", "test.structured", "note/7", true)),
                "future" => OperationResult<ServiceData>.Fail(new((ErrorCode)987654,
                    "A future failure", "test.future", "track/9", true)),
                "fault" => throw new InvalidOperationException("Independent provider failed."),
                _ => OperationResult<ServiceData>.Ok(new(request.Operation,
                    new Dictionary<string, string> { ["calls"] = ProviderCalls.ToString() }))
            };
        }
    }

    // Public shape and portable DTOs do not grant shared assembly identity.
    public interface IPrivateContract { OperationResult<ServiceData> Invoke(ServiceRequest request); }
    private sealed class PrivateProvider : IPrivateContract
    {
        public OperationResult<ServiceData> Invoke(ServiceRequest request) => OperationResult<ServiceData>.Ok(new(request.Operation));
    }
}
'@
foreach ($entry in @(@{Name='AuthorOne';Id='author.one'},@{Name='AuthorTwo';Id='author.two'})) {
    $directory = Join-Path $projects $entry.Name
    & dotnet new riz-plugin --name $entry.Name --PluginId $entry.Id --output $directory --debug:custom-hive $hive
    if ($LASTEXITCODE -ne 0) { throw "Template generation failed: $($entry.Name)." }
    $pluginPath = Join-Path $directory 'Plugin.cs'
    $pluginSource = [IO.File]::ReadAllText($pluginPath)
    $entrySeam = 'context.MainThread.VerifyAccess();'
    if ($pluginSource.Split(@($entrySeam),[StringSplitOptions]::None).Count -ne 2) { throw 'The template probe entry seam changed.' }
    [IO.File]::WriteAllText($pluginPath,$pluginSource.Replace($entrySeam,$entrySeam + "`n        DistributionProbe.Enable(context);"),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $directory 'DistributionProbe.cs'),$errorProbe.Replace('DistributionProbeNamespace',$entry.Name),[Text.UTF8Encoding]::new($false))
    $project = Join-Path $directory ($entry.Name + '.csproj')
    & dotnet restore $project --configfile (Join-Path $proofRoot 'NuGet.config') --use-lock-file --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Isolated SDK restore failed: $($entry.Name)." }
    & dotnet build $project -c Release --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Generated plugin build failed: $($entry.Name)." }
    $destination = Join-Path $mods $entry.Id
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $directory "bin/Release/net6.0/$($entry.Name).dll"),(Join-Path $directory 'plugin.json') -Destination $destination
}
& dotnet run --project (Join-Path $repoRoot 'tests/RizToolbox.Distribution.Tests/RizToolbox.Distribution.Tests.csproj') -c Release -- $package $projects $mods
if ($LASTEXITCODE -ne 0) { throw "SDK distribution conformance failed. Evidence: $proofRoot" }
@{schemaVersion=1;createdAtUtc=[DateTimeOffset]::UtcNow.ToString('o');packageSha256=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant();generatedProjects=$projects;mods=$mods;checksPassed=14;runtimeGameVerification=$false} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $proofRoot 'result.json') -Encoding utf8
Write-Output "SDK distribution evidence: $proofRoot"
