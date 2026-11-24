# LOCAL 之外的底层剩余实现审计

核对日期：2026-10-23，Asia/Shanghai。`flavor=null`，`runtimeAcceptance=false`。

本次选出的三个优先缺口是：**受控 Hook 修改与高级适配登记、独立玩法视觉语义目标、带参数的速度/定位事务**。三者都有可定位的实现缺失，不能只靠增加一次游戏实测补齐；也都有可先离线实现的契约、所有权和拒绝分支。这里的排序依据是对不同类型 Mod 的复用范围和当前调用链的缺失程度，不是完成百分比。

本报告只读核对 B01–B19、状态文档和现有源码；唯一新增物为本文件。没有构建、安装、操作游戏、修改源码/测试或改变任何验收勾选。正在进行的统一构建结果由该批独立证据记录，本报告不提前引用其检查数量或成功状态。

## 范围与判断方法

授权范围为当前仓库内、LOCAL 事务之外、可以继续离线推进的底层工作。以 [SPEC](../../../.docs/foundation-expansion/SPEC.md) 为目标，以 [实现状态](../../IMPLEMENTATION-STATUS.md) 和 [当前检查点](../../../.docs/CURRENT-CHECKPOINT.md) 限定已取得的证据；再检查对应 SDK、Core、Adapter 和现有回归。没有重新提取原生样本。

“实现缺失”表示当前接口或生产调用链没有承载所需行为；“仅缺实机”表示该具体子路径已有实现和离线检查，但没有所需的实际目标/流程证据。一个 B 项可以同时存在两类缺口。文档列出的历史包检查数不等于当前源码的能力覆盖率。

| 优先项 | 主要涉及 | 确定缺口 | 本轮可离线交付 | 仍须另外取得的证据 |
|---|---|---|---|---|
| F-01 | B07、B08，兼顾 B03/B06 | 公共 Hook 修改请求始终拒绝；缺高级适配包的版本化目标登记与受控修改通路 | 可选契约、允许策略目录、仲裁和在途撤回规则、故障/重入回归 | 每个实际开放目标的原生入口、ABI、路径覆盖、修改后行为 |
| F-02 | B12、B13，兼顾 B08 | 视觉发现仍从 UGUI Image/Text/Graphic 及有限 TMP 槽出发；缺音符/轨道/相机/后处理的独立语义适配 | 目标角色/profile、生命周期边界、代次/别名仲裁和资源交接算法 | 精确组件/属性来源、实际写回及双包恢复、可见效果与最终释放 |
| F-03 | B11，兼顾 B03/B08/B09 | SetSpeed/Seek 只有枚举名，现有 Execute/Dispatch 没有数值参数且生产探测明确不支持 | 新可选带参数契约、单位/来源约束、事务与失败责任、离线边界矩阵 | 真实时间单位/映射、原版重置语义、不同速度/刷新率及结果影响 |

## F-01：闭合高级 Hook 登记与受控修改通路

**分类：实现缺失；高置信度。`evidence_ids=[E-01a,E-01b,E-01c,E-02a,E-02b,E-02c,E-02d]`。**

现有 `IHookService` 能枚举目标、读诊断并注册 `Before/After` 观察；`RequestMutation(string)` 没有携带参数/返回值变换或跳过策略的类型。生产 `HookDispatcher.View.RequestMutation` 对任何目标都直接返回 `MutationNotSupported`。现有回归也明确要求修改请求被拒绝。这是正确的当前限制，不能描述成“修改算法已完成，只差实测”。

`ExactGameplayHookModule` 的公开服务只发布 `HookCapabilityIds.Observe`，目标是内建的 Judge/Pause/Resume；声明调用在 Core/Adapter 内。SDK 的 `AdapterModuleManifest` 是声明 DTO，公开 `IAdapterModuleStatusProvider` 是只读状态查询。核对范围内没有高级适配包可用的版本化目标注册入口。当前 LOCAL 的宿主专用跳过策略不构成普通 Mod 的通用修改 API，也不授权插件改任意地址。

