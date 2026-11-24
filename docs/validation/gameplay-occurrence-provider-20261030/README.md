# N2 逐 occurrence 原生 owned producer 与 concrete provider

本包实现了可执行的具体 `INativePlaybackRateProvider`：独立 native owned-call producer 产生私有 Pause/Replay/Resume、source 和 pitch writer 凭据，实际 `NativeRuntime` accessor 读取独立 rooted 图、DSP/media/pitch/progress/nullable origin，协调器在 stock pitch=1 后恢复并读回选中速率，再发布已有媒体 Clock。正式 `GameplayNativeBackend` 与 `GameplayService` 已在离线受控过程中消费该 provider。报告与复现见 [2026-10-31_implementation-N2-owned-occurrence-provider-report.md](2026-10-31_implementation-N2-owned-occurrence-provider-report.md)，来源和边界见 [scope.md](scope.md)。`flavor=null`。

最终 r4：**175/175**（既有 146 + 新 29），native build/managed build/tests actual exit **0/0/0**，零警告/错误；688 个 frozen 输入和八个 owned overlay 构建前后 hash 相同。原件：`D:/riz-build-cache-next/gameplay-occurrence-provider-r4-20261031/`；[evidence-index.json](evidence-index.json) 与 [test-result.json](evidence/test-result.json) 记录精确退出码及输入/输出哈希。

本包只有明确的 controlled factory。native observer 只覆盖该 owned lane 的实际调用，没有真实 stock hooks、直接 Runtime/Unity 外写的完整覆盖或 production GetTime authority。默认 provider 装配没有变更，真实 Speed/Seek 继续关闭；独立 native fixture 不加入发行，没有安装/启动游戏、UI 或账号操作。

2026-10-31 的 r4 增加了 worker Dispose 的实际修复和两层回归：错线程调用在 disposed/root-list 变更前拒绝，七个根与 free-attempt 计数保持不变，主线程仍可使用并重试清理，恰好释放七个根且重复 Dispose 不再 free。r3 的 173 项是旧源码结果，不能验收这次修复；全部历史目录均保留。
