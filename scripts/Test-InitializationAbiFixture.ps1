#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeAssembly,
    [string]$NativeDirectory,
    [string]$DobbyPath,
    [string]$NativeHelperPath,
    [string]$OutputDirectory,
    [switch]$SkipNativeBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $NativeDirectory) { $NativeDirectory = Join-Path $repoRoot 'artifacts\native-initialization-abi' }
if (-not $DobbyPath) { $DobbyPath = Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('artifacts\initialization-abi-run-' + [guid]::NewGuid().ToString('N')) }
$runtimePath = [IO.Path]::GetFullPath($RuntimeAssembly)
$sdkPath = Join-Path (Split-Path -Parent $runtimePath) 'RizToolbox.Sdk.dll'
$testPath = Join-Path $repoRoot 'tests\RizToolbox.Runtime.Tests\NativeInitializationAbiFixtureRegression.cs'
$nativePath = Join-Path ([IO.Path]::GetFullPath($NativeDirectory)) 'RizToolbox.InitializationAbiFixture.dll'
$dobbyFullPath = [IO.Path]::GetFullPath($DobbyPath)
if (-not $NativeHelperPath) { $NativeHelperPath = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($NativeDirectory))) 'RizToolbox.JudgeContext.dll' }
$helperFullPath = [IO.Path]::GetFullPath($NativeHelperPath)
foreach ($path in @($runtimePath, $sdkPath, $testPath, $dobbyFullPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required existing file is missing: $path" }
}
if (-not $SkipNativeBuild) {
    & (Join-Path $PSScriptRoot 'Build-JudgeContext.ps1') -OutputDirectory (Split-Path -Parent $helperFullPath) -DobbyPath $dobbyFullPath
    & (Join-Path $PSScriptRoot 'Build-InitializationAbiFixture.ps1') -OutputDirectory $NativeDirectory
}
if (-not (Test-Path -LiteralPath $helperFullPath -PathType Leaf)) { throw "Build the typed initialization helper first or supply -NativeHelperPath: $helperFullPath" }
if (-not (Test-Path -LiteralPath $nativePath -PathType Leaf)) { throw "Build the native fixture first: $nativePath" }
$runnerRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $runnerRoot) { throw 'Use a new runner directory; an existing build is never overwritten.' }
New-Item -ItemType Directory -Path $runnerRoot | Out-Null
function Escape-Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }
$projectPath = Join-Path $runnerRoot 'InitializationAbi.Runner.csproj'
# No project reference: this runner never touches production/tests bin or obj.
# Its friend assembly name only permits exercising the internal sealed authority.
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType>
    <AssemblyName>RizToolbox.Runtime.Tests</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><LangVersion>10.0</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="RizToolbox.Runtime"><HintPath>$(Escape-Xml $runtimePath)</HintPath></Reference>
    <Reference Include="RizToolbox.Sdk"><HintPath>$(Escape-Xml $sdkPath)</HintPath></Reference>
    <Compile Include="$(Escape-Xml $testPath)" Link="NativeInitializationAbiFixtureRegression.cs" />
    <Compile Include="Runner.cs" />
  </ItemGroup>
</Project>
"@
[IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))
$runner = @'
try
{
    NativeInitializationAbiFixtureRegression.Run(args[0], args[1]);
    Console.WriteLine("{\"kind\":\"production-initialization-native-ABI-fixture\",\"passed\":true,\"realDobby\":true,\"nativeWorker\":true,\"gameRuntimeVerified\":false}");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
'@
[IO.File]::WriteAllText((Join-Path $runnerRoot 'Runner.cs'), $runner, [Text.UTF8Encoding]::new($false))
& dotnet build $projectPath --configuration Release --nologo --disable-build-servers --verbosity minimal 2>&1 |
    Tee-Object -FilePath (Join-Path $runnerRoot 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Isolated ABI runner compile failed: $LASTEXITCODE" }
$runnerAssembly = Join-Path $runnerRoot 'bin\Release\net8.0\RizToolbox.Runtime.Tests.dll'
$previousHelper = [Environment]::GetEnvironmentVariable('RIZ_INITIALIZATION_CONTEXT_HELPER')
try {
    $env:RIZ_INITIALIZATION_CONTEXT_HELPER = $helperFullPath
    & dotnet $runnerAssembly $nativePath $dobbyFullPath 2>&1 | Tee-Object -FilePath (Join-Path $runnerRoot 'fixture-result.json')
    if ($LASTEXITCODE -ne 0) { throw "Production native ABI fixture failed: $LASTEXITCODE" }
} finally { [Environment]::SetEnvironmentVariable('RIZ_INITIALIZATION_CONTEXT_HELPER', $previousHelper) }
$files = @($runtimePath, $sdkPath, $testPath, $nativePath, $dobbyFullPath, $helperFullPath, $runnerAssembly) | ForEach-Object {
    [ordered]@{ path=$_; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ schemaVersion=1; kind='production-initialization-native-ABI-fixture'; realDobby=$true; gameRuntimeVerified=$false; files=@($files) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runnerRoot 'fixture-manifest.json') -Encoding utf8
Write-Output "Isolated runner: $runnerRoot"
