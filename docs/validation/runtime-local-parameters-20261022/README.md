# 借用参数与自建资源实测

2026-10-22（UTC+8），[907 检查包](../foundation-local-parameters-20261022/README.md)在真实游戏验证了 **两个独立 InsideLevelParams root、同一组五个借用输入、原生 GC 后读回、借用撤回拒读及自建 Sprite/AudioClip/Texture2D 的跨帧销毁**。这不是完整 LOCAL 播放验收：空 Chart/JudgeData 只用于构造，没有创建 controller、进入关卡或调用结果/保存路径。

会话 `20261022T095912661Z-4b215e60807e49bf933b1e71078103b5` 已正常退出并冻结，**205 条连续事件：187 Info、16 Debug、2 Warning、0 Error**。两条 Warning 是设置页未打开和目录仍在加载时的模块等待状态，原样保留。

## 实际观察

| sequence / Unity frame | 阶段 | 观察 |
|---|---|---|
| 84 | constructed | 两个不同参数对象，各持 1 条新 root，五引用指向同一组 metadata/cover wrapper/music wrapper/Chart/JudgeData；原生 `System.GC.Collect()` 后读回一致 |
| 85 / 6 | release-requested | 第一个参数释放后第二个经再次原生 GC 仍可读；cover 的 canDestroy=false wrapper 撤回使第二个拒读，但 Sprite/AudioClip 仍活；随后两个参数、两 wrapper、10 条 metadata root 和 Chart/JudgeData 根按责任清理，向三件自建 Unity 对象请求 Destroy |
| 101 / 8 | verified | 后续帧确认自建 Sprite、AudioClip、Texture2D 均已失效，再释放全部观测 root；没有把 Destroy 返回当成销毁已完成 |

构造输入包括自建 2×2 Texture2D/Sprite、80 sample frames / 单声道 / 8000Hz 的静音 AudioClip，以及普通默认 Chart/JudgeData 和独立 metadata。两个 wrapper 均 `canDestroy=false`，由本探针显式负责自己创建的 Unity 对象；借用参数工厂始终不 Release wrapper 或 Destroy 输入。

原生构造后 **Speed=0、Offset=0，speedBits=0、offsetBits=0**。本次只记录原值，没有猜测或改写。0 只是原版控制器设置初始化之前的值；既有样本显示 `NormalMode.GetInsideLevelParams` 后续覆盖 speed/offset 并返回同一参数对象，见[标量消费者证据](../local-launch-20261022/parameter-scalar-consumers.md)。未来真实播放须验证完整原版设置初始化与可玩 Chart，不盲设 speed=1 或另猜 offset 单位，也不把本次空 Chart 用于启动。

## Evidence → Finding → Path

范围为用户授权的本机游戏中自建 scratch 对象，flavor=null。GameAssembly 固定 hash、构建输入及原始文件保持情况见[构建记录](../foundation-local-parameters-20261022/README.md)。

| Evidence | 原始记录 | 支持的结论 |
|---|---|---|
| E-static | [启动边界](../local-launch-20261022/README.md)、本包源码清单中的 NativeLocalParametersFactory/Probe | 精确五参 ctor 及字段语义、只借用输入的实现边界；静态设计本身不构成实机验收 |
| E-build | [907 构建摘要](../foundation-local-parameters-20261022/build-summary.json) | Runtime92、Hooks61、AudioContent143 与两个 fixture，具体未验范围见构建 README |
| E-runtime | [runtime-proof.json](runtime-proof.json)、[完整事件](20261022T095912661Z-4b215e60807e49bf933b1e71078103b5/events-000001.jsonl)、[session](20261022T095912661Z-4b215e60807e49bf933b1e71078103b5/session.json) | 创建/读回/撤回/请求销毁/后帧确认的唯一连续会话；事件 SHA-256 `ab6ea793c7f18cb1642dd0f49e14d91512cc220ddd6f671f8869de6301e01278` |
| E-integrity | [安装核对](../foundation-local-parameters-20261022/installation-check.json)、[源码核对](../foundation-local-parameters-20261022/source-integrity-check.json)、[原文件核对](../foundation-local-parameters-20261022/original-game-after.json) | 实测收尾 18 安装 hash、294 源码输入匹配，原游戏三文件不变 |
| E-marker | [marker 原状态](probe-marker-before.json)、[恢复结果](probe-marker-restoration.json) | `local-native-parameters.enabled` 原不存在，收尾恢复不存在，restored=true |

F-parameters（validated，E-static、E-build、E-runtime，confidence=high）：固定游戏版本的 InsideLevelParams 可按本次精确五输入构造，两个独立 owner 经原生 GC 后保留引用；释放其中一个不释放另一个的参数 root，借用 wrapper 失效时拒绝继续读。

F-scratch-lifecycle（validated，E-build、E-runtime，confidence=high）：canDestroy=false 的两个自建 wrapper Release 保留探针资源；探针随后显式 Destroy 自建三对象，并在 frame8 确认失效后释放观测根。此结论只覆盖本次三对象，不是 GPU/heap/长期 GC 曲线。

P-probe（callflow，E-static、E-runtime）：固定 build gate → 创建独立 metadata 与自有 scratch 对象 → 构造两个借用参数 → 原生 GC/五字段及标量读回 → 释放首个并重读第二个 → cover wrapper 撤回负测 → 参数/输入责任清理 → 请求自有对象销毁 → 后帧检查三者失效 → 观测根释放 → 正常退出与冻结 → marker 恢复。

## 复核与操作归属

在仓库根目录核验冻结会话：

```powershell
python docs/validation/runtime-local-parameters-20261022/freeze-proof.py docs/validation/runtime-local-parameters-20261022/20261022T095912661Z-4b215e60807e49bf933b1e71078103b5
```

脚本校验完整 sequence、唯一三阶段、所有声明断言、标量位模式及当前 18 安装文件 hash。后续安装不同版本会拒绝该安装核验；保留本目录原冻结记录并使用对应版本复现。

本次第一次激活窗口遇到用户输入与错误窗口 state；主代理没有对非目标窗口执行输入，重新枚举/激活目标后恢复。进入 MainMenu 是用户已进行的操作，不记作主代理点击。主代理实际完成最后菜单退出及确认，并核验无 Rizline 进程。

重新 probe 需在对应包、游戏正常退出后，显式启用 `BepInEx/RizToolbox/probes/local-native-parameters.enabled`，完成后正常退出并恢复原值。该 marker 默认不存在。

本次 `controllerCreated/gameCatalogModified/gameResultOrSavePathInvoked/canLaunch` 均 false。新增六全局回调 capture/恢复仅有 12 组 CPU 回归与源码审查，游戏未调用；LOCAL result policy 未安装。下一步是 inactive clone/prefab 发现与激活时序、globals 真实 capture/条件恢复及结果前置门接线，再连接完整 owned play bundle、自有结果/返回/新图重试。完整 B01–B19 不新增勾选。
