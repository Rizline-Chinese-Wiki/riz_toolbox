#requires -Version 7.2
Set-StrictMode -Version Latest

function Get-InstallTarget([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':')) { throw 'Manifest path must be relative.' }
    $parts = $Relative -split '[\\/]'
    if ($parts | Where-Object { $_ -in @('', '.', '..') -or $_.TrimEnd(' ', '.') -ne $_ }) { throw "Unsafe manifest path: $Relative" }
    # changelog.txt was copied and recorded by the earlier diagnostic installer.
    # These two exact new basenames support candidate ownership/recovery and
    # uninstall. The default package whitelist remains its original 23 files.
    if (($parts.Count -eq 1 -and $parts[0] -notin @('winhttp.dll','doorstop_config.ini','.doorstop_version','changelog.txt','.riz_toolbox.install.lock','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')) -or
        ($parts.Count -gt 1 -and $parts[0] -notin @('BepInEx','dotnet'))) { throw "Path is outside loader-owned locations: $Relative" }
    $target = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $target.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Target escaped game directory.' }
    $cursor = $Root
    foreach ($part in $parts) {
        $cursor = Join-Path $cursor $part
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked installation path: $Relative" }
        }
    }
    return $target
}

function Get-InstallHash([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Expected a regular file: $Path" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-InstallManifest([string]$Path, $Manifest) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $Manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporary -Encoding utf8
        Sync-InstallFile $temporary
        [IO.File]::Move($temporary, $Path, $true)
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}

function Read-InstallManifest([string]$Root, [string]$Path) {
    $manifest = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($manifest.owner -ne 'riz_toolbox' -or $manifest.gameRoot -ne $Root) { throw 'Installation manifest ownership mismatch.' }
    $owned = @{}
    foreach ($entry in $manifest.files) {
        $entry.path = Assert-InstallPayloadPath $Root $entry.path
        $target = Get-InstallTarget $Root $entry.path
        if ((Test-Path -LiteralPath $target) -and -not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Manifest target is not a file: $($entry.path)" }
        if ($entry.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or $owned.ContainsKey($entry.path)) { throw 'Invalid or duplicate manifest file entry.' }
        $owned[$entry.path] = $entry.sha256.ToLowerInvariant()
    }
    if (-not ($manifest.PSObject.Properties.Name -contains 'pending')) { $manifest | Add-Member -NotePropertyName pending -NotePropertyValue @() }
    $hasTransaction = $manifest.PSObject.Properties.Name -contains 'transaction'
    if ($hasTransaction) {
        if ($null -eq $manifest.transaction -or $manifest.transaction.schemaVersion -notin @(1,2) -or
            $manifest.transaction.id -cnotmatch '^[0-9a-f]{32}$' -or @($manifest.pending).Count -eq 0) { throw 'Invalid install recovery transaction.' }
        $staging = $manifest.transaction.PSObject.Properties['usesRecoveryStaging']
        if ($staging -and $staging.Value -isnot [bool]) { throw 'Invalid install recovery staging marker.' }
    }
    # Intent precedes copying. Recovery accepts only intended new bytes or recorded old bytes.
    $seen = @{}
    foreach ($entry in $manifest.pending) {
        $entry.path = Assert-InstallPayloadPath $Root $entry.path
        $target = Get-InstallTarget $Root $entry.path
        $isRemoval = $hasTransaction -and $manifest.transaction.schemaVersion -eq 2 -and
            ($entry.PSObject.Properties.Name -contains 'operation') -and $entry.operation -ceq 'remove'
        if (($isRemoval -and ($null -ne $entry.sha256 -or -not $entry.previousSha256 -or $entry.path -cnotin (Get-InstallMigrationPaths))) -or
            (-not $isRemoval -and ($entry.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
                (($entry.PSObject.Properties.Name -contains 'operation') -and $entry.operation -cne 'write'))) -or
            $seen.ContainsKey($entry.path)) { throw 'Invalid pending manifest entry.' }
        $seen[$entry.path] = $true
        if ($null -ne $entry.previousSha256 -and $entry.previousSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid pending previous hash.' }
        if ($null -ne $entry.previousSha256 -and (-not $owned.ContainsKey($entry.path) -or $owned[$entry.path] -ne $entry.previousSha256)) { throw 'Pending operation does not match recorded ownership.' }
        if (Test-Path -LiteralPath $target) {
            $current = Get-InstallHash $target
            if ($current -eq $entry.sha256) { if (-not $hasTransaction) { $owned[$entry.path] = $current } }
            elseif ($null -eq $entry.previousSha256 -or $current -ne $entry.previousSha256) { throw "Interrupted install target was modified: $($entry.path)" }
        }
    }
    # A transactional journal keeps its original ownership map until commit or
    # rollback. Reading it must never adopt a partially upgraded set of files.
    if ($hasTransaction) { return $manifest }
    if (@($manifest.pending).Count -gt 0) {
        Write-Warning 'Legacy interrupted journal has no rollback backups. Only recorded matching bytes can be recovered; the prior complete version cannot be reconstructed from hashes alone.'
    }
    $manifest.files = @($owned.GetEnumerator() | Sort-Object Name | ForEach-Object { [pscustomobject]@{ path=$_.Key; sha256=$_.Value } })
    $manifest.pending = @()
    return $manifest
}

function Get-OwnedInstallFiles($Manifest) {
    $owned = @{}
    foreach ($entry in $Manifest.files) { $owned[$entry.path] = $entry.sha256 }
    return $owned
}

function Get-InstallMigrationPaths {
    return @('RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll','BepInEx\plugins\RizToolbox\RizToolbox.EngineNodeKernel.dll')
}

function Assert-InstallRemovalPlan([string]$Root,[string[]]$Paths,[hashtable]$Owned,$Plan) {
    $seen=@{};$writes=@{};foreach($entry in $Plan){$writes[(Assert-InstallPayloadPath $Root $entry.path)]=$true}
    foreach($path in $Paths){
        $relative=Assert-InstallPayloadPath $Root $path
        if($relative -cnotin (Get-InstallMigrationPaths) -or $seen.ContainsKey($relative) -or $writes.ContainsKey($relative)){throw 'Invalid candidate layout removal.'}
        $seen[$relative]=$true;$target=Get-InstallTarget $Root $relative
        if(Test-Path -LiteralPath $target){
            if(-not $Owned.ContainsKey($relative) -or (Get-InstallHash $target) -ne $Owned[$relative]){throw "Modified/unowned target: $relative"}
        }
    }
}

function Assert-InstallPlan([string]$Root, $Plan, [hashtable]$Owned) {
    $seen = @{}
    foreach ($entry in $Plan) {
        $relative = Assert-InstallPayloadPath $Root $entry.path
        if ($seen.ContainsKey($relative)) { throw "Duplicate install plan target: $relative" }
        $seen[$relative] = $true
        $target = Get-InstallTarget $Root $entry.path
        if (Test-Path -LiteralPath $target) {
            if (-not $Owned.ContainsKey($entry.path) -or (Get-InstallHash $target) -ne $Owned[$entry.path]) { throw "Modified/unowned target: $($entry.path)" }
        }
    }
}

function Assert-InstallPayloadPath([string]$Root, [string]$Relative) {
    $null = Get-InstallTarget $Root $Relative
    $canonical = $Relative.Replace('/', '\')
    if ($canonical -eq '.riz_toolbox.install.lock' -or $canonical -eq 'BepInEx\RizToolbox\installation.json' -or
        $canonical -eq 'BepInEx\RizToolbox\install-recovery' -or
        $canonical.StartsWith('BepInEx\RizToolbox\install-recovery\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Payload cannot claim installer management files: $Relative"
    }
    return $canonical
}

function Enter-InstallLock([string]$Root) {
    $path = Get-InstallTarget $Root '.riz_toolbox.install.lock'
    try { return [IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch { throw "Another install or removal may be active, or its lock is unavailable: $path. $($_.Exception.Message)" }
}

function Sync-InstallFile([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
    try { $stream.Flush($true) } finally { $stream.Dispose() }
}

function Get-InstallRecoveryRelative([string]$Id) {
    if ($Id -cnotmatch '^[0-9a-f]{32}$') { throw 'Invalid install recovery identity.' }
    return ('BepInEx\RizToolbox\install-recovery\' + $Id)
}

function Get-InstallBackupRelative([string]$Id, [int]$Index) {
    if ($Index -lt 0) { throw 'Invalid install backup index.' }
    return ((Get-InstallRecoveryRelative $Id) + '\' + $Index.ToString('D4') + '.bak')
}

function Get-InstallStagingRelative([string]$Id) {
    return ((Get-InstallRecoveryRelative $Id) + '\payload.tmp')
}

function Assert-InstallCurrentHash([string]$Root, [string]$Relative, [AllowNull()][string]$Expected) {
    $target = Get-InstallTarget $Root $Relative
    if (Test-Path -LiteralPath $target) {
        if (-not $Expected -or (Get-InstallHash $target) -ne $Expected) { throw "Install target changed during transaction: $Relative" }
    } elseif ($Expected) { throw "Install target disappeared during transaction: $Relative" }
}

function Write-InstallPayload([string]$Root, [string]$Relative, [string]$Source, [string]$Hash, [AllowNull()][string]$Previous, [string]$StagingRelative) {
    Assert-InstallCurrentHash $Root $Relative $Previous
    $target = Get-InstallTarget $Root $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    if ($StagingRelative) {
        $temporary = Get-InstallTarget $Root $StagingRelative
        if (Test-Path -LiteralPath $temporary) { throw "Install staging file already exists: $temporary" }
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($temporary))
    } else {
        $temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    }
    try {
        Copy-Item -LiteralPath $Source -Destination $temporary
        if ((Get-InstallHash $temporary) -ne $Hash) { throw "Source changed during install: $Relative" }
        Sync-InstallFile $temporary
        Assert-InstallCurrentHash $Root $Relative $Previous
        [IO.File]::Move($temporary, $target, [bool]$Previous)
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}

function Remove-InstallBackups([string]$Root, [string]$Id, $Pending) {
    # Delete only journal-listed backup files; never recurse through a directory
    # that might also contain user files or a different transaction.
    for ($index = 0; $index -lt @($Pending).Count; ++$index) {
        if (-not $Pending[$index].previousSha256) { continue }
        $path = Get-InstallTarget $Root (Get-InstallBackupRelative $Id $index)
        if (Test-Path -LiteralPath $path) {
            if ((Get-InstallHash $path) -ne $Pending[$index].previousSha256) { throw "Preserved modified install backup: $path" }
            Remove-Item -LiteralPath $path
        }
    }
    $directory = Get-InstallTarget $Root (Get-InstallRecoveryRelative $Id)
    if ((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $directory
    }
}

function Restore-InstallTransaction([string]$Root, [string]$ManifestPath, $Manifest) {
    if (-not ($Manifest.PSObject.Properties.Name -contains 'transaction')) { return $Manifest }
    $pending = @($Manifest.pending); $id = $Manifest.transaction.id
    $staging = $Manifest.transaction.PSObject.Properties['usesRecoveryStaging']
    $stagingRelative = if ($staging -and $staging.Value) { Get-InstallStagingRelative $id }
    $current = @{}
    # Preflight the entire rollback before changing any file. An unknown edit
    # or missing/corrupt backup leaves the journal and all backups available.
    for ($index = 0; $index -lt $pending.Count; ++$index) {
        $entry = $pending[$index]; $target = Get-InstallTarget $Root $entry.path
        $hash = if (Test-Path -LiteralPath $target) { Get-InstallHash $target } else { $null }
        if ($hash -and $hash -ne $entry.sha256 -and $hash -ne $entry.previousSha256) {
            throw "Recovery refused modified target: $($entry.path). Original backups and transaction journal were preserved."
        }
        $current[$entry.path] = $hash
        if ($entry.previousSha256) {
            $backup = Get-InstallTarget $Root (Get-InstallBackupRelative $id $index)
            if ((Get-InstallHash $backup) -ne $entry.previousSha256) {
                throw "Recovery backup hash mismatch: $($entry.path). Journal and remaining files were preserved."
            }
        }
    }
    # The journal identifies the staging file even if its copy stopped partway through.
    if ($stagingRelative) {
        $temporary = Get-InstallTarget $Root $stagingRelative
        if (Test-Path -LiteralPath $temporary) {
            if (!(Test-Path -LiteralPath $temporary -PathType Leaf)) { throw "Invalid recovery staging file: $temporary" }
            Remove-Item -LiteralPath $temporary
        }
    }
    for ($index = $pending.Count - 1; $index -ge 0; --$index) {
        $entry = $pending[$index]; $hash = $current[$entry.path]
        if ($entry.previousSha256) {
            if ($hash -ne $entry.previousSha256) {
                $backup = Get-InstallTarget $Root (Get-InstallBackupRelative $id $index)
                Write-InstallPayload $Root $entry.path $backup $entry.previousSha256 $hash $stagingRelative
            }
        } elseif ($hash) {
            Assert-InstallCurrentHash $Root $entry.path $entry.sha256
            Remove-Item -LiteralPath (Get-InstallTarget $Root $entry.path)
        }
    }
    # Keep the original file ownership map, including unchanged/user-edited
    # settings which were deliberately absent from this transaction's plan.
    $Manifest.pending = @(); $Manifest.PSObject.Properties.Remove('transaction')
    Write-InstallManifest $ManifestPath $Manifest
    try { Remove-InstallBackups $Root $id $pending }
    catch { Write-Warning "Rollback completed; retained backup cleanup needs attention. $($_.Exception.Message)" }
    Write-Information 'Recovered interrupted installation to its previous file set.' -InformationAction Continue
    return $Manifest
}

function Invoke-InstallTransaction([string]$Root, [string]$ManifestPath, $Manifest, $Plan, [hashtable]$Owned, [string[]]$RemovePaths=@()) {
    Assert-InstallPlan $Root $Plan $Owned
    Assert-InstallRemovalPlan $Root $RemovePaths $Owned $Plan
    $changes = [Collections.Generic.List[object]]::new()
    foreach ($entry in $Plan) {
        $target = Get-InstallTarget $Root $entry.path
        $previous = if (Test-Path -LiteralPath $target) { Get-InstallHash $target } else { $null }
        if ($previous -ne $entry.sha256) {
            $changes.Add([pscustomobject]@{path=$entry.path;sha256=$entry.sha256;previousSha256=$previous;source=$entry.source;operation='write'})
        }
    }
    foreach($path in $RemovePaths){
        if(Test-Path -LiteralPath (Get-InstallTarget $Root $path)){
            $changes.Add([pscustomobject]@{path=$path;sha256=$null;previousSha256=$Owned[$path];source=$null;operation='remove'})
        }
    }
    if ($changes.Count -eq 0) {
        foreach($path in $RemovePaths){$Owned.Remove($path)}
        $Manifest.files=@($Owned.GetEnumerator()|Sort-Object Name|ForEach-Object{[pscustomobject]@{path=$_.Key;sha256=$_.Value}})
        # Also persist a successfully read legacy journal, if present.
        Write-InstallManifest $ManifestPath $Manifest
        return
    }
    $id = [Guid]::NewGuid().ToString('N')
    $stagingRelative = Get-InstallStagingRelative $id
    $pending = @($changes | ForEach-Object { [pscustomobject]@{path=$_.path;sha256=$_.sha256;previousSha256=$_.previousSha256;operation=$_.operation} })
    $published = $false; $committed = $false
    try {
        # Every old byte is saved and verified before the journal becomes live,
        # and the journal is durable before the first payload replacement.
        for ($index = 0; $index -lt $changes.Count; ++$index) {
            $entry = $changes[$index]
            if ($entry.previousSha256) {
                Write-InstallPayload $Root (Get-InstallBackupRelative $id $index) (Get-InstallTarget $Root $entry.path) $entry.previousSha256 $null
            }
        }
        Assert-InstallPlan $Root $Plan $Owned
        Assert-InstallRemovalPlan $Root $RemovePaths $Owned $Plan
        $journal = $Manifest | ConvertTo-Json -Depth 10 | ConvertFrom-Json
        $journal.pending = $pending
        $transactionVersion=if(@($changes|Where-Object operation -ceq 'remove').Count){2}else{1}
        $journal | Add-Member -NotePropertyName transaction -NotePropertyValue ([pscustomobject]@{schemaVersion=$transactionVersion;id=$id;usesRecoveryStaging=$true})
        Write-InstallManifest $ManifestPath $journal; $published = $true
        foreach ($entry in $changes) {
            if($entry.operation -ceq 'remove'){
                Assert-InstallCurrentHash $Root $entry.path $entry.previousSha256
                Remove-Item -LiteralPath (Get-InstallTarget $Root $entry.path)
            }else{Write-InstallPayload $Root $entry.path $entry.source $entry.sha256 $entry.previousSha256 $stagingRelative}
        }
        foreach ($entry in $Plan) { $Owned[$entry.path] = $entry.sha256 }
        foreach($path in $RemovePaths){$Owned.Remove($path)}
        $Manifest.files = @($Owned.GetEnumerator() | Sort-Object Name | ForEach-Object { [pscustomobject]@{path=$_.Key;sha256=$_.Value} })
        $Manifest.pending = @()
        Write-InstallManifest $ManifestPath $Manifest; $committed = $true
    } catch {
        $failure = $_
        if ($published -and -not $committed) {
            try { $null = Restore-InstallTransaction $Root $ManifestPath (Read-InstallManifest $Root $ManifestPath) }
            catch { throw "Installation failed: $($failure.Exception.Message) Recovery is incomplete: $($_.Exception.Message) Transaction journal and backups were preserved; do not launch this mixed installation." }
        } elseif (-not $published) {
            try { Remove-InstallBackups $Root $id $pending } catch { Write-Warning $_.Exception.Message }
        }
        throw $failure
    }
    try { Remove-InstallBackups $Root $id $pending }
    catch { Write-Warning "Installation committed; retained backup cleanup needs attention. $($_.Exception.Message)" }
}
