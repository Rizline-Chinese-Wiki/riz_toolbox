# 时间线

| UTC | 观察 | 证据 |
|---|---|---|
| 2026-11-15T04:18:46.543641+00:00 → 2026-11-15T04:22:05.222474+00:00 | r1：v1 模块，10 个子步骤自然返回 0 | `runs/r1/result.json`、`runs/r1/supervision/controller-exit.json` |
| 2026-11-15T04:23:33.869349+00:00 | 准备 v2 最小正规数间隔及新控制器；保留 v1 | `controls/runtime/normal-interval-controls.json` |
| 2026-11-15T04:24:31.815021+00:00 → 2026-11-15T04:27:35.475657+00:00 | r2：新完整 source 与新输出；6 个子步骤自然返回 0 | `runs/r2/result.json`、`runs/r2/supervision/controller-exit.json` |
| 2026-11-15T04:29:06.556878+00:00 → 2026-11-15T04:29:36.803774+00:00 | 安装器实际 wait=0；验证当前 23 项自有安装字节 | `install/result.json`、`install/installer.log` |
| 2026-11-15T04:33:55.959972+00:00 | PID15924 有限启动快照；native stage145/row14 阻断 Loader，正常标题页和正常退出 | `runtime-r1/observation.json`、`runtime-r1/native-preinit-current.log` |
| 2026-11-15T04:40:31.331832+00:00 | 读取完成记录并生成可携带包；未执行历史控制器 | `summary.json`、`hash-index.json` |

外部 tool chunk 由父任务报告的出处单独列在 `tool-observations.json`；它不是伪造的外部工具对象。Launch 的 0 仅表示 launch 结束，实际测试退出使用原始 `subprocess.Popen.wait` receipt。时间线没有把后台活动、进程消失或中间日志当作实际成功退出。
