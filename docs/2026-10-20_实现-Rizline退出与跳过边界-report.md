# Rizline 退出与跳过的直接终止边界

本轮补齐了普通采样无法证明的两个结束路径：暂停页退出发出 `Aborted`，SDK Skip 发出 `Skipped`。两个测试插件的单局 scope 均先于普通生命周期监听失效，Skip 操作审计仍归属于原局。当前包已通过 603 项离线检查并安装核对 17 个文件；这些计数不表示 B01–B19 覆盖率。正常完成、迟到结果来源、完整输入和视觉能力仍有缺口，底层任务继续进行。

范围与授权见 [scope](validation/runtime-back-skip-20261020/scope.md)，关键过程见 [时间线](validation/runtime-back-skip-20261020/timeline.md)。报告采用普通实现/逆向结构（flavor=null），不套用恶意软件或攻击报告。

## Evidence

| ID | source_ref / artifact_path | content_hash SHA256 | 可复现命令（仓库根，使用本机既有 Python） |
|---|---|---|---|
| E-01 | [603 构建摘要](validation/runtime-back-skip-20261020/build-summary.json) | `d7c69e7a4850f66cad6bfbbf532cdee8474787cd7b503e13b5f2f727d701c75b` | `Get-FileHash -Algorithm SHA256 -LiteralPath docs/validation/runtime-back-skip-20261020/build-fixed.log`，与摘要逐项比对；完整重跑为 `./scripts/Build.ps1` |
| E-02 | [603 冻结实机证明](validation/runtime-back-skip-20261020/runtime-proof.json) | `14212119b7ee3660517997da0481593c6b27bdd66d83ac7e69faca328e807d11` | `C:/Python314/python.exe docs/validation/runtime-back-skip-20261020/inspect-runtime.py` |
| E-03 | [582 暂停/恢复/Retry 证明](validation/runtime-cancellation-20261020/runtime-proof.json) | `c2f87213913126111b77d9b8c3c3e66eef5bacfffc869d1a53d1dd2d05987ee5` | `C:/Python314/python.exe docs/validation/runtime-cancellation-20261020/inspect-runtime.py` |
| E-04 | [Back 静态原生证明](validation/lifecycle-cancellation-20261020/back-static-proof.json) | `0b9ccaa41a28f2547d130c3ff8ce0d2b0254dcaece1d982a6876e1d06a7b13fc` | `C:/Python314/python.exe docs/validation/lifecycle-cancellation-20261020/back-inspect.py`，需要已记录哈希的游戏二进制和 dump |
| E-05 | [Skip 静态原生证明](validation/lifecycle-cancellation-20261020/skip-static-proof.json) | `95e42314d3fde6610f270c3c305430b8fcb3ecca9dc1e95e085bf50940b6f078` | `C:/Python314/python.exe docs/validation/lifecycle-cancellation-20261020/skip-inspect.py`，需要已记录哈希的游戏二进制和 dump |

E-02 只读取 `session-back-skip` 的 1095 条完整事件，验证导出清单、安装清单与包清单，不读取相邻会话。其安装证明是启动前对实际文件计算哈希，不声称已从进程内存重新散列程序集。E-03 属于上一个实际安装包，保留其旧 Skip 只有 ObservationLost 的失败事实，不能与新包混作同一次运行。

## Findings

| ID | severity / category / status | evidence_ids | location | confidence / 结论 |
|---|---|---|---|---|
| F-01 | n/a_re / design / validated | E-02、E-04 | `ExactBackCancellationModule`、`BackCancellationTracker` | high：请求本身不结束单局；同对象原始 Update 跨过 `_terminated` 和 `_skip` 双边沿后才产生 Aborted，Result 保持 null |
| F-02 | n/a_re / design / validated | E-02、E-05 | `ExactSkipTerminationModule` | high：原始 Skip 的 `_skip` false→true 及同一 PauseUI enabled true→false 已真实执行，独立发 Skipped，没有制造实际结算 |
| F-03 | n/a_re / design / validated | E-01、E-02、E-03 | `VerifiedRunCancellationQueue`、`ToolboxHost.TickCore`、`RunScopeBoundary` | high：源命令先写原局审计，下一宿主 Update 在 Pump/采样前提交终止，两个 owner 的 scope 先于普通监听取消；此结论限定已执行路径 |
| F-04 | n/a_re / other / candidate | E-02 与后续材质拒绝会话 | `NativeVisualBackend.Discover` | medium：窗口与 profile 绑定成功不等于材质成功应用；实际 TMP 材质源与通用 Graphic 材质槽位需要区分，后续修复仍须独立构建/实测 |

