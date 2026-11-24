# V4 原生发布与同进程 CLR 联接验证

2026-10-25；普通逆向与实现验证，`flavor = null`。范围见 [scope.md](validation/foundation-engine-publication-20261025/scope.md)。

本批实现了固定 4096 字节 V4 原生发布记录、四入口首次发布、C# 只读采用和同进程多消费者联接。34 个主要实际进程场景均符合声明预期：6 个正常/托管、26 个故障、2 个动态加载拒绝。26 个故障原件通过 2035 条离线断言，18 个有源写入责任的终止场景另有父观察器完整函数体与页保护读回。真实游戏的 Doorstop→Bootstrap 和生产协调器尚未接通，生产首次写入许可关闭，新候选没有安装。对象/GC、调度取消、全消费者资源释放及 B01–B19 仍未全部验收。

## 实现与实际结果

已验证调用链：

```text
自有 EXE → 自有目标 DLL → Bootstrap → Kernel
                           ↓ V4
              同进程 CLR → 四个共享 dispatcher → 两个消费者/通道
```

Bootstrap 只处理固定 profile 的四个确切入口，先全部准备/配置/绑定，再依次写入、读回、恢复保护和刷新指令缓存。发布记录包含固定模块身份、四行目标/原函数桩/continuation/epoch、源写入与保护责任和有限事件序列。只公开四项 FirstInstall ABI；记录本体通过私有 MAP 符号由父观察器定位。Kernel 沿用既有八项 ABI，不在 CLR 接入时重新 Configure 或修改源函数。

C# 独立检查固定文件及加载模块、V4 双读、原生 Verify、完整函数体/展开信息与 Kernel tuple，再采用既有发布。四个 dispatcher 共享物理入口，每个拥有两个测试消费者；捕获后的消费者职责在解绑后仍完成并释放。SEH/C++ 异常保持真实异常传播，完成事件保留 Abnormal，异常后的 owner 保持 Unknown。真实游戏 `RequireReady()` 仍显式拒绝未证明的启动许可。

| 主要进程组 | 数量 | 实际结果及边界 |
|---|---:|---|
| 正常/托管预检 | 6 | PID 16640/42880/34752/41608/61328/52496，退出均 0；223 行 typed 调用记录 |
| 启动故障矩阵 | 26 | 8 个未写源拒绝，18 个有写入责任后实际 fail-fast；预期失败码按场景判定 |
| 动态加载拒绝 | 2 | PID 58424/44204，退出均 0；owned/production-candidate 均无配置或写入，重复加载不复活 |

六例离线复核 2549 条断言，故障离线复核 2035 条断言，均不是新增进程运行。18 个调试父观察器另计。原生 28 个 Bootstrap 变体成功，连同 Host/Target/Kernel 共 31 个原生二进制；托管 build 6 为 0 警告/错误；7 组纯模型检查通过。上批完整 25 套 1617 项测试没有在本批重跑。

## Evidence

以下路径以仓库根目录为基准；`H` 为 `docs/validation/foundation-engine-publication-20261025/`，`R4` 为 `C:/Users/71957/riz-build-cache/engine-publication-r4-20261025/`。所有 `Get-FileHash` 复现只读，不执行目标；构建/实际运行的可重复命令另列于后文。哈希固定当前观察，后续批次修改源码不能覆盖这些原件。

### E-001

- title: 原生固定发布与 V4 合同。
- observed_at: 2026-10-25。
- source_type: file。
- source_ref: `native/RizToolbox.EngineBootstrap/EngineFirstInstall.cpp`。
- content_hash: `cb15dec90d93ca59b023c6dac9d8dae746e91604182a759a74d8e1cfdb656516`。
- artifact_path: `native/RizToolbox.EngineBootstrap/EngineFirstInstall.cpp`；最终源码 ZIP 内同路径。
- repro_command: 仓库根运行 `Get-FileHash -Algorithm SHA256 -LiteralPath 'native/RizToolbox.EngineBootstrap/EngineFirstInstall.cpp'`，再只读检查 Prepare/Configure/Bind/Commit、Verify 和 Abort 分支。
- raw_excerpt: `RizEngineFirstInstall_GetInfoV4`、`RizEngineFirstInstall_VerifyV4`、`RizEngineFirstInstall_AbortAfterPublication`。
- linked_workitem: n/a。
- supersedes: 历史 r1/r2/r3 失败实现，原件在 `H/history/`，未改写。

### E-002

