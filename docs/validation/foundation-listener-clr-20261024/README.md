# 监听器原生到 CLR 联合验证

2026-10-24。本批把已经配置的原生 listener 通道接到生产共用的 CLR bridge、broker 和真实 UnityEvent ledger；在自有原生目标上完成回调、归属和清理验证。**新包仍在暂存区，游戏实测稍后；没有安装或启动游戏，也不能据此宣称底层全部 19 项完成。** 具体边界见 [scope.md](scope.md)，后续工作见 [next-integration.md](next-integration.md)。

## 已确认的结果

| 证据 | 结果 | 能说明什么 |
| --- | --- | --- |
| [build-run.json](build-run.json)、[build-summary.json](build-summary.json) | 定向进程 `22041`、完整构建 `21231` 均 `exitCode=0`；25 个托管测试项目，`1521/1521`；其中 Hooks `177/177` | 本批冻结输入的离线回归通过。原生 fixture 的 checks 单列，不计入 1521 |
| [listener-managed-evidence.json](listener-managed-evidence.json) | 12 个独立 CLR 子进程、1630 次检查、零失败；所有 `brokerInstallCalls=0`、`nativeInFlight=0` | 实际原生函数进入 CLR 回调，托管采用既有通道，没有另做 Prepare/Commit |
| 同一回调证据的 `normal`、`original-seh`、`original-cpp` | 三个模式 `actualLedgerCaptureVerified=true` | 实际调用生产 ledger 的 Capture/Mid/Before/After；图、metadata 和根后端仍受控，不是 IL2CPP/GC 验收 |
| [build-input-integrity-check.json](build-input-integrity-check.json)、[source-integrity-check.json](source-integrity-check.json) | 502 个声明构建输入与构建前清单、归档及当前文件一致 | 结果可对应到明确源码版本；外部依赖、工具链和干净环境重建是另一个范围 |
| [source-archive-check.json](source-archive-check.json)、[package-archive-check.json](package-archive-check.json)、[staging-check.json](staging-check.json) | 502 个源码条目、22 个暂存包文件逐项哈希匹配；`installed=false` | 源码归档与暂存交付物已冻结，不代表已安装或游戏可用 |

12 个模式为：`normal`、`original-seh`、`original-cpp`、`target-read-av`、`late-attach`、`detach`、`worker`、`nested-same`、`nested-cross`、`foreign-original`、`fault-before`、`fault-after`。它们覆盖三个实际 source CALL、真正的 callee 返回地址、双 sink 的同一父 token 与独立上下文、异常完成、旧 Enter 后 Attach、在途逻辑撤销及 GC 后停止交付、worker/递归遮蔽和托管观察器故障。

`target-read-av` 使用直接原生 bridge：真实 helper 虚表读取 AV 后，收到没有 Before 的 `Abnormal After(token=0)`。它没有经过该模式的 broker/ledger。`foreign-original` 验证自有嵌套 original 绕过父入口时不借用祖先，并保留 Unknown；这两项均只证明已执行的有限路径。

真实 ledger 的正常模式记录 3 次 dispatch、3 次正常返回；两个原生异常模式各记录 3 次 dispatch、2 次正常返回和 1 次 abnormal，并保留 Unknown。三个模式都保留 `Pending=true`、`MutationClosed=false`、`InlineExecutionUnobserved=true`、`DispatchPathVerified=false`；11 条根记录来自受控后端。一次 invokable dispatch 不能推出其中每个 delegate leaf 都执行。

## 版本和交付边界

[changes-from-listener-publication.json](changes-from-listener-publication.json) 比对上一批 `foundation-listener-publication-20261024`：声明输入由 493 增至 502，新增 9、修改 10、删除 0。源码归档 `source-inputs.zip` 的 SHA256 为 `103a371b7aea4344593841508f4fc6b28d3180011b3b7f7207a96e2fa2a61590`；暂存包 `staged-package.zip` 的 SHA256 为 `a962d2576981b6768ae4e43f025a56245c3a8ee71588fff89f57fd123d757791`。

[previous-installation-check.json](previous-installation-check.json) 核对的旧安装 18 项及旧归档输入 326 项保持匹配；[original-game-check.json](original-game-check.json) 核对的游戏 3 文件哈希保持匹配。22 项新暂存包、18 项旧安装和 326 项旧输入是三种清单，不能互相替代。

## 证据到下一步

调用链为：自有 native parent → permanent Bootstrap 通道 → listener pre 尾跳真实 callee → post 重放 → CLR bridge → broker 私有父/子归属 → ledger 的受控图捕获。上述动态证据支持这条有限调用链；[mapped-publication-evidence.json](mapped-publication-evidence.json) 和 [managed-adoption-evidence.json](managed-adoption-evidence.json) 另支持固定映像九次发布与 V2 只读采用，但没有执行固定游戏目标。下一步应把两条证据链接到同一次真实冷启动，验收真实 receipt、IL2CPP 根/metadata 和实际 listener 回调，不能用本批自有目标替代。

原始日志为 [targeted.log](targeted.log)、[build.log](build.log)。如需在相同依赖和固定输入条件下复现，可从仓库根运行本批 [run-build.ps1](run-build.ps1)；完整构建和定向复现都会重新生成产物，阅读本批冻结证据不需要重跑。本批三个说明文档不属于 502 项已冻结构建输入。
