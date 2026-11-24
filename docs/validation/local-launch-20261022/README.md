# LOCAL 启动、重试与结算边界

2026-10-22，离线静态设计，flavor=null。**当前结论是可实施的边界，`runtimeAcceptance=false`。本目录没有启动关卡、执行保存或 Steam API，也没有修改 src/tests 或构建。**

最短闭环是：宿主持有一个完整 LOCAL 会话，在克隆 controller 前建立身份和隔离门；用原版普通 `LevelMetadata` 数据图、独立资源包装与每次新建的 Chart 构造 `InsideLevelParams`；LOCAL 结算跳过整个 `LevelOverControl.Start` 原方法，读取一次真实 `ControllerOver` 结果后显示自有结果面板；返回和重试由同一个宿主会话处理。现有 hook broker 只有观察和原方法返回后的引用替换，**不能跳过原方法**，因此还不能把这条路径开放为 `AvailableForGamePlayback=true`。

范围与授权见 [scope.md](scope.md)。继承固定样本的静态分诊，不重跑既有 17 个范围；只额外补出 stock 重试使用的一个未命名泛型克隆 thunk。工作流停在 synthesis/design，动态验收交给父任务。

## 证据与复现

| E-id | source_ref / artifact | 内容与质量 | fixity / repro |
|---|---|---|---|
| E-triage | [triage.json](triage.json) | 可读原生 PE64、CLR 目录为零、导入/导出分类；动态加载能力不能靠静态导入排除 | 样本 SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` |
| E-launch | [boundaries.json](boundaries.json)、[disassembly.txt](disassembly.txt) | 17 个原有有界范围、逐指令字节、ScriptMethod 声明及范围哈希 | `inspect-launch.py`；完整输入 hash 记录在 JSON |
| E-types | [metadata-excerpts.txt](metadata-excerpts.txt) | dump.cs 原行号、字段类型与签名；生成声明不等于已验证 native ABI | dump SHA-256 `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb` |
| E-result | [前序 metadata 边界](../local-metadata-bridge-20261022/boundaries.json) | `LevelOverControl.Start` 内联保存/Steam、stock LoadLevel 的 TextAsset 读取 | 复用已有提取，不重新提取 |
| E-release | [前序 provider 边界](../local-provider-catalog-20261021/boundaries.json) | `LevelOver.OnDestroy` 消费 cover/music 字段；card/prefab 的真实类型 | 复用已有提取，不重新提取 |
| E-clone | [instantiation-thunk.json](instantiation-thunk.json)、[文本](instantiation-thunk.txt) | 唯一新增范围 `0x5A91B0..0x5A92C0`，272 字节；有 engine clone 调用，但没有命名或关闭 MethodInfo 证明 | `inspect-instantiation-thunk.py`，命令如下 |
| E-broker | [hook-broker-source-snapshot.json](hook-broker-source-snapshot.json) | 五个 dispatch 方法的源码快照、原文件 SHA-256 与行号；这里只声明记录时的 broker 行为 | `verify-static-claims.py --check-live-broker` |
| E-anchors | [static-claims.json](static-claims.json) | 43 个精确指令锚点、五个 broker 原方法调用体及 11 个证据文件 hash | `verify-static-claims.py` |

仓库根目录执行，标准校验不依赖第三方 Python 包，不加载游戏 DLL：

```powershell
& C:\Python314\python.exe docs/validation/local-launch-20261022/verify-static-claims.py
```

还要核对报告时的 broker 是否已被后续实现修改：

```powershell
& C:\Python314\python.exe docs/validation/local-launch-20261022/verify-static-claims.py --check-live-broker
```

只在需要复现额外 thunk 提取时执行以下命令。需要本机已存在的 pefile/capstone；不安装工具、不运行样本：

```powershell
& C:\Python314\python.exe docs/validation/local-launch-20261022/inspect-instantiation-thunk.py --game 'D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll' --symbols 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\script.json'
```

原有 17 范围由 `inspect-launch.py --game ... --dump ... --symbols ... --out ...` 产生；其参数与前述相同，dump 路径为 `C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs`。本次没有重跑。原工具索引所列 IDA/r2 不可用，按父任务复用现成 Python/pefile/capstone；技能相对资源缺失时读取了 `reverse-skill-router` 同名资源。

## 关键发现

所有 severity 为 `n/a_re`。`confirmed-static` 只表示有限静态关系成立。

| F-id | evidence_ids | confidence | location | status / finding |
|---|---|---|---|---|
| F-commit | E-launch,E-types,E-clone | high | `InLevelController.Awake@371810` | confirmed-static：Awake 把对象交给持久场景列表，向 `GameManager.inLevelControl` 写六个回调与 `controllerSet=true`；参数 setter 是后续另一入口。设计上必须把克隆/激活视为不可当作纯准备阶段的提交点 |
| F-params | E-launch,E-types | high | `SetInsideLevelParams@36B0E0` | confirmed-static：仅写实例 `+20` 并尾调写屏障；此地址与大量无关 setter 折叠共享。可按精确 MethodInfo 调用，不能对该地址全局 hook |
| F-result | E-result,E-launch | high | `LevelOver.Start@3D0B20` | confirmed-static：它先调用 ControllerOver delegate 得到真实结果，再内联 ToBestScore/Update/set_Item/CheckAchievement/CheckOffset。拦 ProcessLevelMetadata 或在 Start 的 After 处理均不足以隔离 |
| F-retry | E-launch,E-result | high | pause `Retry@3C1310`、result `Retry@3CFF20` | confirmed-static：暂停重试先销毁旧 controller、切场景/卸载，再走类型特化加载；结果重试也按 concrete metadata 重新加载。普通 LevelMetadata 分支读取原版 TextAsset，Workshop 分支调用 WorkshopLevel.LoadChart；失败路径存在 Application.Quit |
| F-back | E-launch,E-types | high | `LevelOver.Back@3CE020` | confirmed-static：场景名来自 `result.metadata` 的接口 getter，随后调 StartNextScene。普通 `LevelMetadata.get_From` 是固定原版来源，不能表达宿主 LOCAL 返回目标 |
| F-broker | E-broker | high | `ExactHookBroker.InvokeVoid/Enum32/Judge/Reference/Boolean` | confirmed-source-baseline：Before 可以观察，但五种 dispatch 均继续调用 original；Reference 的替换发生在 original 返回后。当前没有 conditional-skip 权限，尤其无法为 Start/Retry 做前置隔离 |
| F-owner | E-release,E-types | high | `LevelOver.OnDestroy@3CFA50` | confirmed-static：只对 `coverReference@158`、`musicReference@160` 走 Release 并清零。跳过 Start 后不能假定这些字段已接管 LOCAL 资源 |

### 把准备与提交分开

`InsideLevelParams` 的精确字段是 metadata `+10`、Sprite reference `+18`、AudioClip reference `+20`、Chart `+28`、JudgeData `+30`、speed `+38`、offset `+3C`。其构造函数接受前五项。

提交前应完成 descriptor/revision 租约、完整谱面解析、新 Chart 实例、native metadata 图、独占或明确借用的 cover/music reference、精确 prefab 与返回场景、所有所需符号和隔离入口验证。把 pending session 放到宿主持有的稳定槽中，然后才克隆 controller。Awake 在 `371907` 调 `DontDestroyOnLoadScene`，在 `371A0D/371AB5/371B5F/371C0A/371CB1/371D04` 写六个回调，最后 `371D43` 写 `controllerSet=true`。这些变化已超出一个新分配对象的局部状态。

stock 两处重试在 `3D84CE/3C6D99` 调未命名 thunk `5A91B0`，之后 `3D84FA/3C6DBD` 才经虚表调用参数 setter。补充范围内 `5A920F` 调 CheckNullArgument，`5A9219` 调 `UnityEngine.Object.Internal_CloneSingle@1540240`，再按 RGCTX 类型做 cast 与 Unity null 检查。这里不能冒称已经解析了闭合 `Instantiate<NormalModeInLevelController>` 的 MethodInfo。可实现候选是用元数据精确选择非泛型 `UnityEngine.Object.Instantiate(UnityEngine.Object)`，核返回 clone 的实际类后再精确调用 setter。

**Awake 在 engine clone 内的精确同步时刻不在本次静态范围中。** 把 Instantiate/激活视提交点是保守事务设计：不得假设“Instantiate 返回后、SetInsideLevelParams 前还没有全局副作用”。这项时序仍需实机探针验证。

若克隆或参数注入中途失败，进入有责任的回滚状态：隔离门继续覆盖 pending/committed LOCAL；只撤销仍与本会话 controller 相同的六个回调/状态，销毁自己创建的 clone，等待场景/Unity销毁完成后再释放资源；不覆盖已被另一 controller 接管的 manager。未知清理结果应保留会话和根，不能清空身份后让原版 Start/Retry误放行。

### 最短结果、返回与重试

| 操作 | 接入对象 | 应走入口 | 应避免的 stock 入口与原因 |
|---|---|---|---|
| 获得真实成绩 | 会话保留的 exact `NormalModeInLevelController`，以及它的 LevelParams/metadata identity | `ControllerOver()` 调用一次并立即 root 返回 `InsideLevelResult`；既有 result-origin 观察可继续核对 provenance | 不构造假结果；不要重复调用。原 `ControllerOver@3BEA60` 自身在 `3BEAA5` 启动协程，非无副作用 getter |
| 显示 LOCAL 结果 | 宿主 session + 自建结果面板；若借用原版 LevelOver 场景，也只复用序列化 Text/Image/Button 组件 | pre-original 门识别 LOCAL 后跳过整个 `LevelOverControl.Start`；handler 获取/复用一次真实结果，读字段展示，按钮走宿主命令 | Start 的 After 已太晚。不能只拦 `ProcessLevelMetadata`。跳过 Start 后 `levelOverInfo@168` 尚未初始化，不能直接读取 |
| 可选借用原版数值排版 | 有效 `LevelOverControl` prefab 实例与真实 result | 精确 `SetDetails(InsideLevelResult)` 只作为后续可选显示步骤，先校验其序列化 UI 字段和数组 | 本范围未见该有限函数直接保存/Steam，但它不能代替 Start 的标题、封面、资源接管、BGM和输入初始化；不凭此宣称原版结果页已复用完成 |
| 返回 LOCAL 列表 | session 中保存的进入场景、LocalId/chartId/revision/选择快照；`GameManager.sceneManager` | 精确实例 `Rizline.SceneManager.StartNextScene(string,float,Func<bool>)`，目标取进入时快照；到达后恢复本地面板/选择 | `LevelOver.Back` 依 metadata.From 路由且操作 BackLock/TouchBlocker；普通 LevelMetadata 来源不会自动变成 LOCAL |
| 暂停重试 | 正在运行且身份匹配的 LOCAL session | 在 `NormalMode.Retry` 原方法前接管；先准备下一次独立 play bundle、新 Chart/参数，失败保留当前暂停状态；成功后由宿主执行已验证场景重建 | 原方法从 `3C13B4` 起已切场景，`3C13E9` Destroy，`3C1479` UnloadSceneAsync，再等待 stock loader；After无法恢复准备失败 |
| 结果重试 | 已结束的 LOCAL session 与其 pinned revision/完整资源责任 | 在 `LevelOver.Retry` 原方法前接管，使用同一 LOCAL launch 管线创建新 run generation | 原 Retry 读 `levelOverInfo.metadata`，进入 `StartPlayLevel` 的 concrete 分支，重读原版资产；不能传一个同名ChartInfo.id替换此责任 |

`LevelOver.Start` 在 `3D0E27` 调 `GameManager.inLevelControl.ControllerOver` 的 delegate，随后 `3D0E2D` 才把结果写入 `levelOverInfo@168`。因此最小自有 handler 应由已注册 LOCAL 会话定位精确 controller，并确保“已捕获真实结果则复用，否则只调用一次”。结果读取失败也继续跳过原方法，显示可返回的 LOCAL 错误状态；不能退回原版结算。

初次启动可复用的 prefab 引用候选已有明确类型：`LevelCardControl.normalModeInLevelController@B8`、`WorkshopLevelCardControl.normalModeInLevelController@D8`、`LevelOverControl.normalModeInLevelController@100`。暂停重试还有当前 controller 的 `StaticReference controllerPrefab@1E0`。这些只是候选来源；必须从当前场景中发现并 root 正确 owner，核确切字段类和 Unity 活性。当前没有实机证明这些来源都在 LOCAL 面板出现时可达，不应靠全局裸指针缓存或替原版启动协程传伪 metadata。

### 隔离门的必要契约

最小能力增量是 **宿主内部、精确 InstanceVoid、前置可跳过 original 的协调门**，可用于 `LevelOver.Start`、`NormalMode.Retry`、`LevelOver.Retry`；若复用结果场景的原版 Back 按钮，则 `LevelOver.Back` 也必须接管。普通插件 observer 不获得任意跳原方法权限，同地址只允许一个内部协调者，原来的观察者仍应收到清楚的执行/跳过结果。

门的分类必须在 managed observer 副作用之前完成。依据宿主持有、已 root 的 pending/committed session identity及controller/metadata归属，不能依据字符串前缀、临时 provider 可用性、用户可写的 bool、`metadata.From` 或尚为空的 `LevelOver.levelOverInfo`。已确定属于 LOCAL 的实例，即使 handler异常、provider撤回、模块停用、重复点击或嵌套调用，也不得回落 original。一般原版实例继续精确保留原路径；不认识且没有 LOCAL 会话关系的实例不得擅自拦截。

会话进入提交状态前，全部门须已安装且保持进程责任。启动后移除业务插件或 provider，不能把保护门和本会话资源责任一起撤走。准备阶段可取消；提交后须执行正常返回/失败回滚。每次重试产生新 controller/run generation 和新 Chart，旧 revision 已加载资源在最后消费者退出后释放；不把同一个可销毁 reference交给两个owner。最短自有结果方案保留 host 资源owner，避免把未运行 Start 的 LevelOver.OnDestroy 当成成功交接证明。

## 原始入口与签名

以下为 dump 的托管语义签名与固定样本 RVA，**不是新增 detour 的 ABI 验收**。ScriptMethod 的生成 C 声明会漏掉实例 this，折叠地址也会带上无关方法名。native调用需通过现有元数据解析/ABI校验并保留 trailing MethodInfo；不要直接把表当函数指针声明。

| 所在类型 | 精确托管入口 | RVA | 采用方式 |
|---|---|---|---|
| `InsideLevelParams` | `.ctor(ILevelMetadata, IResourceReference<UnityEngine.Sprite>, IResourceReference<UnityEngine.AudioClip>, InLevel.Chart, JudgeData)` | `375300` | `NewExact`，验证实例 ctor、五参数类型和根 |
| `UnityEngine.Object` | static `UnityEngine.Object Instantiate(UnityEngine.Object original)` | `153F670` | 非泛型精确 MethodInfo 候选；返回 exact controller类须核验 |
| `InLevelController` | instance `void SetInsideLevelParams(InsideLevelParams newLevelParams)` | `36B0E0` | 精确 invocation；共享代码，禁止全局 detour |
| `NormalModeInLevelController` | instance `InsideLevelParams GetInsideLevelParams(InLevel.LevelInformation)` | `3BEDF0` | 局内 LevelStart 回调消费；继承前序静态消费者证据，不能与参数 setter混同 |
| `NormalModeInLevelController` | instance `void LevelOver()` | `3C0C00` | 结束/跳过会调度协程和转场；不把它当结果纯查询 |
| `NormalModeInLevelController` | instance `InsideLevelResult ControllerOver()` | `3BEA60` | 真实原返回；现有 `InstanceReferenceReturn0` observer可核 provenance |
| `NormalModeInLevelController` | instance `void Retry()` | `3C1310` | LOCAL需要前置接管；候选 `InstanceVoid(receiver,MethodInfo)` |
| `LevelOverControl` | instance `void Start()` | `3D0B20` | LOCAL必须在 original 前跳过；候选 `InstanceVoid` |
| `LevelOverControl` | instance `void SetDetails(InsideLevelResult lastResult)` | `3D00B0` | 可选显示调用，仍需UI字段验证 |
| `LevelOverControl` | instance `void Back()` | `3CE020` | 若按钮仍指向stock则接管；候选 `InstanceVoid` |
| `LevelOverControl` | instance `void Retry()` | `3CFF20` | LOCAL需要前置接管；候选 `InstanceVoid` |
| `LevelOverControl` | instance `IEnumerator StartPlayLevel(ILevelMetadata metadata)` | `3D0A90` | stock类型特化加载，不作为LOCAL统一启动入口 |
| `Rizline.SceneManager` | instance `void StartNextScene(string nextScene, float transTime, Func<bool> waitPredicator)` | `402170` | 主线程精确调用；结果与场景激活/卸载仍需等待并核验 |
| `Rizline.SceneManager` | instance `void DontDestroyOnLoadScene(GameObject cutOutGameObject)` | `4019D0` | Awake已调用的全局责任边界 |

原来的 `SetInsideLevelParams` 机器码是 `mov [rcx+20],rdx; add rcx,20; jmp barrier`，已直接证明 RCX 是实例、RDX 是参数。`ControllerOver` 等无显式参数实例方法也使用 RCX 作为实例。新控制门应继续由 `ResolveExactHookTarget` 验证程序集/类型、实例性、参数、返回类型、固定RVA/样本、无ref-out/泛型和线程条件；未知 MethodInfo/ABI 一律不放行LOCAL launch。

## Path 与验收边界

P-launch，path_type=implementation-proposal，依据 F-commit/F-params/F-broker：完整准备包 → 全部门验证并安装 → pending session注册 → 克隆/激活提交 → root并核controller → SetInsideLevelParams → stock局内LevelStart消费本次params → 进入本地run。这里的 clone 生命周期时序、场景名和图交接需要真实游戏证据。

P-result，path_type=callflow，依据 E-result/F-result：stock正常结束 → LevelOver.Start → manager.ControllerOver delegate → 原始result → concrete LevelMetadata → ToBestScore/Update → DomainWrapper.set_Item → SaveData.Set/SaveService → CheckAchievement/Steam。安全LOCAL路径应在 Start original前分流，不走这条保存链。

P-local-end，path_type=implementation-proposal，依据 F-result/F-back/F-retry/F-owner：LOCAL pre-original门 → 真实结果一次性取得/复用 → 自有结果面板 → 宿主返回或新run重试 → 完成场景/Unity对象清理 → 最后释放旧run资源。准备失败、撤回、取消、重试中断、原生异常和插件停用仍需逐条运行验证。

本报告没有验收以下事项：LOCAL真实播放；非泛型Instantiate返回及Awake时序；LOCAL Start/Retry前置skip；本地结果页渲染和点击；返回/重试场景与BackLock/TouchBlocker平衡；所有局内/result消费者退出后的实际资源释放；原版存档及Steam统计确实零调用/零变化。父任务应先实现前置门和session责任，再开放播放并收集这些实机证据，不能仅凭本文或离线数量勾选 B15。

时间线：继承E-triage/E-launch/E-result → 核对具体入口、六回调与stock结果/重试路径 → 补唯一缺失clone thunk → 冻结broker源码基线 → 43锚点校验通过。decision_delta：可实施方案收敛为同一宿主LOCAL会话贯穿启动/暂停重试/真实结果/结果重试/返回；关键阻断明确为broker缺少前置skip及其长期责任。carry_forward_refs：scope.md、本目录及前序两个boundaries.json；动态阶段由父任务继续。