这项缺口直接影响修改玩法、扩展新观察点或替换特定行为的 Mod：普通插件可以观察，但无法通过受控契约表达一个经适配器允许的变换或跳过请求。

可先离线完成以下闭环：

1. 以新增可选接口承载版本化目标/ABI/允许阶段/允许策略目录，保留现有 SDK 1.x 接口；目标声明与“已经实际安装”分开。
2. 接入现有 `MutationCoordinator` 的对象代次、唯一键与策略验证；对 Hook 的调用期参数、返回值及 original 控制分别规定所有权和一次性语义。不能把持久颜色覆盖栈直接当作每次调用的修改事务。
3. 用受控 broker/dispatcher seam 验证两个/三个提供者、相同优先级、非法变换、提供者异常、嵌套调用、worker、回调中撤回和 logical detach 之后的 retained finally；未知目标/ABI/策略继续拒绝。

离线完成标志是新契约与生产仲裁路径可执行、旧插件 ABI 保持兼容且全部拒绝/异常责任有测试。没有逐目标真实挂接、original 次数及玩法影响证据时，只能声明该目标未开放，不能据此勾选 B07/B08。

## F-02：视觉资源底座尚未连接独立玩法语义目标

**分类：实现缺失与实机缺证并存；高置信度。`evidence_ids=[E-01a,E-01b,E-01c,E-03a,E-03b,E-03c]`。**

现有视觉底座已经有 PNG/Texture2D/Sprite、内置字体、Material clone、精确材质 profile、目标代次、覆盖仲裁与延后清理。不能把这些已实现内容重新列成“没有资源系统”。

具体缺口在目标连接层：`VisualModule.Define` 按 kind 选择 `Image`、`Text` 或 `Graphic`；`NativeVisualBackend` 从这些类型及有限的 `TMP_Text.fontSharedMaterial` 访问器建立候选。它不是从本局 `LevelInformation` 图取得音符/轨道角色，也没有相机/后处理参数模块。现有材质 profile 是有限的 TMP/Translucent 证据，不能推广成相机或后处理支持。

[现有视觉文档](../../Visual-resources.md) 明确限定不提供音符/轨道语义目标、不开放相机/后处理，并指出对象重建后需要重新 Resolve、未实现持久意图自动重绑。这些是源码边界；另一类缺口才是已实现的 TMP/Float 等路径尚欠实际双包恢复、像素/GPU 和最终物理释放 receipt。二者不能互相替代。

可离线推进的最小交付是**每次选择一种具体角色**，建立：

1. `run/scene generation + component role + property/slot` 的目标描述和唯一性规则，明确零匹配、歧义、模板与活跃实例、重建后的旧租约失效。
2. 从角色到现有资源/修改协调器的受控适配接口，规定借用原资源、框架资源及 assignment pin 的交接；未知属性没有 setter。
3. 受控对象图回归：同名不同角色、场景/对象重建、地址复用、两包交错覆盖、游戏外部写入、失败读回、持有中撤回和延后 Destroy。若实现自动重绑，必须产生新代次/新租约，禁止旧句柄跟随新对象。

这些算法可以先在离线 seam 上完成。生产角色的字段/访问器、实例来源和更新者仍须使用精确元数据与有限静态证据确定；未完成该证据前不发布真实可用目标。随后每种角色独立取得游戏实测，普通 Canvas/BG 的 Float 成功不计为音符、轨道、相机或后处理验收。

## F-03：速度与定位缺少可承载数值的事务契约

**分类：实现缺失与原生语义缺证并存；高置信度。`evidence_ids=[E-01a,E-01b,E-01c,E-02d,E-04a,E-04b,E-04c]`。**

