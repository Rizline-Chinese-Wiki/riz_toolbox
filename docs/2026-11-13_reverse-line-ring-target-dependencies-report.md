# Line/ring 目标计算的静态依赖补齐

本次补齐距离数值核心、整数 ease、缺失常量及 renderer 存储边界的磁盘静态依据。**尚未实现完整 target plan 或 writer，也未验收在途的 line/ring source 全矩阵。** 没有安装、启动或操作游戏。分析范围见 [scope](../work/line-ring-target-static-20261113/scope.md)，后续实现入口见 [目标投影交接](../work/line-ring-target-handoff-20261113/implementation-contract.md)。

## 已确认的数值与存储合同

| 依赖 | 静态观察 | 对实现的要求 |
|---|---|---|
| dynamic 世界 Y 窗口 | 上界 `5f`、下界 `-5f`；两端严格越过同一界才跳过 | 等边界保留，不把跳过写成空 buffer |
| 距离分档 | `<0.5f`/`<1.5f`/`<2.5f`/`<3.5f` 依次步长 20/10/5/2，否则 1 | 相等进入后一档；每条分别输出 6/11/21/51/101 个点 |
| 距离 helper `0x3e2c10` | 228 bytes，PE runtime-function 全范围；无 method 名 | 采用下述准确运算顺序，不只凭相似签名替换为通用 Distance |
| GetEaseWithIndex `0x3e75a0` | 从初始化的 EaseInfos[type] 内 Single 数组读样本；index≤0 读0，index≥100读100，否则读index | 无插值、无数值 Clamp01；仅复制原 capture 的已初始化样本，不调用 Instantiation |
| SetPositions(buffer,count) `0x3fb160` | 先保存 `_positionsPlan` 原数组引用，再保存 `_positionCountPlan`；renderer active 时再调 Unity positionCount 与 SetPositions | 数组内容、引用寿命和 count 是存储责任；Unity 调用还产生独立 effects 责任 |
| SetPositions(buffer) `0x3fb280` | 取数组 Length 为 count，随后同类存储和 renderer 路径 | 不能混淆两重载或把 buffer 前缀长度当成整个数组长度 |
| Group.Appear `0x3fb830` | 使用 `ringRendererPlan` 的 pool/child/active 和 `_startColorPlan`；包含 pool.GetItem 与 SpriteRenderer.set_color | 已确认直接 body 不写 `nowLineIndex`；池回调/Unity effects 尚未闭合，不能据此认定完整调用无旁效应 |
| Group.Disappear `0x3fb930` | active 时 ReturnToPool，再把 renderer plan.active 写 false | 不清除 selection 责任或证明完整环境已恢复 |
| Group.set_color `0x3fb990` | 保存 `_startColorPlan`，renderer active 时再调用 SpriteRenderer.set_color | 原色值读回不能关闭已进入的引擎 effects |
| Group..ctor `0x36a8c0` | 多个类共用16-byte metadata span，只跳到 Unity 基类 ctor | 本地 body 没有 index 初始化写入；不能推定运行实例/预制体的 canonical seek seed 为0 |

距离 helper 的有限、非溢出支持域内，`dx/dy/dz` 是 caller 已逐项执行 Single 减法所得的 `A-B`。令 `f` 表示单次 IEEE Single 舍入，数值核心为：

```text
yy = f(dy * dy)
xx = f(dx * dx)
zz = f(dz * dz)
sum = f(f(yy + xx) + zz)
D = f(sqrt((double)sum))
```

依据 `0x3e2c8c..0x3e2cc5`：先三个 `mulss`，随后先 Y²+X²、再加 Z²，转 Double 后 `sqrtsd`、再转回 Single。不是先 Double 平方或平方距离分档。helper 还含元数据/类初始化分支及负值后备 sqrt 调用；这里只提取已初始化、有限且平方和有限时的数值核心，不声称整 native helper 无副作用，也不开放 NaN/overflow 输入。

GetEaseWithIndex 若静态表为 null 会调用 `GetEase.Instantiation`。目标 calculator 必须使用同一次原 Canvas capture 的已初始化、完整且自有复制样本，不能用调用原方法作为“纯计算”。缺样本/type、非法来源或不支持的中间值应明确拒绝，不能凭此 body pin 创建假数据。

## 证据与复现

