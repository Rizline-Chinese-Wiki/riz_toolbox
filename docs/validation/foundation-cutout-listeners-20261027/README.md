# CutOut 实际监听器与下一段实现边界

2026-10-27。本次只读检查固定游戏文件和既有 dump，没有安装、启动或操作游戏。结论用于决定实际功能实现顺序，不能作为运行时对象或效果完成凭据。

原件：[evidence.json](D:/riz-build-cache-next/tween-listener-source-r1-20261027/evidence.json)，SHA256 `561b3225017a6ef99d23edb0881960b67e291e7a24638b94c2edd6226a279a2a`。固定 GameAssembly SHA256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；原件另记录 dump、script.json 和两个 assets 文件的来源哈希。

## 资产中实际存在的工作

`sharedassets4.assets` 中的 NormalModeController（pathID 1443）引用 CutOut 的 EventTrigger（pathID 1406，GameObject 440）。字段顺序依据 dump 解释，运行时仍须核对官方 metadata 和实际对象图。

| 监听器组 | 实际持久化调用 | 目前需要补的功能 |
|---|---|---|
| 第一组 | 两次 `TweenInfo.Play`、三次 `GameObject.SetActive(bool)` | Tween 工作记录与队列转移；固定 Boolean 实际 invokable 解析；显隐触发的后续工作 |
| 第二组 | 两次 `CanvasGroup.set_blocksRaycasts(bool)` | 固定 Boolean 实际 invokable 解析；实际 UI 效果归属 |

两个 TweenInfo（1116、1304）均属于 CutOut，目标为其直属子物体的 RectTransform（877、895）。资产参数为 duration 1、delay 约 0.1、easeType 15、tweenType 3；固定代码该分支写 `Transform.localScale`。目标各轴分别约为 7.692307 和 6.993006。两者 autoPlay、useOnStart、useOnComplete 均为 false，三个子事件的持久化调用数均为零。

这些是静态初值。运行时可能新增委托或修改字段，不能用资产中的零监听器数封闭未来来源。

## 已定位的真实生命周期

| 函数 | RVA | 对实现的直接影响 |
|---|---|---|
| `TweenInfo.Over` | `0x3956e0` | 仅改 `isOver`，不从经理的队列或数组撤销对象 |
| `TweenInfo.Play` | `0x3956f0` | 非瞬时路径经静态 `TweenManager.AddTween` 委托提交实际 this |
| `TweenManager.AddTweenToWaitingList` | `0x396be0` | 在允许时初始化 nowTime，并进入 scheduled List 或 ready Stack |
| `TweenManager.ExecuteTween` | `0x396e40` | 先调用 onComplete，再置 isOver；回调可能重入或抛出异常 |
| `TweenManager.Update` | `0x398d60` | 清 playing slot，并在后续阶段将 scheduled 转移到 ready |

因此 `Play` 返回、`Over` 返回、`isOver=true` 或一次空队列采样都不能单独证明该调用的工作已经结束。场景死亡也不等于托管队列已清空。

## 实现顺序

1. 从已保留根的实际 `CachedInvokableCall<System.Boolean>` 读取官方字段和真实 delegate MethodInfo，把固定参数纳入完整快照及变化检查。只支持核验过的闭类形状，未知形状保持 Opaque。[这一项已实现并通过受控验证](../foundation-cached-boolean-20261028/README.md)，尚未在真实游戏对象上实测。
2. 为实际 Tween 发起者建立独立 owner：核具体 TweenInfo、目标、子事件、曲线，以及静态 AddTween 的实际 manager target 和声明；每次接纳使用独立代次，保留重复队列项的责任。
3. 跟踪同一经理的 scheduled、ready、playing 转移及 Execute/Update/子事件执行。结束凭据必须来自这个 owner，满足该代次所有队列项和实际执行均已排空。
4. 实现该 run 的发行关闭机制。公共 Play 和静态 AddTween 仍可被外部再次调用，纯旁观无法证明未来不会新增工作；需要按保留实例过滤的实际接纳关闭，或由框架持有并可关闭的发行来源。
5. 分别覆盖 SetActive 引发的生命周期回调和 UI setter 后续工作，再接入整个 listener 消费者关闭。完成 Tween 记录只能关闭它自身的责任。

不为旧框架协议保留替代路径；不在这段功能工作中增加性能优化。生产首次执行准入和真实整局验收仍是独立未完成项。