- title: C# 只读采用和实际托管入口。
- observed_at: 2026-10-25。
- source_type: file。
- source_ref: `src/RizToolbox.Runtime/Hooks/NativeEngineFirstInstall.cs`。
- content_hash: `d907a4cfde1e14e6d31ed0384f6c60cae7f2ec429f18b08509ce9e09a1498037`。
- artifact_path: `src/RizToolbox.Runtime/Hooks/NativeEngineFirstInstall.cs`；最终源码 ZIP 内同路径。
- repro_command: 仓库根运行 `Get-FileHash -Algorithm SHA256 -LiteralPath 'src/RizToolbox.Runtime/Hooks/NativeEngineFirstInstall.cs'`，只读检查 `RequireOwnedPublished` 与 `RequireReady`。
- raw_excerpt: `RequireOwnedPublished` 仅采用已驻留的四个确切映像；生产 `RequireReady` 拒绝未证明许可。
- linked_workitem: n/a。
- supersedes: ASLR 误拒绝前版本，保留在 `H/history/managed-aslr-r3/`。

### E-003

- title: 六个已完成进程的独立离线复核。
- observed_at: 2026-10-25；原件自带实际 PID 和时间字段。
- source_type: log。
- source_ref: `H/preflight-reviewed-facts.json`。
- content_hash: `7d7651a4c472a7e0891025a929f8ee8e0667768fd58fedbd21331cd7c2451a13`。
- artifact_path: `validation/foundation-engine-publication-20261025/preflight-reviewed-facts.json`。
- repro_command: 仓库根运行 `Get-FileHash -Algorithm SHA256 -LiteralPath 'docs/validation/foundation-engine-publication-20261025/preflight-reviewed-facts.json'`，读取其 72 条 input SHA，并与对应 `R4/preflight-*` 原始行及托管记录核对；归档器会再次检查这些输入。
- raw_excerpt: `passed=true`，`reviewAssertions=2549`，`childStartedByReviewer=false`。
- linked_workitem: n/a。
- supersedes: none。

### E-004

- title: 26 故障的父离线校验与独立源页证据。
- observed_at: 2026-10-25；真实进程与调试事件时间保存在原件。
- source_type: log。
- source_ref: `H/fault-matrix-verified-r2.json`。
- content_hash: `d70498d5fa6cdfbb0750b5dbc0f2d8d98f249090d2701fbcd83d934b95b1eab6`。
- artifact_path: `validation/foundation-engine-publication-20261025/fault-matrix-verified-r2.json`。
- repro_command: 使用后文的 `verify-fault-matrix.py --matrix ... --output ...`，仅解析已完成进程原件，不启动目标。
- raw_excerpt: `passed=true`，`checks=2035`，`failures=[]`，18 例 `independentSourceReadback=true`。
- linked_workitem: n/a。
- supersedes: r1 的 1631 条离线核验；两份均保留，不能相加。

矩阵原件 `R4/fault-matrix-r1/fault-matrix-result.json` SHA 为 `8fa08937250beb423ddca26bee1531e3d6b69a99a2abcc87c221b60cb8d87478`。父观察器 SHA 为 `f7e2ceb76759afeb4118884e666f9b5966341253b4242fee980fdafc5e481af6`，离线核验器 SHA 为 `4e991af02dea551898a35a5f02e2967ca7f09fa087c439d681a0e3f0b7dd6418`。原始磁盘/映射头、4096 字节 V4、源函数体、SHA、改变偏移及 VirtualQueryEx 段均随原件保存。源码地址由固定导出 RVA 和实际目标模块映射独立计算，不采用生产者提供的任意地址作为读取输入。

### E-005

- title: 两个固定 profile 的真实动态加载拒绝。
- observed_at: 2026-10-25。
- source_type: log。
- source_ref: `R4/dynamic-refusal-r1/dynamic-refusal-result.json`。
- content_hash: `750dcd8fe2aff12f070d151b42f9232e1b0341e9f58becbb1e194eb2bebe4025`。
- artifact_path: 最终 `raw-inputs.zip` 的 `r4-cache/dynamic-refusal-r1/dynamic-refusal-result.json`。
- repro_command: 先用 `Get-FileHash` 读取上述原件；重新运行用后文 `check-dynamic-refusal.py` 和一个全新输出目录。
- raw_excerpt: `passed=true`，`actualParentPid=54636`，两个实际 child 退出 0，`productionAdmissionGranted=false`，`targetImagesLoaded=false`。
- linked_workitem: n/a。
- supersedes: none。

### E-006

