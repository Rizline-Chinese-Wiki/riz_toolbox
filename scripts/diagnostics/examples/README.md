# 同宿主两局验收脚本示例

这里放了一组**全合成**的诊断日志和命令回执，用来演示如何离线跑通 `check_same_session.py`：

1. 先打一局官方谱面：依次执行暂停、设为 0.75 倍速、跳到第 100 秒、恢复，然后正常结算；
2. 紧接着在同一宿主进程里打一局单音符 LOCAL 谱面，记录一次判定和 SDK 结算；
3. 核对 LOCAL 请求前和返回选曲后的官方成绩条数与摘要完全一致（证明 LOCAL 没污染官方存档）。

## 运行方式

在仓库根目录执行：

```bash
python -B scripts/diagnostics/check_same_session.py --events scripts/diagnostics/examples/synthetic-complete/host --watch-output scripts/diagnostics/examples/synthetic-complete/watch-output
```

预期退出码为 `0`，输出中 `status` 为 `verified`，`official.endReason` 为 `Completed`，`local.outcome` 为 `verified`。

## 示例目录内容

- `host/`：同一宿主的 `session.json` 和事件流 JSONL。
- `watch-output/`：四步控制命令各自的执行前快照与 SDK 回执。
