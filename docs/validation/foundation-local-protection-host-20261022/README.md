# 跨模块结果保护宿主：1021 项构建与有限实机

2026-10-22。1021/1021、25 套离线检查通过并已安装；Runtime115、Hooks69、AudioContent226。新增 14 组使用真实 broker 和受控 detour 的宿主集成回归。完整增量日志没有编译诊断，不推断干净重建零警告。两个 Windows x64 native fixtures 分别 31,261/61,358，通过不等于游戏 LOCAL 激活验收。

宿主安装四个精确前置门，只有全部安装且实际事务 handler 就绪才允许新会话。当前没有安装事务 handler，`AcceptingNewRuns=false`，公开 RequestPlay 仍返回 OperationUnsupported。

## 责任与生命周期

`LocalRunProtectionHost` 使用进程强引用保存最多 32 个宿主，先保责任再绑定。四个目标全部完成 metadata/RVA 验证后才开始安装。部分绑定或解绑结果不明时，不重试、不释放责任配额；独立已知 binding 仍各清理一次。

provider 或模块撤回只停止新局。只要存在未退休 session、正在分派的调用、待完成的原生销毁或其他 handler 工作，四门继续存在。即使 registry.Close 已结束，handler.HasPendingWork 仍必须明确为 false；读取或 Tick 失败保留未知责任。独立 process pump 在模块撤回后继续推进。

分类器仅查询已发布的不可变角色身份，不从工作线程读取 root.Target。现有 registry 只保护已经绑定的 exact receiver。首次结果页在 Start 前如何证明归属仍未实现，不能从“当前存在 LOCAL 局”猜测。

## 证据与界限

| Evidence | Finding | 后续实现路径 |
|---|---|---|
| [构建摘要](build-summary.json)、[完整日志](build.log) | 25 套 1021 项通过；日志 SHA256 `8abb0f0f5a8965e0bab8f4ea55f851d549885172a799cbdf4d9cce2593bdd803` | 保持公开播放关闭，继续实际事务接线 |
| [安装核对](installation-check.json)、[原文件核对](original-game-after.json) | 2026-10-22T14:28Z 时 18 安装文件匹配，三个原游戏文件未变 | 新源码不覆盖旧批次安装结论 |
| [源码归档](source-inputs.zip)、[归档核对](source-archive-check.json) | 326 个声明输入逐项 hash 匹配，ZIP SHA256 `d38b6630002cc680924b4313c758ec05189e0a4edd685b3f6018183ade5f307f` | 当前源码已开始下一批修改；旧包复核使用归档 |
| [实机快照说明](../runtime-local-protection-host-20261022/README.md) | 四门实际解析/绑定成功；普通暂停 Retry 原调用一次 | ResultStart、ResultRetry、ResultBack 的真实正向调用仍待完成 |

源码归档不包含完整依赖、工具链和游戏二进制，不等于 B19 干净发布复现。没有 LOCAL 原生 claim/skip、ControllerOver 一次结果捕获、参数初始化、激活、globals 恢复或消费者结束验收，不增加 B01–B19 勾选。

## 复核

原始完整构建命令：

```powershell
./scripts/Build.ps1 -NativeOutputDirectory 'D:\RizToolbox-build-cache\local-protection-host-20261022'
```

这会构建当前工作树。复现本批必须另取此处归档及匹配依赖，不能把更新后的工作树当作 1021 原输入。`summarize-build.py --installed` 要求工作树仍等于本批输入，因此开始下一批后不可用它宣称旧源码未变；新批脚本分别验证安装文件和旧源码归档。
