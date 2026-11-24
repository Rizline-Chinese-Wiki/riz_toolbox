# 每局独立原生资源包：999 项完整构建

2026-10-22。**999/999、25 套检查通过，新包已安装并取得独立资源包实机证据。** Runtime115、Hooks61、AudioContent212；两个 Windows x64 native fixtures 分别 31,261/61,358。完整增量构建日志没有编译诊断；前置定向日志仍保留 15 类 FakeNative CS0649 和 3 条 CS8602，不能据此声称干净重建零警告。

这批实现每局独立的 Texture/Sprite/Audio、Chart、JudgeData、metadata、InsideLevelParams，以及 CPU request fork 和延迟销毁责任。没有激活 LOCAL 控制器，公开 RequestPlay 仍返回 OperationUnsupported。完整 B01–B19 继续，未增加整项勾选。

## 本批产物与复核

| 证据 | 结果 |
|---|---|
| [构建摘要](build-summary.json)、[完整日志](build.log) | 25 套全部通过；log SHA256 `a0c779daafa32445144daf3d1dbe801584204788b9594f717b5395f93f39eb42` |
| [包清单](package-manifest.json)、[安装核对](installation-check.json) | 18 个实际安装文件与本批包完全一致 |
| [源码清单](source-manifest.json)、[收尾核对](source-integrity-check.json) | 320 个输入未变；本批开始纳入 templates/tools/packaging/global.json，数量不能直接与旧 303 项作差解释 |
| [原游戏前值](original-game-before.json)、[后值](original-game-after.json) | exe、GameAssembly、metadata 三个原文件不变 |
| [Runtime115](runtime-targeted-final.log)、[Audio212](audio-targeted-complete.log) | 含输出 publication、临时 free 失败、PCM 上传责任、精确 revision、未知清理不重试 |
| [独立审查](independent-review.md) | 两处责任交接遗漏、非有限 BPM、旧来源误锁与精确 claim 验证；由主代理集成 |
| [完整实机](../runtime-local-bundle-20261022/README.md) | 879 连续事件、0 Error、7 Warning；两包独立、撤回/GC、分帧释放及 CPU 计数归零 |

从仓库根执行构建：

```powershell
./scripts/Build.ps1 -NativeOutputDirectory 'D:\RizToolbox-build-cache\local-bundle-20261022'
python docs/validation/foundation-local-bundle-20261022/summarize-build.py
```

安装前必须停止游戏；既有安装脚本会校验归属和 hash。仅离线复核当前安装及源输入：

```powershell
python docs/validation/foundation-local-bundle-20261022/summarize-build.py --installed
```

## 实现边界

新 publication 接口在临时根/owned Arg 清理前，把输出同步登记到调用者的固定记录。32-call/129-root/128-Arg 的临时责任池保留未知 free，禁止用后续 no-op 或重试伪造完成；旧普通接口的清理契约没有被此结论覆盖。

每包最多 64 MiB、进程 128 MiB 是上传估算；包含待销毁包的估算，不是 native heap/GPU 或临时数组/HGlobal 的实际内存上限。单包固定五个 direct roots 不包括 metadata、wrapper 和参数各自持有的根。

真实判定窗口由活跃普通 NormalMode 的参数读出，再复制到新 JudgeData；没有猜默认模式。实机只证明这四个数被独立复制，不证明时间单位。参数 Speed/Offset 仍是原构造前初始化的 0，未完成原版设置初始化验收。测试 score 是明确 sentinel，未推算 MaxHit/MaxScore。

本批无 commit/push。子代理已交付的代码和审查均保留；后续代理因额度限制停止，主代理完成构建安装与实测收尾。
