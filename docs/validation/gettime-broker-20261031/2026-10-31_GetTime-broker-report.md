# GetTime broker 挂接与原调用权威报告

2026-10-31，`flavor=null`。本批增加实际 Dobby 安装的 broker 私有 GetTime lease，避免调用方通过构造 DTO、原函数指针或验证 Action 声明挂接有效。最终 r7 在自有映像上实际通过 **29 进程 / 427 检查**：14 进程实际 Prepare/Commit、14 前置拒绝、1 进程验证精确内存覆盖区间。5 对托管 PE/PDB、22 个实际编译源码文档、2 对原生 PE/PDB 与实际 COFF 源校验一致；304 条输入/输出证据已冻结。旧 r5 首次封存缺口、r6 相邻 region 误拒的真实反例均保留，已以新矩阵覆盖修复。生产准入继续 false，真实 Speed/Seek 继续关闭。

授权与禁止范围见 [scope](scope.md)，执行顺序见 [timeline](timeline.md)。本任务只执行自有 `OwnedPlayback.dll`，未加载/调用真实 GameAssembly、未安装/启动游戏、未操作 UI 或任何账号。根任务的固定映像 profile 联合验证单列，本报告不替代该结果。

## 实际合同

`ExactHookBroker.BindPlaybackGetTime` 只接受具体 `DobbyDetourEngine` 和 `InstanceSingleReturn0`。它在现有全局地址冲突域保留 owner，成功、失败及撤回的地址均不回收。`NativePlaybackGetTimeHookLease` 的构造私有，签发时要求 exact owner 已在 broker 私有 registry；调用方创建相等标量或另建 owner 不能取得当前 receipt。通用 Bind 明确拒绝这个返回值 ABI。

安装先锁定实际文件及 SEC_IMAGE 身份，核实际 MethodInfo2022 的入口、class、GetTime 名称、零参数、非 static/generic/inflated、非 byref Single；上层仍须由 NativeRuntime API 做真实 metadata 交叉核验。body 哈希必须与指定 prehook pin 相同。映像所有可执行 virtual bytes 在首次封存前按固定文件 raw bytes、零填充和精确 AMD64 DIR64 relocation 核对。只允许 Windows 的具体 ASLR ImageBase 头字段归一化；没有屏蔽 IAT、任意字节差异或既有游戏 hooks 的兼容分支。

随后实际调用 Dobby Prepare；source 在 Prepare 前后完全不变。仅支持固定前缀 `48 89 5c 24 08 57`（MOV [RSP+8],RBX；PUSH RDI），要求实际 Dobby 原跳板为这六字节加精确 FF25 absolute continuation 到 `target+6`，共 20 字节；未知搬移不通过扫描猜测。实际安装在本矩阵是 6 字节 FF25 RIP-relative slot，slot 必须持有同一个 helper entry；source 的其余 634 字节与原文完全相同。Dobby Commit 后恢复原页保护并刷指令缓存，完整 source、slot/relay、映像、metadata、原跳板和 helper 均保留精确 seal。

真实 Dobby FF25 continuation 在尚有 PUSH RDI 的 PC6 状态会被 OS 当作 epilogue；早期 r3 已实际拒绝这个展开结果。本批保留 raw Dobby 原跳板及其证明，另外生成同样六字节 prologue 加 `MOV R11,target+6; JMP R11` 的 19 字节 RX 原调用 thunk。它只改变 ABI volatile R11。私有只读 runtime function/xdata 注册后，在每次当前性检查先核表字节和页身份，再核实际 OS lookup，并于 PC0/5/6/16 用 RtlVirtualUnwind 检查 RIP/RSP/RBX/RDI、其余非易失整数及 XMM6–15。该控制上下文展开不冒充实际在 thunk 指令位置发生的异常。

bridge 的原调用使用这个 broker thunk。独立 native fixture 实际进入原函数、在途阻塞、再抛出 SEH，异常跨 source/helper 到独立 native caller，原异常 code 保持、原调用恰一次、在途归零且权威撤回。原始 Dobby 跳板、执行 thunk、动态 unwind、helper、Dobby、source 尾和 patch 的篡改各有独立进程。恢复字节不会复活 faulted lease。

页身份按实际要求的覆盖区间封存，不把相邻 heap region 的边界当作目标身份。r6 的真实反例中，覆盖 88 字节的 allocation/protection/state/type 均相同，只因邻接第三页改变保护而使其 VirtualQuery RegionSize 从12288变8192，旧版本错误拒绝。r7 对每个覆盖区间遍历当前全部查询子区，要求 AllocationBase、AllocationProtect、State、Protect、Type 每项保持相同，且查询必须包含目标地址并向前推进。邻接拆分/合并、真实 AllocHGlobal 及64轮 heap/StringBuilder grow/free 均通过；覆盖区间跨页时第二页保护改变仍实际拒绝。

## 消费与撤回

