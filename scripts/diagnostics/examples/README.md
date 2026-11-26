# 同宿主两局检查示例

这份示例使用全合成的日志和回执。官方局依次执行暂停、设为 0.75 倍速、跳到第 100 秒和恢复，并正常结算。随后同一宿主运行单音符 LOCAL 谱面，记录一次判定和 SDK 结算。LOCAL 请求前及返回选曲后的官方成绩条数和摘要相同。

在仓库根目录用 Python 运行以下命令。

```bash
python -B scripts/diagnostics/check_same_session.py --events scripts/diagnostics/examples/synthetic-complete/host --watch-output scripts/diagnostics/examples/synthetic-complete/watch-output
```

命令预期退出码为 `0`，输出的 `status` 为 `verified`，`official.endReason` 为 `Completed`，`local.outcome` 为 `verified`。

- `host/` 包含同一宿主的 `session.json` 和事件 JSONL。
- `watch-output/` 包含四步控制各自的命令前快照与 SDK 回执。
