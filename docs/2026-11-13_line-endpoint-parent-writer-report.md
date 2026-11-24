# 端点父对象与位置写入：本批离线验收

父对象与局部位置写入已接入当前主树，并在同一套新构建上通过 125 项专项检查。之前只允许端点当前父对象已匹配目标 Canvas 槽位；现在可以先调用精确的 `Transform.SetParent(target, false)`，再写入 `localPosition`，并按实际已进入的调用顺序逆向恢复所选值。

完整 SDK、B01–B19 和完整 Speed/Seek 仍未完成。本批没有安装或启动游戏。现有 881 输入的开发预览 ZIP 保持原字节，最新 892 输入源码尚未重新打包。

## 实际增加的能力

入口仍为 `NativeSeekLineEndpointCommands.Prepare`。它从原 Canvas basis 获取独立保留的 LineRing source，接管该 source 的唯一归属，继续使用同一个原生 reader 和根引用；不重新捕获另一张谱面或接管调用者的 Run、Canvas aggregate。

准备阶段读取完整的当前父级关系，核对端点记录与关系图的父边一致，并检查最终及每个中间状态的循环、缺失节点和深度。写入按安全顺序切换父对象，再设置局部位置。重复物理 Transform 只在目标完全一致时去重。原父对象为空的情况可用 `SetParent(null, false)` 恢复。

逐调用日志区分 parent/local、apply/restore、native entry/return、读回值、异常和责任对象。发生部分写入后，只对已实际进入的调用逆序恢复，恢复前再次核对完整状态；观察到第三种状态会永久阻止后续写入和逆向操作。

正式 Transform、Vector3、Single、Boolean、Void 类型和方法检查挂接同一个 reader 的 lifetime guard。每次原生元数据调用前后检查取消状态；Boolean/Void 的 class 指针命中缓存后仍重新核对 image、name、namespace 和类型声明，并检查新旧布局一致。

## 验证范围

| 项目 | 本批结果 |
| --- | --- |
| 主源码 | 892 项输入；8 个文件的小补丁，新增 1 个 Runtime 源文件 |
| 六组件 Release | 0 警告、0 错误 |
| 解决方案 | 0 错误；测试 fixture 有既有的 56 个警告 |
| 端点 writer | 31/31，包括父对象切换、部分写入异常、恢复、空父对象、别名、循环/中间深度、回调撤销、缓存类型身份变化、准备阶段父边不一致 |
| 相邻 Judgements | provider6、chart5、endpoint6、numeric13、RingX7、source13/14/17/18/19、basis1 |
| Runtime ABI | 51/51 |
| 参数错误 | 2 项均实际退出 2，stdout 为空、stderr 为精确 usage |
| 合计 | 48 个记录了自然退出的子步骤、125 项正向检查 |
| 字节核对 | 六生产 DLL/PDB pair、两测试 pair、12 个依赖文件、3 个 native fixture、源码与控制均一致 |

旧 300 项在本批只核对清单和顺序；不声称默认 403 项单进程、完整 source19、完整 basis16 或旧 300 已重新执行。

首次主控制进程中断，退出码未知，原批没有被接受。它已经记录自然退出 0 的两次构建、legacy 清单与 writer01–14，共 17 个步骤，经逐 argv、日志、源码和产物字节复核后保留。writer15 虽有 PASS 日志却没有退出记录，writer16 只有 RUN，均在续跑中重做。续跑控制进程 39544 完成另外 31 个步骤，独立保留的原 Windows 进程句柄记录实际 exit0。归档保留分段事实和原未完成结果，没有补写原控制进程的成功退出。

## 保持开放的边界

端点和关系图会多次观测，但 getter、profile 和引擎回调仍可能在最后一次读取后改变状态。journal 明确 `AtomicConditionalWrites=false`、`BeforeIsAtomicPreState=false`。它是实际操作与观测的记录，不提供 Unity 原子事务。

所有已经进入 Unity 的调用，即使所选原始值恢复成功，仍保留 `SideEffectsUnknown`、`ExternalEffectsUnclosed`、原 source 和 owner。恢复值不代表引擎全部效果已撤销；未知清理保留准确异常及责任对象，禁止重复尝试同一个未知 free。

world/camera/scene 的正式来源、canonical Ring seed、scratch/renderer、Hold engine、实际 DSP schedule 和完整时钟事务仍需闭合。下一隔离工作拟从同一 retained reader 读取写入后的实际 world position，不能将旧 `Snapshot.Transforms.Position` 当作目标世界位置；该后继尚未合主或验收。

## Evidence → Finding → Path

范围为用户授权的本地离线 SDK 底层开发与验证，flavor 为 null；不操作游戏、账号、代理或永久环境。

| Evidence | 文件 | 观察 |
| --- | --- | --- |
| E-001 | [本批摘要](validation/line-endpoint-parent-writer-20261113/validation-summary.json) | 892 输入、48 步骤、125 检查与能力边界 |
| E-002 | [续跑退出记录](validation/line-endpoint-parent-writer-20261113/run/controller-handle-exit.json) | 原 Windows 进程句柄保留至 exit0，结果 SHA 一致 |
| E-003 | [完整分段执行记录](validation/line-endpoint-parent-writer-20261113/run/result.json) | 17 个原完成步骤与 31 个续跑步骤，精确 argv、日志和产物校验值 |
| E-004 | [最终源码复核](validation/line-endpoint-parent-writer-20261113/reviews/final-review.md) | fresh 类型身份、父边配对、非法关系图的持续拒绝和未闭合边界 |
| E-005 | [归档清单](validation/line-endpoint-parent-writer-20261113/artifact-manifest.json) | 1166 个成员的逐项 SHA256，另有清单本身 |
| E-006 | [中断观察](validation/line-endpoint-parent-writer-20261113/run/interruption-observation.json) | 原控制进程退出未知、未确认项重新执行 |

F-001（validated，E-001/E-002/E-003）：真实 retained endpoint 的 parent/local 命令已接入主源码，实际后端 fake-native 回归通过。该证据不宣称真实游戏运行已验证。

F-002（validated，E-003/E-004）：类型缓存身份变化和准备阶段父边不一致均会拒绝；部分原生写入、撤销和未知清理仍保留原责任对象。

F-003（open，E-001/E-004）：Unity 原子性、全部引擎效果和完整 Seek 未闭合，不能据本批开启完整 Speed/Seek。

P-001（callflow）：原 Canvas basis → 独立 LineRing source → 端点投影 → 单一命令 owner → 当前端点与父级关系图核对 → 安全顺序 SetParent(false) → localPosition → 读回 → 已进入调用的逆序恢复。E-001/E-003 支持 F-001/F-002；F-003 始终保留。

## 独立复核与续接

归档摘要 SHA256 为 `b21930d90ff9f3970d4441d4b77b104551496af75b6c97e9725983c876e344f0`，续跑结果 SHA256 为 `18486e7e755d1e56bdbea3f5ec9145d7df1ad259e0cc97a854d74aeb34d5cf6e`。归档含当前源码、控制、日志、生产及测试字节、依赖、native fixture、独立复核和旧候选证据，可不依赖当前 D 盘工作目录检查：

```powershell
python -B docs/validation/line-endpoint-parent-writer-20261113/publisher/publish.py --check-only --archive docs/validation/line-endpoint-parent-writer-20261113
```

复现执行需要固定 .NET SDK 和引用包；原始 argv 随 E-003 保留。重做执行须使用新输出目录。续接入口为 [.docs/CURRENT-CHECKPOINT.md](../.docs/CURRENT-CHECKPOINT.md)。
