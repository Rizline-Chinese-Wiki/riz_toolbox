# 本地资源包的独立生命周期审查

2026-10-22。本记录覆盖当前冻结源码与已经完成的定向回归。Runtime **115/115**、Audio/content **212/212**；整包构建和游戏实测由主代理继续执行，此处不提供 native 成功结论。

本轮补齐了音频上传失败时的责任保留，以及对象分配成功、临时 root 清理失败时的同步责任发布。CPU 输入准备保留精确旧 revision，两个 native 包各自创建资源、谱面、判定设置和参数。新的 publication 路径有独立的有界临时记录，未知 free 不重试。普通 `Invoke`、`NewExact`、`CreateSingleArray` 保持原清理契约；这里的增强结论仅适用于显式 publication 接口及已接线的 bundle 路径。

## 范围和证据

授权范围是用户要求的 PC 端兼容性底层。此次工作仅新增、审查本地资源包和相关 Runtime 责任边界；独立代理未构建、安装、启动或操作游戏。本报告采用通用技术审查结构，`flavor = null`，没有安全攻击或恶意样本定性。

[审查源文件清单](review-source-manifest.json)记录以下源码、回归文件和完成日志的 SHA256。它用于识别本次审查输入，不替代整包产物 manifest，也不证明当前安装对应这些源码。

| Evidence | 可复核输入 | 实际观察 |
|---|---|---|
| E-001 | [bundle factory](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalPlayBundleFactory.cs)、[CPU inputs](../../../src/RizToolbox.Adapter/AudioContent/LocalPlayBundleInputs.cs) | 精确 request fork、固定五个 direct root、独立 upload slot、两阶段 Destroy/确认与失败隔离 |
| E-002 | [Runtime 调用](../../../src/RizToolbox.Runtime/NativeRuntime.cs)、[SingleArray](../../../src/RizToolbox.Runtime/NativeRuntime.SingleArray.cs)、[publication ledger](../../../src/RizToolbox.Runtime/NativePublicationCall.cs) | native 输出在临时 root 清理之前通知；ctor 输出在 ctor 进入之前通知；32-call、129-root、128-Arg 上限 |
| E-003 | [probe](../../../src/RizToolbox.Adapter/AudioContent/LocalNativeBundleProbe.cs)、[registry](../../../src/RizToolbox.Adapter/AudioContent/LocalRunSessionRegistry.cs) | 就绪后发布六根来源链、精确 pending claim 校验、独立对象身份及 later-frame 检查代码 |
| E-004 | [Runtime 完成日志](runtime-targeted-final.log)、[Audio 完成日志](audio-targeted-complete.log) | 末行分别为 `Native runtime checks: 115/115 passed.`、`212/212 audio/content checks passed.` |

源码位置相对于仓库；源码及日志完整哈希位于清单。第三方可从仓库根运行下面的离线回归复核 E-004。这些测试加载受控 fake exports，不加载真实游戏 DLL。

```powershell
dotnet run --project tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj -c Release --no-restore --verbosity quiet
dotnet run --project tests/RizToolbox.AudioContent.Tests/RizToolbox.AudioContent.Tests.csproj -c Release --no-restore --verbosity quiet
```

最后的未知 free 用例会有意保留测试进程内的责任槽；不得通过重试 free 或清空静态表制造清理成功。

## Findings

| Finding | 状态与证据 | 已完成修正和界限 |
|---|---|---|
| F-001：`SetData` 失败可遗漏已取得的 pinned upload | 源码修正、离线回归通过；E-001、E-004 | upload 有独立记录和一次性 free 标记；构建中止后也进入确认清理。未确认 free 保留记录、包额度和 CPU fork |
| F-002：已分配输出可在临时 free 抛错时未交给调用者 | 新 publication 路径已修正并注入故障验证；E-002、E-004 | 引用结果、ctor 输出、SingleArray 输出均同步通知。临时 root 与 owned Arg 独立清理；失败记录有界保留，不隐式回收已通知的 caller 输出 |
| F-003：BPM 的 NaN 可绕过差值比较 | 源码已补明确有限值检查；E-001 | `Build` 和 `Read` 均先拒绝非有限 BPM；该检查不等于逐 note 的 native 结构验收 |
| F-004：not-ready 观测会锁住旧 delegate，误拒绝下一次有效普通局 | 源码改为候选链完整就绪后发布；E-003 | 候选六根独立逆序释放，失败保留且停止重试；确认 exact、live、active NormalMode 和正有限 JudgeData 后才成为来源链。等待实际游戏证据 |
| F-005：包消费者只能由本 session 当前 claim 导出 | CPU registry 回归通过；E-001、E-003、E-004 | 同时核验 claim 引用、metadata identity 和实际 adopted bundle。拒绝操作不消费 caller ownership。真实 native 消费者终止 verifier 尚未由本轮实测证明 |