输入文件全部只读，采集前后 SHA-256 一致：GameAssembly.dll `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，script.json `a51cc6ee5881110fbeb884679b13d20bce4ac4df9055bf535f3cdc476bf35d10`，dump.cs `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`。IDA 服务检查因缺少 idalib-mcp 返回错误，未安装服务；实际使用现有 pefile 2024.8.26 / Capstone 5.0.6。DLL 作为字节文件解析，未进行可执行映射或游戏函数调用。

从项目根执行，第一条会重新读取指定的原始文件，第二条只复核已冻结 JSON/文本：

```powershell
C:/Python314/python.exe -B work/line-ring-target-static-20261113/collect_static.py
C:/Python314/python.exe -B work/line-ring-target-static-20261113/verify_evidence.py
```

当前限定验证通过：8 个完整范围、1636 bytes、473 条指令，5 个常量（四个新缺口及既有1.5f），与旧已冻结 SetDynamicLines 的6条相关 callsite 对应。这是静态字节/文本一致性，不是 current C# build、测试或 live ABI 成功。

边界口径：metadata 方法读取至下一不同 RVA 的完整 span，包含 padding 及所有连续 unwind fragments；匿名距离 helper 使用 exact PE runtime-function 范围。Appear 有三个连续 `.pdata` entry，首条不是整个方法。首版 collector 误只取首片，并对共享 ctor 读取过长窗口；其原 JSON、脚本和文本保存在 [r1 拒绝记录](../work/line-ring-target-static-20261113/iterations/r1-runtime-fragment/rejection.txt)，未用作完整 body 结论。修正后的 [提取记录](../work/line-ring-target-static-20261113/target-dependency-static-evidence.json) SHA-256 为 `a43ae0e74c5c1db08e204d9783057abbc225fad7f39709c1de5d34fbef7bbc1f`；原 metadata 的 Signature 仅作线索，不能独立证明 IL2CPP receiver ABI。

| Evidence | 观察 | Finding / Path |
|---|---|---|
| E-001：[架构及完整 imports/exports](../work/line-ring-target-static-20261113/triage-imports-exports.json) | AMD64 PE32+ DLL，静态导入/导出解析成功，并保存分类；存在动态解析入口，不据导入表推定能力缺失 | F-001：允许继续静态解码；P-001 起点 |
| E-002：[完整字节/指令/边界/常量](../work/line-ring-target-static-20261113/target-dependency-static-evidence.json) | 上述8范围及字面量，原文件哈希前后一致 | F-002：数值核心和存储/renderer直接边界已定位；P-001 数值与存储阶段 |
| E-003：[冻结文本复核](../work/line-ring-target-static-20261113/target-dependency-text-verification.json) | 当前 collector、记录字节、文本、旧 caller 及计数一致 | F-003：限定静态记录可复核；不替代 native/live 观察 |
| E-004：[原交接及现有输入缺口](../work/line-ring-target-handoff-20261113/implementation-contract.md) | 原 target endpoint/world/camera/UI/seed 依赖仍存在 | F-004：尚不能产完整任意时间 target 或启用 Seek；P-001 继续所需条件 |

F-002 为 `category=reverse_algo`、`status=validated`、`confidence=high` 的限定静态结论，仅涉及 E-002/E-003 的代码和数据。F-004 为 `category=other`、`status=candidate`，限制来自当前模型和仍开放的初始化/Unity 语义，不是实测崩溃结果。

P-001（callflow）：E-001 固定文件身份和架构 → E-002 的 caller 端点差 → 距离/整数 ease/已确认常量 → producer-input buffer → SetPositions 原引用/count 存储及可选 renderer 调用 → E-004 要求的显式 target basis 与 writer。最后两步尚未实现或验证完整引擎闭包；未观察 ABA、原子帧发布、池回调和 Unity resolved internal calls。

## 下一项实际实现

先完成当前 source 全矩阵，不改动其冻结输入。之后把这批8范围和常量纳入独立 target profile 的 fresh 准入，提供同一 Canvas capture 的只读 ease 数据；继续证明 Start 中 actual line/endTransform 与 Canvas slot 的初始化绑定、目标 camera/UI/world 基准、任意时间回跳的 group seed。缺这些数据不能以旧帧 pose 或 baseline 标量填充目标。

已经移除“距离 helper/整数 ease/四个 literal 尚无字节”的缺口；尚未关闭目标父链与初始化运算、seed、pool/renderer consumer、显式 writer、missing runtime、Hold/input/clock 与 engine closure。完整 B01–B19 未完成，真实 Speed/Seek 保持关闭。
