# Hold 存活检查：隔离候选验证

HoldSourceCapture 的 Unity 存活判断已补上方法查找和调用内部的撤回检查。当前是936输入的隔离候选，尚未合主；主图仍928，原 producer main-r3 长回归继续运行。完整 SDK、B01–B19 和真实 Speed/Seek 尚未验收。

| 证据 | 支持的发现 | 实现路径 |
|---|---|---|
| [936输入结果与52个自然步骤](../work/hold-alive-call-guard-next-20261114/candidate-evidence-r3/run/result.json) | 当前新六项、原guard10/owner15/Hold17和Runtime375均通过 | [实际Backend调用](../work/hold-alive-call-guard-next-20261114/src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.HoldLifecycle.cs) |
| [冻结的精确方法检查](../work/hold-alive-call-guard-next-20261114/candidate-evidence-r3/source/src/RizToolbox.Runtime/NativeRuntime.HoldCaptureAlive.cs) | bounded canonical op_Implicit(UnityEngine.Object)→Boolean；逐export guard，不使用缓存布局批准新的检查 | [Runtime helper](../work/hold-alive-call-guard-next-20261114/src/RizToolbox.Runtime/NativeRuntime.HoldCaptureAlive.cs) |
| [故障测试源码](../work/hold-alive-call-guard-next-20261114/candidate-evidence-r3/source/tests/RizToolbox.Judgements.Tests/HoldLifecycle/HoldLifecycleChecks.AliveGuard.cs) | 实际一字节false/true、11类warm后签名变化、lookup/invoke撤回、未知释放不重试 | [新六项检查](../work/hold-alive-call-guard-next-20261114/tests/RizToolbox.Judgements.Tests/HoldLifecycle/HoldLifecycleChecks.AliveGuard.cs) |
| [原root退出记录](../work/hold-alive-call-guard-next-20261114/candidate-evidence-r3/controls/tests-r3-root-exit.json) | controller39116、root30634实际exit0/chunk8af8c7 | [完整归档清单](../work/hold-alive-call-guard-next-20261114/candidate-evidence-r3/summary.json) |

旧路径只在整个 Method/InvokeValueWithPublication 前后检查 source，中间可能继续调用官方export。新路径在每个方法元数据export后检查同一个 source，并通过 guarded value publication 调用实际方法、消费返回的Boolean。原receiver根始终由原调用方持有；撤回后的已取得临时根继续承担精确清理责任，未知释放保留owner且不重试。该读取表示Unity的truth value，不证明native owner已独立销毁、engine detach/release、scheduler terminal或HoldIdle。

验证范围：六组件/Judgements/Runtime构建W0/E0；新alive6、已有guard10/owner15/Hold8/Stop9共48项Judgements；Runtime375/375另列。52个自然步骤全部exit0，936冻结输入和当前overlay均未变。未运行整个SDK矩阵或真实游戏。Root负责源码复核；Sol/max代理遇到额度限制，没有独立代理执行PASS。

历史原样保留：r1重复方法夹具把class指针当成type指针，导致预期的重复项未进入canonical枚举，测试自然失败；修夹具后r2全部通过。后续复核发现通用Describe缓存会绕过同type指针上变化的byref/宽度等检查，现改成重新读取官方type class/code/byref/value/enum/size/name，新增对应fault后r3完整重跑通过。r2不认证最新源码。

证据归档2985文件、64,976,142字节。冻结发布原root25553实际exit0/chunkf70f53；直接运行归档中的检查器，root1990实际exit0/chunkdbe1cf。检查器只读归档。summary SHA256：`e6b897a97655cc7142b8ad35f050777457f800de77bd1c1273d396deedff7dd8`；当前root receipt SHA256：`26c783ee9763a7340b4e55fa8f21d3a115fddbe8afb079c639d6ec5c74c7389d`。

下一步是先等待并接受原producer主回归，然后直接合入完整936组合改动，跳过未执行的933中间合入。六个合主控制已准备并通过语法检查，尚未执行；要求父928归档和独立acceptance先通过，应用14文件（13 C#及Build.ps1）前保留原字节备份，随后在936主图重新构建、跑53个自然步骤（48项Judgements与Runtime375），另做归档和独立验收记录。

其它controller discovery/type/array-publication多export路径、全局factory/origin、真实engine/source/scheduler闭合仍待补。FullSeek、HoldIdle、FullCoverage和Startup均保持false。未安装/启动游戏、操作游戏UI/账号、改永久环境、提交Git或覆盖旧ZIP；旧881输入预览包没有更新。
