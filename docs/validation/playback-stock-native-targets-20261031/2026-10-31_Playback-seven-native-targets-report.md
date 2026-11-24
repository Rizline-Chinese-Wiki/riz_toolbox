# 七个 source 物理入口的受控 native 实现与验证

2026-10-31；`flavor=null`；[范围与授权](scope.md)。final r14 在固定既有工具链下实际通过 **14 进程/36675 检查**，每进程 8 source 入口，共 **112 Prepare/112 Commit**。新增七个 fixture 各自具备精确 stock 入口前缀、真实受控原正文、独立原跳板与 OS 展开验证；Progress 使用独立对象，其 `+0x42` 字段实际参与 CMP/JE。游戏原方法与 DllMain 均未执行，所有 native 执行只发生在本包自有 DLL/EXE 中。Replay/Resume/Progress 的 fixture RF 数为 **3/3/3**，固定游戏原件为 **5/5/4**，不签完整生产正文等价或生产准入。下一步使用这些独立事实整合 broker 的有限成员凭据，正式 occurrence/writer/provider/Clock 仍另行闭合。

## 实际实现

| 角色 | fixture 正文 | 实际搬移前缀 | actual original | 原跳板/source OS PC 数 |
|---|---:|---|---:|---:|
| SetPitch（支撑成员） | 320B | `40534883ec30` | 6+14=20B | 4/4 |
| MUG Pause | 160B | `40534883ec20` | 6+14=20B | 4/4 |
| ReplayScheduled | 560B | `40534883ec40` | 6+14=20B | 4/11 |
| ResumeScheduled | 720B | `40574883ec50` | 6+14=20B | 4/14 |
| Audio PlayScheduled / Double1 | 96B | `40534883ec30` | 6+14=20B | 4/7 |
| Audio set_clip / Ref1 | 80B | `48895c240857`，后接 `4883ec20` | 6+14=20B | 4/6 |
| Progress.Play / Bool1 | 272B | `4883ec28385142` | 7+14=21B | 4/9 |
| Audio Pause | 64B | `40534883ec20` | 6+14=20B | 4/4 |

每个 Prepare 后直接读取实际 Dobby original，核完整 prefix、`FF2500000000`、其 64 位槽地址恰好是 `target + movedLength`。独立 RX 原跳板保留同一搬移指令，再用 `MOV R11, continuation; JMP R11` 回原目标；原 Dobby 搬移与独立跳板地址分开保存。每进程原跳板 32 个、source 正文 59 个选定指令/晚保存边界均调用 `RtlLookupFunctionEntry` 与 `RtlVirtualUnwind`，核返回 RIP/RSP、8 个非易失 GPR、XMM6–15 和 flags。这里的 59 是明确列出的实际边界，不声称遍历正文每个 NOP 或每条游戏指令。

Replay 的受控原正文在 RBX/0x40 frame 中真实保存 XMM6@rsp30，随后在独立 chained RF 保存/使用 RDI@rsp50；Resume 使用 RDI/0x50 frame，在 offset0x72 才完成 RBX@rsp60 保存，下一 chained RF 还真实保存 XMM7@rsp30。两者实际调用已 hook 的 pitch reset 与 Double1 schedule leaf，sequence/parent、原 float 延迟 bits、计算后的原 double bits、incoming MI、no-op 和原异常均由 native 产生。return observer 的恢复 pitch 仍只属于测试受控行为。

Progress 对象的 Class 与 player 独立，Player 指针在 +0x10，FromBeginning 在 +0x42。完整 7B 含 `CMP byte [RCX+42],DL`，原+7 的 `JE` 直接消费搬移后的 flags：相等实际走 +100 的记录/no-op 分支；不等实际走 +31 的晚 RBX 保存分支，进入真实 AudioPause leaf、清 player origin、更新字段。六组 0/1/2/0x7f/0x80/0xff 的等值与反值（含高位垃圾实参）验证低 8 位 Bool、flags 延续和真实 nested parent，未把 player 指针冒充 Progress。

set_clip 的独立原跳板在 PC5 尚未 PUSH RDI，RBX 未被 clobber；SAVE_RBX 必须 defer 到 PC6 后才用 frame+16。r10/r11 的失败精确保留，r12 起修复这个独立元数据，未更改目标完整 UW 的 offset10 保存事实。clip/null/同值/ABA、原 MI、源 fence、写后 SEH 与 RBX/RDI 恢复均实际验证。

## 实际矩阵与原件身份

| mode | 检查 | native calls | 原异常回调 |
|---|---:|---:|---:|
| normal | 1078 | 13 | 0 |
| bits | 1300 | 21 | 0 |
| nested-aba | 998 | 10 | 0 |
| original-seh | 834 | 4 | 3 |
| inline-pause | 774 | 2 | 0 |
| dormant-withdraw | 12726 | 600 | 0 |
| foreign-thread | 751 | 1 | 0 |
| observer-fault | 748 | 1 | 0 |
| journal-overflow | 5609 | 257 | 0 |
| inflight | 754 | 1 | 0 |
| after-inflight | 754 | 1 | 0 |
| machine | 1011 | 10 | 0 |
| worker-withdraw | 6759 | 301 | 0 |
| target-specific | 2579 | 66 | 11 |

target-specific 的 11 个异常回调来自四个 A/B 各自原异常、Replay/Resume 各自原异常、两次 nested schedule 异常同时穿过父层，以及 Progress 原异常。每个目标自己的原正文调用次数恰好递增一次；journal 仍区分 Returned、Abnormal 与实际 PhysicalExit，不把回调返回冒充物理退出。机器种子包含实际经过 C/D 晚保存路径后的 8 GPR 和 10 XMM 比较；flags 只把 Windows DF 合同作为返回寄存器不变条件，CMP/JE 另用真实分支结果验证。