- title: 游戏、安装文件与旧包未变化。
- observed_at: 2026-10-25。
- source_type: file。
- source_ref: `H/environment-current.json`。
- content_hash: `fa36f3798d25a1edead9c8a1e2e68116af0be94ea0f16f6b27d4e2d4c3f815a5`。
- artifact_path: `validation/foundation-engine-publication-20261025/environment-current.json`。
- repro_command: 在仓库根读取该 JSON，逐项 `Get-FileHash -LiteralPath $row.path -Algorithm SHA256` 与 `expected` 比对；归档器也会核对全部 45 项。
- raw_excerpt: `allMatched=true`，`installedFiles=18`，`originalGameFiles=4`，`packageFiles=23`。
- linked_workitem: n/a。
- supersedes: none。

## Findings

### F-001

- title: 自有静态启动能够先发布四入口，再由同进程 CLR 只读采用。
- severity: n/a_re。
- category: design。
- status: validated。
- evidence_ids: [E-001, E-002, E-003]。
- location: `EngineFirstInstall.cpp`、`NativeEngineFirstInstall.RequireOwnedPublished`、`NativeEnginePublicationManagedEntry`。
- impact: 正常、双消费者、捕获中解绑和原始异常共享同一 native owner/epoch；已捕获职责不会因解绑直接丢弃。
- confidence: high；仅限六个实际自有用例。
- repro_steps: 按后文运行六种 `--mode`，核对实际 PID、行 SHA、Kernel 计数及每消费者完成/释放计数。
- remediation: 下一步接现有生产加载链与消费者，不另造一套 publisher。
- optional_attack: n/a。

### F-002

- title: 已声明启动故障保留源写入责任，动态加载不能取得首次发布许可。
- severity: n/a_re。
- category: design。
- status: validated。
- evidence_ids: [E-001, E-004, E-005]。
- location: 固定故障 stage/index、V4 Unknown、AbortAfterPublication。
- impact: 8 个未写入场景保留原调用，18 个已有写入责任的场景实际终止；动态加载和重复加载不会配置四通道或恢复许可。
- confidence: high；仅限 26 个明确 stage/index 和两个固定 profile，不声称覆盖任意 OS 故障。
- repro_steps: 重跑故障矩阵及动态拒绝，在独立输出目录校验真实状态与原件；故障离线核验不执行目标。
- remediation: Doorstop 成为普通依赖调用方后，所有后继失败返回须先合并 Bootstrap 责任，不能直接卸载仍被源代码引用的模块。
- optional_attack: n/a。

### F-003

- title: 本批成立不等于真实游戏底层完成。
- severity: n/a_re。
- category: other。
- status: validated。
- evidence_ids: [E-001, E-002, E-005, E-006]。
- location: profile2 未授权分支、生产 `RequireReady()`、[下一步审计](validation/foundation-engine-publication-20261025/next-production-admission-review.md)。
- impact: 新候选不能作为已接入游戏的发布包；B03 整项/B18a 子项之外，B01–B19 尚未全部验收。
- confidence: high。
- repro_steps: 读取生产拒绝分支与 Build/Install 当前选择，核对游戏 18 项安装文件仍为原有字节。
- remediation: 按下一步施工表接普通依赖和失败责任、固定候选部署、生产采用/协调器，再闭合真实对象/GC/调度取消与消费者退休；游戏冷启动证据等待用户恢复实测。
- optional_attack: n/a。

## Path

### P-001

- title: 固定自有入口的发布、采用和故障责任。
- path_type: callflow。
- start: 自有 EXE 的普通静态依赖初始化。
- goal: 四入口原始行为与观察职责共存，失败保留真实责任。
- steps:
  1. action: Bootstrap 校验确切映像，准备/配置/绑定四入口并完成顺序发布；evidence: E-001, E-003；finding: F-001。
  2. action: 真实 TLS/C/C++/DllMain 及工作线程执行四类原调用；evidence: E-003；finding: F-001。
  3. action: 同一 PID 启动 CLR，独立只读采用既有 V4/Kernel，再附加两个消费者/通道；evidence: E-002, E-003；finding: F-001。
  4. action: 解绑仍完成已捕获职责，SEH/C++ 传播并保留 Unknown；启动故障按写入责任拒绝或终止；evidence: E-003, E-004；finding: F-002。
  5. action: 动态加载不获许可、重复加载不复活；本批保持真实游戏未变；evidence: E-005, E-006；finding: F-003。
- residual_risks: 六个正常用例没有父独立源页快照；8 个非致命故障没有终端源页外读；正常用例 Kernel 为直接 GetInfo 的 JSON 投影而非单独原始字节；测试消费者的 Dispose 不证明 Unity GC、真实资源释放或全并发安全；不证明游戏初始化顺序和首次执行窗口。

