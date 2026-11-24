# Rizline 输入采集与资源保留：阶段报告

日期：2026-10-20。范围为用户授权的 PC Mod 底层开发与本机游戏测试，完整目标仍是 `.docs/foundation-expansion/SPEC.md` 的 B01–B19。此报告不宣布整个底层完成。原版 GameAssembly 内容固定为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，本次没有修改原版游戏二进制。报告类型为普通工程实现/逆向验证，flavor=null。

## 已实现的行为

键盘动作从原版输入更新结束后采集，按 invocation/updateCount 去重后保存普通值，再由 Host 帧按现有场景和焦点分发。注册之前或已经撤回的动作不会收到旧队列边沿，容量超限会明确清空和撤回采集。原生 UGUI 按钮路径保持独立。

视觉资源的原生赋值安全记录独立于 mutation channel：写入前保留可能赋值的对象和原 baseline，恢复失败、回滚失败或模块撤回后仍持有目标根/依赖/预算。延后重试只在当前值确为本次保留对象时恢复；外部游戏写保留。原生存活不可读时不按死亡处理，未决项不被驱逐。公共 lease 的失效与原生对象实际解除引用分开。

逐音符基础新增内部注册表与 C++/MASM 入口 helper。注册表以真实保留对象分配 run 内单调 ID，区分图引用唯一/歧义/缺失，撤回后等待最后一个在途调用结束再释放。helper 在 CLR 之前捕获真实调用者上下文；尚需解决 broker 发布窗口并接上12条游戏来源，未提供完整公开 Note 服务。

另外新增本地开发启动器，移除新游戏子进程继承的旧 Doorstop 初始化环境键。它不修改全局环境、Steam、原版二进制或持久 disable 设置，游戏账户和授权仍由正常 Steam 接口处理。

## Evidence

| ID | 不可变材料 | 证明范围 |
|---|---|---|
| E01 | [完整构建](validation/runtime-input-capture-fix-20261020/build-summary.json)、build.log；日志SHA `b2c94313d909bdb090e01753bf3fc532f6a62d48e86a9712353dd085d53b7a63` | 25项目644/644；Visual55、UI/input52、Hooks27、Judgements17。保留一个既有 Game.Tests CS8604 告警，无编译错误 |
| E02 | [安装实际哈希](validation/runtime-input-capture-fix-20261020/installation-check.json)、package-manifest.json、source-integrity-check.json | 启动前17项发布文件一致；构建后226个源码/工程/脚本文件内容未变 |
| E03 | [实体F8证据](validation/runtime-input-capture-fix-20261020/physical-key-proof.json)，SHA `1edfee78ea6b6ba9d77d06c9a45c8e5593c3883e8de704a31cddcae2e30d9654`；561条所选连续事件 | 用户确认实体按键，native device1 更新时间推进；invocation147711/updateType1/updateCount147715 → Host排空一次 → example.panel回调一次，sceneGeneration3 |
| E04 | [前469条材质/输入准备证据](validation/runtime-input-capture-fix-20261020/runtime-proof.json)，SHA `e101e788b339a475a8bbd7a01aab6ece9d124c3534e4cf7cc9e6ae9b9c77aa15` | 两次Float clone/assignment读回成功，manual与scene-change lease释放和探针归零，延迟销毁保留根/预算告警；此时尚无F8回调，后续导出保留该前缀不变 |
| E05 | [首个644包失败](validation/runtime-input-retirement-20261020/README.md)、只读steam-launch-environment.json、launcher-self-test.json、launcher-last-launch.json | 已裁剪版本接口导致输入初始化失败；Steam子进程继承disable/initialized；8项启动准备检查及后续真实宿主session恢复 |
| E06 | [原生helper验收](validation/judge-context-20261020/validation.md)、native-proof.json，SHA `3368427b97935fef56ca08927bfc3d735aa784dd85f7b2a57241bd588bbf4028` | 15类真实原生fixture、31261检查、269 original、268 Enter/Exit、最后inFlight0；包括SEH/C++异常/重入/并发与unwind，未集成游戏 |

## Finding → Path

F-input（validated，有限实机）：E01/E02/E03 → 精确 enum32 ABI 和 after-original 采集在本构建执行，真实物理 F8 完成一次端到端派发 → 可以继续验收重绑、重复、手动更新和双Mod焦点。工具按键未进入原生设备的旧观察保留，不伪造该工具动作成功。

F-resource（validated，生产类离线；实机部分）：E01 的恢复失败与回滚失败 seam 回归、E04 的原生延迟销毁路径 → 保留逻辑 lease 失效后仍需安全持有的原生资源 → 后续增加最终物理释放 receipt、完整故障/双Mod/压力矩阵。探针计数不是 GPU 计数，告警消失也不能单独证明物理释放。

F-note（内部实现）：E01/E06 → 已具备保留身份和捕获原调用者的独立基础 → 仍须唯一 broker 安全发布、验证12个返回RVA及实际对象关系，再以内层真实判定为准生成语义事件。外层 no-op 不可伪造判定，未知来源只转发原函数。

F-launch（validated，本机）：E05 的实际环境键和新宿主session → 子进程环境标准化解决该机的启动差异 → 未覆盖新操作系统或全部 Steam 启动路径；独立启动器需要 .NET8，不属于17文件游戏安装清单。

## 复核与边界

在桌面仓库根目录运行 `scripts/Build.ps1` 可重复完整离线构建。两份冻结导出分别由以下命令复核；它们不启动游戏：

```powershell
C:/Python314/python.exe docs/validation/runtime-input-capture-fix-20261020/inspect-runtime.py
C:/Python314/python.exe docs/validation/runtime-input-capture-fix-20261020/inspect-physical-key.py
```

本轮C盘耗尽后，自动批准审查拒绝删除临时构建目录，原因只返回 `blocked by policy`。改为完整迁移本仓库旧自动生成的 SDK 验证目录到 `D:/RizToolbox-build-cache/`，保留源码/正式包/实测材料，映射见 [归档索引](validation/runtime-input-capture-fix-20261020/sdk-cache-archive.jsonl)及前一目录README。

仍未验收：完整 Note 服务与时间/偏差语义、自然完成/迟到结果矩阵、TMP Color及玩法视觉目标、最终原生清理receipt、完整本地曲包链、双Mod输入/设置/依赖矩阵、四基线/20轮、最新包新系统/升级分发矩阵。B03与B18a之外的整项勾选没有增加。
