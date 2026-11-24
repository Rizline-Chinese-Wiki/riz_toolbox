# 下一轮 S2：七个剩余 physical source hook 的精确入口与实施顺序

2026-10-31，`flavor=null`。本页是下次正式实现的交接，不是七个新 hook 已完成。当前私有共同组只有 **GetTime + SetPitch 两个 physical 成员**；独立 native r6 的八个 owned source wrapper 证明五种 ABI/生命周期及自身搬移，不能转写成九个真实游戏成员。此次只读固定 PE、既有 ASM/metadata 和当前实现，未更改冻结 native/src/scripts，未重新执行十三进程，未对本页七目标做 Dobby Prepare/Commit，未执行 GameAssembly 方法/DllMain。

[原 native 报告](2026-10-31_Playback-stock-native-report.md)与[scope](scope.md)继续有效。新增 [next-source-hooks-evidence](next-source-hooks-evidence.json) 保存全部七目标的前24B、body SHA、所有RF与原始UNWIND bytes、输入hash和明确的 `actualDobbyPrepareCount=0`。这是 file-only 定向解析，未增加新的函数反汇编。新增 [artifact-index-r2](artifact-index-r2.json)和[native-report-integrity-r2](native-report-integrity-r2.json)链接并保留旧索引与seal，不覆写旧D原件。

## 1. 固定输入与静态范围

