#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
function Replace-One([string]$Body, [string]$Needle, [string]$Replacement, [string]$Seam) {
    if ($Body.Split(@($Needle), [StringSplitOptions]::None).Count -ne 2) {
        throw "Pinned process-state seam missing or ambiguous: $Seam"
    }
    $Body.Replace($Needle, $Replacement)
}
function Save-Source([string]$Path, [string]$Body) {
    [IO.File]::WriteAllText($Path, $Body, [Text.UTF8Encoding]::new($false))
}
$windows = Join-Path $SourceDirectory 'src/windows'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'riz_process_environment.h') -Destination $windows
$path = Join-Path $windows 'config.c'
$body = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$body = Replace-One $body '#include "../config/config.h"' "#include `"../config/config.h`"`n#include `"riz_process_environment.h`"" 'config include'
$needle = '    shutenv(disable_env);'
$replacement = @'
    shutenv(disable_env);
    /* Independent of ignore_disable_switch: aliases must not hook twice.
     * Foreign inherited markers belong to another process and do not disable us. */
    if (RizDoorstopOwnsProcessMarker(RIZ_DOORSTOP_HOOK_PROCESS)) {
        LOG("Doorstop hooks already belong to this process; skipping alias injection.");
        config.enabled = FALSE;
    }
'@
$body = Replace-One $body $needle $replacement 'private hook ownership, preserving manual switch'
Save-Source $path $body

$path = Join-Path $windows 'entrypoint.c'
$body = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$body = Replace-One $body '#include "entrypoint.h"' "#include `"entrypoint.h`"`n#include `"riz_process_environment.h`"" 'entry include'
$needle = @'
        LOG("Hooks installed, marking DOORSTOP_DISALBE = TRUE");
        setenv(TEXT("DOORSTOP_DISABLE"), TEXT("TRUE"), TRUE);
'@
$replacement = @'
        /* Never export our internal state as the user's disable switch: a
         * launcher inherits it and can otherwise disable its next game child. */
        if (!RizDoorstopMarkProcessMarker(RIZ_DOORSTOP_HOOK_PROCESS)) {
            LOG("Cannot record process-local hook ownership after IAT publication.");
            ExitProcess(ERROR_DLL_INIT_FAILED);
        }
        LOG("Hooks installed; recorded this process lifetime.");
'@
$body = Replace-One $body $needle $replacement 'hook publication ownership'
$needle = "    if (reasonForDllLoad == DLL_PROCESS_DETACH)`n        SetEnvironmentVariableW(L`"DOORSTOP_DISABLE`", NULL);`n"
$body = Replace-One $body $needle '' 'do not erase manual disable on alias detach'
Save-Source $path $body

$path = Join-Path $SourceDirectory 'src/bootstrap.c'
$body = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$body = Replace-One $body '#include "windows/riz_preinit.h"' "#include `"windows/riz_preinit.h`"`n#include `"windows/riz_process_environment.h`"" 'bootstrap include'
$needle = @'
    if (getenv(TEXT("DOORSTOP_INITIALIZED"))) {
        LOG("DOORSTOP_INITIALIZED is set! Skipping!");
        return;
    }
    setenv(TEXT("DOORSTOP_INITIALIZED"), TEXT("TRUE"), TRUE);
'@
$replacement = @'
#if _WIN32
    if (RizDoorstopOwnsProcessMarker(RIZ_DOORSTOP_CLR_PROCESS)) {
        LOG("Managed bootstrap already belongs to this process; skipping.");
        return;
    }
    if (!RizDoorstopMarkProcessMarker(RIZ_DOORSTOP_CLR_PROCESS)) {
        LOG("Cannot record process-local managed bootstrap ownership; skipping.");
        return;
    }
#else
    if (getenv(TEXT("DOORSTOP_INITIALIZED"))) {
        LOG("DOORSTOP_INITIALIZED is set! Skipping!");
        return;
    }
    setenv(TEXT("DOORSTOP_INITIALIZED"), TEXT("TRUE"), TRUE);
#endif
'@
$body = Replace-One $body $needle $replacement 'mono process state, unchanged non-Windows path'
$needle = "void il2cpp_doorstop_bootstrap() {`n"
$replacement = @'
void il2cpp_doorstop_bootstrap() {
#if _WIN32
    if (RizDoorstopOwnsProcessMarker(RIZ_DOORSTOP_CLR_PROCESS)) {
        LOG("CoreCLR bootstrap already belongs to this process; skipping.");
        return;
    }
#endif
'@
$body = Replace-One $body $needle ($replacement + "`n") 'explicit IL2CPP duplicate guard'
$needle = @'
    setenv(TEXT("DOORSTOP_INITIALIZED"), TEXT("TRUE"), TRUE);
    setenv(TEXT("DOORSTOP_INVOKE_DLL_PATH"), config.target_assembly, TRUE);
'@
$replacement = @'
#if _WIN32
    if (!RizDoorstopMarkProcessMarker(RIZ_DOORSTOP_CLR_PROCESS)) {
        LOG("Cannot record process-local CoreCLR bootstrap ownership; skipping.");
        return;
    }
#else
    setenv(TEXT("DOORSTOP_INITIALIZED"), TEXT("TRUE"), TRUE);
#endif
    setenv(TEXT("DOORSTOP_INVOKE_DLL_PATH"), config.target_assembly, TRUE);
'@
$body = Replace-One $body $needle $replacement 'CoreCLR ownership before initialize'
Save-Source $path $body

$note = @'

## 2026-11-15: Windows process ownership during launcher relays

Windows hook and managed-bootstrap deduplication uses private process identity
markers (PID plus process creation time). Inherited markers do not disable a
different process. Internal startup no longer writes DOORSTOP_DISABLE or
DOORSTOP_INITIALIZED on Windows, and alias detach never clears a user's manual
disable switch. The existing manual disable/ignore switch remains effective;
same-process hook deduplication is independent of that override. Non-Windows
bootstrap behavior is unchanged. If recording ownership after IAT publication
fails, startup terminates rather than expose unowned hooks to a second alias.
'@
[IO.File]::AppendAllText((Join-Path $SourceDirectory 'RIZLINE-MODIFICATIONS.md'), $note + "`n", [Text.UTF8Encoding]::new($false))
