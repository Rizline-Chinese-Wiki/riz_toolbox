# Shared toolchain discovery for the RizToolbox build and test scripts.
#
# Every entry point accepts an explicit override (parameter or environment
# variable). These helpers only provide defaults so a fresh clone works on a
# normal Windows developer machine without editing absolute paths.
#
#   RIZ_VCVARS                 -> vcvars64.bat
#   RIZ_MSVC_DIRECTORY         -> <VS>/VC/Tools/MSVC/<version>
#   RIZ_WINDOWS_SDK_DIRECTORY  -> Windows 10/11 SDK root
#   RIZ_WINDOWS_SDK_VERSION    -> Windows SDK version (e.g. 10.0.22621.0)
#   RIZ_PYTHON                 -> python.exe
#   RIZ_DOTNET                 -> dotnet.exe
#   RIZ_HOSTFXR                -> hostfxr.dll
#   RIZ_HOST_HEADERS           -> Microsoft.NETCore.App.Host.win-x64 native dir

function Get-RizVcVarsPath {
    [CmdletBinding()]
    param([string]$Path)

    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($Path) { $candidates.Add($Path) }
    if ($env:RIZ_VCVARS) { $candidates.Add($env:RIZ_VCVARS) }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        foreach ($switch in @('-latest', '-all')) {
            $installations = & $vswhere $switch -products * `
                -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
                -property installationPath 2>$null
            foreach ($installation in @($installations)) {
                if (-not [string]::IsNullOrWhiteSpace($installation)) {
                    $candidates.Add((Join-Path $installation 'VC\Auxiliary\Build\vcvars64.bat'))
                }
            }
        }
    }

    foreach ($root in @(${env:ProgramFiles}, ${env:ProgramFiles(x86)})) {
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        foreach ($year in @('2022', '2019')) {
            foreach ($edition in @('Enterprise', 'Professional', 'Community', 'BuildTools')) {
                $candidates.Add((Join-Path $root "Microsoft Visual Studio\$year\$edition\VC\Auxiliary\Build\vcvars64.bat"))
            }
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    throw 'Visual Studio C++ x64 tools not found. Install the "Desktop development with C++" workload (VS 2019 or 2022) or set RIZ_VCVARS to vcvars64.bat.'
}

function Get-RizMsvcDirectory {
    [CmdletBinding()]
    param([string]$VcVarsPath)

    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($env:RIZ_MSVC_DIRECTORY) { $candidates.Add($env:RIZ_MSVC_DIRECTORY) }
    if ($VcVarsPath) {
        # <VS>/VC/Auxiliary/Build/vcvars64.bat -> <VS>/VC/Tools/MSVC
        $vcRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $VcVarsPath))
        $toolsRoot = Join-Path $vcRoot 'Tools\MSVC'
        if (Test-Path -LiteralPath $toolsRoot -PathType Container) {
            Get-ChildItem -LiteralPath $toolsRoot -Directory |
                Sort-Object -Property @{ Expression = { try { [version]$_.Name } catch { [version]'0.0.0.0' } } } -Descending |
                ForEach-Object { $candidates.Add($_.FullName) }
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'bin\Hostx64\x64\cl.exe') -PathType Leaf) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    throw 'MSVC toolset not found. Install the Visual Studio C++ x64 tools or set RIZ_MSVC_DIRECTORY to <VS>/VC/Tools/MSVC/<version>.'
}

function Get-RizWindowsSdkDirectory {
    [CmdletBinding()]
    param([string]$Path)
    if ($Path) { return $Path }
    if ($env:RIZ_WINDOWS_SDK_DIRECTORY) { return $env:RIZ_WINDOWS_SDK_DIRECTORY }
    $root = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
    if (Test-Path -LiteralPath $root -PathType Container) { return $root }
    throw 'Windows 10/11 SDK not found. Install it with the Visual Studio C++ workload or set RIZ_WINDOWS_SDK_DIRECTORY.'
}

function Get-RizWindowsSdkVersion {
    [CmdletBinding()]
    param([string]$Directory, [string]$Version)
    if ($Version) { return $Version }
    if ($env:RIZ_WINDOWS_SDK_VERSION) { return $env:RIZ_WINDOWS_SDK_VERSION }
    if (-not $Directory) { $Directory = Get-RizWindowsSdkDirectory }
    $include = Join-Path $Directory 'Include'
    if (Test-Path -LiteralPath $include -PathType Container) {
        $versions = Get-ChildItem -LiteralPath $include -Directory |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object -Property @{ Expression = { [version]$_.Name } } -Descending
        if ($versions) { return $versions[0].Name }
    }
    throw 'Windows SDK version not found. Set RIZ_WINDOWS_SDK_VERSION to an installed SDK version.'
}

function Get-RizPythonPath {
    [CmdletBinding()]
    param([string]$Path)
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($Path) { $candidates.Add($Path) }
    if ($env:RIZ_PYTHON) { $candidates.Add($env:RIZ_PYTHON) }
    foreach ($name in @('python.exe', 'python3.exe')) {
        $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { $candidates.Add($command.Source) }
    }
    foreach ($candidate in @('C:\Python314\python.exe', 'C:\Python313\python.exe', 'C:\Python312\python.exe')) {
        $candidates.Add($candidate)
    }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }
    throw 'python.exe not found. Install Python 3.8+ and add it to PATH, or set RIZ_PYTHON.'
}

function Get-RizDotnetPath {
    [CmdletBinding()]
    param([string]$Path)
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($Path) { $candidates.Add($Path) }
    if ($env:RIZ_DOTNET) { $candidates.Add($env:RIZ_DOTNET) }
    $command = Get-Command 'dotnet.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { $candidates.Add($command.Source) }
    $candidates.Add((Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'))
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }
    throw 'dotnet.exe not found. Install the .NET 8 SDK, or set RIZ_DOTNET.'
}

function Get-RizHostFxrPath {
    [CmdletBinding()]
    param([string]$Path, [string]$DotnetPath)
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($Path) { $candidates.Add($Path) }
    if ($env:RIZ_HOSTFXR) { $candidates.Add($env:RIZ_HOSTFXR) }
    if ($DotnetPath) {
        $fxrRoot = Join-Path (Split-Path -Parent $DotnetPath) 'host\fxr'
        if (Test-Path -LiteralPath $fxrRoot -PathType Container) {
            Get-ChildItem -LiteralPath $fxrRoot -Directory |
                Sort-Object -Property @{ Expression = { try { [version]$_.Name } catch { [version]'0.0.0.0' } } } -Descending |
                ForEach-Object { $candidates.Add((Join-Path $_.FullName 'hostfxr.dll')) }
        }
    }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }
    throw 'hostfxr.dll not found. Install the .NET runtime, or set RIZ_HOSTFXR.'
}

function Get-RizHostHeadersDirectory {
    [CmdletBinding()]
    param([string]$Path, [string]$DotnetPath)
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($Path) { $candidates.Add($Path) }
    if ($env:RIZ_HOST_HEADERS) { $candidates.Add($env:RIZ_HOST_HEADERS) }
    if ($DotnetPath) {
        $packs = Join-Path (Split-Path -Parent $DotnetPath) 'packs\Microsoft.NETCore.App.Host.win-x64'
        if (Test-Path -LiteralPath $packs -PathType Container) {
            Get-ChildItem -LiteralPath $packs -Directory |
                Sort-Object -Property @{ Expression = { try { [version]$_.Name } catch { [version]'0.0.0.0' } } } -Descending |
                ForEach-Object { $candidates.Add((Join-Path $_.FullName 'runtimes\win-x64\native')) }
        }
    }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'nethost.lib') -PathType Leaf)) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }
    return $null
}

# Several evidence scripts shell out to ripgrep (`rg --files ...`) to inventory
# sources deterministically. Provide a small compatibility shim when ripgrep is
# not installed so a fresh clone still builds and self-validates.
if (-not (Get-Command rg -CommandType Application -ErrorAction SilentlyContinue)) {
    function global:rg {
        [CmdletBinding()]
        param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

        $files = $false
        $hidden = $false
        $noIgnore = $false
        $roots = [System.Collections.Generic.List[string]]::new()
        $globs = [System.Collections.Generic.List[string]]::new()

        for ($index = 0; $index -lt $Arguments.Count; $index++) {
            $argument = $Arguments[$index]
            switch -CaseSensitive ($argument) {
                '--files' { $files = $true; continue }
                '--hidden' { $hidden = $true; continue }
                '--no-ignore' { $noIgnore = $true; continue }
                '-g' {
                    $index++
                    if ($index -ge $Arguments.Count) { throw 'rg shim: -g requires a glob.' }
                    $globs.Add($Arguments[$index])
                    continue
                }
                default {
                    if ($argument.StartsWith('-')) { throw "rg shim does not implement argument '$argument'." }
                    $roots.Add($argument)
                }
            }
        }
        if (-not $files) { throw 'rg shim only implements --files.' }
        if ($roots.Count -eq 0) { $roots.Add('.') }

        $excludedDirectories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        [void]$excludedDirectories.Add('.git')
        if (-not $noIgnore) {
            foreach ($name in @('bin', 'obj', 'out', 'artifacts', 'work', '.tools', '.vs', 'node_modules', 'TestResults', '__pycache__')) {
                [void]$excludedDirectories.Add($name)
            }
        }

        $patternCache = @{}
        $toRegex = {
            param([string]$glob)
            if ($patternCache.ContainsKey($glob)) { return $patternCache[$glob] }
            $builder = [System.Text.StringBuilder]::new('^')
            $position = 0
            while ($position -lt $glob.Length) {
                $character = $glob[$position]
                if ($character -eq '*') {
                    if (($position + 1) -lt $glob.Length -and $glob[$position + 1] -eq '*') {
                        if (($position + 2) -lt $glob.Length -and ($glob[$position + 2] -eq '/' -or $glob[$position + 2] -eq '\')) {
                            [void]$builder.Append('(?:.*[/\\])?')
                            $position += 3
                            continue
                        }
                        [void]$builder.Append('.*')
                        $position += 2
                        continue
                    }
                    [void]$builder.Append('[^/\\]*')
                    $position++
                    continue
                }
                if ($character -eq '?') { [void]$builder.Append('[^/\\]'); $position++; continue }
                [void]$builder.Append([regex]::Escape([string]$character))
                $position++
            }
            [void]$builder.Append('$')
            $regex = [regex]::new($builder.ToString(), [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            $patternCache[$glob] = $regex
            return $regex
        }

        $results = [System.Collections.Generic.List[string]]::new()
        $base = (Get-Location).Path
        foreach ($root in $roots) {
            if (-not (Test-Path -LiteralPath $root)) { throw "rg shim: path not found: $root" }
            $item = Get-Item -LiteralPath $root
            $entries = if ($item.PSIsContainer) {
                Get-ChildItem -LiteralPath $item.FullName -Recurse -File -Force:$hidden -ErrorAction SilentlyContinue |
                    Where-Object {
                        $relative = [IO.Path]::GetRelativePath($item.FullName, $_.DirectoryName)
                        $skip = $false
                        foreach ($segment in ($relative -split '[\\/]')) {
                            if ($excludedDirectories.Contains($segment)) { $skip = $true; break }
                        }
                        -not $skip
                    }
            } else {
                @($item)
            }
            foreach ($entry in $entries) {
                if (-not $hidden -and ($entry.Attributes -band [IO.FileAttributes]::Hidden)) { continue }
                $relativePath = [IO.Path]::GetRelativePath($base, $entry.FullName).Replace('\', '/')
                $fileName = $entry.Name
                $decision = $null
                foreach ($glob in $globs) {
                    $negated = $glob.StartsWith('!')
                    $body = if ($negated) { $glob.Substring(1) } else { $glob }
                    # gitignore/ripgrep semantics: a glob without a slash matches the file name.
                    $subject = if ($body.Contains('/') -or $body.Contains('\')) { $relativePath } else { $fileName }
                    if ((& $toRegex $body).IsMatch($subject)) { $decision = -not $negated }
                }
                if ($null -eq $decision) { $decision = -not ($globs | Where-Object { -not $_.StartsWith('!') } | Select-Object -First 1) }
                if ($decision) { $results.Add($relativePath) }
            }
        }
        $sorted = $results | Sort-Object -Unique
        $global:LASTEXITCODE = 0
        $sorted
    }
}
