# 自有早期加载与四入口发布

2026-10-25。**自有加载机制已实现并经过实际执行验证；游戏生产接入仍未完成。** 完整构建session99557实际exit0，25套1617/1617；额外39子进程/7316父断言分列，576声明源码和22包已冻结核对，新包未安装。保留94条去重原始编译诊断。[正式报告](../../2026-10-25_底层-自有早期加载验证-report.md)、[范围](scope.md)、[下一段实现](next-integration.md)。

本批新增普通静态依赖Host→Importer→Proxy→Recorder/helper/固定Dobby。Importer在真实TLS、C和C++初始化器、DllMain阶段执行四种目标，Start同步Execute/Release；先4Prepare、再4不可变Configure、最后4Commit并复核，Ready早于这些首次调用。Observer只在加载返回后Attach，覆盖实际worker、完整EAX与SEH/C++原异常。Recorder不依赖CRT，保留原子定长共享事件和实际tuple，父进程先hash并持续持有只读禁止写删的输入句柄。

| 原件 | 实际结果 | 范围 |
| --- | --- | --- |
| [完整构建](build-run.json)、[日志](build.log)、[汇总](build-summary.json) | 25/1617，session99557 exit0 | 包括此前所有回归及本批宿主；未执行游戏 |
| [39宿主汇总](early-loader-evidence.json)、[完整原件树](native-build/early-loader-cases/fixture-result.json) | 7316父断言；324次原调用、168观察配对、24异常捕获 | 38无debug子进程加1额外debug子进程；计数不能当游戏覆盖率 |
| [normal](native-build/early-loader-cases/mode-0/result.json)、[worker](native-build/early-loader-cases/mode-3/result.json) | 86/92原调用，各56观察配对 | 真实四阶段调用、首次安装顺序、加载后Attach/EAX/原生异常 |
| [额外调试观测](native-build/early-loader-cases/normal-debugger/result.json) | 19个DLL load/TLS/真实entry与return事件 | 仅自有child的临时内存断点；helper/Dobby初始化返回先于Proxy安装，不代表无扰动/并发或生产游戏证明 |
| [迟到安装](native-build/early-loader-cases/mode-2/result.json)、[部分写前故障](native-build/early-loader-cases/fault-5-2/result.json)、[部分写后故障](native-build/early-loader-cases/fault-6-2/result.json) | 已有实际worker时0写拒绝；后两者分别保留2/3次完成写入并进入Unknown | 不放行Importer，不重试/假回滚；已Prepare/Configure的永久责任也保留 |
| [Dobby依赖审查](dobby-loader-audit.md)、[原始JSON](research/dobby-loader-evidence.json) | 固定CRT堆、日志/加载候选、未解vcall被明确记录 | 不能从正常39进程通过推出全部loader路径安全 |
| [源码归档核对](source-archive-check.json)、[包归档核对](package-archive-check.json)、[输入前后](build-input-integrity-check.json) | 576源/22包全部hash匹配 | Python父验证器也显式列入构建输入；不包含完整工具链或干净换机复现 |
| [旧安装](previous-installation-check.json)、[原游戏](original-game-check.json) | 18已装文件/326旧源ZIP/3原游戏文件匹配 | 当前游戏仍旧1021安装 |

wrong-hash/architecture/prefix/ABI分别改变expected值，missing-dependency使用假名查询（固定Dobby仍静态加载），dependency-not-ready是selector拒绝。它们证明受控准入拒绝路径，不能称实际换入坏模块、缺少静态DLL或未初始化helper的矩阵。24故障位于成功API的前后边界，未制造Dobby内部OOM、非零返回或机器码中途写失败。

三代理分别实现Proxy/Recorder及Dobby依赖研究、Importer/Host与有限交叉审查、父验证器与报告；root实现builder/debugger并唯一构建。完整B01–B19仍active，仅B03整项/B18a子项验收。生产V4、真实coordinator/对象/GC、调度/async及Start内联清理、全部writer/future/消费者退休和Retry/Return/Loader仍须实现与验收。EngineCancelled=false、Pending保留、RequestPlay关闭，游戏实测按用户安排暂缓。
