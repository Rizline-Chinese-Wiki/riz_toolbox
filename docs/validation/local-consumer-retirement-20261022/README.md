# LOCAL 原生消费者结束证据

2026-10-22，flavor=null，`runtimeAcceptance=false`。[范围](scope.md)。本次只读源代码、已有证据与固定样本的两个小范围，没有构建、安装、启动/终止进程或输入游戏。

**本轮可实现的正向分支是“从未激活的独占克隆，在参数注入失败或取消后的退休验证”。已激活 run 的完整 `ILocalRunConsumersEndedVerifier` 尚缺协程与 hook 在途证据，不得发 token。** 不能把结果一次捕获组件、空 verifier、Destroy 调用返回、SetActive 调用返回、Unity-dead 或场景名称改变替代完整的消费者结束证明。

## 现在就可复用的原生观测

| 责任 | 已有真实 API / 实现 | 实施用途与限制 |
|---|---|---|
| 原生身份和根 | `NativeRuntime.Root`、`RootHandle.Target`、`ObjectClass`、`Read<T>`、`ReadStatic<T>`、`RequireInstanceFieldLayout`、`InspectDeclaredMethod` | 每个比较来自仍持有的 root；精确类/字段布局先验证。数字 identity 是报告内容，不是重新获得对象的授权 |
| Unity 活性 | `UnityEngine.Object.op_Implicit(UnityEngine.Object)`；`NativeControllerGlobalsBackend.Alive`、`NativeControllerPrefabDiscovery.Alive`、inactive factory 的 `Alive` | 对 controller、GO、parent、transform、已捕获 info/UI 各自检查。`activeSelf=false` 只是禁止继续调度的准备状态；`op_Implicit=false` 不证明其他对象或外置协程已结束 |
| 六个 globals | `OwnedNativeControllerGlobals.BaselineSnapshot/ObserveCurrent`；backend `CaptureDelegate/TargetsController` | `ObserveCurrent` 只输出六个 delegate identity 与 owner/flag。active 退休还必须检查六个 delegate 的 invocation list/Target，不得只比较 identity；本轮 never-active 分支要求整个 baseline 完全不变 |
| delegate 语义 | backend 通过 `Delegate.get_Target@374D80` 和 `MulticastDelegate.GetInvocationList@F70660` 精确调用；当前仅接受单播 | 可复用这一路径检查当前六字段目标均非旧 controller。多播、未知目标类、取值异常应失败，不可把“不相同的delegate地址”当“没有旧target” |
| 完整对象发现 | `NativeControllerPrefabDiscovery` 的 `Resources.FindObjectsOfTypeAll(Type)`、`Type`、rooted 数组、`get_Rank/get_Length/GetValue(Int32)` | 将“完整扫描 + 上限失败即拒绝”的模式用于 LevelInformation 和结果 UI。已有 `NativeArrayScanCursor` 是分批发现工具；未扫描完整的一批不能证明不存在消费者 |
| Scene handle 与加载状态 | `GameObject.get_scene` 返回 Scene 后按已有后端 `Unbox<int>` 取 handle；`Scene.IsValidInternal(Int32)@155A8B0`、`GetIsLoadedInternal(Int32)@155A7F0` | 捕获原场景的 handle、宿主 generation，并验证卸载操作 `AsyncOperation.get_isDone@1534390`。场景名称可复用；相同 handle 也须防 generation 混用。持久对象可能已移出原 scene，仍需单独检查 |
| Awake 未发生 | `NativeControllerAwakeLedger.CaptureWindow.SnapshotFor(root)` | 窗口从 Instantiate 之前开始，持续到晚于 Destroy 请求的帧；`TimingVerifiable=true`、所有 native/original/before/after 为零，InFlight=0，失败数组为空；仅此分支可排除 Awake 发布六globals、DontDestroy 注册及继发入口 |
| 延迟销毁 | `OwnedNativeInactiveController.RequestDestroy/TryCompleteDestroy` | 真实请求销毁，再在更晚 Unity frame 轮询 controller、controller GO、parent、两个 transform 都 dead；不确认的 allocation/free 不重试变成成功。该 owner 明确只拥有未激活克隆，不能作为 activated-run verifier |
| host 工作责任 | `LocalRunProtectionHost.Read`、handler `HasPendingWork`、process `TickProcessOwners` | 可证明 owner仍被保留、有无未知责任。host 的 `_handling/_classifying` 目前为私有，`Read` 不暴露在途；`Boundaries.NativeCalls/OriginalCalls` 为累计入口计数，不能用差值当 active-call count |

