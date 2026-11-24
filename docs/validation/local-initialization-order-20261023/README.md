# LOCAL 初始化顺序的有限静态证据

**必须新增初始化隔离范围。** `InLevel.LevelControl.Start@3EA580` 在前段调用 LevelStart/getter，末段才设置 `info.loadOver=true`，随后直接调用 Steam Timeline 的 GameMode 和 Tooltip。它们位于 `Progress.Update` 之外；只保护 Progress 的终止尾部不能覆盖 LOCAL 初始化。

同场景的 info/progress 相互引用已存在于磁盘序列化数据中；getter 在写 controller.Info 后立即使用 info.progress。**不能由此断言 first Progress.Update 晚于 getter。** 可以确定：当 Update 实际观察到 `info!=null && info.loadOver==false`，它通过独立 epilogue 返回，进不到原版 LevelOver/Timeline/音量调用；入口懒元数据初始化不属于“无副作用”结论。

## 复现

在仓库根目录使用已有 Python 依赖执行，不启动游戏：

```powershell
C:\Python314\python.exe docs\validation\local-initialization-order-20261023\verify-evidence.py
```

本次输出为 `9` 个有限函数范围、`27` 个指令锚点、`3` 个序列化组件，`runtimeAcceptance=false`。需要重新提取时分别运行同目录 `inspect-initialization.py`、`inspect-scene-references.py`；它们会重写本目录派生证据。

## 可直接用于设计的边界

| 位置 | 确定事实 | 设计含义 |
|---|---|---|
| LevelControl.Start `3EA5DB` / `3EA92C` / `3EA935` | this 保存在 r12；实参来自 `[this+20]`；经 `GameManager.inLevelControl.LevelStart` delegate 调用 | 可以先为精确 LevelControl 建未归属的栈帧；不能在此仅凭“当前有 LOCAL”授予权限 |
| Getter `3BEF51` / `3BEF81` / `3BEF9B` | 写 controller.Info；读取 `info.progress(+28)`；立即改 progress.playOffset | 实际 LOCAL getter 原调用前，是核对父帧 info、独立 root 并发布 reciprocal mapping 的候选边界 |
| LevelControl.Start `3ECDFE` | getter 正常返回后，方法末段才写 `info.loadOver(+BE)=1` | 本方法普通控制流中，getter 调用支配该写点；异常边、间接被调用者不在 CFG 证明内 |
| LevelControl.Start `3ECE0E` / `3ECE30` | 直接调用 `SetTimelineGameMode` 和 `SetTimelineTooltip`；另一文本分支 `3ECED7` 合流 Tooltip | 父 Start 帧必须持续到原方法退出，并覆盖两个初始化 API；不能只扩大 Progress 的终止标记条件 |
| Progress.Update `4010AE`→`4010B5`→`401617..401630` | loadOver=false 跳到仅恢复寄存器并返回的 epilogue | 即使首 Update 更早，此条件下不会经本函数触发 LevelOver/Timeline；仍须从实际对象读值，不能靠默认值或同 GO 猜顺序 |

`SetTimelineTooltip@48A980` 的固定符号签名为 `void(string description, float timeOffsetSeconds, MethodInfo*)`。Start 调用点以 `RCX=string`、`XMM1=float`、`R8=0` 传参；wrapper `48A986` 保存 RCX，`48A990` 保存 XMM1，`48A9B3` 尾调 `ISteamTimeline.SetTimelineTooltip@44E9E0`。这是本镜像原生调用形状的证据；宿主绑定仍要逐项验证当前 metadata/ABI 和画像。

## 文件与 SHA

| 来源 | SHA-256 |
|---|---|
| GameAssembly.dll | `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` |
| dump.cs | `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb` |
| script.json | `a51cc6ee5881110fbeb884679b13d20bce4ac4df9055bf535f3cdc476bf35d10` |
| Rizline_Data/level6（73,288 字节） | `e95ea6ba4b09ade889b087fc91addc7cfa89274b367477927550759d4dd188bc` |
| globalgamemanagers.assets（214,324 字节） | `0b40ece56a0a01b0aa91d3ccfca44144177bc1507db23b0d6a9e75483112149f` |

逐范围 SHA 见 [boundaries.json](boundaries.json)，组件原始字节/独立 hash 见 [scene-references.json](scene-references.json)。[正式报告](2026-10-23_逆向-LOCAL初始化顺序-report.md) 包含 Evidence→Finding→Path、置信度与未知范围。
