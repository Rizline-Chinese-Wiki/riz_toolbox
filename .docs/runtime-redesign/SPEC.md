---
plan: runtime-redesign
version: 1.0
created: 2026-11-17
status: active
---

# Spec: runtime-redesign

本文全部是待实现行为。现有错误类型复用 `OperationError/ErrorCode`；没有 HTTP 服务，所有错误的 HTTP 状态均为 N/A（进程内 SDK）。计划中的接口名称不代表当前 SDK 已提供。重型检查移出热路径意味着保障范围变化，不能将新行为标作通过了原来的逐调用完整证明合同。

## 运行模式

### runtime-modes

IF 启动配置选择 Standard 或未指定模式，使用普通运行日志与按需模块计划，完整映像/全部原生发布图审计只在启动安装、明确安装变更或显式诊断时执行。
ELIF 启动配置选择 Developer，允许显式开启完整审计、proof 插件、详细采样与后台请求工具；状态报告哪些诊断实际开启。
ELSE 配置错误，进入安全的普通观察配置；有写入风险的可选功能保持不可用并显示原因。

模式在启动时确定；不在已有活动事务中切换检查合同。Standard 不承诺实时检测任意第三方内存改写。两个模式都保留实际安装所有权、正确 ABI、原跳板、线程、原生通道 epoch/known/in-flight/Unknown 与挂起责任。

#### Error handling

- `InvalidArgument` → HTTP N/A；消息：“运行模式无效；已关闭可选写入功能。”
- 安装指纹/发布证明失败 → `CapabilityUnavailable`，HTTP N/A；消息：“当前游戏版本或安装状态不受支持。”原生未完成发布不伪装可用。

#### Tests

- [ ] Standard 的普通 accepted host tick 不进入完整发布图/整节审计；启动仍实际执行完整安装验证。
- [ ] Developer 显式审计能发现其声明覆盖的真实篡改，且不会将该结果借给后续独立操作。
- [ ] 非法模式、安装失败均有明确原因，不能启用写入能力；状态报告实际模式和保障边界。

## 主线程执行

### tick-admission

IF 当前原生回调不是匹配的 Tick 或正在递归执行宿主，不派发宿主工作。
ELIF 回调线程、登记通道、epoch、known/in-flight 或 sticky Unknown 不满足入口合同，停止该桥的新派发并保留实际退出责任。
ELSE 分配单调 `HostTickSequence`，顺序执行入口检查、命令前 boundary drain、命令、命令后 boundary drain、采集、维护、发布。

未证实 Unity frame ID 前，所有计数使用 host tick，不声称每个 Unity 渲染帧恰好进入一次。两次 drain 分别只处理新增 sequence；命令可能产生 boundary，因此不能直接删除命令后的 drain。

#### Error handling

- 通道/代次失效 → `TargetExpired`，HTTP N/A；消息：“运行入口已失效；本次未派发。”
- 错线程 → `WrongThread`，HTTP N/A；消息：“该操作需要游戏主线程。”

#### Tests

- [ ] 不匹配与递归回调不执行 host；合法完成回调恰按登记路径派发，原函数仍执行一次。
- [ ] 错线程、真实 native Unknown、epoch 变化、异常原返回不被视为正常 Tick。
- [ ] 命令产生的新 boundary 在同 tick 采集前处理；旧 sequence 不重复发布。

### shared-observation

IF 本 tick 已发布该能力的观察，GetState/订阅分发复用同一不可变 revision，不重新扫描游戏对象。
ELIF 能力有需求且观察到期或绑定变脏，在指定采集阶段更新一次；首次订阅先取得已有快照或明确的未就绪状态。
ELSE 返回带可用性、观察时间和原因的状态，不隐式执行控制、创建 writer 或推进重型准备。

观察的 revision 不授予写权限。已知边界撤销相关观察；写入必须另取当前真实会话和操作资格。背景线程仅处理已复制 DTO，不访问 Unity/IL2CPP 对象。

#### Error handling

- 无当前观察 → `NotReady`，HTTP N/A；消息：“当前场景尚未提供此数据。”
- 快照来源已撤销 → `TargetExpired`，HTTP N/A；消息：“此状态来自已结束的会话。”

#### Tests

- [ ] 多插件和订阅回调内递归 GetState 不增加同 tick 的 native capture 次数。
- [ ] 状态/能力查询不调用 DSP、完整安装审计、writer 或 Seek 准备。
- [ ] 场景/局/源切换后旧观察标为失效；不能用它授权原生写入。

