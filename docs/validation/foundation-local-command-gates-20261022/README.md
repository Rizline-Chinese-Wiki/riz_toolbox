# LOCAL 重试与返回前置门：1007 项构建

2026-10-22。**1007/1007、25套完整构建通过，产物已生成，尚未安装或接入游戏。** 当前游戏安装仍是已完成资源包实测的999版。新包Runtime115、Hooks69、AudioContent212；两个原生helper/Dobby fixture仍为31,261/61,358。

新增暂停重试、结果重试、结果返回三个宿主内部精确前置门，与已有结果Start共用原调用前的分类/独占/异常/线程/重入分派。LOCAL一旦claim，该次调用不会因handler失败、worker入口或in-flight detach回落stock；普通调用仍保原参数和原异常。没有新增普通SDK任意跳过方法权限。

## 实际证据

| 输入 | 结果与限制 |
|---|---|
| [完整构建摘要](build-summary.json)、[日志](build.log) | 1007/1007；SHA256 `c8af96c7aecb2a5788a5b8af91df9947e2f544a69983679410bc64265d2dc5c2`；15条FakeNative CS0649测试stub警告保留，无错误。前批三条CS8602可空警告已按既有非空契约修正 |
| [定向Hooks69](hooks-after-import.log) | 原61项继续通过，新增8组同时覆盖三命令，包括每命令真实cdecl calli。受控engine/original不是Dobby或游戏实测 |
| [暂存包核对](staged-package-check.json)、[322输入](source-integrity-check.json) | 18暂存文件与新manifest一致，322源码输入未变 |
| [仍安装999核对](installed-999-check.json) | 当前18文件与独立999清单一致，新包未安装 |
| [原游戏前值](original-game-before.json)、[后值](original-game-after.json) | 原exe/GameAssembly/metadata三文件不变 |
| [下一事务计划](../local-launch-20261022/next-activation-transaction.md) | 具体接线顺序和故障矩阵；计划不算激活证据 |

首个定向构建曾缺少HookDiagnostics的SDK namespace，原失败日志[保留](hooks-targeted.log)；修正后定向和完整构建均通过。此次没有游戏session，不把999包旧证据改写成1007。

## 精确权限和后续接线

`ExactHookBroker.LocalRunCommandTarget.Resolve`仅接受已固定GameAssembly指纹与三种命令，通过NativeRuntime精确验证declaring class、instance/no-arg/void ABI、MethodInfo及RVA：

| 命令 | 类型/方法 | RVA |
|---|---|---|
| ControllerRetry | NormalModeInLevelController.Retry | 3C1310 |
| ResultRetry | LevelOverControl.Retry | 3CFF20 |
| ResultBack | LevelOverControl.Back | 3CE020 |

这些锚点来自已保存的静态报告；本轮没有运行真实metadata Resolve或安装新policy。未知enum/build在native访问前拒绝。classifier契约为any-thread的root-free精确身份查询；不能依据曲名、metadata.From、provider是否可用或“当前有LOCAL局”认领未知receiver。

逻辑Dispose会恢复后续stock调用，因此真正宿主owner必须跨provider/模块撤回持有所有仍被LOCAL消费者依赖的门，直至强退休证明完成。门尚无生产绑定、首次结果receiver归属仍缺；RequestPlay继续OperationUnsupported，完整B01–B19没有新增勾选。

离线复核暂存文件、源码以及仍安装的999包：

```powershell
python docs/validation/foundation-local-command-gates-20261022/summarize-build.py --staged
```

完整重建：

```powershell
./scripts/Build.ps1 -NativeOutputDirectory 'D:\RizToolbox-build-cache\local-command-gates-20261022'
```