不以孤立 `_terminated`、场景离开或控制器消失判断取消；Capture 同时拒绝实际已结束、已失败、已跳过状态，避免 250 ms 采样滞后误归类。retained 根随每次原调用释放；Back 的 pending 根最多保留一组，在 run/scene 改变或模块撤回时清理。原生补丁与 trampoline 仍为进程所有，仅撤回逻辑订阅，不承诺热卸钩。

## Path

P-01，path_type=callflow，目标是明确结束旧局并让插件清理发生在旧任务执行之前：

1. Back 或 Skip 的固定构建精确入口验证签名、RVA 和原生对象关系；保留本次所需根与当前 run token。（E-04/E-05，F-01/F-02）
2. 原调用恰好执行一次，正常返回后检查各自真实双后置；Back 先等待同一 Update，Skip 直接检查其 LevelOver 副作用。未通过则不制造终止。（E-02/E-04/E-05，F-01/F-02）
3. 只把普通 token、原因和证据排队，源 SDK 操作仍可记录原局审计；不把 IL2CPP 对象传给 Core 或插件。（E-01/E-02，F-03）
4. 下一宿主 Update 在调度队列 Pump 和观察采样前 Drain，严格拒绝过期 token；发布 RunEnded 时先使单局 scope 失效、取消，再调用普通监听。（E-02，F-03）

残余限制：尚无正常通关验收、所有状态/故障矩阵、同谱迟到结果原生 origin 与完整压力曲线。GetJudge 当前观察入口参数，不含可靠 NoteId，也不等于最终计分；长按/漏判存在特殊参数，不能统一标成秒误差。材质和输入动作的缺口继续跟踪在 [实现状态](IMPLEMENTATION-STATUS.md)，不以本报告代替完整验收。

## 后续 607 包的独立材质验证

607/607 完整离线构建包含 TMP `fontSharedMaterial` 与普通 Graphic 材质槽位区分、原生数组跨迭代持根、真实组件优先发现；新包再次正式安装并核对17文件。它与 E-02 的603包不同，不能自动继承全部玩法实测。新会话 `20261020T103706806Z-674f077f11744d8bb9ca694bbe0ee0d6` 的488条冻结事件证明：刷新选择实际 LevelList Canvas/BG 半透明组件后，两次创建克隆、写入 `_Brightness/_Vibrancy/_Flatten`，内部读回通过并完成覆盖；一次手动、一次场景变化释放覆盖与资产租约，探针保留克隆/覆盖/估算字节归零。

E-06：[607独立证明](validation/runtime-material-slots-20261020/runtime-proof.json)，SHA256 `9056a580d632e0c9eea7c99e66486758223df06311677ff3ca3484dbd62fa435`；复核命令为 `C:/Python314/python.exe docs/validation/runtime-material-slots-20261020/inspect-runtime.py`。F-05（n/a_re / design / validated / high，evidence_ids=E-06，location=`NativeVisualBackend`、`MaterialRuntimeProbe`）：本次 Float 参数与受控租约路径已实际执行；P-02（callflow）为真实组件槽位→固定资源 hash/精确 Shader 相等→克隆参数与赋值读回→手动/场景 Dispose（各步骤 E-06/F-05）。

F-04 的错槽源码修复因此有新的构建和部分成功执行证据，但 TMP Color 没有成功调用；常见中文 TMP 使用另一 Shader，而当前可信 outline profile 的候选主要为未激活 gamepad hint，不能放宽同名 Shader 来制造成功。SDK 没有独立 baseline 恢复回执或 GPU 对象计数，探针计数归零不等于所有 native 资源/像素恢复完整验收；128目标已满时的动态饥饿也仍未解决。具体静态来源见[槽位研究](validation/visual-20261020/material-live-selection-research.md)。
