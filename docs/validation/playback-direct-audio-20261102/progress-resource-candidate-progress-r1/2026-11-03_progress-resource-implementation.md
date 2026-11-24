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

私有标记256只说明一个实际调用拥有 journal 完成责任。它不授予 Fresh、当前音源、Clock、SDK 或生产权限。返回时必须属于已登记的实际 Before；物理完成仍由原生 journal 证明。worker／class／SEH 的 Unknown 规则继续有效。

## 最新验证范围

- `functional-matrix-r2/`：最新源码 fresh build 已 exit0；首捕获 late 28、同局 handoff late 31 实际通过，包含最终 Clock、永久拒绝旧票和晚到旧清理。完整12模式的终态以其 `test-result.json` 为准，不能把正在运行的 SDK 模式写成通过。
- `symbols-functional-r2/test-result.json`：5对实际 PE/PDB、522份源码文档与913冻结输入匹配；只签构建来源，不代替行为验证。
- 原生 r6 使用 helper SHA `ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c`；原46模式、额外 journal 资格对照和修正后的 host 对照分别保留其真实结果，不相加失败检查作为全过。
- `foundation-r1/`：与最新 r2 相同的484托管产品输入，core1039通过、全树前16套699通过，随后GameCamera32／33实际 exit1；不能报告整树通过。只更新这一元数据负例测试后，整树28套／solution／独立SDK／六Release须另以 `foundation-r2` 完成新验证。功能组合 r2 没有编译该相机测试文件，其484产品和522份实际PDB文档保持独立对应。
- `original-eight-r1/`：实际使用新r6 helper执行14个原生ABI／异常展开回归，36675检查全部 exit0；复用的是自有测试fixture，不复用旧helper测试结果。
- 固定853d映像只做文件检查／`DONT_RESOLVE_DLL_REFERENCES` 映射。旧 `metadata-r2` 的149和正常联合41只签旧 freeze。最新 `mapped-r3` 已fresh build0／零警告错误，metadata149／149实际通过；normal、pitch-tail、body在运行，consumer排队，尚未签joint全部完成。

## 关机中断与续跑

2026-11-03 00:23:43 系统触发低电量事件524，00:24发起关机并停止事件日志，07:33:22.5重新启动。原件见 `shutdown-audit-r2.json`。原功能矩阵中的 `0x40010004`／`0xC0000142` 保留为实际退出记录；它们与关机时点关联，不能伪装为成功或当作已确认的代码回归。

确认原进程不存在且编译源／产品／native输入未变后，`functional-continuation-r2` 仅补原来未完成的九项。该续跑实证了 F-06，因此又冻结并构建 `functional-matrix-r2` 验证修正后的源码。旧三项通过、系统中断、真正代码失败和最新修复结果各保留原件。

T 的 SDK 原六模式339检查已完成，其余12项由 T 的新 `formal-continuation-r3` 补跑；两轮来源合并后才可封 T closure。U 合主和本批验收继续等待 T closure 及 U 自己所需验证，不以旧包代签新源。

完整 generation、未知 lower writer、生产首次准入、任意 Seek、effect 以及 B01–B19 仍未因此完成。未安装或启动真实游戏，未调用游戏方法／DllMain，未操作游戏 UI 或账号。