## 具体 native 责任映射

下列字段/地址均属于固定样本。运行时仍须经现有 NativeRuntime 精确元数据验证；表格不授权裸偏移调用。

| 消费者 | 所有权/身份从哪里建立 | 退休时应见到的强证据 |
|---|---|---|
| `NormalModeInLevelController` | inactive clone publication 返回的 exact root；`InLevelController.LevelParams+20`、`LevelInformation+28` | 原 controller/GO/parent/transform 全部 dead；旧 callbacks 已排空；注册源与 globals 均不再使其可进入 |
| `InLevel.LevelInformation` | stock `GetInsideLevelParams@3BEDF0` 的 `3BEF51` 把实际参数写进 controller `+28`；进入后即时 root | 本次 exact info 及其原场景退出，另查可能持久化/移场景的消费者；`levelOver=true`、`loadOver`、`isPlaying=false` 都不是消费者已结束 |
| info 引用的局内组件 | `LevelInformation` 字段 `levelControl+20, progressControl+28, challengeTimeControl+30, lineControl+38, noteControl+40, holdControl+48, inputControl+50, noteJudgeControl+58, getJudge+60, uIControl+68, canvasControl+70, cameraControl+78, lowLevelTouch+80` | 启动后捕获的各实际组件 dead，所属原 scene 卸载完成；完整发现扫描不可再发现活的旧 info 消费者。chart 与拆出的数组是普通托管对象，不能对它们调用 Unity null 来证明结束 |
| 结果 UI | 先绑定 exact `LevelOverControl` root + scene generation；`levelOverInfo+168` 绑定本次真实 result；自建面板则独立持有 owner | 绑定 UI、其 GO、自建面板与绑定回调均退场；结果 scene 卸载完成；宿主队列没有旧 result 命令；结果捕获不在途。`OnDestroy@3CFA50` 只释放 `coverReference+158`、`musicReference+160`，不能证明整个结果责任结束 |
| 六个 `InLevelControl` 回调 | 原 root owner；`GameManager.Instance` 静态；`GameManager.inLevelControl+38`；六字段 `+28..+50` | 对仍活的旧 owner 与当前 owner分别完整核六字段，任何 invocation target为旧controller均失败；当前 owner 读前/读后相同。successor delegate不能被旧会话清理覆盖 |
| `controllerSet+20` | 与六字段同一个 `InLevelControl` | 条件恢复/readback是独立证据；仅 `false` 不排除非空旧回调，仅 `true` 也不说明当前回调仍属旧controller |
| `Rizline.SceneManager` | `GameManager.sceneManager+20` 的 exact root | 观察实际 `_lock+30=false,_async+58=null`，原异步操作已完成；`_waitPredicator+50`、五个 event/action list `+60,+68,+70,+78,+80` 没有可恢复旧 run 的未处理责任；旧/current owner变化需分别处理 |
| 持久和 cut-out GO | SceneManager `_dontDestroyGameObjects+28`、`_cutOutGameObjects+20` | 按精确旧 GO identity检查完整容器，不允许其中有仍活旧对象；不要要求所有字典/list为空，游戏会保留dead wrapper；也不要调用全局 Clear 方法摧毁别人的对象 |
| controller 本地协程 | `GetInsideLevelParams` 在 `3BEFD5/3BF0AD` 把 SimpleDelay/WaitUntil 挂在旧controller；`ControllerOver` 在 `3BEAA5` 启动controller协程 | 有界ledger记录开始/结束、取消已完成，或精确已知宿主GO真实销毁且确认没有移交到外置宿主/队列；frame前后只是等待，不单独充当证据 |
| 外置延迟与 scene 协程 | `LevelOver@3C0F69` 的 SimpleDelay 由另一个静态单例 MonoBehaviour承载；SceneManager自身也运行 `StartNextSceneEnumerator` | 不能因旧controller dead就判完。需记录确切外置操作/返回句柄/完成回调并排空；不得 StopAllCoroutines 当前全局manager来制造成功 |
| hook 在途 | `ExactHookBroker` policy和ordinary retained context + native judge invocation、host分类/handler | 有明确入/出配对计数与发行屏障，核当前session/receiver的所有在途为0；关闭只能在边界handler退出后的host Tick。绑定Dispose只移除logical sink，不等待已经进入的callback |

