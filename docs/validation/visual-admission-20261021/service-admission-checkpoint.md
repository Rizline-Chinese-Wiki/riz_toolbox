# B12 视觉服务动态准入检查点

日期：2026-10-21。本子任务修改 `VisualResourceService.cs` 和 `Visual.Tests/Program.cs`。原生窗口、数组访问和共享长度上限由并行任务维护，见[原生发现窗口说明](native-discovery-window.md)。未执行全量构建、安装或游戏操作。

## 已实现的行为

- 容量仍为每种 Visual 目标 128 个。发现遇到未绑定目标不再直接结束枚举；同轮窗口里的已有目标继续更新 metadata、alias 和内部优先级，保留现有规范键与代次。
- `VisualCandidate` 末尾新增可选内部 `int DiscoveryPriority=0`。Native 后端传入已验证槽位的优先级；普通 Sprite/Font 默认 0。
- 有空位时直接准入；容量满时每轮最多替换一个在本轮开始前已存在的绑定。优先选择更高优先级候选；同优先级先服务从未准入者，再按上次成功准入次序轮换。被替换绑定必须重新解析，旧句柄不复活。
- 保留有效覆盖持有者、`NativeDepth != 0`、retiring/finished、未确认 assignment，以及仍持有框架 owned 资源的空闲接收者。淘汰前再次读取真实值并核对持有者；观察失败不能当作可回收证明。
- 淘汰走原有通道恢复、逻辑失效、physical retirement 路径。若产生 cleanup pending，本轮不再新增绑定，根和资源继续保留；没有通过删除字典或丢弃 ticket 强行腾位。
- 最佳候选跨枚举结束时独立持根；该 root 原样转交给 Bind，避免重复 Retain。候选被更好候选取代、过期、无法准入或枚举抛错时均释放临时持有。异常不被宽泛吞掉以伪装发现成功。
- 服务端准入历史只存身份字符串和计数，不保存 native 指针对象。上限与 `NativeArrayScanCursor.MaximumArrayLength` 共用 **50000**；模块销毁时清空。容量延迟诊断按状态变化输出，不逐候选刷日志。

## 专项验证

| 阶段 | 实际结果 | 证据 |
| --- | --- | --- |
| 真实旧服务准入逻辑 + 首批新增行为回归 | **59/67，8 失败，进程退出 1** | [红日志](service-regression-before-rebuilt.log)、[强制构建日志](service-red-build.log) |
| 初始 4224 条历史实现 + 5000 身份回归 | **67/68，历史公平性失败，进程退出 1** | [历史红日志](service-history-regression-before.log)、[构建日志](service-history-red-build.log) |
| 最终共享 50000 上限版本 | **68/68，进程退出 0；构建 0 警告、0 错误** | [绿日志](service-regression-after.log)、[强制构建日志](service-green-build.log)、[最终源码 SHA256](service-final-source-hashes.json) |

首批回归验证了满容量仍刷新语义、迟到高优先级准入、同优先级每轮一次轮换及尾部最终进入、活动覆盖保持/释放后继续准入、低优先级不能淘汰高优先级、owned 游戏基线保护、淘汰读回失败保根并停止新准入、发现枚举异常的根释放，以及 setter 内重入/撤回安全性。

新增的 5000 身份回归通过真实服务依次激活/停用对象，每次只发现一个候选，随后让历史前缀与真正未准入对象竞争。4224 上限错误地将已遗忘前缀标记为“从未准入”，新目标继续被挡；50000 版本正确优先新目标。这里没有靠 sleep 或实际数小时游戏运行验证公平性。

### 增量缓存失效尝试保留

第一次尝试把旧源码快照复制回工作区后执行普通 `dotnet run`，文件时间使 MSBuild 复用了新 Adapter 程序集，得到 **不可信的 67/67**。该输出保留在 [service-regression-before.log](service-regression-before.log)，**不得当作旧版本通过或有效红阶段证据**。

随后每一有效阶段都先运行：

```powershell
dotnet build tests/RizToolbox.Visual.Tests/RizToolbox.Visual.Tests.csproj --configuration Release --no-restore --no-incremental
dotnet run --project tests/RizToolbox.Visual.Tests/RizToolbox.Visual.Tests.csproj --configuration Release --no-build
```

旧逻辑快照 [VisualResourceService.before.cs.txt](VisualResourceService.before.cs.txt) 仅额外包含兼容并行 Native 修改所需的可选优先级字段，不包含准入算法修改。切换红源码前另存新源码，结束时通过 `finally` 恢复；没有回退 Native 文件或使用 Git HEAD 替换工作区。初始快照与哈希见 [service-red-green-inputs.json](service-red-green-inputs.json)，4224 历史缺陷版本也[单独保存](VisualResourceService.before-history-expansion.cs.txt)。

## 范围与仍需验证

公平性适用于数量不超过支持上限、身份及扫描顺序稳定、持续可扫描且存在安全空闲受害者的同优先级候选。更高优先级或活动覆盖占满容量时，低优先级/等待目标可以持续延迟；保留覆盖安全性优先，不强制淘汰。任意重排、持续新增超过历史上限的对象总体以及间歇出现的目标，不在严格公平保证内；也不承诺固定秒级准入延迟。

本次 68 项为离线服务与既有视觉回归，未证明真实 TMP/Translucent 在场景中超过 128 时的完整行为，未取得最终原生物理释放、基线恢复或像素/GPU receipt。新包仍需全量整合和独立实机验证，B12/B13 不因此整项验收。
