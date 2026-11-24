# 真实开局与失败结算关联

2026-10-19 用 Computer Use 进入已安装的 Rizline，在选曲页启动 Greatness Earth EZ，等待真实失败结算。画面显示 Score 0、Miss 479，与复制自真实结果对象的日志一致。没有使用历史最好成绩生成结果。

证据为 [运行摘要](run-summary.json)、[会话日志](runtime-session/events-000001.jsonl)、[状态快照](status.json) 和 [本次实机包清单](installed-test-package-manifest.json)。17 个已安装包文件均按此清单核对 SHA-256；随后工作树的新服务接入构建属于另一批包，不能混作本次游玩证据。

Evidence → Finding → Path：seq 183 的 `game.run.started` 与 seq 219 的 `game.run.finished` 具有同一个 RunId `15944581258b4fd885f49c0d22e4ade1` 和谱面 ID → 这次真实失败局的开始/结算关联通过 → 继续正常完成、重试、暂停恢复、提前退出四类流程，B01 保持未完成。一次 Escape 输入未打开暂停菜单，因此不将其登记为暂停验证。

复核方式：解析日志，筛选 `eventCode` 严格等于 `game.run.started` 或 `game.run.finished` 的记录；两类各一条，比较 `context.runId`，并核对上列序号、谱面及真实计数。完整验收条件见 [SPEC](../../../.docs/foundation-expansion/SPEC.md#b01-gameplay-closure)。