## 两个最小补证的结论

`NormalMode.<>c.<LevelOver>b__108_0@3C7E90..3C7F30` 只有 `TouchBlocker.Release@3C7EB4` 与 `MultiLock.Unlock@3C7F1C`，没有读取入参 receiver，也没有直接读取 controller/params/info/result。其声明类只含静态单例与两个缓存 Action 字段。这支持“该有限函数体不直接持有/读取本次run图”，**不能支持“外置delay已经完成”**；它仍会在将来改变全局输入/锁状态。未经外置操作ledger不得发 active-run消费者结束token。

`Rizline.SceneManager.ClearDontDestroyOnLoadObject@401710..401820` 遍历 `_dontDestroyGameObjects+28`，用 `Object.op_Inequality` 过滤活项后 `Object.Destroy@4017E8`，随后直接返回；**没有清空或移除 list 元素**。名称中的 Clear 不能当成容器清空证据；也不能把这次Destroy请求返回当销毁完成。

已有 `StartNextSceneEnumerator.MoveNext` 的状态4终结路径在 `40412E` 清 `_async`、`40413D` 清 `_lock` 后返回false；`404705..404781` 清三个 wait list。这些值可作为静态可解释的完成信号，仍须与本次捕获的scene/operation身份、回调排空证据合取。

## 本轮最小真实 backend

建议实现专用 `NativeNeverActivatedConsumersEndedVerifier`，不要给它 active-run通用成功分支。这个组件自己从真实NativeRuntime读取以下事实，而不是接收调用方填入的“已结束”bool。

1. 创建受界限约束、进程保留的责任记录；持有clone独立root、Awake窗口、globals baseline与待结束session exact identity。禁止把可调用clone指针交给provider/plugin。记录在参数setter/注入前已存在，调用失败也保留。
2. 窗口从Instantiate前持续观察；未执行任何激活/SetParent到活树等入口；所有最后一次真实inactive读回满足parent inactive、controller inactiveInHierarchy、父子精确一致、`LevelInformation=0`，参数仅允许0或此session的exact独占参数。只依据自身封闭事务的调用记录与真实读回，不依据用户可写bool。
3. 结束请求前后分别 `ObserveCurrent`，核相同的 current manager/control、六个delegate identity和`controllerSet`与baseline完全一致；`SnapshotFor`持续验证所有Awake观察为零且无失败/在途。若任一方法调用发生未知原生副作用或publication未确认，即保持pending/unknown。
4. 请求销毁自有clone GO与parent；记录Unity frame；在更晚frame继续检查Awake为零、globals完全不变，并由真实 `TryCompleteDestroy` 核五个Unity对象dead。保留registry採用的独立controller root，直到对应token Close完成，不能仅保留历史pointer。
5. 明确要求 `ResultSceneIdentity=0,ResultSceneGeneration=0,ResultIdentity=0`，且事务从未调用`ControllerOver`/结果UI/LevelStart/场景转移。controller identity必须和本记录exact root匹配；无controller的Prepared取消应单独证明native输出未产生且无publication在途，不能套一个空native verifier。
6. 在后续host Tick且handler已退出时验证hook责任；现有累计counter不够，增加内部“真实入/出 + 未知为粘性失败 + 发行屏障”读口，或在这个封闭never-active分支证明没有任何boundary/retained回调见过该receiver且没有向原生可调用源发布它。不要把普通`Read()`里的`HandlerWorkPending=false`当作hook排空。
7. 组件只对全部合取成立的同一retirement request返回true，并给出有界evidence（session/generation、frame、Awake=0、globals baseline未变、five-dead、callback drain）。然后才能 `Sessions.VerifyConsumersEnded` → exact token → `Close`；释放异常保留unknown责任，不重试free。

这个分支有原生读取、真实迟延销毁与严格可达性前提，能够覆盖本轮参数注入失败/取消；它没有激活controller，不需要伪造“实际场景已结束”。将来一旦记录了Awake、info、result或scene转移，则拒绝进入本分支，交给扩展的active-run ledger。

