# Progress 资源连续性实现与验证记录

当前候选位于 `source/`，尚未合入桌面主目录。主目录保持 T 的894个冻结输入／482个托管产品输入；U 最新组合冻结为913输入／484托管产品输入。源码、构建和行为证据按各次 freeze 分列，不能相互代验。

## 这次实际补的功能

`ProgressControl.set_Clip(IResourceReference<AudioClip>)` 更换下一次 Start 使用的资源引用。正常主线程仅更换这个引用时，当前 AudioSource 的 clip 和已获得的控制权继续有效；旧的未来捕获票立即失效，同值、空值和 ABA 都不能复活旧票。SDK 已准备的当前音频事务另有实际验证，不根据这一语义直接宣称通过。

未来捕获票由真实 Progress watch、独立保留的 ResourceReference 对象根和原生同锁检查发行。实际 Start 必须重新读取引用、调用其 `get_Resource`，把实际新 AudioClip 传入 `set_clip`；完成票、七根候选捕获和 source-open 再核对同一资源修订，随后才能通过真实 Pause 建立唯一新 Clock。

资源切换可以打断 yield，也可以打断已经准入但尚未 source-open 的候选。后者以独立的首捕获／同局移交重试记录处理：旧票永久过期，新候选按实际初始化身份重新消费，旧七根保留到新 watch 精确替换后再释放。实际 GetTime 借用者尚未退出时，旧根继续等待，不能把等待误判为永久失败。

## 本轮修复的实际问题

| Finding | Evidence | 修正与验证路径 |
|---|---|---|
| F-01 正常撤下物理组后资源根无法退休 | `native-agent-withdraw-repro-r3-r4/` 的六种正常 Withdraw 实际失败；`retry-review-r1/RESOURCE-RETRY-REVIEW.md` 保留修前源码 | 仅在已知状态且物理责任退出后允许退休，Unknown 继续保留；r5原生46模式通过，最新组合另验已知 Withdraw |
| F-02 声明核验命中缓存，漏掉发行后方法移出／重复 | `metadata-r1/metadata.result.json`，146／147，原失败保留 | 精确声明诊断每次重新枚举给定类，核对名称、static、参数、唯一性；`metadata-r2` 149／149通过，只签该 freeze |
| F-03 phase6 资源失效已发生，但托管路由漏收通知 | `late-integration-r2/`：原生 Phase5、资源票 Phase4、Known=true，managed 观察数0、tracker未失效 | 原生仅在真实初始化 journal 分配成功后标记 frame 私有事实256；Bridge 经实际 TLS／CallbackActive／owner／token／整帧核对接收责任，普通 closed gate 无 journal 的调用不登记 |
| F-04 首捕获重试误要求没有旧候选 | 首次只读审查发现旧七根候选使新 admission 身份检查失败 | 只允许精确保留的旧候选作为重试前任；新 watch staged 后再退休，不能接受任意 source |
| F-05 新借用者进入后旧七根未立即释放，被误判为永久失败 | `integration-r3/resource-handoff-late-retry.result.json` | 把精确旧 source 保留到真实 drain，source-open 的原生门仍关闭；最新 late-handoff 验证实际等待／替换／根释放 |
| F-06 先消费票、后读取仍要求未消费的对象图 | `functional-continuation-r2/resource-handoff-late-retry.result.json`，实际 exit1 | 在 phase4 先派生对象图，再调用原生 retry 消费到 phase6；`functional-matrix-r2` 两 late 实际 exit0 |
| F-07 既有相机负例依赖旧缓存命中后的异常阶段 | `foundation-r1/full-r1/GameCamera-tests.log`，32／33，实际 exit1 | 错误参数现在在当前声明枚举时即无匹配；只修精确测试期望并保持错误返回值的独立拒绝用例，不回退产品查找，不重做相机接入；新整树另 freeze 验证 |
| F-08 独立RootFree测试项目漏链接新增共享partial | `foundation-r2/solution-r2/solution-build.log`，CS0103，actual exit1 | 仅在该测试csproj补 `FakeNative.ProgressResource.cs` 的Compile链接；产品484／native48均不变。新r3另验四个受影响行为及solution／SDK／六Release；旧r2的28／2599实际通过和随后solution失败各自保留 |
| F-09 RootFree测试没有按当前合同绑定实际Progress watch | `foundation-r3/root-free-diagnostics-r3/`：两个tracker实为roots4、Before0、Known=true／InFlight0，原断言checks5实际失败 | 测试改用准确resource夹具、真实Router.Attach和实际yield／资源票；按实际三个tracker独立根核7→4与三次释放尝试。无watch的旧profile不能凭地址获得观察，不修改产品复活退休夹具 |
| F-10 异常释放测试的using会提前释放仍有未决责任的夹具内存 | 独审源码 `FakeNative.cs` 的Dispose无条件FreeHGlobal；r4 resource失败分支实际合同要求host4和Unknown责任保留 | 最终测试保留完整Unknown上下文到单次进程结束。r4尚未执行，不伪造其通过／失败；最终另freeze，要求独审和真实五mode，cleanupCompleted=false不得冒充根已释放 |
| F-11 SDK长测在旧prepared仍活时启动下一事务 | `functional-matrix-r2/resource-sdk-current.result.json`，08:41:20 actual1／checks34；Commit+Confirm及资源保持断言已实际通过到33 | Clock保留恢复所需reservation到Dispose；测试原using作用域直到后续SDK操作之后才结束。仅在独立 `sdk-current-dispose-r1` 测试加显式prepared.Dispose，原断言和旧失败保留，产品与正式SDK不变；新单场景还在运行，未签其成功 |

