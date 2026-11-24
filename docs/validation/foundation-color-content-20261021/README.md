# 内容生命周期、TMP 颜色与手柄身份整合

2026-10-21。正式完整包 **766/766、25个检查项目通过，0编译警告、0编译错误**，已安装并取得[本包局部实机证据](../runtime-color-content-20261021/evidence-snapshot-20261021T044942Z/README.md)。AudioContent64、UI/input73、GameResources25；原生fixture31,261/61,358，inFlight均0；SDK旧插件、公开契约和真实引用包分发检查继续通过。局部实测不能验收完整B01–B19。

`installation-check.json` 核对18个安装文件哈希匹配，`source-integrity-check.json` 核对256项源码输入未变。`original-game-before.json` / `original-game-after.json` 确认 Rizline.exe、GameAssembly.dll、globalgamemanagers 哈希不变；不把这个结论扩大到未记录的文件。包在 `artifacts/package`，SDK在 `artifacts/sdk/RizToolbox.Sdk.1.2.0.nupkg`。

## 本批改变

| 改变 | 专项证据 | 实际范围 |
| --- | --- | --- |
| 单项/批量统一事务、阶段清理聚合、未知根与依赖保留、错误线程拒绝、模块恢复预算 | [内容审查](../content-transaction-audit-20261021/README.md) | 原52项扩至64；旧租约结束前不重建空预算，未知free失败保留到进程退出 |
| TMP_Text.color 精确访问器、共享颜色目标/代次/仲裁、独立基线 | [TMP颜色](../tmp-color-20261021/README.md) | GameResources18扩至25；托管目标及元数据seam，不是实际TMP/GPU恢复 |
| Gamepad.current A直接切B的旧边沿失效、精确deviceId及场景清理顺序 | [手柄身份](../gamepad-device-identity-20261021/README.md) | UI/input70扩至73；真实硬件/驱动尚未验 |
| 目录真实消费者与ResourceReference资源责任 | [本地目录边界](../local-provider-catalog-20261021/README.md) | 25个有界静态范围；无新音乐/封面/目录hook，不冒充完整本地曲包 |

本批早期三个子代理遇到使用限制。前8项内容修复、TMP实现和静态目录提取已落盘；主代理完成内容后续生命周期与模块预算修复、手柄身份和场景顺序、最终构建/安装/交接。之后的[内容事务独立只读审查](../content-transaction-audit-20261021/independent-review.md)已完成：有界范围内未发现可由正常API序列确认的新正确性缺陷，未重新运行测试，也不覆盖WorkshopChartService全部释放路径或完整B15接入。

## 构建与运行证据

`build.log` 与 `build-summary.json` 属于最后冻结源码。`build-before-scene-order.log` 与 `build-before-retained-recovery.log` 均为765项中间版本，不用于证明最终源码；因后续发现真实时序和旧消费者配额缺陷，重新完成最后构建。`source-manifest.json`、`package-manifest.json` 和 `native-build/` 绑定实际文件。

从仓库根目录复验：

```powershell
./scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/color-content-20261021'
./scripts/Install.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline'
```

电脑操作此前 kernel assets 初始化失败已原样保留在 `runtime-744-attempt-20261021`。连接恢复后首次因Steam未运行/待人工登录而启动失败，[失败启动记录](../runtime-color-content-20261021/launch-attempt.json)与[首次启动器结果](../runtime-color-content-20261021/launcher-result.json)均保留。用户完成登录后真实766会话已启动，独立证据见[04:49:42 UTC冻结快照](../runtime-color-content-20261021/evidence-snapshot-20261021T044942Z/README.md)，不使用旧666日志证明766。

本次真实观察包括SDK Pause成功、约21秒内游戏时钟冻结而host/DSP推进、Resume后续恢复Playing、Retry对旧局Aborted并生成同谱新RunId，以及一次hold.end原调用返回和Perfect计数1→2。真实TMP对象发现30次新binding、32次基线getter读回；这不证明TMP覆盖/双插件恢复。Translucent三项Float克隆读回并应用，切场景后probe的created1/released1/active0；adapter同时记录cleanup pending并保留根/预算，**材质物理释放与独立基线恢复未确认**。本次没有正常Completed或实际结算证据，RawGameUnits到秒的映射仍未验证。

游戏已正常退出；967条同session结构化事件序号连续，所选本次范围Error0、Warning13，旧失败会话原样保留。临时probe已恢复原Absent状态，字节备份、格式变化的语义复核及最终状态见[恢复记录](../runtime-color-content-20261021/module-probe-restoration.json)。后续为原生退休成功增加receipt的源码、构建或实机属于下一包，不能补证766物理释放。

仍需挑战长按完整身份/判定来源、普通正常完成/迟到结算、TMP实际覆盖/双插件恢复、动态目标、真实手柄、GC/长期引用、本地曲包完整解析，以及四基线/20轮压力与发布矩阵。CHECKLIST仍只B03与B18a勾选。