## 复现与冻结

以下为 PowerShell 7 命令，默认仓库绝对路径。重跑会创建新的自有宿主输出；先确认各 `repro-*` 路径尚不存在，原件目录不允许覆盖。仅执行第一个块即可离线复核 26 故障，不启动目标：

```powershell
Set-Location -LiteralPath 'C:/Users/71957/Desktop/riz_toolbox'
C:/Python314/python.exe docs/validation/foundation-engine-publication-20261025/verify-fault-matrix.py --matrix 'C:/Users/71957/riz-build-cache/engine-publication-r4-20261025/fault-matrix-r1/fault-matrix-result.json' --output 'C:/Users/71957/riz-build-cache/repro-v4-fault-verified.json'
```

实际重跑使用已验证的 r4 构建，六种 mode 各一个全新输出目录。故障矩阵和动态拒绝各有独立入口：

```powershell
Set-Location -LiteralPath 'C:/Users/71957/Desktop/riz_toolbox'
$buildRoot = 'C:/Users/71957/riz-build-cache/engine-publication-r4-20261025'
foreach ($mode in @('native','normal','detach','original-seh','original-cpp','exceptions')) {
    C:/Python314/python.exe docs/validation/foundation-engine-publication-20261025/run-owned-preflight.py --build $buildRoot --mode $mode --output "C:/Users/71957/riz-build-cache/repro-v4-$mode"
    if ($LASTEXITCODE -ne 0) { throw "Owned preflight failed: $mode" }
}
C:/Python314/python.exe docs/validation/foundation-engine-publication-20261025/run-fault-matrix.py --build "$buildRoot/publication-build-manifest.json" --output 'C:/Users/71957/riz-build-cache/repro-v4-fault-matrix'
C:/Python314/python.exe docs/validation/foundation-engine-publication-20261025/check-dynamic-refusal.py --build "$buildRoot/publication-build-manifest.json" --output 'C:/Users/71957/riz-build-cache/repro-v4-dynamic-refusal'
```

从源码重新构建需要 MSVC 2019 14.29.30133/SDK 19041、.NET SDK 8、hostfxr/host headers 8.0.24、Python 3.14 的 pefile/capstone 和已验证包内 Kernel。当前模型入口为：

```powershell
Set-Location -LiteralPath 'C:/Users/71957/Desktop/riz_toolbox'
dotnet build tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj -c Release --no-restore --nologo -m:1 -p:UseSharedCompilation=false -v:minimal
dotnet tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll --engine-publication-model-checks
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File scripts/Build-EnginePublicationFixture.ps1 -OutputDirectory 'C:/Users/71957/riz-build-cache/repro-v4-native-build'
```

构建清单 `R4/publication-build-manifest.json` SHA：`650b59fec29f1c288444ea8b293139f0715a58a2828af72aa0e97d9b81b81ee9`。owned Bootstrap 为 `33afc798c86c243cb0569d72a2da9f47963e8d57d4154b817bb1c9493fb8e7be`，production-candidate 为 `691ceaee05ca4d4a954b29cef29ad22ed07e2855906f754feaf24b63381c27a7`，沿用 Kernel 为 `923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662`。DLL 内构建身份与输出时间可能影响重编译 SHA，须使用新构建自动生成的固定 profile，不把新二进制冒充本批字节。

最终归档入口 `H/archive-final.py` 只读取现有文件，核对 G 的 626 项基线、恰好 7 个旧源变化与 23 个新增，共 649 项声明源码，再生成四个独立 ZIP 并逐成员复读 SHA/EOF：`source-final-inputs.zip`、`binaries.zip`、`raw-inputs.zip`、`history-raw-inputs.zip`。这里的源码冻结是最终输入核对及原件绑定，不声称托管构建曾具有额外的构建前原子快照。报告、进度页等文档另列于 `document-manifest.json`，完整计数和哈希以 [summary.json](validation/foundation-engine-publication-20261025/summary.json) 为准。

失败历史保留：原生 DATA_DIRECTORY 拼写导致编译失败、缺 memset 链接失败、ASLR ImageBase 与静态启动 lpReserved 误拒绝、早期观察器过严保护判断及未读源页版本。它们不计入本批 34 个主要通过场景，也没有用后来的成功日志覆盖。下一步按 [生产接线施工表](validation/foundation-engine-publication-20261025/next-production-admission-review.md) 推进；完整目标继续保持 active。