SDK 的 `GameplayControlAction` 含 `SetSpeed`、`Seek`，但 `IGameplayControlLease.Execute` 只接受 action 和取消 token，没有速度值、目标位置、时间单位或定位原点。Adapter 的 `GameplayCapture.Dispatch` 同样只接 action；`GameplayNativeBackend.Probe` 直接把两项标为 `Supported=false`。因此仅增加一个 RVA 或把 Supported 改为 true，仍无法表达用户所需的参数，更不能安全执行。

已有独占 `GameplayControlLease`、controller generation、状态前置检查和修改审计可以复用；不能重新造一套无代次的控制口。游戏时钟当前按 `RawGameUnits` 输出，DSP/host 以秒输出，`SecondsMappingVerified=false`；现有回归专门防止把未证明映射标成已验证。这是可靠的拒绝边界，尚无证据可把三个时间轴直接换算。

可离线交付：

1. 新增可选带参数请求契约，不向旧接口添加强制成员：区分速度倍率、明确单位/原点的定位请求、作用的 exact run/controller generation，以及有限值/范围/取消的验证。
2. 定义独占事务步骤和原生调用前的预检、调用开始后的 unknown 状态、后置读回及不可撤销边界；跨歌曲时间、音频、note/judge/hold 缓存的半重置不能靠 `Dispose` 声称回滚。
3. 以生产事务算法的受控后端验证非法单位、未校准映射、过期代次、暂停/结束状态、冲突、回调重入、部分副作用异常和后继 run 隔离；修改审计携带原始请求与来源。

离线算法和契约可以完成，生产 `SetSpeed/Seek` 仍应返回 `OperationUnsupported`，直到原版各消费者与重置顺序得到静态/动态依据。之后还需不同刷新率、速度、暂停恢复、判定顺序/计数及本地结果/提交路径的独立验证。仅有 DTO 或 mock 成功不算 B11 控制原语完成。

## 其余 B 项如何区分

以下是对状态目录的筛选说明，不是对其每个源码分支重新做全量认证；未入前三也不表示已完成。

| B 项 | 当前文档给出的剩余类型 | 本次处理 |
|---|---|---|
| B01/B09 | 正常完成、初始化/中途启用/迟到结果和订阅完整矩阵；已有生命周期与部分真实流程 | 主要列为集成/实机缺证，不能用新 LOCAL 代码或受控回放填补真实 Completed |
| B02 | 已支持布局、root、publication 已有实现；完整异常/长期 GC/heap/GPU 尚缺 | 仍需具体路径逐一审计，不把 root 记录归零当 heap 归零；不重复本轮 LOCAL 责任工作 |
| B03 | 当前唯一整项已验收 | 新可选接口仍须保持旧 ABI 并重跑兼容回归，不改既有勾选 |
| B04/B05/B06 | 包依赖/升级、作用域/调度、能力故障撤回恢复仍缺完整矩阵 | 可继续增加离线组合故障测试；本次未核出比上述三条更明确的新接口空缺，不宣称仅剩实机 |
| B07/B08 | Hook 修改通路与仲裁连接缺失；现有修改协调器不等于所有玩法操作已接通 | 纳入 F-01；现有颜色仲裁的真实交错验证仍单列 |
| B10 | 当前局结果/设置路径已有实现，实际字段语义和流程待验 | 保留数据语义/集成缺证，不以最佳成绩推断单局 |
| B11 | 速度/定位事务及其参数表达缺失；时钟映射、判定来源另缺真实证据 | 纳入 F-03；未把已有 Pause/Resume/Retry/Skip 全部写成未实现 |
| B12/B13 | 通用目标/资源框架存在；角色接线与自动重绑边界、完整实际恢复证据仍缺 | 角色连接纳入 F-02；任意数组重排的严格公平性另列，不用稳定顺序回归冒充 |
| B14 | 已有 Rebind、焦点栈、键鼠/手柄声明和菜单导航；双包/实际设备矩阵未完 | 属已有实现与硬件/集成证据混合，不能因没有手柄实测就说缺全部输入实现 |
| B15 | LOCAL 以外还有限制，但本次按任务排除当前 LOCAL 主线 | 本报告不改变 RequestPlay 和其他生产准入状态 |
| B16/B17 | 存储/语言/设置租约及跨插件服务已有实现，故障/交错/升级完整矩阵未完 | 可离线补矩阵，仍须保留真实双包与用户外部写入证据边界 |
| B18 | 采集/比较/阈值 schema 与拒绝分支已实现；真实四条件、阈值校准、20 轮和 CI 收口未完 | 不选“整套性能工具缺失”。接 CI 可离线做，但没有真实校准输入时必须 NotEligible，不能制造发布 Pass |
| B19 | 可复现构建/归属 hash/恢复已有分包证据；完整发布/新环境/真实故障矩阵未完 | 待统一证据门槛与新环境/实际流程补证；本次不以当前机器一次构建冒充完整分发验收 |

