# 监听器变化观察与协程身份底层

2026-10-24。本批把底层从“看到监听器被调用”推进到“记录已确认入口上的增删变化”，并补上协程返回句柄的归属信息。**完整构建通过 25 套、1553 项托管检查；新包已经冻结暂存，尚未安装或进行游戏实测，整体底层全覆盖任务仍未完成。** 具体边界见 [scope.md](scope.md)，后续安排见 [next-integration.md](next-integration.md)，完整证据链见[正式报告](../../2026-10-24_底层-监听变更与取消身份-report.md)。

## 本批增加了什么

- **监听器变化记录。** 生产后端已接入固定版本的 12 个允许入口，覆盖 Event、InvokableCallList 和 PersistentCallGroup 的已确认操作。每次观察保存来源、参数、代次和前后图；先添加再移除、最终列表恢复原样，也不会抹掉中途发生的记录。上层方法会内联修改列表，因此同时接上层和下层入口；嵌套记录不能相加当作不同的逻辑变化。
- **对象先保活、再读取。** 实际生产 `CaptureDispatch` 先对 Invokable、List、第三处调用的 RDX 对象申请并保存根，再读 Target、Class 和列表。新增故障注入验证部分 Root 失败、首个 Target 失败、后续 Class 失败及预算不足时保留已承担的责任，不重试未知结果。
- **协程身份。** 三类 tracker 保存实际 Start 返回的 Coroutine 句柄，并与原有 Enumerator、receiver、owner 和可复用槽的代次关联。句柄先记入诊断，再确认保活；失败时仍保留原始值和 Unknown。该身份供未来 Stop 请求匹配，当前没有接入 Stop 钩子，也不产生“取消完成”结论。

## 当前已核对的证据

| 验证层 | 已通过的结果 | 说明 |
| --- | --- | --- |
| [构建终态](build-run.json)、[测试汇总](build-summary.json) | 定向进程 `32299`、完整进程 `60161` 均以 0 结束；25 套 `1553/1553`，其中 Hooks `185/185`、AudioContent `597/597` | 新增 8 个 broker、10 个对象采用、8 个 mutation ledger、6 个协程身份测试均包含其中；不能把这些计数当作全覆盖率 |
| [自有原生 Operation fixture](operation-native-evidence.json) | 1156 次检查、72 次实际 original，Entered/Exited 均为 72 | 执行三种精确 native void ABI，验证原生回调、异常清理、worker 和嵌套等有限路径；没有 Dobby 发布证明 |
| [自有原生 → CLR Operation 联测](operation-managed-evidence.json) | 5 个独立进程、259 次检查、18 次实际 original | 真实 helper 通道接生产共用 bridge/broker，模式为 normal、original-seh、original-cpp、detach、observer-fault；没有在该链路调用实际 mutation ledger |
| [源码归档检查](source-archive-check.json)、[暂存包检查](package-archive-check.json) | 522 个声明源码输入、22 个暂存包文件逐项哈希匹配 | 归档对应本次构建；不包含真实游戏验收、外部依赖与工具链的干净重建证明 |

本批新链路有两条分开的动态证据：一条是“自有 native 目标 → helper → CLR bridge → broker”；另一条是“受控操作帧/图/根后端 → 实际 mutation ledger 与生产对象采用方法”。目前尚未把两条串成同一次真实游戏调用。固定入口和 ABI 的离线依据见 [research.md](research.md)、[mutation-abi.json](mutation-abi.json)，取消设计依据见 [cancellation-research.md](cancellation-research.md)。

## 用户现在可以怎样理解它

底层已经有可复用的原生观察通道、共享一次安装的 broker、对象保活和归属账本。本批让账本能记录已知增删操作及协程身份；还缺真实游戏冷启动下的新入口发布验证、实际 IL2CPP 对象联测、全部动态写者和 delegate leaf、取消完成及资源全生命周期退休。因此仍保留 `Pending=true`、`MutationClosed=false`，不据此开放完整的本地游玩与资源释放权限。

## 交付与复核

[源码变化清单](changes-from-listener-clr.json) 对比上一批：声明输入由 502 增至 522，新增 20、修改 14、删除 0。[构建前后检查](build-input-integrity-check.json)和[当前源码检查](source-integrity-check.json)确认冻结输入一致。源码归档 `source-inputs.zip` 的 SHA256 为 `afa730574abc39d1e55faf10f2b78cf400e05751043b7601eefa70d23a4a7fe3`，暂存包 `staged-package.zip` 为 `2ebc43af8a7295574f3fad6a732874489f5bb1362226af97e470ef32a1067043`。

[旧安装检查](previous-installation-check.json)确认旧安装 18 项及旧归档 326 项输入仍匹配；[游戏文件检查](original-game-check.json)确认 3 个原游戏文件未变化；[暂存清单检查](staging-check.json)明确 `installed=false`。522 个新源码输入、22 个新暂存文件与旧安装清单分别核对，不能互换。

首轮定向失败日志 [targeted-first.log](targeted-first.log) 保留了缺少旧原生联测环境变量和对象采用 fixture 初始化问题；修复后最终定向通过。此前的编译接口遗漏也已由主构建者修正。[完整日志](build.log)提取到 94 条不同原始 compiler diagnostic 文本，含 upstream 警告、ANSI 差异及已有警告，不等于 94 个独立问题，也不能称零警告构建。

如需在同样本机依赖下复现，从仓库根执行本批 [run-build.ps1](run-build.ps1)；这会重新构建和生成产物，阅读冻结证据不需要重跑。本批三份说明文档不属于 522 项已冻结构建输入。