## 模块与生命周期

### demand-and-scene

IF 一个已发现、有效且 desired-enabled 的插件清单或其硬依赖请求能力，先记录需求再判断 capability 是否 Available；Waiting 插件仍保留需求。
ELIF 用户显式启用某可选功能或诊断 profile，也将其加入需求计划。
ELSE 模块保持 Dormant，不创建可选 Canvas、不扫描/保留音源或相机、不启动功能专用 gates。

有需求且场景符合时沿现有模块依赖闭包激活；可选需求使用独立声明/需求句柄，不能把所有实验能力塞进面板的 RequiredCapabilities。最小场景与生命周期观察者保持活动以唤醒服务。GetService 查询只读当前状态，不同步触发任意重型初始化。

需要首次执行前安装的原生入口按启动安装计划提前准备；业务服务按需激活，不把晚装 Hook 假装等价于早装。第一批先保留现有原生安装链，后续按实际依赖削减。

#### Error handling

- 未请求/不支持 → `CapabilityUnavailable` 或 `OperationUnsupported`，HTTP N/A；消息：“此功能未启用或不适用于当前场景。”
- 需求依赖环/清单冲突 → 复用现有依赖/冲突错误，HTTP N/A；消息包含具体插件和能力原因。

#### Tests

- [ ] Waiting 插件能够先产生需求、激活其模块并在能力就绪后 Enable，不形成相互等待。
- [ ] 空 mods 的 Standard 不建立可选游戏资源；最小观察和明确列出的早装入口仍工作。
- [ ] 场景变化唤醒正确模块，互不相关模块保持 Dormant；可选能力失效不拖停普通面板。
- [ ] 早装职责和按需服务分开验证，不能用晚装成功替代首次执行覆盖。

### retirement-and-faults

IF 最后消费者、当前场景或当前源结束，先撤回新请求和旧服务视图，再执行现有 scope 清理。
ELIF 尚有 native 调用、writer、getter、初始化或退出责任，保留它们的真实 owner/roots 并由具名 retirement 阶段继续维护。
ELSE 释放已证明结束的资源；插件 DLL 更新继续要求重启，不承诺即时卸载任意引用。

普通 managed 模块异常撤回该模块及其实际依赖者，继续独立阶段。native 内存破坏、进程崩溃及不返回的同步调用不属于可隔离故障；时间预算不能抢占它们，也不能宣称回滚已完成的回调。

#### Error handling

- 已结束作用域/会话 → `ScopeExpired` 或 `TargetExpired`，HTTP N/A；消息：“此插件资源或游戏会话已经结束。”
- 释放未完成 → 返回具名 retained/restart-required 诊断，HTTP N/A；消息：“仍有原生操作尚未退出，已停止接受新请求。”

#### Tests

- [ ] 停用后旧句柄/回调不能修改游戏；主题恢复仍遵守原优先级和基线规则。
- [ ] 最后需求归零时在途 roots 不提前释放；真实退出后恰好释放，不把 Unknown 当作完成。
- [ ] 单个 managed 阶段/清理/日志故障不会跳过独立阶段；不宣称能隔离 native 崩溃或同步死循环。

## 播放控制

### playback-operation

IF 提交请求不属于当前插件作用域、当前 run/source 或合法值域，进入原生写入前拒绝。
ELIF 同一播放会话已有冲突命令，返回 Conflict；排队请求尚未执行时可以取消，执行后的未知结果不自动重发。
ELSE 由该会话唯一的操作入口绑定真实根与 tickets，执行准备、写前检查、实际原生操作、读回、时钟发布和确认；嵌套包装只检查本地登记身份，不递归 Probe 整个 provider。

每次操作保持线程、注册身份、run/scene/service/source 代次、对象边和活动状态、Clock publication、writer/source/scheduling fences、值域与真实 reservation 检查。getter 重入、局/源切换和释放会撤销后续旧对象写入；恢复只针对仍属于自己的写入。失败不能从最终标量相同推定原生操作未发生。

安装图的全量审计由 runtime-modes 规定。新增窄的安装状态/通道/操作检查接口；不能仅删旧 VerifyCurrent、缓存 validated=true，或将旧 SourceVerificationScope 跨多个操作使用。

命令状态明确为 Queued、Executing、Succeeded、Rejected、Cancelled、Unknown；查询它们只读取记录。若准备必须跨 tick，分阶段保留取消/失效责任，提交前重新检查即时资格。不能将 180 秒外部等待期限当成原生调用已取消。

#### Error handling

