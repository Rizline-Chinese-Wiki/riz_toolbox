# LOCAL 借用参数与全局责任基础包

2026-10-22（UTC+8）最终包通过 **907/907、25 套**检查，已安装并完成借用参数探针实测。Runtime92、Hooks61、AudioContent143；两个原生 helper/Dobby fixture 为 31,261 / 61,358。最终[增量构建日志](build.log)无编译诊断；前置[AudioContent 定向日志](audio-targeted.log)仍含 14 类 FakeNative CS0649 警告，这些警告没有通过源码修改消除，不能由增量日志推断干净重建零警告。

## 本包新增范围

| 交付 | 已验证内容 | 边界 |
|---|---|---|
| 借用 InsideLevelParams | 五参 ctor 精确类型/接口、固定 RVA 375300、五引用字段及 Unity 活性核验；只拥有一个新参数 root。两个独立参数 owner 经游戏原生 GC 后读回及各自释放已实测 | metadata、Sprite/AudioClip wrapper、Chart、JudgeData 均由调用者继续持有；不是完整 owned play bundle |
| 参数 scratch 探针 | 自建 2×2 Texture2D/Sprite、80 sample 单声道 8kHz 静音 AudioClip、普通默认 Chart/JudgeData 与独立 metadata；wrapper 撤回负测、false Release 保留资源、三对象后帧销毁/观测根释放 | 空 Chart/JudgeData 仅用于参数构造，不是可玩谱面；无 controller、场景提交、结果或保存调用 |
| 六全局回调责任 | 新增 NativeControllerGlobalsBackend/OwnedNativeControllerGlobals；64 槽登记所有新 root、精确单播 Target 核验、逐字段条件恢复并保留后继、最后复核六字段才恢复 controllerSet；未知 free 保留责任且不重试，12 组回归 | CPU 控制 backend 与源码审查通过；本包未在游戏中调用 globals capture/恢复，没有原生消费者结束验证器 |
| 继承的会话/结果门 | 895 包已实现的固定 Start 前置门、一次结果 claim/cache、精确 revision 续持和有界会话仍在本包，Hooks61 | 本次探针没有安装 LOCAL result policy，没有真正启动/重试/结果链路 |

实测参数构造默认值为 **Speed=0、Offset=0，两个 IEEE 754 bits 均为 0**；只记录、不修改。这是原版控制器设置初始化之前的值，不能把参数构造成功当成可直接启动。既有固定样本范围表明 `NormalMode.GetInsideLevelParams` 随后会用 `SettingsControl.get_MappedSpeed` 覆盖 speed，并读取设置 offset 后按已证常量 1000 换算再写入 offset，最后返回同一个参数对象；须验证完整原版初始化路径，不盲设 speed=1 或另猜 offset 单位。见[标量消费者证据](../local-launch-20261022/parameter-scalar-consumers.md)。

## Evidence → Finding → Path

范围为本机用户授权游戏中的自有 scratch 对象与宿主底层实现，采用普通逆向记录结构，flavor=null。固定 GameAssembly SHA-256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。

| Evidence | 记录 | 支持的结论 |
|---|---|---|
| E-build | [build.log](build.log)、[build-summary.json](build-summary.json)、[native-build](native-build/) | 907/907、25 套与两个 fixture；最终日志 SHA-256 `76d953ba0432e01c4f74f179fc96debc6beb5d8e09260baa673ddb0561adfecd` |
| E-install | [package-manifest.json](package-manifest.json)、[installation-check.json](installation-check.json) | 实测收尾读取的 18 项实际安装 SHA-256 与本包匹配 |
| E-source | [source-manifest.json](source-manifest.json)、[source-integrity-check.json](source-integrity-check.json) | 本包 294 个源码输入在收尾复核时未变 |
| E-original | [before](original-game-before.json)、[after](original-game-after.json) | 原始 Rizline.exe、GameAssembly.dll、global-metadata.dat 三文件不变 |
| E-runtime | [参数实测](../runtime-local-parameters-20261022/README.md) | 205 条完整事件中的参数/资源生命周期实际观察，独立列明未验范围 |

F-build（validated，E-build、E-source）：本包编译并通过了已声明的参数准入/根责任、全局恢复算法和既有服务回归；fixture 与 CPU backend 不替代游戏内 globals 和结果隔离验收。

P-package（callflow，E-build、E-install、E-source、E-runtime）：最终构建与检查 → 冻结包/源码清单 → 安装 → 启用一次显式参数 marker → 游戏 probe → 正常菜单退出 → 冻结完整日志并核对安装/源码 → 恢复 marker。

复核当前安装和源码，在仓库根目录执行：

```powershell
python docs/validation/foundation-local-parameters-20261022/summarize-build.py --installed
```

后续实现改变源码或安装版本后，这条核对会正确拒绝与冻结包不同的状态。上一 [895 构建/smoke](../foundation-local-session-20261022/README.md)及 [846 metadata 实测](../runtime-local-metadata-20261022/README.md)原样保留，各自只证明对应包。

下一步为 inactive clone 的精确 prefab 发现与激活时序、真实 globals capture/条件恢复，以及已实现 result policy 的原生接线。[inactive-parent 设计](../local-launch-20261022/inactive-parent-design.md)有官方 Unity 资料支持，尚未在目标游戏实测。完整 B01–B19 继续进行，只勾选 B03 整项/B18a 子项；RequestPlay 仍 OperationUnsupported。
