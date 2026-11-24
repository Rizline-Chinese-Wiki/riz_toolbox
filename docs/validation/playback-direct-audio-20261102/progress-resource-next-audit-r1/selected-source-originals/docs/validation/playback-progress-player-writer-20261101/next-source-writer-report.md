# 2026-11-01 下一source writer离线材料

下一小范围增量为 `AudioSource.set_time@0x1511C00` 的实际写入观察。它只在尝试发生前撤旧Clock/source/writer/prepared票，不算Seek。现有Resume实际经过此入口，加入观察后必须预期SetTime→SetPitch→PlayScheduled三个子调用；不能把正常Resume误判为未知外写。

本包只交静态材料和实施方案，没有改产品、运行游戏或验证新产品行为。`MUGMusicPlayer.audioSource`的共享构造器与Start内联store事实另列，不能用单一setter/ctor签完整source generation。

## 范围与输入

授权与只写新D目录约束见[scope.md](scope.md)，`flavor=null`。GameAssembly SHA `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；dump SHA `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`。

初读为9 hook+5 reader的14-body基底；采样末尾主树逐文件已出现role9/set_MusicPlayer，成为10+5=15。父任务现已确认writer合入。后读12文件只保逐项hash，不称整树冻结或整树测试。本包另从SHA `299d9cb8286b5f651dc4a79e536473a3b101a16b0c20d8bda2c84bf0f2183c3e`的canonical 785 ZIP抽取12相关文件作为稳定基底，14个原pin均与固定PE匹配。

已有完整executable/file+DIR64 seal继续覆盖未单列代码。因此“入口未观察”不等于“代码未校验”。本轮未重测相机/N1/N4/Play19–21/host handoff。

## 已确认的覆盖边界

| 路径 | 真实固定指令 | 当前覆盖与边界 |
|---|---|---|
| MUG.set_Clip `3FC370` | `3FC374`读player+10；`3FC384`tail `1511A20`，R8D=0 | 已经过role6 set_clip，无需重复wrapper hook |
| AudioSource.set_clip `1511A20` | RCX/RDX保存，`1511A5F jmp rax` | 已有实际Before source fence；resolver字符串`1855218`，cache `1FB0698`在.data零填充虚拟尾，固定文件不能给出真实lower指针/body |
| MUG..ctor `36C3E0` | `36C3FB mov [rbx+10],rdi` | 49B非leaf，RF `[36C3E0,36C411)`/UW `1CE4D20`；dump有62个声明共享该RVA，非专属MUG入口 |
| Progress.Start MoveNext `404990` | `404B27 mov [rdi+10],rbx`；`404B37 mov [rsi+38],rdi` | MUG源字段与Progress player字段均内联，绕过ctor/setter入口；前面`404B01`已call已观察set_clip，本包不声称旧票实际存活反例 |
| AudioSource.set_time `1511C00` | 单声明Single1；Resume `3FC198`CALL，returnPC `3FC19D` | 原14与后读15/role1–9均无此观察入口，是下一窄增量 |

`404AA0→36C3E0`实际为Start状态机的WaitUntil构造；不能当成MUG构造调用。真正MUG分配后的source赋值内联在`404B27`。`263F60`只按地址记录，未签未经证明的语义名；它在store之后被调用，也不能提供Before-store fence。

Rooted accessor已核player/source/clip实际图，持续替换能被读回拒绝；末值相同的未观察同值/ABA不能据图末值签所有来源。直接内存/反射字段/未知engine内部写仍不完整。

## set_time精确施工方案

[next-candidate-profile.json](next-candidate-profile.json)包含完整span、RF、UW与lower resolver信息。

- set_time 80B pin SHA：`af9ed985ff99f5f8b1c58c0ca92ea565a2de6cac11381c9147dd8cccc4763b63`。
- RF `[1511C00,1511C43)`，UW `1CE57A4`，字节`011504001568020006520230`。
- 首6B `40534883EC30`与pitch搬移形状相同，仍需此目标独立pin/metadata/原跳板/continuation/unwind限定证明，不能套leaf/noRF例外。
- ABI `void(self,Single,incoming MI)`：RCX/XMM1原32位/R8，Resume调用前R8D清零。原参数包含NaN/signed-zero等都须原样交原调用。
- 下层resolver字符串`UnityEngine.AudioSource::set_time(System.Single)`位于`18551B8`，cache `1FB0688`，最终`1511C40 jmp rax`。离线不推出实际lower destination。

| 落点 | 必需行为 |
|---|---|
| NativeRuntime.PlaybackHooks、SourceGroupTargets、StockCodeProfile | 私有发行精确set_time目标/metadata/Single1 ABI/body pin；共同组新增一个物理成员，保留既有15body及full executable/DIR64/foundation校验 |
| native/CLR source role与配置 | role9之后新增SetTime角色，不复用旧号。建议归Scheduling attempt fence，Before先递增；同值/ABA及其他原始位/写后失败同样结束旧票，不靠末值相同复活 |
| SourceOwner、trampoline proof/unwind | 精确set_time名称、void返回、Single参数/实例类/MI与此目标独立前缀证明；未知形状拒绝 |
| Group实际CALLconsumer | Resume→SetTime仅接受returnPC `3FC19D`、精确self/parent/线程/深度/MI/原位参数，不能按origin或delay推导witness |
| StockOccurrenceProducer | Resume依次SetTime→SetPitch→PlayScheduled，Replay仍SetPitch→PlayScheduled；Scheduling实际增量包含SetTime，Source identity fence不冒充完整generation |
| RootedSource/Router/provider与owned fixture | 裸Runtime.set_time实际进入Before后，旧Clock/source/writer/prepared Commit/Restore在store前失效；正常Resume仍能恢复所选pitch并发行新票 |

定向验收只需受影响专项：实际Resume三子序列正向；native Before/store前same-value撤旧票；A→B→A不复活；原参数/MI/return/physicalexit与once-only原调用；写后失败与真实native故障分类；跨run旧self/worker/复制票拒绝；新增成员body/metadata/original篡改sticky失效。角色/协议/组/producer与上一writer共享，故本轮不并行实施产品patch。

共享ctor或Start入口须另立范围：ctor必须处理62共享声明的精确receiver/class/MI和foreign透明通过；不能让任意无关构造都使组Unknown。Start入口则需真正保留AL的Boolean-return0桥、状态机`<>4__this+0x20`精确归属及完整RF展开；不能用Void0或late barrier替代Before。二者都不等于所有未来字段/engine writer封口。

## Evidence → Finding → Path

| Evidence | artifact / source_ref | hash / repro |
|---|---|---|
| E-imports/E-exports | [static-evidence.json](static-evidence.json)：228 imports/386 exports、模块符号原件 | index SHA；`python analyze.py` |
| E-static | 同文件bodies/directXrefs；[bodies.asm.txt](bodies.asm.txt) | index SHA；`python analyze.py` |
| E-inline | [start-movenext-and-leaves.asm.txt](start-movenext-and-leaves.asm.txt)、candidate.inlineStores | index SHA；`python candidates.py`复核完整464B pin/两store |
| E-source | [baseline-source-manifest.json](baseline-source-manifest.json)、[baseline-fourteen-profile.json](baseline-fourteen-profile.json) | manifest逐文件SHA/ZIP SHA；`python capture-baseline.py` |
| E-accept | [static-verification-result.json](static-verification-result.json) | index SHA；`python verify-static.py` |

| Finding | severity/status | evidence_ids | confidence/location | 结论 |
|---|---|---|---|---|
| F-clip | n/a_re/candidate | E-static,E-source | high/3FC384→1511A20 | wrapper已有观察入口 |
| F-inline | n/a_re/candidate | E-static,E-inline,E-source | high/36C3FB,404B27,404B37 | 共享ctor与inline writer未完整观察；无已执行旧票存活反例 |
| F-time | n/a_re/candidate | E-static,E-source,E-accept | high/1511C00,3FC198 | 下一可实施入口；产品实现/执行未做 |
| F-lower | n/a_re/candidate | E-imports,E-static | high/1511A5F,1511C40 | resolved target/body不能由固定文件推出 |

P-next，path_type=callflow：Resume root → E-static确切`3FC198`CALL → F-time私有SetTime/Before fence → E-source producer严格三子序列 → RootedSource API读回/现provider消费。residual_risks：没有产品运行、Unity/engine lower destination、完整generation、production first-admission、Seek或effect完成证明。

## 实际验证与复现

`verify-static.py`已实际exit0，50/50静态检查：输入/12基底文件hash、14既有body pin、候选RF/UW、真实CALL与store。不是50个产品用例或runtime/native positive。工具为Python3.14、pefile2024.8.26、Capstone5.0.6。

在本目录执行`python verify-static.py`可复核；`analyze.py`/`candidates.py`可重建静态材料。`source-readback`是最后逐文件采样，不代替785 ZIP或role9最终freeze。`analyze-slow-r1/r2/r3.py`为本轮主动停止的分析脚本草稿，无终态验证结果；采用最终`analyze.py`。

Timeline：读最新checkpoint/N2与技能 → 核共享ctor及wrapper → 识别两inline store和前置set_clip → 锁set_time → 改用固定785归档基底 → 50项静态复核通过 → 按父任务停止扩查并封存报告/index。