`VerifyCurrent(module,address,methodInfo)` 由 owner 重新核物理安装，而不是调用者回传 bool。`MatchesOriginalBody(sha,640)` 只确认 owner 保存的完整原文 pin；错 hash、长度、module、MethodInfo、重复安装及 fake engine 均拒绝。根任务的 profile 仍须校验四个 producer/converter 和固定 Progress consumer；不能以这个 receipt 放弃它们。

实际联合链是 broker lease → `NativePlaybackClockPublication` → `NativePlaybackClock` → 原生 Single-return helper。fixture 的暂停模型 media=6、rate=2，经实际原调用之后返回 6；stock 路径返回 1。精确 publication object 是私有 cleanup ticket，相等 `record with { }` 不拥有它。原调用的 null MethodInfo 也按原值传递；本包没有声明固定 Progress null-caller 正向执行已完成。

Dispose 逻辑撤回，native patch、delegate、映像、thunk、unwind 与在途责任保留进程寿命，不冒充物理卸载/释放。实际阻塞原调用时 `ReadActivity` 为 Known/1；撤回后仍为 Known/1，返回前最终 gate 保留 stock bits，完成后 Known/0。实体篡改或原异常导致 sticky Unknown，不能把它当作可释放证明。准备期间 publish 被 native busy CAS 拒绝，原 revision 保持。

## Evidence → Finding → Path

全部文件的 source_ref、artifact_path、SHA256 与来源分类在 [evidence-index.json](evidence-index.json)。关键原件位于 `D:/riz-build-cache-next/gettime-broker-freeze-r2-20261031/`。

| E-id | source_ref / repro_command | content_hash |
|---|---|---|
| E01 | `evidence/checks-r3/test-result.json`；下面 Test-PlaybackBroker 命令可用新目录重现；29 个原始 log 与各实际 exit 原件一同保留 | `0ea79ab4ae3abb2a72b494cad7ab235f18d10f64bc17697b2b26746b42ef9acb` |
| E02 | `evidence/managed-build.log` / `.exit.txt`；实际 r7 Release 编译命令及 0 warning/error、exit 0；完整命令见复现 | 逐件见 index |
| E03 | `managed-symbol-source-proof.json`；portable PDB 读 actual document SHA256，PE CodeView GUID/age/stamp 与 actual PDB 对应 | `acb6bce1af51139bc430a75fb89b53cdd527bd464409254833cbafae42c61561` |
| E04 | `native-symbol-source-proof.json`；实际原生 PE/PDB GUID/age、CPP/ASM COFF C13 源校验、fixture target getter/RF/body/imports 静态复核 | `c1eb310a0160ed4654e5d37314e0ec856c6e14875db7e09a123c4134ab172621` |
| E05 | `evidence/r5-counterexample/result.jsonl` 及 `.csproj`/Program/actual r5 binary；Dobby 映像 padding RVA0x3870 CC→CB、盘不变、未执行该字节但成功签出 lease | 逐件见 index；结果 reproduced=true，不是修复绿灯 |
| E06 | `freeze-manifest.json`、`freeze-integrity-proof.json`；独立 ValidateFreeze 脚本核全部 304 条、当前 22 源、29 exits/427 checks | manifest `64df05ee912914321eb6c3240a1bb81d5a1bb04ceff1a225142828a838ec865a` |
| E07 | `evidence/checks-r1-historical/`、`checks-r2-historical/`；旧24/386与28/414分别保留，不用作当前 r7 验收 | 逐件见 index |
| E08 | `evidence/r6-region-counterexample/result.jsonl`、`root-mapped-r5-failure/`；旧r6实际拒绝仅邻接region边界变化、covered属性不变的88B，先保留反例再修复 | result `9900161ccec8dff471a8a319ee540a05c4105060f7601f3c5b91807587299539` |

| F-id | severity / confidence / status | location / evidence_ids |
|---|---|---|
| F01：当前 detour 与原调用权威只能经实际 broker owner 签发；完整 source 尾、映像、metadata、实际 Dobby 与执行 thunk 再验 | n/a_re / high / validated-controlled | `ExactHookBroker.Playback*.cs`、`NativePlaybackGetTimeHookLease.cs`；E01/E03/E04/E06 |
| F02：Dobby raw FF25 不适于此 PUSH 状态的 OS 展开；等价 R11 thunk 和私有 unwind 已逐边界验证，实际原异常仍传播 | n/a_re / high / validated-controlled | `NativePlaybackTrampolineUnwind.cs`、`Playback.Install.cs`、owned ASM/native caller；E01/E03/E04 |
| F03：r5 首次快照能收进预先改过的可执行字节；r6 按文件及精确 reloc 校验在签发前拒绝 | n/a_re / high / validated-controlled | `NativePlaybackExecutableSeal.File.cs`；E01 四类 precapture modes/E03/E05/E07 |
| F04：实际在途撤回与私有 publication cleanup 保持；Unknown 不成为可回收证明 | n/a_re / high / validated-controlled | `Playback.Live.cs`、`NativePlaybackClockPublication`；E01/E03 |
| F05：本批不能授予生产 Speed/Seek，也不能证明真实连续写者、DSP schedule 或游戏图权威 | n/a_re / high / scope-limit | [scope](scope.md)；E01/E06 及 ProductionAdmission=false |
| F06：相邻 region 拆分/合并不再误撤回；要求覆盖内每个子区的属性变化仍拒绝 | n/a_re / high / validated-controlled | `NativePlaybackExecutableSeal.MemoryFact`、`MemoryRanges.cs`；E01 memory-ranges/E03/E08 |

