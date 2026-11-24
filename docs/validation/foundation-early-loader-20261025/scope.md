# 自有早期加载与四入口发布验证范围

2026-10-25。沿用用户“先继续代码，实测稍后”和多代理协作授权。本批实现独立自有 Windows x64 宿主、静态 importer/proxy/recorder 依赖及其父进程验证器，推进兼容底层的首次发布时机研究。root 是唯一构建与测试执行者；各代理只在其拥有的新文件中实现。

本批只执行本仓库编译的独立 fixture。普通静态导入链、真实 TLS/C/C++ initializer/DllMain、四个自有目标、真实固定 Dobby Prepare/Configure/Commit、原始返回/异常/嵌套/worker，以及首写前后故障均以实际 PE 和共享事件核对。父进程先核固定二进制 SHA256，持有禁止写入和删除的只读句柄；每个模式独立预创建 named mapping，退出后保存原始字节、事件、元组、日志与输入 hash。编译失败、超时、日志缺失/溢出、字段不一致和未知事件不能当作测试通过。超时仅终止父进程亲自启动的那个 child。

normal 的验收是实际四次发布完成和 Ready 早于 importer 的 TLS、CRT、DllMain 及首次目标调用；写前拒绝必须有零写事实，写尝试后的失败保留 Unknown，不能继续 importer、重试或假回滚。迟到或先前调用模式必须用实际历史证明拒绝，不能用配置标签或子进程自报 passed 代替事件验证。观察者只在 loader 窗口外启用。

不启动或操作 Rizline、Steam、QQ 或其他桌面程序，不修改游戏文件，不安装新包，不操作他人进程，不 commit/push。前批冻结ZIP、manifest和历史证据保持原样；当前工作树可按授权继续演进，本批新增源码并修改Build.ps1接入完整构建，新输入单独冻结归档。新 fixture 不能签发 UnityPlayer V4、生产首次安装凭据或取消完成。真实系统 WinHTTP 代理/转发、游戏 cold-start、完整 loader 依赖安全性、实际 IL2CPP metadata/GC、scheduler/async 来源与消费者闭包、EngineCancelled、Pending 清理及根退休均未由本批自动证明；所有对应能力位保持 false。

上批依据见 [早期宿主验收设计](../foundation-engine-binding-20261025/early-loader-design.md)、[首次安装研究](../foundation-engine-binding-20261025/engine-installation-research.md)与[调度责任合同](../foundation-engine-binding-20261025/scheduler-cleanup-research.md)。本页声明实施边界，不预报运行通过；最终结果以本批实际构建和父验证器证据为准。
