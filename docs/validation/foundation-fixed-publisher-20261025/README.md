# 固定四入口发布组件：独立验证完成

2026-10-25。**最小发布组件已实现并通过独立自有验证，尚未接入早期加载或游戏。** 它用固定容量记录和静态原函数桩替代这条路径的Dobby动态路由、堆和CRT日志。当前仅允许精确命名和标记的自有目标DLL；不是任意地址patch接口，也不签发游戏V4。

本批与[上批早期加载宿主](../foundation-early-loader-20261025/README.md)分列：上批完整构建99557为25套1617/1617；本批没有重新运行这些托管套件，实际完成的是session5523的独立组件构建与57个子进程。两者尚未连成同一条生产加载链。

| 已核原件 | 实际结果 | 证据范围 |
| --- | --- | --- |
| [root实际终态](component-run.json)、[构建](build.log)、[矩阵日志](test.log) | session5523 exit0；57/57进程通过 | 先冻结12项声明输入，再独立构建并运行；未安装或执行游戏 |
| [汇总](build-summary.json)、[57完整原件](raw/cases/fixture-result.json) | 2560个host检查；11522条独立receipt断言 | 56故障子进程加1正常进程，均持有输入只读句柄并核前后hash |
| [实际PE](raw/cases/pe-proof.json)、[依赖审查](publisher-dependency-review.md) | Publisher无CRT/Dobby/heap/loader导入；固定Kernel32与原生SEH外部依赖 | 本地调用图及三项已命名helper查询；不证明所有OS/helper传递实现安全 |
| [正常调用](raw/cases/normal/case.calls.jsonl)、[独立调用核对](raw/cases/normal/calls-proof.json) | 28个调用案例，34次原调用、20观察配对、16异常捕获；271父断言 | 8个未修改基线异常与20个发布后案例分列；完整EAX、Start嵌套、worker、一次DEC及ZF、Detach诊断 |
| [正常展开](raw/cases/normal/unwind-proof.json) | 每进程50个控制上下文；合计2850个、25878父断言 | 固定前缀的指令边界、静态桩两条尾指令和目标continuation首PC；不是每PC实际异常执行或全部epilog覆盖 |
| [部分真实写入](raw/cases/fault-11-0/receipt-proof.json)、[保护恢复注入](raw/cases/fault-13-0/receipt-proof.json) | 前者确实只写3字节；后者保留页保护责任 | 每个进程额外保存240字节实际目标页快照，独立核32字节与同页保护影响 |
| [源码](source-archive-check.json)、[二进制](binary-archive-check.json)、[输入前后](component-input-integrity-check.json) | 12源/4二进制逐条hash和ZIP检查通过 | helper复用上批精确DLL，未在本批重新编译；工具链/SDK和完整换机复现另计 |

Prepare核精确模块、磁盘hash、导出、完整非NOP前缀和原函数桩，四项全通过后绑定静态continuation。Bind独立核真实helper ABI2及四项已Configure的target/original/entry/slot/epoch，再允许依次Commit。保护修改、写入尝试、实际字节数、回读、恢复保护和缓存刷新各有事实记录；源写尝试后的故障保持Unknown，保护恢复失败也单独保留责任，永久槽不能复用。

## 测试实际改变了实现

[历史原件](history/observed-runs.json)保留最初链接/编译失败以及两次真实测试失败。第一版静态桩尾部FF25虽被放进prologSize，Windows实际展开仍在Execute、Release、Detach的尾PC错误读取返回地址，46个控制上下文中3个失败；测试在任何目标入口发布前停止。现改成MOV R11读取continuation再JMP R11，50个控制上下文及实际SEH/C++测试通过。它保持RFLAGS并只改动易失寄存器R11，合同是四种精确ABI，不声称完整GPR/RAX或任意异步异常透明。

第二次失败是测试错误假设软件SEH flags为0；实际平台记录0x80。修正方法是先运行四类SEH与四类C++的未修改真实基线，随后逐kind精确比较code、flags、参数和payload；没有屏蔽或忽略任何flags位。该失败也保留，未计入最终通过。

## 仍然缺什么

stage12/13/14在API调用前注入受控失败，不是制造了真实OS API内部失败；ApiCalled和调用次数分别保存。部分写入3字节是真实源写，但不等于穷尽任何时刻的硬件写中断。控制展开不是逐PC执行，普通调用尚无独立八个非易失寄存器实调前后哨兵，全部目标后半/epilog和未知签名不在证明范围。源补丁按字节写入，尚无并发首写或loader阶段准入授权。

下一步把本组件接入早期宿主，证明没有Dobby的实际TLS/CRT前四项Prepare/Bind/Commit、原异常和所有故障终态；再完成游戏模块身份、系统WinHTTP转发及独立V4、生产coordinator/真实对象GC、调度/async/Start内联清理和全部消费者退休。[下一步](next-integration.md)与[正式报告](../../2026-10-25_底层-固定发布组件验证-report.md)详列边界。完整B01–B19仍active，仅B03整项/B18a子项验收；EngineCancelled=false、Pending保留、RequestPlay关闭，游戏实测稍后。