## Evidence → Finding → Path

以下 SHA-256 是本次读取时的源码/文档状态，后续构建或更新文档不会让旧 hash 自动代表新状态。证据文件不复制原生样本、不含账号或存档。`source_type=file`，`observed_at=2026-10-23`，`artifact_path=n/a`（引用仓库原文件），`supersedes=none`。

| Evidence | source_ref 与关键观察 | content_hash / SHA-256 | linked_workitem |
|---|---|---|---|
| E-01a | `.docs/foundation-expansion/SPEC.md`，B07:60、B08:70、B11:94、B13:112、B18:152 | `46844ea123bf9c31764a1f829c459f81a015b31099e22cf44353344b82cc4665` | F-01/F-02/F-03 |
| E-01b | `docs/IMPLEMENTATION-STATUS.md`，19 项状态保留实现与实机边界 | `3fd87a7ec7f7211d1704a1cfb2acf83a401a0039aebb08b25a11e2560f8ff44f` | F-01/F-02/F-03 |
| E-01c | `.docs/CURRENT-CHECKPOINT.md`，完整 B01–B19 未缩小 | `e8317bdfaa950675216bdbf5d44f332dd10eee7561f8a76bb8c6a186dea10238` | F-01/F-02/F-03 |
| E-02a | `src/RizToolbox.Sdk/HookContracts.cs:12`，只读订阅及无类型修改请求 | `d6a0a42d76421cc45ff212a144ae3d3e80963ac7b2fb619873f3e80e1e866671` | F-01 |
| E-02b | `src/RizToolbox.Core/Hooks/HookDispatcher.cs:109`，修改请求无条件拒绝 | `f66d1b95a27299d2cc4840366d5f783b8c150c8bcbe793f128c897265f7dc090` | F-01 |
| E-02c | `src/RizToolbox.Adapter/Modules/ExactGameplayHookModule.cs:48`，内建目标；:70 仅发布 Observe | `b891120d62337b33adefb65d7d7efe0f5ac912d8aa8bfbf7d4183db36f639d73` | F-01 |
| E-02d | `src/RizToolbox.Core/Mutation/MutationCoordinator.cs`，已有通道/策略/代次验证可复用 | `013e659f000263c95a8ee240159aca7ad469a6afac3295157852c9b1807991a5` | F-01/F-03 |
| E-03a | `src/RizToolbox.Adapter/Visual/VisualModule.cs:20`，仅 Image/Text/Graphic 接收者选择 | `8ac78bf002056dd931b7253db6f70c7624e4bd5b98cf16aec8968f5de8979232` | F-02 |
| E-03b | `src/RizToolbox.Adapter/Visual/NativeVisualBackend.cs`，UGUI 与有限 TMP 槽的实际发现/读写链 | `d3e5eb50336faff40d82f534df70088cc4bd22e0614202c40e0264f637ceab1a` | F-02 |
| E-03c | `docs/Visual-resources.md:11,93,112,136`，角色/自动重绑/相机后处理的明确边界 | `e1b0d88c80c75427b52141887c9d184fadf93f49a41746347d4823854e168824` | F-02 |
| E-04a | `src/RizToolbox.Sdk/GameplayContracts.cs:17,34`，枚举有速度/定位，Execute 无数值参数 | `8f280915473ef4482a057675163b1f65b091f683518164a02434e5efbabc5d9b` | F-03 |
| E-04b | `src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs:80,215`，两操作明确不支持，Dispatch 仍只有枚举 | `ed43ffd30ec835136fb6f0266433e4f7096d8c8c6d261c718797bc679ed3b366` | F-03 |
| E-04c | `src/RizToolbox.Adapter/Gameplay/GameplayService.cs:116`，已有租约/代次/前置检查/审计，派发无参数 | `3a244f632fbf910e6eda56cecc0762a51831ad8b09c9b9ac8ebbc5343de44b53` | F-03 |
| E-05 | `docs/Performance.md:5`，已有工具但无真实四基线/校准/20轮 | `d126599a2754d85983b47106f2b70eb9e01d88687650a4f31d8e7fd7492e30c1` | 筛选边界 |