| 实际输入 | SHA256 / 用途 |
|---|---|
| `D:/riz-build-cache-next/playback-stock-metadata-r1-20261031/GameAssembly.dll` | `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；全部字节/RF/unwind只从该固定copy读取 |
| 同目录 `evidence/bodies.json` | `5fc01d05cb843ea325ee6105cdd38e87ff9e4a9346a5de471ffbf8c97df33414`；14个既有完整body边界和hash，本页只取七项 |
| 同目录 `manifest.json` | `a638ac64742af1d711b620e0be4630d6d72c3db86e40419564957086af6458a1`；固定文件/dump/原始函数来源 |
| 同目录 `static-result.json` | `8605f35783e3f5d621c4edeeb8669335428aa0d20f6d53e577a8ae526ceffcf5`；既有定向静态提取结果 |
| `D:/riz-build-cache-next/gameplay-native-values-static-r1-20261030/evidence-r3/functions.json` | `dc5a2dfe3e7df3c01cd1e052ae4ceac96af037505e0b2de920572499e14dcc99`；既有ASM/metadata函数边界 |
| `D:/riz-build-cache-next/playback-stock-metadata-r1-20261031/evidence/AudioSource.SetClip.asm` | `4aaf4dfe3699cbb37bececea9b20f316b371f6bb86dee53b51f7f26a42f06772`；此前单独提取的set_clip正文 |

既有 `Music.Pause.asm`、`Music.ReplayScheduled.asm`、`Music.ResumeScheduled.asm`、`Progress.Play.asm`、`AudioSource.PlayScheduled.asm` 均来自上表 `evidence-r3` 目录；逐文件hash连同本次解析器、Python与pefile实际路径/hash完整列于新增JSON的 `inputs`，避免凭同名文件认来源。AudioSource.Pause在旧metadata包只做了body hash；本页只补读其既有固定copy的raw前缀与RF/unwind，没有新增全文反汇编。

## 2. 七目标的前缀与搬移候选

下表的“候选”仅按已知指令边界提出，**不是 actual Dobby original 长度**。后续每目标都必须实际Prepare后读回其完整搬移、continuation、relay/slot，再决定是否准入；未知形状保持拒绝。所有raw前24字节及body SHA见JSON。

| 物理目标 / ABI | RVA / 完整pin长度 | 精确候选前缀 | 当前已知差异与下一native fixture点 |
|---|---|---|---|
| MUG Pause / Void0 | `3fbd90` / 160B | `40 53 48 83 ec 20`，6B：PUSH RBX/ALLOC20 | 独立ALLOC20与原调用一次/SEH；不是SetPitch的ALLOC30。受控body真实调用AudioPause leaf，再清origin |
| ReplayScheduled / Single1 | `3fbe30` / 560B | `40 53 48 83 ec 40`，6B：RBX/ALLOC40 | +6即RIP-relative cmp；原RF有XMM6@rsp30及后段RDI保存/CHAININFO。fixture须有独立RBX40与真实nested reset/schedule/no-op/异常 |
| ResumeScheduled / Single1 | `3fc060` / 720B | `40 57 48 83 ec 50`，6B：RDI/ALLOC50 | +6即RIP-relative cmp；XMM6@rsp40、晚RBX@rsp60，不能套Replay。fixture保存media后真实reset/schedule，并核晚保存边界 |
| Progress.Play / Bool1 | `400700` / 272B | `48 83 ec 28 38 51 42`，7B：ALLOC28、CMP byte[RCX+42],DL | 不能切成6B。+7是JE，搬移CMP的结果flags须延续；晚保存RBX。须独立Progress布局使+42字段真实参与Bool分支，并真实进入AudioPause leaf |
| AudioSource.Pause / Void0 | `1511640` / 64B | `40 53 48 83 ec 20`，6B：RBX/ALLOC20 | +6是RIP-relative MOV；目标与MUG Pause独立，leaf原调用/MI/SEH独立验收 |
| AudioSource.PlayScheduled / Double1 | `15116d0` / 96B | `40 53 48 83 ec 30`，6B：RBX/ALLOC30 | 首6形状同SetPitch，但它有3个RF，后段保存XMM6、maxsd(0,Double)、tail icall；独立Double raw64/parent/原异常证明仍缺 |
| AudioSource.set_clip / Ref1 | `1511a20` / 80B | `48 89 5c 24 08 57`，6B；后4B=`48 83 ec 20` | MOV[RSP+8],RBX/PUSH RDI。首6同GetTime，完整ALLOC20/RBX保存rsp30与GetTime50/60不同；独立Ref/null、原MI、写后SEH与RBX/RDI恢复 |

现有 `NativePlaybackSetPitchTrampolineUnwind.cs` 只认 `40 53 / 48 83 ec 30` 后接R11 continuation，验证PC边界0/2/6/16；`NativePlaybackTrampolineUnwind.cs`只认 MOV-save/PUSH-RDI 的首6与R11 continuation，验证0/5/6/16。这些机械步骤可以在独立精确source pin和actual Dobby证据闭合后复用实现，不能据相同前几字节发行另一目标的完整展开证明。对于Progress，R11 MOV/JMP本身不改flags；任何改flags的搬移尾序列都必须拒绝或有独立保持证明。

## 3. 所有 RF 与原始 UNWIND bytes

下列均是固定PE中的真实静态记录；`flags=4`表示CHAININFO，首byte `21`为version1/CHAININFO。每项UNWIND包含按code槽4字节对齐后的必要padding及CHAININFO尾部；这不是新增动态OS unwind验收。完整UNWIND RVA、prologSize、codeSlots也在JSON中。

| 目标 / RF `[begin,end)` | UNWIND hex |
|---|---|
| Pause `[3fbd90,3fbe2d)` | `0106020006320230` |
| Replay `[3fbe30,3fbec8)` | `011504001568030006720230` |
| Replay `[3fbec8,3fbfaf)` | `2105020005740a0030be3f00c8be3f00585fce01` |
| Replay `[3fbfaf,3fbfc0)` | `2100000030be3f00c8be3f00585fce01` |
| Replay `[3fbfc0,3fc00f)` | `2100020000740a0030be3f00c8be3f00585fce01` |
| Replay `[3fc00f,3fc058)` | `2100000030be3f00c8be3f00585fce01` |
| Resume `[3fc060,3fc155)` | `0172060072340c001568040006920270` |
| Resume `[3fc155,3fc27e)` | `210502000578030060c03f0055c13f0004b1ce01` |
| Resume `[3fc27e,3fc294)` | `2100000060c03f0055c13f0004b1ce01` |
| Resume `[3fc294,3fc29a)` | `210002000078030060c03f0055c13f0004b1ce01` |
| Resume `[3fc29a,3fc32c)` | `2100000060c03f0055c13f0004b1ce01` |
| Progress `[400700,40071f)` | `0104010004420000` |
| Progress `[40071f,40079a)` | `2105020005340400000740001f074000304dce01` |
| Progress `[40079a,4007ba)` | `21000000000740001f074000304dce01` |
| Progress `[4007ba,400809)` | `2100020000340400000740001f074000304dce01` |
| AudioPause `[1511640,1511673)` | `0106020006320230` |
| PlayScheduled `[15116d0,15116e5)` | `0106020006520230` |
| PlayScheduled `[15116e5,151171c)` | `2105020005680200d0165101e5165101c850ce01` |
| PlayScheduled `[151171c,1511722)` | `21000000d0165101e5165101c850ce01` |
| set_clip `[1511a20,1511a62)` | `010a04000a3406000a320670` |

这里20个RF属于七个入口，不是20个独立hook；metadata pin保留到下一声明边界的padding，不能用RF end替换整个body pin。

## 4. A→D 的最小实现顺序

**A：PlayScheduled与两个Pause。** 先为Double1 PlayScheduled建立独立actual Prepare/Commit/readback和原调用raw64/parent/MI/SEH；再加ALLOC20的MUG Pause、AudioPause独立Void0 fixture。每目标先核exact body、自己的Dobby搬移和continuation，再注册只覆盖该原跳板的展开信息；每个实际PC边界用RtlLookupFunctionEntry/RtlVirtualUnwind核RIP/RSP/相关非易失GPR/XMM，并以真实原异常穿过entry/original。fixture必须执行自己的受控leaf，不能调用任何游戏icall。

**B：set_clip。** 新Ref1 fixture复现MOV-save/PUSH-RDI/ALLOC20和source→RDI、clip→RBX、恢复RCX/RDX的路径。相同首6允许考虑共用已经过逐目标重新核验的机械代码，full RF/body pin仍独立。验证ref/null/rawMI、同clip/ABA的Before fence、写后异常、RBX/RDI恢复；不能借GetTime类名或“都是6B”省掉证明。

**C：Replay与Resume。** 分别做RBX/ALLOC40与RDI/ALLOC50的Single1 fixture，保留对应XMM6、晚非易失保存和链式展开路径。原native实际reset pitch、schedule leaf、no-op和异常要产生自己的native sequence和parent；Replay/Resume的单一真实schedule/原Doublebits、原delaybits和incomingMI均独立。不能因两者同为Single1而共享未经验证的原跳板事实。

**D：Progress.Play。** 新建专用Progress受控对象布局，真实使用`[self+42]`与原Bool低8位比较，不能继续只把player传给相同wrapper。先验证至少7B完整指令搬移及CMP→原+7 JE的flags延续，再处理晚RBX保存/CHAININFO。nativebody应实际在已知分支调用AudioPause leaf并清origin，形成真实Bool parent；固定既有 `400779→1511640`/`400781`关系是语义依据，不推测其它inline站点。

每步只把已闭合的owner纳入broker私有有限成员表与安装epoch；保留全executable/file+准确DIR64比较，组内每项也比较实际patch/relay/original/尾部，禁止caller mask或地址列表替代私有凭据。阶段未全部commit/readback前不得arm；成员fault/withdraw立即同步撤回全部source/clock authority并保留在途责任。新增一个目标先独立fixture验收，再验证组合，最后才扩大组合范围。

## 5. Evidence → Finding → Path 与交接终点

- **E-NEXT-FILE**：上表固定PE/body/ASM原件及hash，新增JSON逐项回读完整body hash，并读取20个RF/UNWIND；source_type=file，raw参数不来自执行，repro_command见下文。
- **E-NEXT-CURRENT**：已有冻结 `NativePlaybackSetPitchTrampolineUnwind.cs`、`NativePlaybackTrampolineUnwind.cs`、`ExactHookBroker.Playback.Group.cs`、targets/resolver/profile 的限定实现；新增JSON的inputs逐项固定六个实际源码路径/hash，本页未修改这些实现。
- **F-NEXT-SHAPES**：severity=n/a_re，status=validated，confidence=high，evidence_ids=E-NEXT-FILE/E-NEXT-CURRENT；location为表中七入口及两个现有unwind类。结论是前缀/展开存在上述具体差异，当前两成员不能覆盖七项；不宣称actual Dobby或游戏执行已验证。
- **P-NEXT**：path_type=callflow；固定file/body/metadata→每目标actual Dobby读取与独立fixture/OS unwind→broker私有成员→完整file+DIR64组合→全部闭合后arm。start为本页静态事实，goal为下轮逐项可复核实现；后四步当前均是计划，residual_risks包含原游戏执行、正式provider、下层writer完整覆盖与FirstInstall，仍不签production。

本轮新增证据的解析脚本为 `Seal-Next-Source-Hooks.py`；已在本目录新建JSON和r2索引后拒绝同路径覆写。它只读固定copy与既有ASM/JSON，不分配native对象、不LoadLibrary、不调用Dobby或游戏。现有证据和旧seal的完整只读复核：

```powershell
& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-native-20261031/Verify-Report-r2.py'
```

此命令只验证原件/hash和已保存的结果，不重新执行13个native进程；旧 `Verify-Report.py` 仍可独立验证原r6报告seal。