F-001、F-002 的失败分支直接决定“清理完成”是否可信；修正后的代码在 unknown 状态保留进程责任，不能继续退额度。Publisher 的内部契约要求先同步记录输出，再做可能抛异常的工作。通知发生即转移输出责任，publisher 抛错也不会触发 runtime 的隐式 output free；未确认 receipt 保留 call 槽。

## Path：从精确 CPU revision 到确认释放

1. **准备输入**：`LocalPlayBundleInputs.CaptureBorrowing` fork 已加载 request，通过 snapshot `ReferenceEquals` 和 kind 唯一取出音频、封面及选中 chart source。provider/content/catalog 撤回或同 ID 重建不会替换该 revision。只释放自己的 fork，不单独 Dispose 共享 lease/template。证据 E-001、E-004；Finding F-001。
2. **建立独立图**：保留上传估算额度后，复制真实来源 JudgeData 的四个窗口值，创建 metadata、Texture/Sprite/Audio、全新 Chart、全新 JudgeData 和参数。Unity 分配及数组上传使用 publication 接口；其同步回调只写入预分配 slot。证据 E-001、E-002；Finding F-002。
3. **限制消费者**：导出参数 root 前核验 registry 实际 adopted bundle 和当前 pending claim，并先记录该 session。已附 session 的包在缺少对应消费者结束证据时拒绝 Destroy。证据 E-003、E-004；Finding F-005。
4. **请求销毁**：先释放参数和 `canDestroy=false` wrappers，再请求自有 Sprite/Audio/Texture Destroy。请求返回不等于销毁确认；direct roots、CPU fork 和额度保持。证据 E-001。
5. **确认并释放**：确认三类 Unity 对象均 dead 后，独立处理 upload、逆序 direct roots、metadata 及 CPU fork。失败停止再次 free 并保留责任；全部完成才退额度。probe 按两个严格后续帧分别退休两包，最终应观察 CPU prepared count `2 → 1 → 0` 和来源六根释放。证据 E-001、E-003；后半段是验收条件，尚非本记录提供的游戏结论。

## 尚需真实证据的边界

验收脚本位于[本轮 freeze-proof.py](../runtime-local-bundle-20261022/freeze-proof.py)。它要求完整正常停止的 session、所有 canonical 阶段和连续序列、0 Error、两图共十八个不同的已记录 native 身份、跨帧退休、CPU 计数、六个 source root 的释放，以及实际提供的十八文件安装 manifest 全匹配。Warning 会完整保留，脚本不把最后一条日志当作退出证明。

上传额度是 `PCM samples × 4 + RGBA32 pixels × 4 + sprite 1024` 的估算。它包含 retirement pending 的保留估算，不包含同时存在的 IL2CPP 上传数组、HGlobal、Unity/GPU 内部开销，因此不是实际 native heap 上限。

判定值取自仍存活的普通 NormalMode controller；包仅复制来源，不拥有或销毁这个 controller 或其设置。probe 的 score 字段是明确的测试 sentinel，没有推算可玩谱面的 MaxHit/MaxScore。参数 ctor 的 speed/offset 是前初始化观察；完整原版控制器设置初始化仍需单独验证，不盲设 speed 为 1 或推测 offset 单位。

本轮没有把 LOCAL 图注入或激活 controller，没有证明实际结果策略及存档隔离接线，也没有完成完整 LOCAL 游戏流程或 B01–B19 全覆盖。