## Evidence → Finding → Path

| E-id | source_ref / artifact | content_hash / repro | 观察 |
|---|---|---|---|
| E-source | `src/RizToolbox.Adapter/AudioContent/{LocalRunSessionRegistry,NativeControllerGlobalsBackend,OwnedNativeControllerGlobals,NativeInactiveControllerFactory,NativeControllerAwakeLedger,LocalRunProtectionHost}.cs` 与 `src/RizToolbox.Runtime/Hooks/ExactHookBroker*.cs` | 本目录 `source-manifest.json`；`verify-evidence.py` | 原生真实读口、trust boundary、现有在途信息不足、inactive销毁合同 |
| E-launch | [launch disassembly](../local-launch-20261022/disassembly.txt)、[metadata](../local-launch-20261022/metadata-excerpts.txt)、[triage](../local-launch-20261022/triage.json) | 本目录source-manifest；保留前序固定样本triage/import/export，不重跑全量 | Awake全局发布、ControllerOver协程、LevelOver外置delay、scene终结状态 |
| E-info | [metadata bridge disassembly](../local-metadata-bridge-20261022/disassembly.txt) | 本目录source-manifest；`3BEF51,3BEFD5,3BF0AD` | stock保存实际LevelInformation，并在controller上启动delay/wait |
| E-destroy | [provider disassembly](../local-provider-catalog-20261021/disassembly.txt) | 本目录source-manifest；`3CFA50..3CFB00` | result OnDestroy只处理两个resource reference |
| E-small | [retirement-boundaries.json](retirement-boundaries.json)、[retirement-disassembly.txt](retirement-disassembly.txt) | 每范围hash在JSON；下述命令 | 仅新增272字节ClearDontDestroy和160字节外置callback范围 |
| E-types | [metadata-excerpts.txt](metadata-excerpts.txt) | dump整体hash在boundaries；下述命令 | LevelInformation组件、scene/async APIs、coroutine声明与静态callback类 |

| F-id | severity / status / confidence | evidence_ids | finding |
|---|---|---|---|
| F-inactive | n/a_re / confirmed-source-and-static / high | E-source,E-launch,E-info | 不激活、Awake持续为零、globals未变、真实迟延销毁和hook责任有界时可验证never-active退出；不能推广为active-run token |
| F-delay | n/a_re / confirmed-static / high | E-launch,E-small,E-types | 外置delay不随旧controller销毁完成；小callback只释放全局触摸/锁，有限函数体无run图读取 |
| F-list | n/a_re / confirmed-static / high | E-small,E-types | ClearDontDestroy不清列表，dead wrapper可残留，不能要求空容器或借名扩大销毁权限 |
| F-hooks | n/a_re / confirmed-source / high | E-source | Host诊断与broker累计入口计数不是排空证明；在handler内部或仅Dispose binding后不得Close |

P-never-active，path_type=callflow：先登记责任与Awake窗口（E-source/F-inactive）→ 独占inactive clone与exact参数注入（E-source）→ 失败/取消后捕获真实inactive/globals未变（E-source/E-info）→ 销毁后续帧五Unity对象dead且Awake=0（E-source/F-inactive）→ 核未发布可调用源与hook排空（E-source/F-hooks）→ issuance barrier中发exact token并Close（E-source）。残余：本文件没有实机执行该路径，active-run retirement仍不得通过。

时间线：读取已有责任层与静态证据 → 定位外置delay与SceneManager持久容器 → 只补两个缺失入口 → 收敛never-active正向分支与[负例矩阵](negative-matrix.md)。

## 离线复现

```powershell
& C:\Python314\python.exe docs/validation/local-consumer-retirement-20261022/inspect-retirement.py --game 'D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll' --symbols 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\script.json' --dump 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs' --out docs/validation/local-consumer-retirement-20261022
& C:\Python314\python.exe docs/validation/local-consumer-retirement-20261022/verify-evidence.py
```

技能检查：范围已明确；固定输入hash校验通过；采用现有Python/pefile/capstone路径，无安装；两个有限原生范围与完整指令字节已输出；E/F/P与负例矩阵存在；原生运行验收未发生。scope/本目录产物之外没有修改。
