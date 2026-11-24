# 创建中途失败仍保留对象和输入责任

2026-10-22。**1038/1038、25套完整离线检查通过，新包已暂存、未安装。** Runtime115、Hooks69、AudioContent243；新增17组责任交接回归。游戏安装仍是1021包，按用户“先继续代码，实测稍后”要求没有操作游戏或替换安装。

未激活控制器的parent/clone现在通过runtime publication先登记输出，再进行临时清理；即使后续构造或清理抛错，宿主仍持有对象和销毁闭包。配额拒绝时输入仍归调用方，控制器名称因此增加一个固定根记录，完整图从历史7根变为8根，bool参数有明确using责任。资源包另用128个固定参数槽，在进入publication调用前保住字符串根及值缓冲；异常后不丢记录、不重试、不退责任配额。

## 已验证的范围

| Evidence | Finding | 下一步 |
|---|---|---|
| [最终构建](build.log)、[摘要](build-summary.json) | 1038项/25套通过，native fixtures31,261/61,358；完整增量日志0编译诊断 | 本批尚无游戏验证；不推断干净重建零警告 |
| [内容定向](audio-bundle-input-retention-fixed.log) | 243项通过；实际runtime调用API配合受控native导出，覆盖成功/构造异常/临时free失败/32-call配额拒绝/线程与重入 | 不证明真实Unity Instantiate、Awake或销毁时间 |
| [暂存核对](staging-check.json)、[源码核对](source-integrity-check.json) | 新包18项和329项源码输入匹配 | 安装及实测按用户安排延后 |
| [旧安装核对](previous-installation-check.json)、[原游戏核对](original-game-check.json) | 现有18项安装仍匹配1021及其326输入源码归档；三个原游戏文件未变 | 不把更新后的工作树当作旧安装源码 |
| [源码归档](source-inputs.zip)、[归档核对](source-archive-check.json)、[差异](source-delta.json) | 最终329个输入已逐项冻结，修改4个既有输入、新增3个 | 不等于完整依赖/工具链及B19发布复现 |

最终日志SHA256：`43c14bb581ba862b4a92d667df8fc15319b4e3ebc5bf89426b63f7fa57596be2`。源码ZIP SHA256：`db0b33e49adaf7e5c9a11f238392f97106b9acf5ce01fc4b503e8ff748d3048b`。

## 复核命令

从仓库根运行，不启动或操作游戏：

```powershell
dotnet run --project tests/RizToolbox.AudioContent.Tests/RizToolbox.AudioContent.Tests.csproj -c Release
./scripts/Build.ps1 -NativeOutputDirectory 'D:\RizToolbox-build-cache\inactive-publication-20261022'
python docs/validation/foundation-inactive-publication-20261022/summarize-build.py
python docs/validation/foundation-inactive-publication-20261022/summarize-build.py --staged
```

`--staged`分别核对新暂存包/当前输入、旧1021实际安装/源码归档，以及三个原游戏文件；不会安装或停止进程。源输入后续更新后，不应继续用本批脚本宣称“当前源码未变”。

## 限制和下一步

本批没有激活LOCAL、安装结果事务handler或开放RequestPlay。首次结果Start前的receiver归属、实际设置初始化、受控激活/Awake正向、六globals发布/条件恢复、真实结果捕获/重试/返回及消费者结束仍需接线和验收。当前四门真实会话按[现场说明](../runtime-local-protection-host-20261022/README.md)留待后续，只有普通ControllerRetry取得正向调用。

历史953包的7根实机证据仍只证明当时源码；新8根工厂需要新实测，不能修改旧证明来适配。普通Invoke/NewExact的既有清理契约没有因本批publication增强而自动扩大，完整异常和长期内存矩阵仍未完成。未增加任务勾选，未commit/push。

中间1031构建保存在 `build-before-input-retention.log`；`audio-bundle-input-retention.log`包含三个fixture把static方法错误设为instance的失败。修正fixture元数据后243项通过，原失败原样保留，不能拿中间日志替代最终构建。