P01（`path_type=callflow`）：pinned file/SEC_IMAGE → 精确 executable+DIR64+metadata/body → broker registry 保留 → helper 预验 → actual Dobby Prepare → actual relocation 语义核 → 私有 RX thunk/readonly unwind → actual Commit/保护恢复 → source 实际 span 与尾再验 → 私有 lease（E01/E03/E04，F01/F02/F03）。

P02（`path_type=callflow`）：NativePlaybackClockPublication 的 immutable snapshot → lease/native CAS → GetTime actual entry → broker verify → broker 原调用 thunk → 自有 source 原函数恰一次 → native policy/sample → 最终 withdrawal gate → candidate 或 stock raw bits；并发撤回保留在途计数至完成（E01/E03，F04）。

P03（`path_type=callflow`）：r5 映像先改 padding/盘不变 → 首次 snapshot 合法化 → leaseIssued=true（E05，F03 原反例）；r6 同位置改动 → disk+DIR64 字节不符 → 签发前拒绝，helper/source-tail/consumer 同类覆盖（E01，F03 修复）。

P04（`path_type=callflow`）：r6封存88B → 邻接页合法保护变化 → VirtualQuery整段边界变化但covered属性不变 → 旧版误拒（E08）；r7仅在要求覆盖区间逐子区再验 → 邻接变化通过 → 真正覆盖第二页保护变化拒绝（E01，F06）。

## 复现与产物

固定工具：.NET SDK 8.0.418、MSVC 2019 BuildTools/Windows SDK 10.0.19041.0、Python 3.14/pefile；native fixture 用 `/O2 /W4 /WX /EHa /Zi /guard:cf`。fixture 的导入为 KERNEL32、VCRUNTIME140 与 CRT runtime API set；完整 imports/disasm/unwind 原件与符号 proof 中保留。没有安装 IDA 或操作桌面工具。原 helper 沿用 SHA256 `90b6133cf6ecd98dd00cd34472d81cd91493419ea4cb06b0aa59ffb0f7f0b355`，本批未重编/重跑其旧 19/159。

当前 r7 Runtime SHA256 为 `8eb1f45b44d52c6fb63f0a62980e77642970b998752917aa7961f6b71d2c5aa2`，owned native DLL SHA256 为 `18b702f87c5be44ab3a11f5e809b921cd56cde3d7545003bb2175579f2dfd011`。这是专项实际产物，22 个相关源经 portable PDB 证明；不是整个 live 树的源冻结。根任务后续全树/映像整合使用新候选与独立数字。

以下仅执行冻结的自有 fixture；输出目录必须新建且不存在：

```powershell
$repo = 'C:/Users/71957/Desktop/riz_toolbox'
$f = 'D:/riz-build-cache-next/gettime-broker-freeze-r2-20261031'
& "$f/sources/scripts/Test-PlaybackBroker.ps1" `
  -ManagedAssembly "$f/artifacts/managed/RizToolbox.Gameplay.Tests.dll" `
  -FixturePath "$f/artifacts/native/OwnedPlayback.dll" `
  -HelperPath "$f/artifacts/helper/RizToolbox.SingleReturn.Prototype.dll" `
  -DobbyPath "$f/artifacts/dobby/dobby.dll" `
  -OutputDirectory 'D:/riz-build-cache-next/gettime-broker-independent-rfresh'

# 新构建使用相同当前仓库；执行前先检查 frozen source hashes。
dotnet build "$repo/tests/RizToolbox.PlaybackBroker.Tests/RizToolbox.PlaybackBroker.Tests.csproj" `
  -c Release --artifacts-path 'D:/riz-build-cache-next/gettime-broker-independent-build-rfresh'
& "$repo/scripts/Build-PlaybackBrokerFixture.ps1" `
  -OutputDirectory 'D:/riz-build-cache-next/gettime-broker-independent-native-rfresh'
```

Build 脚本以仓库位置解析 native source；上述命令使用仓库脚本，其字节已在 freeze 单列。执行已冻产物的 Test 脚本不依赖 repo 根。符号与 source proof 工具位于 freeze `verification/`；`Freeze.ps1`/`ValidateFreeze.py` 拒绝覆盖已有 manifest/proof，复核应另用新目录或只读核其字段，不能抹去历史原件。

## 保留边界

本批验证的是实际 broker 安装/原调用、精确同形 prologue、自有 clock publication 和 fail-closed 责任。fixture 的 run/source/roots 与 DSP 都受控，未成为生产 source authority。默认正式 producer、完整 Pause/Replay/Resume occurrence、连续 pitch writer/ABA、实际 DSP schedule、失败音频恢复、可闻同步和任意 Seek 重建仍需独立完成。没有复跑旧 27/2436 或把它们与 427 相加；完整 B01–B19 未完成。