私有标记256只说明一个实际调用拥有 journal 完成责任。它不授予 Fresh、当前音源、Clock、SDK 或生产权限。返回时必须属于已登记的实际 Before；物理完成仍由原生 journal 证明。worker／class／SEH 的 Unknown 规则继续有效。

## 最新验证范围

- `functional-matrix-r2/`：fresh build exit0，11模式237实际通过；首捕获 late 28、同局 handoff late 31 包含最终Clock、永久拒绝旧票和晚到旧清理。完整矩阵已真实终态，末SDK因F-11 actual1／checks34，不是仍运行。独立测试顺序修正的 `sdk-current-dispose-r1` fresh build0、单场景10880运行，尚不签12场景全部完成。
- `symbols-functional-r2/test-result.json`：5对实际 PE/PDB、522份源码文档与913冻结输入匹配；只签构建来源，不代替行为验证。
- 原生 r6 使用 helper SHA `ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c`；原46模式、额外 journal 资格对照和修正后的 host 对照分别保留其真实结果，不相加失败检查作为全过。
- `foundation-r1/`：与最新功能组合相同的484托管产品输入，core1039通过、全树前16套699通过，随后GameCamera32／33实际 exit1，原件保留。只更新这一元数据负例测试后，`foundation-r2` core1039和full28／2599已actual0；后续solution因F-08 actual1，SDK／六Release未执行。新 `foundation-r3` 只补受影响RootFree四模式及剩余构建链，并以精确测试差异证明引用已过整树范围。功能组合 r2 没有编译该相机测试或RootFree项目，其484产品和522份实际PDB文档保持独立对应。
- `original-eight-r1/`：实际使用新r6 helper执行14个原生ABI／异常展开回归，36675检查全部 exit0；复用的是自有测试fixture，不复用旧helper测试结果。
- 固定853d映像只做文件检查／`DONT_RESOLVE_DLL_REFERENCES` 映射。旧 `metadata-r2` 的149和正常联合41只签旧freeze。最新 `mapped-r3` 已fresh build0／零警告错误，metadata149与四joint41／45／45／45合计325实际0全部完成；913冻结源／14产物／4输入及三最终hash guards通过，最终独审 `final-audit-r4/CLOSE-EVIDENCE.json` 已封。未执行游戏方法或DllMain。

2026-11-03 08:58追加范围：最终RootFree测试修正后五模式113已实际通过，来源为r5原四80＋r7新resource33，未知resource仍保留host4／未决责任；r7 rootfree5PE/PDB497通过，fresh solution0错误／56警告。r6外部profile路径误替、r7初符号路径读取、第一remaining续跑变量未定义的编排实际1均保留，已成功子命令不重跑；SDK分发14／六Release及其符号尚待最小remaining续跑，不冒整链完成。旧full28／2599按相同484产品及精确tests-only差异单列继承。

## 关机中断与续跑

2026-11-03 00:23:43 系统触发低电量事件524，00:24发起关机并停止事件日志，07:33:22.5重新启动。原件见 `shutdown-audit-r2.json`。原功能矩阵中的 `0x40010004`／`0xC0000142` 保留为实际退出记录；它们与关机时点关联，不能伪装为成功或当作已确认的代码回归。

确认原进程不存在且编译源／产品／native输入未变后，`functional-continuation-r2` 仅补原来未完成的九项。该续跑实证了 F-06，因此又冻结并构建 `functional-matrix-r2` 验证修正后的源码。旧三项通过、系统中断、真正代码失败和最新修复结果各保留原件。

T 的 SDK 原六模式339检查已完成，其余12项由 T 的新 `formal-continuation-r3` 补跑；两轮来源合并后才可封 T closure。U 合主和本批验收继续等待 T closure 及 U 自己所需验证，不以旧包代签新源。

完整 generation、未知 lower writer、生产首次准入、任意 Seek、effect 以及 B01–B19 仍未因此完成。未安装或启动真实游戏，未调用游戏方法／DllMain，未操作游戏 UI 或账号。