- 无准备条件 → `NotReady`，HTTP N/A；消息：“当前歌曲尚不满足该操作的条件。”
- 失效/冲突 → `TargetExpired` / `Conflict`，HTTP N/A；消息：“会话已变化或另一项播放操作正在进行。”
- 值域非法 → `InvalidArgument`，HTTP N/A；消息：“倍率或时间参数超出支持范围。”
- 已开始操作但无法确认/恢复 → Unknown 终态并携带现有错误，HTTP N/A；消息：“执行结果无法确认，已关闭相关控制；请勿重复提交。”

#### Tests

- [ ] 当前 paused 源经实际 writer 写入及读回，倍率与唯一 Clock 同步；普通状态读取不重新准入。
- [ ] 旧作用域/旧 run/旧 source/错误线程/非法值与并发请求在写前拒绝。
- [ ] getter 内切换、同值外写、ABA、写后异常、Clock 发布失败、恢复失败保持实际责任，旧对象不再写入。
- [ ] 排队取消与 executing/Unknown 区分；超时无自动重试，无伪造成功或成功恢复。
- [ ] 当前包真实暂停、倍率、恢复和跨局验收；不以 owned fixture 或旧 Ready 结果代替。

## 诊断与交付

### diagnostics-and-budget

IF 运行 Standard，输出必要的状态变化与限流错误；后台持久化只接收不可变副本，过时状态合并丢弃。
ELIF 显式请求 Developer 诊断，允许完整状态、包指纹、proof/采样和完整审计，并计入其真实开销。
ELSE 诊断故障只报告诊断不可用，不能再次读取已释放游戏对象或阻断正常阶段。

持续统计分别覆盖 entry admission、host 各阶段、插件回调和实际控制。不得把 host 内计时解释为包括进入 Host 前完整验证的总成本。预算仅决定后续工作是否派发，不能中断正在执行的同步调用。全局 FindObjectsOfTypeAll 即使返回后只处理 N 项，发现本身也不能宣称已限时。

初始实机目标为 Standard 空 mods 稳态（固定可玩场景、预热后至少 60 秒）的框架托管总成本 p95≤1ms、p99≤2ms；入口与宿主均纳入，排除游戏原函数但另记录帧间隔。基础功能场景不接受可归属框架的单次≥50ms稳态阻塞。它们是设计目标，不是当前结果；转场、扫描、上传和控制分别记录最大耗时与恢复情况，不能藏进稳态分位数。

#### Error handling

- 诊断请求错误/过量 → `InvalidArgument` / 现有配额错误，HTTP N/A；消息：“诊断请求无效或超过限制。”
- 写盘失败 → 记录限流警告，HTTP N/A；消息：“无法保存诊断；游戏功能继续运行。”

#### Tests

- [ ] Standard 不定时全包哈希、不输出逐对象 Debug/proof、不在主线程序列化完整诊断。
- [ ] Developer 实际执行所声明的诊断；同时报告诊断模式的成本，不能借 Standard 数字掩盖。
- [ ] 总计时包含入口；文件阻塞/慢消费者下后台队列有界并合并旧快照。
- [ ] 同机原版与当前唯一候选按相同场景测量，逐项报告目标是否达到及尚未覆盖内容。

### deliverable-scenarios

普通包仅含基础运行时、适配配置和用户主动选择的插件；Author 包另含 SDK、模板和示例；Validation 包另含 proof、fixture 与采证脚本。迁移不删除用户已有插件、设置、日志或研究证据。

IF 普通包通过歌曲/成绩面板、颜色主题启停恢复、快捷键面板与自有 WAV 三个当前包实机场景，并通过安装/重启/故障恢复与成本检查，标记为基础预览可交付。
ELSE 保留 preview/unverified 状态，逐项列出失败；不沿用旧包或测试数量签收。

倍率与任意跳转另列实验状态。基础预览交付不意味着完整 B01–B19、任意 Seek 或 production admission 已完成。

#### Error handling

- 当前包/来源不一致 → 验收失败，HTTP N/A；消息：“验证记录与当前安装包不一致。”
- 某可选能力不可用 → 返回对应原因，HTTP N/A；消息：“此实验功能尚未通过当前版本实测。”

#### Tests

- [ ] 同字节当前包完成三个实机场景，停用恢复、重启配置和插件共存可观察。
- [ ] 验包及安装归属校验有效，原用户文件和旧未知结果不被覆盖成成功。
- [ ] Runtime/Author/Validation 边界与实际内容一致；未通过项保持未完成。
