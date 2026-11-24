# 独立重放九成员与映像联合检查

这些命令只使用本机已有输入，在新的 D 盘目录构建冻结源码并执行离线验证。需要 Windows x64、.NET 8、本项目固定 PowerShell，以及原结果内记录的完整 frozen source/native package；不得用工作树源码覆盖 frozen source，也不得只替换单个 Program.cs。

## 固定输入

| 输入 | 实际位置 | SHA256 |
|---|---|---|
| 当前 core-r4 proof | `D:/riz-build-cache-next/foundation-nine-source-core-r4-20261031/test-result.json` | `d96e595c2b8244d176b3b748f72d265212489f2a845791f4ba9f6b2a647707d0` |
| native r14 proof | `D:/riz-build-cache-next/playback-stock-native-build-r14-20261031/build-result.json` | `3eca8399ba5bf44c4677e68affefb0bc53677b2a09de5cc25c2bfc81d5218918` |
| 私有 foundation helper proof | `D:/riz-build-cache-next/foundation-private-native-r3-20261031/native-manifest.json` | `a809f75b0819d64149cc0abdc9b4e3d7f54b70b0347fe3f7cfc9191f1811fe26` |
| 原 GameAssembly 文件 | `D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll` | `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` |
| GetTime r4 helper | `D:/riz-build-cache-next/single-return-managed-native-r4-20261030/RizToolbox.SingleReturn.Prototype.dll` | `90b6133cf6ecd98dd00cd34472d81cd91493419ea4cb06b0aa59ffb0f7f0b355` |
| Dobby | `C:/Users/71957/Desktop/riz_toolbox/.tools/bepinex-789/BepInEx/core/dobby.dll` | `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded` |

## 运行命令

每次运行自动产生不同目录；脚本也会拒绝复用已有目标目录。固定 GameAssembly 只读，mapped runner 为每个模式复制独立映像，并以 `DONT_RESOLVE_DLL_REFERENCES` 映射。

```powershell
$ErrorActionPreference = 'Stop'
$env:PATH = 'C:/Users/71957/.cache/opencode/bin;' + $env:PATH
$pwsh = 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
$workspace = 'C:/Users/71957/Desktop/riz_toolbox'
$core = 'D:/riz-build-cache-next/foundation-nine-source-core-r4-20261031/test-result.json'
$native = 'D:/riz-build-cache-next/playback-stock-native-build-r14-20261031/build-result.json'
$foundation = 'D:/riz-build-cache-next/foundation-private-native-r3-20261031'
if ((Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash.ToLowerInvariant() -ne
    'd96e595c2b8244d176b3b748f72d265212489f2a845791f4ba9f6b2a647707d0') {
    throw 'The recorded current source proof differs.'
}
$reproRoot = Join-Path 'D:/riz-build-cache-next' ('playback-nine-repro-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $reproRoot
& $pwsh -NoProfile -File (Join-Path $workspace 'scripts/Build-PlaybackSourceGroupSnapshot.ps1') `
    -SourceSnapshotManifest $core -NativeBuildManifest $native `
    -OutputDirectory (Join-Path $reproRoot 'group82')
if ($LASTEXITCODE -ne 0) { throw 'The owned nine-member batch failed.' }
& $pwsh -NoProfile -File (Join-Path $workspace 'scripts/Test-PlaybackStockJointProfile.ps1') `
    -SourceSnapshotManifest $core -NativeBuildManifest $native `
    -FoundationHelperDirectory $foundation -OutputDirectory (Join-Path $reproRoot 'mapped7')
if ($LASTEXITCODE -ne 0) { throw 'The mapped joint batch failed.' }
```

## 验收和输出粒度

九成员 runner 的固定模式集为 82：3 个 pre-capture 反例、25 个正常/采纳/stream/异常/撤回模式、9 个物理成员各 6 种篡改。三种 pre-capture 模式必须报告 `actualDobbyPrepareCount=0`、`actualDobbyCommitCount=0`、`actualGroupArmed=false`、`nativeEntriesInvoked=false`；其余79模式各有实际9 Prepare、9 Commit和单 shared hub。

mapped runner 加 `FoundationHelperDirectory` 后固定为7模式：normal、pitch-tail、body、consumer、foundation-normal、foundation-outside、foundation-unwind。每模式都有实际9 Prepare/9 Commit、publication revision 1→2→3；game original、source Entered/Exited/Before/After、DllMain、对象分配及生产准入必须为0/false。

group 子进程分别保留 `.stdout.log`、`.stderr.log` 和 `.exit.txt`。当前 mapped runner 按模式保存 stdout/stderr 合流的 `.log` 与独立 `.exit.txt`；最终调度器另外保存两个 runner 的独立 stdout、stderr 和实际退出码。报告如实保留这种粒度，不把合流日志描述成独立 stderr 证据。

两条路径均保存新 source archive、source-before/source-after、完整 native 输入、实际构建日志、5 个实际 PE/portable PDB 的 CodeView/ID 和逐文档 SHA256 对照。native r14 的 `pe-pdb-source-verification.json` 另保留3PE/PDB、5COFF与源文件核验。不能只看控制台最后一行替代这些记录。
