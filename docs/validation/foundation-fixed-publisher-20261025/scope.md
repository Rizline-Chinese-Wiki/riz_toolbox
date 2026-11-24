# 固定四入口最小 publisher 实施范围

2026-10-25。沿用用户的底层架构实现、多代理协作及“先继续代码，实测稍后”授权。本批仅新增自有固定 publisher、四个真实非 NOP 目标与静态 original stub、自有测试 host 和独立离线验证器。root 是唯一构建与执行者；不改前批576项冻结源码、ZIP、manifest或证据。构建集成若需要改现有入口，须由root另行明确实施并独立冻结，不能中途漂移。

初始版本是单进程自有组件验证，尚未接入早期 loader fixture，不声称首次发布先于TLS/CRT/worker。生产UnityPlayer、真实系统WinHTTP、游戏/Steam/QQ或其他UI均不启动、不操作、不修改，不安装新包，不操作现有他人进程，不commit/push。真实游戏V4、gameReady、scheduler/async/future producer封口、EngineCancelled、Pending清理与根退休均不由本批授予。

验证目标是固定容量POD描述和永久责任槽，在任何目标source写前完成四项Prepare和Bind，校验精确实际非NOP prefix、固定original stub与所有显式修正；不使用Dobby、动态CRT、堆、logger、迟到loader或泛用虚路由。独立验证器读取实际PE普通/延迟导入、代码/重定位/异常目录、真实receipt及事件原件；没有对应imports只是必要证据，不能单独证明没有静态链接CRT、间接调用或全部OS内部依赖。

真实写入合同分别记录保护修改、实际byte写入/部分写、保护恢复、指令缓存刷新及回读；首写尝试后故障保持StickyUnknown，并保留尚未恢复的保护/bytes责任，禁止重试、假回滚、卸载或继续授Ready。内部API的可控失败与实际API失败必须分列；调用前注入不能伪称OS API内部失败或部分写。编译失败、证据不足、未知事件、超时、溢出、指令/函数表不匹配不得当作通过。

RtlVirtualUnwind检查必须使用实际PE/RUNTIME_FUNCTION/UNWIND_INFO，在每个已声明指令边界保留控制PC、输入/输出CONTEXT、受控栈地址和值、返回PC与独立非易失寄存器哨兵。构造CONTEXT展开证明和真实正常/worker/nested/SEH/C++执行证据分别列出：前者不能伪称每个PC真实执行或证明任意异步异常/flags透明。非NOP/RIP相对重放、continuation状态、原调用恰好一次、完整Execute EAX和Release副作用须独立观察。

本页只限定实施和证据要求，不预报结果。receipt/event/unwind具体结构由实际新Header和host落盘后对齐，不先创造平行schema进行自证。前批依据见 [Dobby有限loader审查](../foundation-early-loader-20261025/dobby-loader-audit.md)及[自有早期加载报告](../../2026-10-25_底层-自有早期加载验证-report.md)；其原件保持原样，完整B01–B19继续逐项验收。
