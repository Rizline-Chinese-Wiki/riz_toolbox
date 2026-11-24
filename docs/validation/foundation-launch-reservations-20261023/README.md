# 激活前责任预留与共享准备状态

2026-10-23。**1187/1187、25 套完整构建通过，新包仅暂存；游戏安装仍为 1021 四门包。** Runtime130、Hooks82、AudioContent364；较历史1148批增加39组 AudioContent 离线检查，不能据此计算底层覆盖率。两个 Windows x64 helper fixture 为31261/61358，均不等同于新初始化入口的游戏验收。完整增量构建日志编译诊断列表为空；前置定向编译输出和两次整合失败原样保留。

本批遵循 [scope](scope.md)，[交付审查](independent-review.md) 记录实际独立核对范围及限制。[正式报告](2026-10-23_底层-启动责任预留-report.md) 给出证据链与下一步。

| 实现 | 本批能证明什么 |
|---|---|
| `NativeLocalRunLaunchReservation` | clone/native 尝试前统一预留 capture、factory、queue、graph 四类原有配额；验证同一 host/handler/bridge/claim/bundle；容量失败作独立空回滚，发生 native 尝试后不走空取消 |
| `LocalRunLaunchCommitState` | controller 先 Held，实际 info/progress 采用后为 Initializing，再经图核验与真实 drain；最终沿既有 registry commit 发布同一个 CommittedReady token。token 不等同于真实开局或激活证明 |
| pending result/capture factory | 共用原32-owner上限；空预留不读 native source/root target、不调用 ControllerOver、不消耗 result claim。controller 交接后仍 Held；已提交并共享 ready 后才在原预留槽初始化真实 capture |
| pending graph/queue | 使用独立 controller/info/progress 根与实际反向图核验；初始化图发布 pure identity，worker 分类不读 native；早到 LevelOver 保留 terminal 责任而不提前捕获 |
| worker 与清理边界 | 纯 exact controller claim 可在 main handler 被拒前记录 early terminal；root 交接失败有强 owner，未知初始化/free 不重试、不退配额；Closed 证据不被替换为几个调用方 bool |

生产尚未接入实际 `LevelControl.Start` / `GetInsideLevelParams` / `Progress.Play` 初始化钩子或执行激活。现有图采用接口需要下一批由实际原生入口调用；测试中显式驱动状态不能当作游戏自己完成初始化。完整结果 sink、Retry/Back、active 消费者退休及 Save/Steam 全路径仍缺，`RequestPlay` 继续关闭。本轮遵守“先继续代码，实测稍后”，没有新游戏实测。

## 冻结证据

- [build-summary.json](build-summary.json)：25套1187/1187；[build.log](build.log) SHA256 `1c48f6c82146b095c72cb5d44742ac56990332547ac2875c87db76e0466b68e3`。
- [staging-check.json](staging-check.json)：18份暂存文件；[package-archive-check.json](package-archive-check.json)：逐项匹配 [staged-package.zip](staged-package.zip)，ZIP SHA256 `c545e9d7ac1d4e6580c46247f7f9ceed0259e823527b26623b1064f2213b0d64`。这是暂存发布文件归档，不含全部 loader 依赖和工具链。
- [source-integrity-check.json](source-integrity-check.json)：352份声明构建输入；[source-archive-check.json](source-archive-check.json)：逐项匹配 [source-inputs.zip](source-inputs.zip)，ZIP SHA256 `63707187cba305a17c64db3ceb9878ccb922254efec9f02b7a73fe7f5029a576`，manifest SHA256 `a04fedc5322ff65845fcb2486bf3d4641fa4ff1023da04696d13f739f6d66c9e`。
- [previous-installation-check.json](previous-installation-check.json)：实际1021安装18项与其326份历史源码归档独立匹配；不是拿1187源码要求旧包未变。
- [original-game-check.json](original-game-check.json)：Rizline.exe、GameAssembly.dll、global-metadata.dat 三文件哈希保持原值。

历史过程没有改写：[首次整合](audio-integrated-first.log) 为358/364，六项因已有bridge的host handler安装条件被拒；[第二次](audio-integrated-second.log) 为363/364，容量断言记录 `Expected 32; actual 29`；[第三次](audio-integrated-third.log) 才是364/364。完整1187构建为后续独立结果。首次定向357/357不是最终整合覆盖数。

复建命令会改写新的暂存输出，不安装或操作游戏：

```powershell
Set-Location C:\Users\71957\Desktop\riz_toolbox
& .\scripts\Build.ps1 -NativeOutputDirectory D:\RizToolbox-build-cache\launch-reservations-20261023
```

上述源码/发布 ZIP 固定本批 declared inputs，不证明干净新系统重现。继续开发后核对历史 ZIP，而不是要求旧 source-integrity 检查证明新工作树不变。仅核对当前暂存时才运行 `summarize-build.py --staged`，该脚本会重写核对 JSON，本轮交付审查未运行它。

## 下一批

[初始化隔离实施任务](../local-initialization-order-20261023/next-initialization-isolation.md) 明确共享现有 Mode entry，新增精确 Start/getter/Tooltip 接线与独立 Progress.Play around，将完整真实在途加入 preCommit/结果/退休 drain。静态已发现 Start 末尾的 Mode/Tooltip 和 Play 两处 Mode 调用，现有 Progress 终止 around 不能替它们提供覆盖。其它底层缺口继续按 [全范围剩余审计](../foundation-local-termination-20261023/foundation-remaining-audit.md) 推进，不把全目标缩为 LOCAL。
