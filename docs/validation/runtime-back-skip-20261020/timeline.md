# 时间线（UTC）

| 时间 | 观察 | 产物 |
|---|---|---|
| 2026-10-20 07:54–07:57 | 上一 582 包暂停冻结、恢复后推进，Retry 发出旧局 Aborted 并启动新局 | `../runtime-cancellation-20261020/runtime-proof.json` |
| 2026-10-20 10:14 | 603/603、24 项目完整离线回归完成；先前测试 Check 调用编译失败日志保留 | `build.log`、`build-fixed.log`、`build-summary.json` |
| 2026-10-20 10:15:06 | 正式安装后 17 个文件实际哈希相符；调用者曾误读陈旧 LASTEXITCODE，已以文件验证纠正，未重复安装 | `installation-check.json` |
| 2026-10-20 10:15:11 | 新诊断会话启动 | `session-back-skip/session.json` |
| 2026-10-20 10:17:45 | Back 请求被接受，随后同一原生 Update 完成双终止边沿；下一宿主更新先取消 scope，再发 Aborted | `runtime-proof.json`，序号 529–535 |
| 2026-10-20 10:18:30 | Skip 原调用完成双后置，SDK 记录旧局审计，下一宿主更新取消 scope 并发 Skipped | `runtime-proof.json`，序号 891–904 |
| 2026-10-20 10:21:50–10:22:10 | 实际 DiscTabItem 的 TMP/Translucent 参数请求均被精确 Shader 不匹配拒绝，没有 clone/applied 成功 | `session-material-refusal/` |
| 2026-10-20 10:25 左右 | 前台游戏一次 F8 注入未观察到 INPUT F8 日志；不能据设备可读取声明动作验收 | computer-use 操作记录及 `session-material-refusal/` |
| 2026-10-20 10:27 左右 | 游戏关闭，保留 603 已安装包和独立冻结实测；继续修正材质源槽位 | 主代理操作记录 |
