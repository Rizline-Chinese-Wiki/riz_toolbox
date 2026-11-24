# 固定publisher与真实早期宿主集成范围

2026-10-25。沿用用户对兼容底层实现、多代理协作和“先继续代码，实测稍后”的授权。本批只新增自有EarlyFixed宿主、proxy/recorder、实际非NOP importer/host、debugger适配、父runner和离线验证器，将前批分开的固定publisher与early-loader机制实际接通；root唯一构建和运行。旧12项组件输入、576项早期里程碑源码及其manifest/ZIP/证据保持冻结，不在builder执行期间漂移。

普通静态依赖为自有Host→RizFixedOwnedTargets.dll→OwnedProxy→固定publisher/helper/recorder，无Dobby。固定publisher没有entry/TLS，不伪造其初始化返回；helper实际初始化与proxy进入/Prepare/4Configure/Bind/4Commit/Ready的先后由独立debugger补充样本观察，主模式仍无调试器。Importer真实TLS/C/C++/DllMain首调用必须位于实际全部发布之后，控制日志不能替代真实执行历史。

新version2共享协议追加实际GetInfo和页快照，独立父验证按具体Header解析PrepareGroup、四个helper Configure、BindGroup和四个Commit，不复用Dobby四Prepare事件假装同一合同。故障须保存真实已写byte、实际页保护、永久准备/绑定和Unknown责任；共享页影响按本机pageSize核，不仅相信receipt。forced API selector明确是注入，不能冒充OS真实失败。超时只终止父进程亲自启动的那个owned child；编译、schema或证据缺失均不能算passed。

mode14 BaselineOnly是单独未发布的自有进程，使用相同Targets binary与RVA，经历真实初始化后运行原SEH/C++样本；零Prepare/Configure/Bind/Commit/Ready，不Attach观察器。父先运行它，再按kind/异常类别将正常、worker、debugger样本的原始code/flags/参数数量/payload/kind逐项比较，不mask差异，不用direct body或twin入口伪造同入口基线。该对比是同binary、同系统、不同child；callId、异常对象/模块地址按本child实际关系核，不称同对象/同run完全等价。

mode15是受控Capture重入拒绝：外层实际持有原子gate时递归调用同一Capture，内层实际触发Busy返回2；外层、之后独立Capture和拒绝前最终Capture都保留永久Busy并失败，原生立即保留Unknown，在Proxy/Importer执行前终止。该样本有明确注入selector，不等于自然跨线程竞争实测；父验证真实返回、调用计数、flags7和零写责任，不能用仅修改flags代替执行。

本批不启动游戏、Steam、QQ或其他UI，不修改游戏文件、不安装、不操作既有他人进程、不commit/push。真实系统WinHTTP命名/转发与依赖路径、固定游戏V4、全部helper/OS传递loader安全、任意PC异常或完整flags透明、scheduler/async/futureStart闭包、EngineCancelled、Pending清理和根退休均未自动证明。完整B01–B19继续，实际结果以新冻结输入、真实终态和原件逐项验证为准。

既有依据：[早期自有宿主报告](../../2026-10-25_底层-自有早期加载验证-report.md)、[固定发布组件报告](../../2026-10-25_底层-固定发布组件验证-report.md)。两份历史通过结果不能拼成本批集成已成功；本页只限定实施与证据边界。
