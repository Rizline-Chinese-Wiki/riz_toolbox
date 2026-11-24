#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$SourceDirectory = (Split-Path -Parent $PSScriptRoot),
    [string]$MsvcDirectory = '',
    [string]$WindowsSdkDirectory = '',
    [string]$WindowsSdkVersion = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $MsvcDirectory) { $MsvcDirectory = Get-RizMsvcDirectory -VcVarsPath (Get-RizVcVarsPath) }
if (-not $WindowsSdkDirectory) { $WindowsSdkDirectory = Get-RizWindowsSdkDirectory }
if (-not $WindowsSdkVersion) { $WindowsSdkVersion = Get-RizWindowsSdkVersion -Directory $WindowsSdkDirectory }

$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
$compilerDirectory = Join-Path $MsvcDirectory 'bin/Hostx64/x64'
$sdkInclude = Join-Path $WindowsSdkDirectory "Include/$WindowsSdkVersion"
$sdkLib = Join-Path $WindowsSdkDirectory "Lib/$WindowsSdkVersion"
$includeDirectories = @((Join-Path $MsvcDirectory 'include')) +
    @('ucrt', 'shared', 'um', 'winrt', 'cppwinrt' | ForEach-Object { Join-Path $sdkInclude $_ })
$libraryDirectories = @((Join-Path $MsvcDirectory 'lib/x64'),
    (Join-Path $sdkLib 'ucrt/x64'), (Join-Path $sdkLib 'um/x64'))
$singleSource = Join-Path $sourceRoot 'native/RizToolbox.SingleReturnPrototype/SingleReturnContext.cpp'
$playbackSource = Join-Path $sourceRoot 'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp'
$writerSource = Join-Path $sourceRoot 'native/RizToolbox.PlaybackSourcePrototype/PlaybackWriterCall.asm'
foreach ($path in @($singleSource, $playbackSource, $writerSource) +
    @('cl.exe', 'link.exe', 'ml64.exe' | ForEach-Object { Join-Path $compilerDirectory $_ })) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing playback build input: $path" }
}
foreach ($path in $includeDirectories + $libraryDirectories) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "Missing playback compiler directory: $path" }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$logPath = Join-Path $output 'playback-runtime-build.log'
[IO.File]::WriteAllText($logPath, '', [Text.UTF8Encoding]::new($false))

function Invoke-PlaybackCompiler([string]$Tool, [string[]]$Arguments) {
    # Arguments and SDK settings go directly to the child process. No shell
    # expansion or persistent developer-environment changes are needed.
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $compilerDirectory $Tool
    $start.WorkingDirectory = $output
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment['PATH'] = $compilerDirectory + ';' + $start.Environment['PATH']
    $start.Environment['INCLUDE'] = $includeDirectories -join ';'
    $start.Environment['LIB'] = $libraryDirectories -join ';'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw "Could not start $Tool" }
        try { $process.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal } catch { }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::AppendAllText($logPath, "$Tool $($Arguments -join ' ')`n$text`n", [Text.UTF8Encoding]::new($false))
        if ($process.ExitCode -ne 0) { throw "$Tool failed with exit $($process.ExitCode). See $logPath`n$text" }
    } finally { $process.Dispose() }
}

# Build only the two runtime helpers. Fixture binaries and long test runs have
# their own commands and are not needed to create an installable package.
$compile = @('/nologo', '/c', '/std:c++17', '/O2', '/W4', '/WX', '/EHa', '/fp:strict', '/MD', '/Z7', '/guard:cf')
$link = @('/nologo', '/DLL', '/DEBUG', '/MACHINE:X64', '/INCREMENTAL:NO', '/OPT:NOICF', '/DYNAMICBASE', '/NXCOMPAT', '/CETCOMPAT', '/GUARD:CF')
Invoke-PlaybackCompiler 'cl.exe' ($compile + @('/DRIZ_SINGLE_RETURN_PROTOTYPE_BUILD', '/FoSingleReturnContext.obj', $singleSource))
Invoke-PlaybackCompiler 'link.exe' ($link + @('SingleReturnContext.obj', '/OUT:RizToolbox.SingleReturn.Prototype.dll', '/PDB:RizToolbox.SingleReturn.Prototype.pdb', '/IMPLIB:RizToolbox.SingleReturn.Prototype.lib'))
Invoke-PlaybackCompiler 'cl.exe' ($compile + @('/FoPlaybackSourceContext.obj', $playbackSource))
Invoke-PlaybackCompiler 'ml64.exe' @('/nologo', '/c', '/Zi', '/FoPlaybackWriterCall.obj', $writerSource)
Invoke-PlaybackCompiler 'link.exe' ($link + @('PlaybackSourceContext.obj', 'PlaybackWriterCall.obj', '/OUT:RizToolbox.PlaybackSourcePrototype.dll', '/PDB:RizToolbox.PlaybackSourcePrototype.pdb', '/IMPLIB:RizToolbox.PlaybackSourcePrototype.lib'))
Write-Output "Playback runtime helpers: $output"
