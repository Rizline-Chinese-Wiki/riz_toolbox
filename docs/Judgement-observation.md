# 逐音符判定的接入边界

已有实验性的 `IGameplayJudgementService`，通过 `riz.gameplay.judgements` 能力获取。原生调用者捕获、局内身份注册表、真实对象图遍历及 SDK 订阅已经接通。受支持本体为 Steam build 25977441 / Unity 2022.3.62f2 / Windows x64。当前游戏证据见[集成验证](validation/runtime-judgement-identity-20261020/README.md)。十二个调用源的完整实机覆盖、偏差单位标定和 B11 整项验收仍在进行。

原游戏的外层 `GetJudgeControl.GetJudge` 参数是 int32 和浮点偏差，内层 NormalMode 参数才是 enum32。12 个实际调用点通过不同寄存器、列表或协程字段持有 NoteInformation，不能按时间最近的音符猜测归属。内层 NormalMode 还可能提前返回，或根据游戏状态改变评分。已实现的调用链为：

1. 原生入口在进入 CLR 前复制实际调用者的寄存器和返回地址。
2. 按固定本体 SHA、返回 RVA 和真实类型/字段关系识别调用源。未知来源仍只转发原函数。
3. 匹配当前 session/run/scene、controller 和 LevelInformation，保留真实 NoteInformation 与 chart 对象图。
4. 实际进入内层评分函数后，复制判定快照；最后一个判定即使结束该局也保留捕获时的旧局来源。
5. SDK 只接收不可变的语义数据。原生地址、寄存器、Unity 对象和 InstanceID 留在内部。

## 在 Mod 中使用

```csharp
var service = context.GetService<IGameplayJudgementService>(
    JudgementCapabilityIds.Observe);
if (service.Service is { } judgements)
{
    var result = judgements.Subscribe(observation =>
    {
        // 回调运行在主线程。保留语义 DTO 可以，不能在这里做长耗时工作。
        var runtimeNote = observation.RuntimeNote;
        var chartLocation = observation.ChartNote;
        var countersAfter = observation.CountersAfter;
    });
    // 检查 result.Success/Error；订阅归属当前插件作用域，可提前 Dispose。
}
```

能力已安装不等于当前局已取得身份。用 `GetStatus()` 查看 `RunIdentityAvailable`、`ObservedRunId`、`UnassociatedCalls`、各源 `CapturedCalls` 与拒绝原因；切换场景或提供者撤回后重新获取服务，旧代次不可复用。

| 字段 | 含义 |
|---|---|
| RuntimeNote | session / observed run / 局内单调序号；重试后是另一身份 |
| ChartNote / ChartAssociation | 根据保留的真实 noteData 引用定位 line/note 序号；重复别名为 Ambiguous，图外对象为 Missing |
| SourceId / DeltaKind | 固定调用点来源，以及实测时间差或哨兵值的区分 |
| RawJudgeType / RawDelta | 原参数；`DeltaUnit` 仍为 RawGameUnits，尚未承诺统一秒单位 |
| OriginalReturned | 实际内层原函数已正常返回；不表示请求必然计入分数 |
| CountersBefore / CountersAfter | 内层原函数前后读取的真实计数；与请求参数分别解释 |
| SceneGeneration / LifecycleRevisionAtEntry | 捕获时来源；最后一次判定结束该局也不改成下一局 |

SDK 订阅受插件作用域、优先级和异常归属约束。旧版 `GameplayJudgementObservation` 构造签名保留，新字段通过 init 属性扩展。

## 内部保证与验证范围

- broker 用固定 Dobby 的 Prepare → 配置和保留回调 → Commit 顺序安装，进程内每地址只有一个补丁。原生 helper 已随正式包安装。失败后不卸载可能仍在用的回调或重复修补该地址。
- 原生 fixture 分别完成 31,261 项 ABI/展开检查及 61,358 项真实 Dobby 发布检查。这里不宣称目标入口改写对并发执行入口的线程是原子的。
- Hooks 38 项、Judgements 27 项生产类回归覆盖共享入口、重入深度、异常原调用、局结束时保留来源、未知来源撤回和计数读取失败。完整构建为 665/665。
- ID 只基于保留的真实对象；同一对象的多次判定共享身份，不同对象即使值相同也不合并。单局最多保留 65,536 个音符及有界在途租约。
- 未知调用源、对象关系改变、容量耗尽或计数读取失败会撤回本局身份覆盖；原游戏继续执行。重试进入新局后可以重新建立注册表。
- 撤回后立即拒绝新观察，最后一个在途调用退出后逆序释放音符根，最后释放 run/chart 根；嵌套且未识别的调用不能借用外层音符身份。

这些保证和离线检查不能代替全部游戏分支、结果语义及长期 GC 测试。速度修改、任意跳转仍未开放。各调用点与哨兵值依据见[语义研究](validation/gameplay-semantics-20261020/research.md)。