`evidence/r14/build/pe-pdb-source-verification.json` 包含 3 PE/PDB GUID+age、5 实际 COFF 的 C13 源 checksum、所有导入/导出、9 个正文 hash/精确前缀/排序 RF/CHAININFO。导入表仅为受控 Windows/VC 运行时依赖；完整列表保存在四个 imageSurveys（含固定 Dobby），不据其宣称游戏行为。GetTime/SetPitch 既有正文长度继续独立固定；七新正文各有 End 导出。十个零参 StockMethod 导出返回真实 MethodInfo2022（88B），供内部 resolver 自取，未提供 caller 地址表。

`.text` 内的真实 `StockExecutableDir64Anchor` 仍在所有 RF 之外，COFF `.text$mn` 为 AMD64_ADDR64(type1)，PE 为 DIR64(type10)。14 进程实际 loaded base 与磁盘 preferred base 不同，RX MEM_IMAGE anchor 值指向实际 SetPitch。3 PE/PDB、5 COFF 与全部原始 dump 既在 D 原件中保留，也复制进本包；r7–r14 与失败进程的闭合集合 hash 见索引。

## Evidence → Finding → Path

完整字段、content hash 和可运行命令见 [evidence-index](evidence-index.json)，阶段结果见 [timeline](timeline.md)。

| Evidence | source_ref / 类型 | 复核内容 |
|---|---|---|
| E-TARGET-STATIC | `target-proof.json`；file | 固定游戏七目标静态 prefix/RF 与本批 fixture 事实分列；游戏 actual Prepare 仍 0 |
| E-TARGET-PE | `evidence/r14/build/pe-pdb-source-verification.json`；file | 3 PE/PDB/5 COFF/9 body/显式排序 RF、CHAININFO、导出与 DIR64 |
| E-TARGET-ACTUAL | `evidence/r14/checks/target-specific.stdout.log`；memory/log | 8 个实际搬移及 continuation、2579 检查、66 calls、11 abnormal、32/59 OS PC、无在途遗留 |
| E-TARGET-MATRIX | `evidence/r14/checks/test-result.json`；command/log | 14 进程/36675 全 exit0、每进程 8 Prepare/Commit、source/physical-exit/withdraw 责任 |
| E-TARGET-PC5 | D 原件 `checks-r11-ab.../stage-ab.stderr.log`；log | 独立 set_clip PC5 的精确失败，r12 与 r14 通过反向约束修复 |

- **F-TARGET-PREFIX**：severity=n/a_re，status=validated，confidence=high，location=八 source fixture 入口；evidence_ids=E-TARGET-STATIC/E-TARGET-PE/E-TARGET-ACTUAL。七新目标各有独立实际搬移长度和 continuation，Progress 是完整 7B。
- **F-TARGET-ABI-UW**：severity=n/a_re，status=validated，confidence=high，location=PlaybackStockFixture.asm 与独立原跳板；evidence_ids=E-TARGET-PE/E-TARGET-ACTUAL/E-TARGET-MATRIX。受控原异常、实际机器保存、原实参/MI、选定 OS PC 和嵌套 parent 闭合，scope 不延伸到完整游戏正文。
- **F-TARGET-CMP**：severity=n/a_re，status=validated，confidence=high，location=StockProgressPlay+0/+7/+31/+100；evidence_ids=E-TARGET-PE/E-TARGET-ACTUAL。真实+42对象和 12 次分支试验约束 CMP flags 延续、equal no-op 和 nonEqual inline Pause。
- **P-TARGET-CONTROLLED**：path_type=callflow；start=固定游戏 file-only 前缀事实；goal=同前缀自有 fixture 的实际 Dobby 原调用与物理退出；steps=E-TARGET-STATIC → E-TARGET-PE → E-TARGET-ACTUAL → E-TARGET-MATRIX；residual_risks=原游戏执行、完整 stock occurrence/continuous writer/provider/Clock/跨局 handoff/生产 FirstInstall 均未完成。

## 只读复核与重新执行

只读复核不会重新运行 native 或执行游戏代码：

```powershell
& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-targets-20261031/Verify-Report.py'
```

需要重新编译或执行时必须使用从未存在的绝对输出目录。下例为新的独立目录；脚本遇到已存在目录会拒绝，不能覆盖 r14：

```powershell
$repoPath = 'C:/Users/71957/Desktop/riz_toolbox'
$pwshPath = 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
$newBuild = 'D:/riz-build-cache-next/playback-stock-native-reproduce-r1-20261031'
$newChecks = 'D:/riz-build-cache-next/playback-stock-native-reproduce-checks-r1-20261031'
& $pwshPath -NoProfile -File "$repoPath/scripts/Build-PlaybackSourcePrototype.ps1" -OutputDirectory $newBuild
& $pwshPath -NoProfile -File "$repoPath/scripts/Test-PlaybackSourcePrototype.ps1" -BuildDirectory $newBuild -DobbyPath "$repoPath/.tools/bepinex-789/BepInEx/core/dobby.dll" -OutputDirectory $newChecks
& 'C:/Python314/python.exe' "$repoPath/scripts/Verify-PlaybackSourcePrototype.py" --build $newBuild --dobby "$repoPath/.tools/bepinex-789/BepInEx/core/dobby.dll" --output "$newBuild/pe-pdb-source-verification.json"
```

本次执行使用对应 r14 目录，命令记录在冻结脚本/manifest；复现目录是明确的新输出示例，不声称已经执行。所有工具均为既有本地依赖。更广的根任务整合、9 成员 broker 与原游戏有限 proof 由各自分包证明，不与此表加总。
