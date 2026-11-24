# 固定发布器早期加载链

2026-10-25。**固定发布器已接入自有早期加载链，73 个独立子进程实际通过。** root 唯一构建，session34351 实际终态 exit0；14 项声明输入与 7 个二进制已冻结归档。此次完成的是原生机制的同链验证；游戏生产接入仍待完成，完整 B01–B19 保持 active。

主程序通过普通静态导入加载 Targets，再进入 Proxy；Proxy 使用 Recorder、精确 helper 和固定 Publisher。它依次完成全 Prepare、四项 Configure、Bind 和四项 Commit，之后 Targets 才执行真实 TLS、C/C++ 初始化器及 DllMain 中的四类调用。这条链没有导入 Dobby。Observer 在加载返回后 Attach，测试真实嵌套、worker、完整 Execute EAX、Release 的一次 DEC/ZF 和原生异常。

| 原件 | 实际结果与范围 |
| --- | --- |
| [实际终态](component-run.json)、[构建日志](build.log)、[测试日志](test.log)、[汇总](build-summary.json) | 73/73，13031 条独立父断言；16 模式、56 内部故障和 1 个额外调试子进程 |
| [正常进程](raw/cases/mode-0/result.json)、[worker](raw/cases/mode-3/result.json) | 分别 86/92 次原调用，各 56 观察配对；Ready sequence38 早于首次初始化 sequence39 |
| [未发布基线](raw/cases/mode-14/result.json) | 相同 Targets 二进制与 RVA 的独立进程：68 次原调用、8 次异常，零发布和观察器；SEH raw flags0x80、C++0x81 与已发布样本逐类精确匹配，不屏蔽 flags |
| [Busy 原件](raw/cases/mode-15/result.json) | 真实受控递归 Capture 返回2；外层及后续返回1，最终4次捕获、永久 Busy7、Unknown/failure22；未进入 Proxy/Importer 初始化，无源写入 |
| [真实部分写](raw/cases/fault-11-0/result.json)、[恢复失败注入](raw/cases/fault-13-0/result.json) | 前者实际写1字节；每个进程保存真实32字节与页面保护，按共享页影响核责任；stage12/13/14 是受控 API 结果注入，不能称 OS 内部故障 |
| [调试补充](raw/cases/normal-debugger/result.json)、[实际调试行](raw/cases/normal-debugger/debug-events.jsonl) | 17 条实际 load/TLS/entry/return 观测；helper 初始化返回早于 Proxy；无入口/TLS 的 Publisher 只有 load 事件。临时内存断点仅用于自有 child |
| [完整原件](raw/cases/fixture-result.json)、[独立复核](review.md)、[原生依赖审查](native-dependency-review.md) | 392 次原调用、168 配对、32 次异常捕获，含未发布基线；这些数字不代表游戏覆盖率 |
| [源码归档](source-archive-check.json)、[二进制归档](binary-archive-check.json)、[输入核对](component-input-integrity-check.json) | 14/7 逐项与 ZIP hash 匹配；复用精确 Publisher/helper DLL，未在本批重编这两项；旧 fixed verifier 是显式传递输入 |
| [历史与现场核对](preservation-check.json) | 旧 576/12 项声明源、1021 已装18文件及326源ZIP保持匹配；EXE、GameAssembly、metadata、UnityPlayer 四项原件 hash 匹配 |

本轮确实修复了原生错误：Capture 竞争失败原来只设置 Busy 位，后续仍可能返回成功；现在 Busy 和捕获错误永久关闭准入，Fail 明确归 Unknown。新增 mode15 通过一次受控递归实际进入冲突分支，验证后续成功查询也不能清除责任；它不证明所有自然线程竞争。日志字段顺序的交叉审查误报在构建前由 root 按实际源码纠正，最终条件再次由原始 mapping 核验。

源 ZIP SHA256：`a455676e512ac075ed7cd85c13d3c766d26d6ef47e103912cccfee1d9d7a0245`。二进制 ZIP：`477bf08fc920ddb89735f22aa3e0ebb4bacf7af517b0a55f32f3d3418cc4c50e`。

上一轮完整构建仍是 [99557 的25套1617/1617及39个早期宿主](../foundation-early-loader-20261025/README.md)；[5523 的57个固定组件用例](../foundation-fixed-publisher-20261025/README.md)另计。本批没有重新运行25套托管回归，也没有重新执行上批50控制展开上下文；这两份历史证据均不冒充本批结果。

下一步见[游戏生产接入合同](next-production-contract.md)和[正式报告](../../2026-10-25_底层-固定发布早期链验证-report.md)。真实系统 WinHTTP 转发、首次执行前的游戏发布凭据、coordinator/实际对象与 GC、scheduler/async/Start 内联清理、全部消费者退休和完整 Retry/Return/Loader 均未因此完成。EngineCancelled=false、Pending 保留、RequestPlay 关闭。游戏实测按用户安排稍后；本批未安装或执行游戏。

复现时在仓库根目录运行以下脚本，并选择不存在的新输出目录；原始工具链、SDK、Python 与两份已冻结 DLL 仍是外部前提：

```powershell
pwsh -NoProfile -File docs/validation/foundation-early-fixed-20261025/run-component.ps1 `
  -BuildDirectory D:/RizToolbox-build-cache/early-fixed-reproduce `
  -CasesDirectory D:/RizToolbox-build-cache/early-fixed-cases-reproduce `
  -LogDirectory D:/RizToolbox-build-cache/early-fixed-reproduce-logs
```
