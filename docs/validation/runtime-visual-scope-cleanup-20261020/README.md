# 正式停用插件后的视觉资源清理修复

666包已正式安装，并在新游戏进程中完成两次带有真实材质覆盖的插件停用及一次中间重新启用。所选273条事件没有 Error；此前665包的 `ScopeGate.Dispose` 作用域错误未复现。该结论仅确认此清理修复，完整B05/B13仍未验收。

## 原因与修复

665包正式停用 `example.panel` 时，宿主先使插件作用域失效，再释放作用域拥有的资源。视觉 `ScopeGate.Dispose` 通过插件 dispatcher 校验线程，因作用域已失效而抛出 `ObjectDisposedException`。原失败记录保留在[665包证据](../runtime-judgement-identity-20261020/README.md)，事件955。

666将清理时的线程检查改用宿主 dispatcher。普通视觉服务访问仍经过插件作用域检查，因此清理期间不能继续使用旧服务。真实 `PluginHost` 回归覆盖父子资源、停用后旧视图拒绝、资源释放一次、预算归零及重新启用；同一回归修复前为[55/56](regression-before-fix.log)，修复后为[56/56](regression-after-fix.log)。离线预算/原生替身计数不当作实机物理释放证据。

## 构建与安装

- [完整构建摘要](build-summary.json)：25个检查项目，666/666；Visual56、Hooks38、Judgements27、UI/input52。构建仍记录1类既有CS8604警告，编译无错误。
- 原生ABI/异常展开fixture31,261项，固定Dobby发布fixture61,358项，均通过；证据在 `native-build/`。
- [安装核对](installation-check.json)：18项文件全部匹配[包清单](package-manifest.json)SHA；[源码核对](source-integrity-check.json)为242项、变化0。
- 本次游戏PID42288，诊断会话 `20261020T151803129Z-5c04823ca56c45d29847ade3937d65b8`。`launcher-last-launch.json`只说明启动进程；实际宿主运行由新会话和status验证。

## 所选实机证据

[机器核对结果](cleanup-proof.json)对应 `session-cleanup/` 的273条冻结事件。导出哈希、会话ID、连续序号、两轮对象/租约身份与顺序、正式命令结果、四份status及安装清单均由[核对脚本](inspect-cleanup.py)断言。

| 阶段 | 实际证据 |
|---|---|
| 第一轮覆盖 | MainMenuCanvas/BG的Translucent材质目标；事件188请求三个Float参数，189创建有效clone，190应用有效lease |
| 第一次停用 | 201恢复请求后lease不再active，202管理clone租约失效，203正式disable成功；status由2个scope/35个owned资源/15个service view变为0/0/0 |
| 重新启用 | 208创建新子作用域、209重新创建UGUI面板、211正式enable成功；status恢复2个scope/33个owned资源/15个service view |
| 第二轮覆盖 | 248再次请求三个Float参数，249/250创建并应用另一clone和lease；目标描述仍为MainMenuCanvas/BG，但目标键/代次不同，未据此宣称完整目标重建验收 |
| 第二次停用 | 268恢复请求后lease不再active，269管理clone租约失效，270正式disable成功；status再次为0/0/0 |

两轮请求均为 `_Brightness=0.7`、`_Vibrancy=1.1`、`_Flatten=0.1`，没有Color参数。创建日志反映适配器接受了原生Float读回；应用日志反映适配器赋值成功。三个其他已启用示例在四份status中保持Active。主代理通过Computer Use观察到停用后面板消失、重新启用后面板恢复；该视觉观察与JSON机器断言分开记录。

所选会话有0条Error、7条Warning，其中4条明确为两个材质的 `Visual cleanup pending`，根和预算继续保留；另外3条为目录未完成加载或设置页不可用时的模块等待/挂起。Warning没有被删去或算作最终原生释放。

复查命令：

```powershell
C:\Python314\python.exe docs/validation/runtime-visual-scope-cleanup-20261020/inspect-cleanup.py
```

脚本仅读取本目录冻结证据并重写 `cleanup-proof.json`，不会启动或操作游戏。运行结果为273事件、两次创建/应用/管理释放、两次disable、一次中间enable，Error0。

## 未由本次证明的范围

管理作用域、资源计数和服务视图归零，不等于Unity材质、原生根和GPU资源已物理释放。本会话没有最终物理销毁receipt或独立材质基线读回；两次清理不能替代20轮压力测试。TMP Color、动态128目标准入、双插件逐资源覆盖、音符/轨道/相机/后处理以及完整B05/B13仍缺验收。

该273事件导出停在菜单清理测试结束；后续游玩另存导出，不能把正常完成、判定来源或游戏流程结论追加为本导出已证明。旧665失败、旧644实体F8与材质证据保持原样。

## 后续949事件：新失败另存

[后续机器核对](auto-return-proof.json)对应 `session-auto-return-failure/`，同一666包、同一诊断会话，前273条与清理成功导出完全相同。该后续游玩出现两个新的缺口，不能将前273条的Error0扩展为完整会话无错误。

- 局 `6fb6c0ef2a0e417c91714c841682f48e` 在590观察到开局，639/668记录暂停/恢复，715进入AwaitingResult，720返回LevelList仍为AwaitingResult。导出内没有RunEnded或结果，不能验收NormalCompleted。
- 末尾内层NormalMode Hook计数为479次原调用/前后观察；这**不等于479次成功音符身份观察**。687记录一次有效 `auto.challenge-max` SDK样本，688是同样本内部日志；689随后在 `NativeJudgementBackend.NoteValue.ReadHoldNote → RetainExactObject → VerifyReadable` 撤回本局身份覆盖，错误为原生元数据内存不可读或跨未提交区域。日志没有将失败的确切调用源另行记出，不能把紧邻的成功 `auto.challenge-max` 样本当作失败来源。692最后一次详细状态仅为281次身份观察、8次未关联、RunIdentityAvailable=false；最终479次原调用后没有新的完整身份计数快照。该导出共7条SDK详细样本。
- 929的 `FrameUpdate` 回调在 `VisualResourceService.GetTargets → MaterialRuntimeProbe.NextTarget → Tick` 抛出 `VisualCleanupPending`，931宿主停止 `example.panel`。这是视觉服务可用性切换未被正确处理的新问题，与前述已修复的ScopeGate.Dispose错误不同。935模块进入Waiting，937恢复Active，但末尾status中插件仍是Faulted（State4、DesiredEnabled=true），管理资源为0；总计2条Error。

需要修复视觉可用性切换与Hold来源读取，再另包验证；现有949事件失败证据保持不变。复查：`C:\Python314\python.exe docs/validation/runtime-visual-scope-cleanup-20261020/inspect-auto-return.py`。脚本验证失败证据的结构、哈希、前缀与计数，不代表这些失败已修复。
