# 603 包的材质拒绝与 F8 未观察证据

实测目前只证明拒绝分支：旧选择目标不可用时停止创建；对当前真实 `Canvas/DiscTabItem(Clone)` 材质请求 TMP 和 Translucent 参数时，精确 shader 身份不匹配，两个创建请求均被拒绝。探针的 created/applied/保留克隆/活跃覆盖计数始终为0，不能记为材质成功应用。

此处使用 docs-generator 的 Evidence → Finding → Path 结构。范围是同一603包、同一游戏会话的后续冻结导出；正在修订的源码不会改变此旧包证据。

## 重复检查

```powershell
C:\Python314\python.exe docs/validation/runtime-back-skip-20261020/inspect-material-refusal.py
```

在项目根目录运行。脚本只读冻结文件，生成 [material-refusal-proof.json](material-refusal-proof.json)。已实际运行核对 **1237条事件、单会话、连续序号、导出长度及哈希**；前1095条与 [Back/Skip原冻结会话](README.md)完全一致，增加142条事件。会话为 `20261020T101511620Z-c6589396de114bf2aac7a3f1dfe89a7a`，后续导出最终事件为2026-10-20 18:27:38（UTC+8）。每条摘录保留序号、时间与源文件行号。

输入为 [后续导出清单](session-material-refusal/export-manifest.json)、[冻结状态](status-material-refusal.json) 与原包清单；其哈希写入生成摘要。它仍是前述17文件安装检查所对应的603运行包，不是后续源码修复包。

## Evidence → Finding

| ID | 事件序号 | 观察与结论 |
|---|---|---|
| E-001 / F-001 | 915、1107–1110 | 持有的跨场景旧选择为 `NormalModeController(Clone)/Image`，首次 TMP 参数按钮后记录 TARGET MISSING、code20；没有发出 CREATE REQUEST。可验证目标不可用拒绝，不擅自把code20解释成TargetExpired或证明原生内存已销毁。 |
| E-002 / F-002 | 1139、1153–1159 | 新选目标为LevelList中的 `Canvas/DiscTabItem(Clone): material`。请求 `rizline.pc.tmp-mobile-outline.v1` 后明确返回 `UnsupportedShaderSchema: source shader is not the exact trusted resource shader.`，没有创建成功。 |
| E-003 / F-003 | 1165–1171 | 同一目标请求 `rizline.pc.translucent-ui-float.v1`，返回同一精确shader身份拒绝，目标和代次与请求一致。 |
| E-004 / F-004 | 全部 RESOURCE COUNTS | created、applied、released、restoreRequests、retainedProbeClones、activeProbeOverrides均为0，assetValid均null；没有任何成功创建/应用标签。计数只覆盖探针自有租约，不是全进程原生资源统计。 |
| E-005 / F-005 | 76、77及完整1237条导出 | 输入登记成功、键盘Support=2/RuntimeAvailable；整个导出没有 INPUT F8 回调。此证据不能把设备就绪升级成快捷键动作验收。 |

主代理另行报告：18:25前对前台游戏成功执行过一次computer-use F8按键操作。这是日志之外的操作观察，当前日志只证明“未观察到回调”，不能独立证明游戏收到该按键，也不能确定缺失原因。

材质模块在冻结status中是active、RuntimeProbed。模块就绪与具体参数成功是不同条件；拒绝结果不代表材质覆盖全矩阵已完成。TMP原生属性槽等问题的原因仍在后续源码调查/修复，不能用推测补写此包的成功结果。

## Path

P-001：旧选择目标 → 参数按钮 → 目标就绪检查失败 → TARGET MISSING → 不发出创建请求（E-001）。

P-002：真实Canvas/DiscTabItem材质 → TMP/Translucent参数请求 → 精确来源shader身份比较失败 → CREATE REFUSED → created/applied保持0（E-002–E-004）。

P-003：键盘设备快照RuntimeAvailable及登记成功 → 主代理报告F8操作 → 冻结日志仍无INPUT F8（E-005）。这条路径缺少实际动作回调证据，输入验收保持未完成。

下一修复包须独立证明正确TMP/材质属性来源、参数写入读回、真实目标覆盖与停止恢复；并独立取得动作边沿/回调，继续验证文本与模态焦点、双插件冲突和设备重新出现。不能改写此603会话的拒绝事实。