Finding 的 `severity=n/a_re`、`category=design`、`status=validated`、`confidence=high`；仅表示已核实源码缺口，不表示完成原生验收。F-01 的 location 是 Hook SDK/dispatcher/module，F-02 是 Visual module/native target discovery，F-03 是 Gameplay SDK/dispatcher/native probe。

### P-01：从实际缺口到可离线交付

`path_type=callflow`。起点是当前公开请求及生产适配链，目标是补齐可执行的底层路径且不提前开放未经验证的原生操作。

1. 按 E-01a/E-01b/E-01c 对齐 SPEC 与当前状态，确定 F-01/F-02/F-03 各自缺失的调用连接。
2. 复用 E-02d 的现有所有权/仲裁基础；分别补可选 Hook 策略、角色适配或带参数玩法事务，保持 B03 ABI。
3. 通过生产算法 seam 运行已列拒绝、并发/重入、代次和 unknown 清理矩阵；仍不得把 mock 或测试数量写成游戏能力已验。
4. 每个实际目标补精确静态依据与受控原生接线，再按其独立实机矩阵升级验证等级。没有该证据，继续拒绝真实写操作。

`residual_risks`：本次未验证新增目标的 ABI、对象完整图、时间单位、可见渲染或真实原生消费者结束；离线成功不能消除这些条件。B18 的真实校准/压力与 B19 的干净环境/实际故障验收也不因上述实现推进而完成。

## 第三方只读复核

以下命令从仓库根执行，只读文件，不构建或调用游戏：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath ./.docs/foundation-expansion/SPEC.md,./docs/IMPLEMENTATION-STATUS.md,./.docs/CURRENT-CHECKPOINT.md
rg -n 'RequestMutation|Declare<|RegisterScoped<IHookService>' ./src/RizToolbox.Sdk/HookContracts.cs ./src/RizToolbox.Core/Hooks/HookDispatcher.cs ./src/RizToolbox.Adapter/Modules/ExactGameplayHookModule.cs
rg -n 'var component=|TMP_Text|LoadFont|Semantic|Camera|post' ./src/RizToolbox.Adapter/Visual
rg -n 'SetSpeed|Seek|Dispatch\(GameplayControlAction|Execute\(GameplayControlAction|SecondsMappingVerified' ./src/RizToolbox.Sdk/GameplayContracts.cs ./src/RizToolbox.Adapter/Gameplay
rg -n 'uncalibrated|calibration|ReleaseGate|20 轮' ./docs/Performance.md ./scripts/Compare-FoundationPerformance.ps1
```

本次时间线：读取 SPEC/状态/检查点 → 核对 Hook、Visual、Gameplay 的公开与生产链 → 对照性能工具排除“整套未实现”的错误分类 → 保存本报告。没有修改三份状态文件。
